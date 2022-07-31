using Godot;
using Godot.Collections;


// Possible crouch values:
// maxWalkSpeed = 1.5f;
// maxAccel = 7.0f;
// stopSpeed = 1.75f;

/*
 * TODO:
 * * Investigate an air control var.
 * * Investigate smoother mouse look.
 */

public class PlayerMovee : RigidBody
{
	[Export]
	// The top speed, in meters per second, that a player may initiate without movement tricks (bhopping, etc)
	// See: sv_maxspeed = 10.0
	private float maxWalkSpeed = 10.0f;
	[Export]
	private float maxAirSpeed = 0.9375f;
	[Export]
	// How much friction is applied to decelerating the player. A measure of how "slippery" surfaces will feel.
	// See: sv_friction = 4.0
	private float friction = 4.0f;
	[Export]
	// The maximum amount a player can accelerate in a single physics step.
	// See: sv_accelerate = 10.0
	private float maxAccel = 10.0f;
	[Export]
	// A modifier to control how quickly the player decelerates to a stop at low speeds, in combination with friction.
	// See: sv_stopspeed = 3.125
	private float stopSpeed = 2.125f;
	[Export]
	// Speed of constant gravity force, in meters per second.
	private float gravity = -25.00f;
	[Export]
	// How much upward velocity is instantly applied to the player on jump.
	private float jumpForce = 8.2f;
	[Export]
	// A limit on how steep a slope can be before a player cannot traverse it. 
	private float maxWalkAngle = 45.0f;
	[Export]
	// The maximum height a player can smoothly step up onto without jumping.
	private float maxStepHeight = 0.5625f;

	[Export]
	private float horzSens = 0.4f;
	[Export]
	private float vertSens = 0.4f;

	// TODO: Expose to console.
	bool drawDebug = true;
	bool autojump = false;

	private Vector3 velocity;
	private Vector3 wishDir;
	private Camera camRef;
	private float pitch = 0.0f;
	private float yaw = 0.0f;

	private float colliderMargin;

	CollisionShape collider;
	StateMachine movementStates;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		movementStates = new StateMachine();
		movementStates.PushState(MovementStates.Ground);

		collider = GetNode<CollisionShape>("CollisionShape");
		GetNode<Spatial>("ForwardHint").Visible = false;
		Input.SetMouseMode(Input.MouseMode.Captured);

		camRef = GetNode<Camera>("Camera");
		camRef.SetAsToplevel(true);
		maxAccel = maxAccel * maxWalkSpeed;
		colliderMargin = collider.Shape.Margin;
	}

	internal bool TestGround(PhysicsDirectBodyState state)
	{
		if (state.GetContactCount() == 0) { return false; }
		if (velocity.y > 0.0f) { return false; }

		for (int i = 0; i < state.GetContactCount(); i++)
		{
			Vector3 surfNormal = state.GetContactLocalNormal(i);

			float angle = Mathf.Rad2Deg(Mathf.Acos(surfNormal.Dot(Vector3.Up) / surfNormal.Length()));
			if (angle < maxWalkAngle) { return true; }
		}
		return false;
	}

	float CalcMaxSpeed()
	{
		if (movementStates.ContainsState(MovementStates.Walk))
		{
			return maxWalkSpeed;
		} else if (movementStates.ContainsState(MovementStates.Air))
		{
			return maxAirSpeed;
		}

		return 0;
	}

	public override void _IntegrateForces(PhysicsDirectBodyState state)
	{
		AxisLockLinearY = false;
		bool wasAirborneLastFrame = movementStates.ContainsState(MovementStates.Air);
		if (TestGround(state))
		{
			movementStates.RemoveHierarchy(MovementStates.Air);
			movementStates.PushState(MovementStates.Ground);
			movementStates.PushState(MovementStates.Walk);
		} else
		{
			movementStates.RemoveHierarchy(MovementStates.Ground);
			movementStates.PushState(MovementStates.Air);
		}

		if (movementStates.ContainsState(MovementStates.Air) && (velocity.y < 0))
		{
			if (movementStates.ContainsState(MovementStates.Jump) && !movementStates.ContainsState(MovementStates.Fall))
			{
				OnPlayerJumpApex();
			}
			if (!movementStates.ContainsState(MovementStates.Fall))
			{
				OnPlayerBeginFalling();
			}
			movementStates.PushState(MovementStates.Fall);
		}

		// Did we land this frame?
		if (wasAirborneLastFrame && movementStates.ContainsState(MovementStates.Ground))
		{
			OnPlayerLanded();
		}

		CalcWishDir();
		// While we calculate velocity every frame for internal use, 
		// we only send the updated velocity to the physics server if we DIDN'T find a slope/step
		// and teleport the player. Otherwise, we end up with excessive speed as the physics server
		// adds our new velocity on top of the teleport we did.
		state.LinearVelocity = CalcNewVel(state) ? velocity : Vector3.Zero;
	}

	float AttemptStep(Vector3 moveDelta, PhysicsDirectBodyState state)
	{
		if (velocity == Vector3.Zero) { return 0; }
		Array ignoreThis = new Array(this);
		var space = GetWorld().DirectSpaceState;

		CylinderShape traceShape = new CylinderShape();
		traceShape.Radius = ((CylinderShape)collider.Shape).Radius;
		traceShape.Height = ((CylinderShape)collider.Shape).Height;
		Array results = new Array();

		Utils.TestMotion(space, traceShape, collider.GlobalTransform.origin, Vector3.Up * maxStepHeight, ref results, ignoreThis);
		Vector3 maxUpPos = collider.GlobalTransform.origin + Utils.ScaleVector(Vector3.Up * maxStepHeight, (float)results[0]);
		// If we hit a ceiling, we need to track how far up we moved for later calculations.
		float missingTraverseAmount = Mathf.Abs((maxStepHeight * (float)results[0]) - maxStepHeight);
		results.Clear();

		// Account for margin, otherwise we can't step up at slow speeds.
		Vector3 forwardMotion = (moveDelta * state.Step).Normalized() * ((moveDelta * state.Step).Length() + colliderMargin);
		Utils.TestMotion(space, traceShape, maxUpPos, forwardMotion, ref results, ignoreThis);
		Vector3 maxForwardPos = maxUpPos + Utils.ScaleVector(forwardMotion, (float)results[0]);
		results.Clear();

		// Reduce the shape's radius very slightly to avoid detecting walls the player is rubbing up against.
		traceShape.Radius = ((CylinderShape)collider.Shape).Radius;
		Utils.TestMotion(space, traceShape, maxForwardPos, Vector3.Down * (maxStepHeight - missingTraverseAmount), ref results, ignoreThis);
		Vector3 finalPos = maxForwardPos + Utils.ScaleVector(Vector3.Down * (maxStepHeight - missingTraverseAmount), (float)results[0]);

		if (Mathf.IsEqualApprox((float)results[0], 1.0f))
		{
			results.Clear();
			// Didn't find a place to step...
			if (movementStates.ContainsState(MovementStates.Air)) { return 0; }
			// Attempt to step down
			Utils.TestMotion(space, traceShape, finalPos, Vector3.Down * maxStepHeight, ref results, ignoreThis);
			finalPos = finalPos + Utils.ScaleVector(Vector3.Down * maxStepHeight, (float)results[0]);
			if (!Mathf.IsZeroApprox((float)results[0]))
			{
				// Found a spot to step down.
				// Confirm that what we are stepping onto is within our max slope allowance.
				Dictionary hitResult = new Dictionary();
				Utils.TestIntersection(space, traceShape, finalPos, ref hitResult, ignoreThis);
				if (hitResult.Count == 0) { return 0; } // Why?
				Vector3 resNormal = (Vector3)hitResult["normal"];
				float angle = GetVec3Angle(resNormal, Vector3.Up);
				if (angle <= maxWalkAngle)
				{
					var oldTransform = state.Transform;
					Vector3 oldOrigin = oldTransform.origin;
					oldTransform.origin = finalPos;
					state.Transform = oldTransform;
					return (state.Transform.origin - oldOrigin).Length();
				}
			}
		} else
		{
			// Found a spot to step up!
			// Confirm that what we are stepping onto is within our max slope allowance.
			Dictionary hitResult = new Dictionary();
			Utils.TestIntersection(space, traceShape, finalPos, ref hitResult, ignoreThis);
			if (hitResult.Count == 0) { return 0; } // Why?
			Vector3 resNormal = (Vector3)hitResult["normal"];
			float angle = GetVec3Angle(resNormal, Vector3.Up);
			if (angle <= maxWalkAngle)
			{
				var oldTransform = state.Transform;
				Vector3 oldOrigin = oldTransform.origin;
				oldTransform.origin = finalPos;
				state.Transform = oldTransform;
				return (state.Transform.origin - oldOrigin).Length();
			} else
			{
				// Hack to (mostly) prevent bumping up and down steep slopes. Doesn't work all the time (why?)
				if (movementStates.ContainsState(MovementStates.Ground)) { AxisLockLinearY = true; }
			}
		}

		return 0;
	}

	void CalcWishDir()
	{
		wishDir = new Vector3();
		// Ignore Y dir of basis vectors so that camera pitch doesn't cause player to move up or down by looking.
		Vector3 forward2D = new Vector3(camRef.GlobalTransform.basis.z.x, 0, camRef.GlobalTransform.basis.z.z);
		Vector3 right2D = new Vector3(camRef.GlobalTransform.basis.x.x, 0, camRef.GlobalTransform.basis.x.z);

		if (Input.IsActionPressed("move_forward"))
		{
			wishDir -= forward2D;
		}
		if (Input.IsActionPressed("move_back"))
		{
			wishDir += forward2D;
		}
		if (Input.IsActionPressed("strafe_left"))
		{
			wishDir -= right2D;
		}
		if (Input.IsActionPressed("strafe_right"))
		{
			wishDir += right2D;
		}

		wishDir = wishDir.Normalized();
	}

	void ApplyFriction(float physDelta)
	{
		// Ensure there exists a single frame after landing where we do not apply friction.
		if (movementStates.GetCurrentState() == MovementStates.Landed) { movementStates.PopState(); return; }
		float lastSpeed = GetVec2D(velocity).Length();
		// Zero out small float values to ensure we come to a complete stop.
		if (lastSpeed <= 0.03f)
		{
			velocity = Vector3.Zero;
			return;
		}
		if (lastSpeed != 0)
		{
			// stopspeed only scales decel at low speeds.
			float finalStopSpeedScale = Mathf.Max(stopSpeed, lastSpeed);
			Vector3 decelAmount = GetVec2D(velocity).Normalized() * friction * finalStopSpeedScale;
			velocity -= decelAmount * physDelta;

			// Fix for jittery back-and-forward velocity changes just before stopping.
			var signedDecel = decelAmount.Sign();
			var signedVel = velocity.Sign();
			if (!(Mathf.IsEqualApprox(signedDecel.x, signedVel.x) && Mathf.IsEqualApprox(signedDecel.z, signedVel.z)))
            {
				velocity = Vector3.Zero; return;
			}
		}
	}

	void ApplyGravity(float physDelta)
	{
		velocity.y += (Vector3.Up * gravity * physDelta).y;
	}

	bool CalcWallVelSlide(ref Vector3 nextVel, PhysicsDirectBodyState state)
	{
		var space = GetWorld().DirectSpaceState;
		// Set up our initial query to overlap our real collider minus our step height.
		CylinderShape traceShape = new CylinderShape();
		// Apply collider margin to avoid missing detections of the wall.
		traceShape.Radius = ((CylinderShape)collider.Shape).Radius + colliderMargin;
		traceShape.Height = ((CylinderShape)collider.Shape).Height - maxStepHeight;

		Array<Dictionary> results = Utils.TestMultiIntersection(space, traceShape, GlobalTransform.origin, new Array(this));

		foreach (Dictionary hitResult in results)
		{
			Vector3 normal = (Vector3)hitResult["normal"];
			if (GetVec3Angle(nextVel, -normal) < 90.0f)
			{
				// Slide our velocity along the wall normal.
				Vector3 goalPos = GlobalTransform.origin + nextVel;
				Vector3 distance = goalPos - GlobalTransform.origin;
				distance = distance.Slide(normal);
				nextVel = distance;
				return true;
			}
		}
		return false;
	}

	bool CalcNewVel(PhysicsDirectBodyState state)
	{
		if (movementStates.ContainsState(MovementStates.Ground)) { ApplyFriction(state.Step); }

		// Clamp velocity based on dot product of wishdir and current velocity, because that's how quake did it
		// for some reason. See: https://www.youtube.com/watch?v=v3zT3Z5apaM
		float curSpeed = velocity.Dot(wishDir);

		// Cap air speed to prevent dramatic movements in the air as a result of no frictional force.
		float maxSpeed = CalcMaxSpeed();
		float addSpeed = Mathf.Clamp(maxSpeed - curSpeed, 0, maxAccel * state.Step);

		// Increase our vel based on addSpeed in the wishdir.
		Vector3 predictedNextVel = velocity + addSpeed * wishDir;

		// Test for wall collisions that would change our velocity.
		CalcWallVelSlide(ref predictedNextVel, state);
		// Check for slopes. Modulate velocity by the amount we had to teleport, if any.
		float amtMoved = AttemptStep(predictedNextVel, state);
		velocity = predictedNextVel.Normalized() * (predictedNextVel.Length());

		// Apply gravity after we've determined our horz trajectory.
		if (movementStates.ContainsState(MovementStates.Air)) { ApplyGravity(state.Step); }

		// Return whether we should actually post this velocity to the physics server (if we didn't manually teleport the player.
		return Mathf.IsZeroApprox(amtMoved) ? true : false;
	}
	
	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseMotion motionEvent)
		{
			pitch -= motionEvent.Relative.y * horzSens;
			yaw -= motionEvent.Relative.x * vertSens;

			// Clamp camera to avoid flipping
			if (pitch >= 89.0f)
			{
				pitch = 89.0f;
			}
			if (pitch <= -89.0f)
			{
				pitch = -89.0f;
			}

			Vector3 cameraRotDeg = camRef.RotationDegrees;
			cameraRotDeg.y = yaw;
			cameraRotDeg.x = pitch;
			camRef.RotationDegrees = cameraRotDeg;
		}
	}

	public override void _Process(float delta)
	{
		bool jumpPress = autojump ? Input.IsActionPressed("Jump") : Input.IsActionJustPressed("Jump");
		if (jumpPress && movementStates.ContainsState(MovementStates.Ground))
		{
			OnBeginJump();
		}

		// Update cam pos with player.
		var transform = camRef.Transform;
		transform.origin.x = this.GlobalTransform.origin.x;
		transform.origin.z = this.GlobalTransform.origin.z;
		transform.origin.y = this.GlobalTransform.origin.y + 1.0f;
		camRef.Transform = transform;

		if (Input.IsKeyPressed((int)KeyList.Capslock))
		{
			DebugDraw.Freeze3DRender = true;
		}
		if (drawDebug)
		{
			Vector3 rayOrigin = camRef.Transform.origin;
			rayOrigin.y -=  0.75f;
			DebugDraw.DrawArrowRay3D(rayOrigin, wishDir.Normalized(), 1.25f, new Color(255, 0, 0));
			DebugDraw.DrawArrowRay3D(rayOrigin, velocity.Normalized(), velocity.Length() / 5, new Color(0, 255, 0));

			DebugDraw.TextBackgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.1f);
			DebugDraw.TextForegroundColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);
			DebugDraw.BeginTextGroup("Player Information");
			DebugDraw.SetText("Real Horz Velocity:", GetVec2D(velocity), 0);
			DebugDraw.SetText("Real Horz speed:", GetVec2D(velocity).Length(), 1);
			DebugDraw.SetText("Movement State Stack:", movementStates.ToString(), 3);
			DebugDraw.EndTextGroup();
		}
	}

	protected virtual void OnPlayerLanded()
	{
		movementStates.PushState(MovementStates.Landed);
		velocity.y = 0;
	}
	protected virtual void OnBeginJump()
	{
		velocity.y += jumpForce;
		movementStates.RemoveHierarchy(MovementStates.Ground);
		movementStates.PushState(MovementStates.Air);
		movementStates.PushState(MovementStates.Jump);
	}

	protected virtual void OnPlayerJumpApex()
	{
		GD.Print("Jump apex!");
	}

	protected virtual void OnPlayerBeginFalling()
	{
		GD.Print("Begin falling!");
	}

	Vector3 GetVec2D(Vector3 inVec)
	{
		return new Vector3(inVec.x, 0, inVec.z);
	}

	float GetVec3Angle(Vector3 first, Vector3 second)
	{
		// TODO: Is this right?
		return 90 - Mathf.Rad2Deg(first.Dot(second) / (first.Length() * second.Length()));
	}
}
