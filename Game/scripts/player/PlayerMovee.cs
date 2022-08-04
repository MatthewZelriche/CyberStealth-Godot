using Godot;
using Godot.Collections;
using Hsm;


public class PlayerMovee : RigidBody
{
	const float QODOT_INVERSE_SCALE = 32.0f;

	[Export]
	// The top speed, in units, that a player may initiate without movement tricks (bhopping, etc)
	// See: sv_maxspeed. Default: 320 hammer units
	private float maxSpeed = 320.0f;
	[Export]
	// The speed at which the player attempts to move while walking.
	// See: Similar to cl_forwardspeed. Default: 400 hammer units
	private float maxWalkSpeed = 400.0f;
	// The top speed, in hammer units, that a player may initiate while crouched.
	// See: https://www.jwchong.com/hl/duckjump.html May not be entirely accurate because HL1 uses cl_Forwardspeed
	// in this calculation, which I do not. But the rationale is 400 * 0.333 = 133.2.
	[Export]
	private float maxCrouchSpeed = 133.2f;
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
	[Export]
	// The height, in hammer units, of the player's collider while they are in the crouch state.
	// Default: 36.0
	private float CrouchHeight = 36.0f;
	[Export]
	private float CrouchTime = 0.4f;
	[Export]
	private float UncrouchTime = 0.2f;

	// TODO: Expose to console.
	bool drawDebug = true;
	bool autojump = true;

	private float currentMaxGroundSpeed;
	private Vector3 velocity;
	private Vector3 wishDir;
	private CameraController camRef;
	private float currentEdgeFriction = 1.0f;
	private float floorY = 0.0f;

	private float colliderMargin;

	CollisionShape collider;
	PhysicsDirectBodyState physBodyState;
	bool noFrictionThisFrame = false;
	StateMachine movementStates;
	StateMachine groundedStates;
	
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		movementStates = new StateMachine();
		groundedStates = new StateMachine();
		movementStates.Init<Walk>(this);
		groundedStates.Init<Air>(this);

		collider = GetNode<CollisionShape>("CollisionShape");
		GetNode<Spatial>("ForwardHint").Visible = false;
		Input.SetMouseMode(Input.MouseMode.Captured);

		camRef = GetNode<CameraController>("Camera");
		camRef.SetEyePos(0 - ((CylinderShape)collider.Shape).Height / 2 + ((CylinderShape)collider.Shape).Height - camRef.EyeHeightDistanceFromTop);

		colliderMargin = collider.Shape.Margin;
		physBodyState = PhysicsServer.BodyGetDirectState(GetRid());


		maxSpeed /= QODOT_INVERSE_SCALE;
		maxWalkSpeed /= QODOT_INVERSE_SCALE;
		maxCrouchSpeed /= QODOT_INVERSE_SCALE;
		maxAirSpeed /= QODOT_INVERSE_SCALE;
		stopSpeed /= QODOT_INVERSE_SCALE;
		gravity /= QODOT_INVERSE_SCALE;
		jumpForce /= QODOT_INVERSE_SCALE;
		maxStepHeight /= QODOT_INVERSE_SCALE;
		CrouchHeight /= QODOT_INVERSE_SCALE;
	}

	internal bool TestGround(PhysicsDirectBodyState state)
	{
		if (state.GetContactCount() == 0) { return false; }
		if (velocity.y > 0.0f) { return false; }

		for (int i = 0; i < state.GetContactCount(); i++)
		{
			Vector3 surfNormal = state.GetContactLocalNormal(i);
			
			float angle = Mathf.Rad2Deg(Mathf.Acos(surfNormal.Dot(Vector3.Up) / surfNormal.Length()));
			if (angle < maxWalkAngle) { floorY = state.GetContactColliderPosition(i).y; return true; }
		}
		floorY = Mathf.NaN;
		return false;
	}

	public override void _IntegrateForces(PhysicsDirectBodyState state)
	{
		movementStates.ProcessStateTransitions();
		groundedStates.ProcessStateTransitions();
		movementStates.UpdateStates(state.Step);
		groundedStates.UpdateStates(state.Step);

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
			if (groundedStates.IsInState<Air>()) return 0;
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
				// Don't attempt a teleport if we are detecting the same floor we are already standing on.
				float hitY = ((Vector3)hitResult["point"]).y;
				if (Mathf.Abs(hitY - floorY) < (0.1 / QODOT_INVERSE_SCALE)) return 0;
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
				if (groundedStates.IsInState<Ground>()) AxisLockLinearY = true;
			}
		}

		return 0;
	}

	void CalcWishDir()
	{
		wishDir = new Vector3();
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
		if (groundedStates.IsInState<Air>()) return false;
		
		var space = GetWorld().DirectSpaceState;
		// Source for 16 hammer units: https://www.jwchong.com/hl/movement.html#edgefriction
		Vector3 traceBeginPos = GlobalTransform.origin + GetVec2D(velocity).Normalized() * 16.0f / QODOT_INVERSE_SCALE;
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
		// Scale wishDir by our requested movement speed (cl_forwardspeed)
		Vector3 accel = wishDir.Normalized() * currentMaxGroundSpeed;
		// Clamp to the maxspeed to prevent input directly contributing to speeds greater than maxspeed.
		accel = accel.Normalized() * Mathf.Clamp(accel.Length(), 0, maxSpeed);

		// Apply friction if necessary.
		if (ShouldApplyEdgeFriction(state.Step)) { currentEdgeFriction = edgeFrictionMult; } else { currentEdgeFriction = 1.0f; }
		if (groundedStates.IsInState<Ground>()) ApplyFriction(state.Step);

		// www.adrianb.io/2015/02/14/bunnyhop.html
		float projVel = velocity.Dot(accel.Normalized());
		// Clamp our requested movement speed in the air to account for the lack of friction. 
		float clampedSpeed = groundedStates.IsInState<Ground>() ? accel.Length() : (accel.Normalized() * Mathf.Clamp(accel.Length(), 0, maxAirSpeed)).Length();
		float additionalVel = clampedSpeed - projVel;

		// Don't add velocity if velocity has ended up negative, or it will slow us down, which is exclusively the job of friction.
		if (additionalVel >= 0)
		{
			// www.projectborealis.com/movement.html
			accel *= maxAccel * state.Step;
			accel = accel.Normalized() * Mathf.Clamp(accel.Length(), 0, additionalVel);
			velocity += accel;
		}

		// Test for wall collisions that would change our velocity.
		CalcWallVelSlide(ref velocity, state);
		// Check for slopes. Modulate velocity by the amount we had to teleport, if any.
		float amtMoved = AttemptStep(velocity, state);

		// Apply gravity after we've determined our horz trajectory.
		if (groundedStates.IsInState<Air>()) ApplyGravity(state.Step);

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
			DebugDraw.DrawArrowRay3D(rayOrigin, GetVec2D(velocity).Normalized(), GetVec2D(velocity).Length() / 5, new Color(0, 255, 0));

			DebugDraw.TextBackgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.1f);
			DebugDraw.TextForegroundColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);
			DebugDraw.BeginTextGroup("Player Information");
			DebugDraw.SetText("Real Horz Velocity:", GetVec2D(velocity), 0);
			DebugDraw.SetText("Real Horz speed:", GetVec2D(velocity).Length(), 1);
			DebugDraw.SetText("Movement State Stack:", movementStates.GetStateStackAsString(), 3);
			DebugDraw.SetText("Grounded State Stack:", groundedStates.GetStateStackAsString(), 4);
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
		// When using autojump, there's a small change the player will still be grounded next frame,
		// and IsActionPressed will return true, attempting to jump the player twice and doubling
		// vertical velocity as a result. This is a simple hack to avoid that. 
		bool stillAttemptingJump = false;
		public override Transition GetTransition()
		{
			if (!Owner.TestGround(Owner.physBodyState))
			{
				return Transition.Sibling<Air>();
			}

			return Transition.None();
		}

		public override void Update(float aDeltaTime)
		{
			bool didRequestJump = Owner.autojump ? Input.IsActionPressed("Jump") : Input.IsActionJustPressed("Jump");
			if (didRequestJump && !stillAttemptingJump)
			{
				stillAttemptingJump=true;
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
			if (Owner.groundedStates.IsInState<Jump>()) { Owner.OnPlayerJumpApex(); }
			Owner.OnPlayerBeginFalling();
		}
	}

	class Walk : StateWithOwner<PlayerMovee>
	{
		public override void OnEnter()
		{
			Owner.currentMaxGroundSpeed = Owner.maxWalkSpeed;
		}

		public override Transition GetTransition()
		{
			if (Input.IsActionJustPressed("Crouch")) { return Transition.Sibling<CrouchIn>(); }

			return Transition.None();
		}
	}

	class CrouchIn : StateWithOwner<PlayerMovee>
	{
		SceneTreeTimer timer;
		float startHeight;
		bool firstFrame = true;
		public override Transition GetTransition()
		{
			if (timer.TimeLeft <= 0) { return Transition.Sibling<Crouch>(); }
			if (Input.IsActionJustPressed("Crouch") && !firstFrame)
			{
				return Transition.Sibling<CrouchOut>(false);
			}
			
			return Transition.None();
		}

		public override void OnEnter()
		{
			timer = Owner.GetTree().CreateTimer(Owner.CrouchTime);
			startHeight = ((CylinderShape)Owner.collider.Shape).Height;
			Owner.currentMaxGroundSpeed = Owner.maxCrouchSpeed;
		}

		public override void Update(float aDeltaTime)
		{
			float ass = Mathf.Lerp(startHeight, Owner.CrouchHeight, Utils.Normalize(0.0f, Owner.CrouchTime, -(timer.TimeLeft - Owner.CrouchTime)));
			float deltaHeightChange = Mathf.Abs(((CylinderShape)Owner.collider.Shape).Height - ass);
			((CylinderShape)Owner.collider.Shape).Height = ass;

			// When grounded, "snap" the player to the ground so that they aren't repeatedly "Falling" each time
			// we decrease the collider size.
			if (Owner.groundedStates.IsInState<Ground>())
			{
				Transform bab = Owner.physBodyState.Transform;
				bab.origin = new Vector3(bab.origin.x, bab.origin.y - (deltaHeightChange / 2), bab.origin.z);
				Owner.physBodyState.Transform = bab;
			}
			Owner.camRef.SetEyePos(0 - ((CylinderShape)Owner.collider.Shape).Height / 2 + ((CylinderShape)Owner.collider.Shape).Height - Owner.camRef.EyeHeightDistanceFromTop);
			firstFrame = false;
		}
	}

	class CrouchOut : StateWithOwner<PlayerMovee>
	{
		SceneTreeTimer timer;
		float startHeight;
		bool firstFrame = true;

		public override Transition GetTransition()
		{
			if (timer.TimeLeft <= 0) { return Transition.Sibling<Walk>(); }
			if (Input.IsActionJustPressed("Crouch") && !firstFrame)
			{
				return Transition.Sibling<CrouchIn>();
			}
			return Transition.None();
		}
		public override void OnEnter(object[] aArgs)
		{
			// TODO: Sprinting
			bool sprintTransition = (bool)aArgs[0];
			timer = Owner.GetTree().CreateTimer(Owner.UncrouchTime);
			startHeight = ((CylinderShape)Owner.collider.Shape).Height;
			Owner.currentMaxGroundSpeed = Owner.maxWalkSpeed;
		}

		public override void Update(float aDeltaTime)
		{
			float colliderAdjustAmt = Mathf.Lerp(startHeight, 2.25f, Utils.Normalize(0.0f, Owner.UncrouchTime, -(timer.TimeLeft - Owner.UncrouchTime)));
			float deltaHeightChange = Mathf.Abs(((CylinderShape)Owner.collider.Shape).Height - colliderAdjustAmt);
			((CylinderShape)Owner.collider.Shape).Height = colliderAdjustAmt;

			// When grounded, "snap" the player to the ground so that they aren't repeatedly "Falling" each time
			// we decrease the collider size.
			if (Owner.groundedStates.IsInState<Ground>())
			{
				Transform bab = Owner.physBodyState.Transform;
				bab.origin = new Vector3(bab.origin.x, bab.origin.y + (deltaHeightChange / 2), bab.origin.z);
				Owner.physBodyState.Transform = bab;
			}

			Owner.camRef.SetEyePos(0 - ((CylinderShape)Owner.collider.Shape).Height / 2 + ((CylinderShape)Owner.collider.Shape).Height - Owner.camRef.EyeHeightDistanceFromTop);
			firstFrame = false;
		}
	}

	class Crouch : StateWithOwner<PlayerMovee>
	{
		public override Transition GetTransition()
		{
			if (Input.IsActionJustPressed("Crouch")) { return Transition.Sibling<CrouchOut>(false); }

			return Transition.None();
		}
	}
}
