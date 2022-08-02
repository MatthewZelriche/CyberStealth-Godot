using Godot;
using Godot.Collections;
using Hsm;

// Possible crouch values:
// maxSpeed = 50;
// maxAccel = 8.0f;
// stopSpeed = 55;
// edgeFrictionMult = 1.0

public class PlayerMovee : RigidBody
{
	const float QODOT_INVERSE_SCALE = 32.0f;

	[Export]
	// The top speed, in units, that a player may initiate without movement tricks (bhopping, etc)
	// See: sv_maxspeed. Default: 320 hammer units
	private float maxWalkSpeed = 320.0f;
	[Export]
	// The top speed, in units, that a player may initiate while in the air. 
	// Dramatically reduced to account for zero friction being applied to the player while they are in the air.
	// Default: 30 hammer units
	private float maxAirSpeed = 30.0f;
	[Export]
	// How much friction is applied to decelerating the player. A measure of how "slippery" surfaces will feel.
	// See: sv_friction. Default: 4.0
	private float friction = 4.0f;
	[Export]
	// How much the friction should be multiplied by when the player is near a steep ledge.
	// A value of 1.0 disables edge friction.
	// Default: 2.0;
	private float edgeFrictionMult = 2.0f;
	[Export]
	// A measure of how quickly a player accelerates per physics step.
	// See: sv_accelerate. Default: 10
	private float maxAccel = 10.0f;
	[Export]
	// A modifier to control how quickly the player decelerates to a stop at low speeds, in combination with friction.
	// See: sv_stopspeed. Default: 100
	private float stopSpeed = 100.0f;
	[Export]
	// Maximum speed of gravity force, in hammer units per second.
	private float gravity = -800.0f;
	[Export]
	// How much upward velocity, in hammer units, is instantly applied to the player on jump.
	private float jumpForce = 268.3f;
	[Export]
	// The maximum slope angle, in degrees, that a player may walk on without sliding off.
	private float maxWalkAngle = 45.573f;
	[Export]
	// The maximum height, in hammer units, a player can smoothly step up onto without jumping.
	private float maxStepHeight = 18.0f;

	// TODO: Expose to console.
	bool drawDebug = true;
	bool autojump = true;

	private float currentMaxSpeed;
	private Vector3 velocity;
	private Vector3 wishDir;
	private CameraController camRef;
	private float currentEdgeFriction = 0.0f;

	private float colliderMargin;

	CollisionShape collider;
	PhysicsDirectBodyState physBodyState;
	bool noFrictionThisFrame = false;
	StateMachine movementStates;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		movementStates = new StateMachine();
		movementStates.Init<Ground>(this);

		collider = GetNode<CollisionShape>("CollisionShape");
		GetNode<Spatial>("ForwardHint").Visible = false;
		Input.SetMouseMode(Input.MouseMode.Captured);

		camRef = GetNode<CameraController>("Camera");
		camRef.SetEyePos(camRef.StandingEyeHeight - ((CylinderShape)collider.Shape).Height/2);

		colliderMargin = collider.Shape.Margin;
		physBodyState = PhysicsServer.BodyGetDirectState(GetRid());


		maxWalkSpeed /= QODOT_INVERSE_SCALE;
		maxAirSpeed /= QODOT_INVERSE_SCALE;
		stopSpeed /= QODOT_INVERSE_SCALE;
		gravity /= QODOT_INVERSE_SCALE;
		jumpForce /= QODOT_INVERSE_SCALE;
		maxStepHeight /= QODOT_INVERSE_SCALE;
		maxAccel *= maxWalkSpeed;
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

	public override void _IntegrateForces(PhysicsDirectBodyState state)
	{
		bool wasAirborneLastFrame = movementStates.IsInState<Air>();
		movementStates.ProcessStateTransitions();
		movementStates.UpdateStates(state.Step);

		// See: AttemptStep. This is for helping prevent weird RigidBody movement on steep slopes.
		AxisLockLinearY = false;

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

		Utils.TestMotion(space, traceShape, maxForwardPos, Vector3.Down * (maxStepHeight - missingTraverseAmount), ref results, ignoreThis);
		Vector3 finalPos = maxForwardPos + Utils.ScaleVector(Vector3.Down * (maxStepHeight - missingTraverseAmount), (float)results[0]);

		if (Mathf.IsEqualApprox((float)results[0], 1.0f))
		{
			results.Clear();
			// Didn't find a place to step...
			if (movementStates.IsInState<Air>()) return 0;
			// Attempt to step down
			Utils.TestMotion(space, traceShape, finalPos, Vector3.Down * maxStepHeight, ref results, ignoreThis);
			finalPos = finalPos + Utils.ScaleVector(Vector3.Down * maxStepHeight, (float)results[0]);
			if (!Mathf.IsZeroApprox((float)results[0]))
			{
				// Found a spot to step down.
				// Confirm that what we are stepping onto is within our max slope allowance.
				Dictionary hitResult = new Dictionary();
				Utils.TestIntersection(space, traceShape, finalPos, ref hitResult, ignoreThis);
				if (hitResult.Count == 0) { return 0; }
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
			if (hitResult.Count == 0) { return 0; }
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
				if (movementStates.IsInState<Ground>()) AxisLockLinearY = true;
			}
		}

		return 0;
	}

	void CalcWishDir()
	{
		wishDir = new Vector3();
		// Ignore Y dir of basis vectors so that camera pitch doesn't cause player to move up or down by looking.
		Vector3 forward2D = GetVec2D(camRef.GetForwardVector());
		Vector3 right2D = GetVec2D(camRef.GetRightVector());

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

	bool ShouldApplyEdgeFriction(float physDelta)
	{
		if (movementStates.IsInState<Air>()) return false;
		
		var space = GetWorld().DirectSpaceState;
		Vector3 traceBeginPos = GlobalTransform.origin + velocity.Normalized() * ((CylinderShape)collider.Shape).Radius;
		traceBeginPos = traceBeginPos + Vector3.Down * ((CylinderShape)collider.Shape).Height / 2;
		Array throwaway = new Array();
		Dictionary throwawayDictionary = new Dictionary();
		Array excludeThis = new Array(this);

		if (Utils.TestIntersection(space, collider.Shape, traceBeginPos, ref throwawayDictionary, excludeThis)) { return false; }
		return Utils.TestMotion(space, collider.Shape, traceBeginPos, traceBeginPos + Vector3.Down * ((CylinderShape)collider.Shape).Height / 2, ref throwaway, excludeThis);
	}

	void ApplyFriction(float physDelta)
	{
		// Ensure there exists a single frame after landing where we do not apply friction.
		if (noFrictionThisFrame) {  noFrictionThisFrame = false; return; }
		float lastSpeed = GetVec2D(velocity).Length();
		// Zero out small float values to ensure we come to a complete stop.
		if (lastSpeed <= 0.03f)
		{
			velocity.x = 0;
			velocity.z = 0;
			return;
		}
		if (lastSpeed != 0)
		{
			// stopspeed only scales decel at low speeds.
			float finalStopSpeedScale = Mathf.Max(stopSpeed, lastSpeed);
			Vector3 decelAmount = GetVec2D(velocity).Normalized() * friction * currentEdgeFriction * finalStopSpeedScale;
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
		if (ShouldApplyEdgeFriction(state.Step)) { currentEdgeFriction = edgeFrictionMult; } else { currentEdgeFriction = 1.0f; }
		if (movementStates.IsInState<Ground>()) ApplyFriction(state.Step);


		// Clamp velocity based on dot product of wishdir and current velocity, because that's how quake did it
		// for some reason. See: https://www.youtube.com/watch?v=v3zT3Z5apaM
		float curSpeed = velocity.Dot(wishDir);

		// Cap air speed to prevent dramatic movements in the air as a result of no frictional force.
		float maxSpeed = currentMaxSpeed;
		float addSpeed = Mathf.Clamp(maxSpeed - curSpeed, 0, maxAccel * state.Step);

		// Increase our vel based on addSpeed in the wishdir.
		Vector3 predictedNextVel = velocity + addSpeed * wishDir;

		// Test for wall collisions that would change our velocity.
		CalcWallVelSlide(ref predictedNextVel, state);
		// Check for slopes. Modulate velocity by the amount we had to teleport, if any.
		float amtMoved = AttemptStep(predictedNextVel, state);
		velocity = predictedNextVel.Normalized() * (predictedNextVel.Length());

		// Apply gravity after we've determined our horz trajectory.
		if (movementStates.IsInState<Air>()) ApplyGravity(state.Step);

		// Return whether we should actually post this velocity to the physics server (if we didn't manually teleport the player.
		return Mathf.IsZeroApprox(amtMoved) ? true : false;
	}

	public override void _Process(float delta)
	{

		if (Input.IsKeyPressed((int)KeyList.Capslock))
		{
			DebugDraw.Freeze3DRender = true;
		}
		if (drawDebug)
		{
			Vector3 rayOrigin = camRef.GlobalTransform.origin;
			rayOrigin.y -=  0.75f;
			DebugDraw.DrawArrowRay3D(rayOrigin, wishDir.Normalized(), 1.25f, new Color(255, 0, 0));
			DebugDraw.DrawArrowRay3D(rayOrigin, velocity.Normalized(), velocity.Length() / 5, new Color(0, 255, 0));

			DebugDraw.TextBackgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.1f);
			DebugDraw.TextForegroundColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);
			DebugDraw.BeginTextGroup("Player Information");
			DebugDraw.SetText("Real Horz Velocity:", GetVec2D(velocity), 0);
			DebugDraw.SetText("Real Horz speed:", GetVec2D(velocity).Length(), 1);
			DebugDraw.SetText("Movement State Stack:", movementStates.GetStateStackAsString(), 3);
			DebugDraw.EndTextGroup();
		}
	}

	protected virtual void OnPlayerLanded()
	{
		noFrictionThisFrame = true;
		velocity.y = 0;
	}
	protected virtual void OnBeginJump()
	{
		velocity.y += jumpForce;
	}

	protected virtual void OnPlayerJumpApex()
	{
	}

	protected virtual void OnPlayerBeginFalling()
	{
	}

	Vector3 GetVec2D(Vector3 inVec)
	{
		return new Vector3(inVec.x, 0, inVec.z);
	}

	float GetVec3Angle(Vector3 first, Vector3 second)
	{
		return 90 - Mathf.Rad2Deg(first.Dot(second) / (first.Length() * second.Length()));
	}

	// Movement States
	class Ground : StateWithOwner<PlayerMovee>
	{
		public override Transition GetTransition()
		{
			if (!Owner.TestGround(Owner.physBodyState))
			{
				return Transition.Sibling<Air>();
			}

			return Transition.InnerEntry<Walk>();
		}

		public override void Update(float aDeltaTime)
		{
			bool didRequestJump = Owner.autojump ? Input.IsActionPressed("Jump") : Input.IsActionJustPressed("Jump");
			if (didRequestJump)
			{
				Owner.OnBeginJump();
			}
		}
	}

	class Air : StateWithOwner<PlayerMovee>
	{
		public override Transition GetTransition()
		{
			if (Owner.TestGround(Owner.physBodyState))
			{
				Owner.OnPlayerLanded();
				return Transition.Sibling<Ground>();
			}
			if (Owner.velocity.y > 0)
			{
				return Transition.InnerEntry<Jump>();
			}
			return Transition.InnerEntry<Fall>();
		}

		public override void OnEnter()
		{
			Owner.currentMaxSpeed = Owner.maxAirSpeed;
		}
	}

	class Walk : StateWithOwner<PlayerMovee>
	{
		public override void OnEnter()
		{
			Owner.currentMaxSpeed = Owner.maxWalkSpeed;
		}
	}

	class Jump : StateWithOwner<PlayerMovee>
	{
		public override Transition GetTransition()
		{
			if (Owner.velocity.y < 0) { return Transition.InnerEntry<Fall>(); }

			return Transition.None();
		}
	}

	class Fall : StateWithOwner<PlayerMovee>
	{	
		public override Transition GetTransition()
		{
			return Transition.None();
		}
		public override void OnEnter()
		{
			if (Owner.movementStates.IsInState<Jump>()) { Owner.OnPlayerJumpApex(); }
			Owner.OnPlayerBeginFalling();
		}
	}
}