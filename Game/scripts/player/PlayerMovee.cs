using Godot;
using Godot.Collections;
using Hsm;

public class PlayerMovee : RigidBody
{
	const float QODOT_INVERSE_SCALE = 32.0f;

	[Export]
	// The top speed, in units, that a player may initiate without movement tricks (bhopping, etc)
	// See: sv_maxspeed. Default: 320 hammer units
	protected float maxSpeed = 320.0f;
	[Export]
	// The speed at which the player attempts to move while walking.
	// See: Similar to cl_forwardspeed. Default: 400 hammer units
	protected float maxWalkSpeed = 400.0f;
	[Export]
	// The top speed, in hammer units, that a player may initiate while crouched.
	// See: https://www.jwchong.com/hl/duckjump.html May not be entirely accurate because HL1 uses cl_Forwardspeed
	// in this calculation, which I do not. But the rationale is 400 * 0.333 = 133.2.
	protected float maxCrouchSpeed = 133.2f;
	[Export]
	// The top speed, in hammer units, that a player may initiate while in the air. 
	// Dramatically reduced to account for zero friction being applied to the player while they are in the air.
	// Default: 30 hammer units (projectborealis.com/movement.html) (Can't find info on a hl1 default)
	protected float maxAirSpeed = 30.0f;
	[Export]
	// How much friction is applied to decelerating the player. A measure of how "slippery" surfaces will feel.
	// See: sv_friction. Default: 4.0
	protected float friction = 4.0f;
	[Export]
	// How much the friction should be multiplied by when the player is near a steep ledge.
	// A value of 1.0 disables edge friction.
	// Default: 2.0;
	protected float edgeFrictionMult = 2.0f;
	[Export]
	// A measure of how quickly a player accelerates per physics step.
	// See: sv_accelerate. Default: 10
	protected float maxAccel = 10.0f;
	[Export]
	// A modifier to control how quickly the player decelerates to a stop, in combination with friction.
	// See: sv_stopspeed. Default: 100
	protected float stopSpeed = 100.0f;
	[Export]
	// Maximum speed of gravity force, in hammer units per second.
	protected float gravity = -800.0f;
	[Export]
	// How much upward velocity, in hammer units, is instantly applied to the player on jump.
	// See: www.jwchong.com/hl/duckjump.html#jumping
	protected float jumpForce = 268.3f;
	[Export]
	// The maximum slope angle, in degrees, that a player may walk on without sliding off.
	protected float maxWalkAngle = 45.573f;
	[Export]
	// The maximum height, in hammer units, a player can smoothly step up onto without jumping.
	// Default: 18.0
	protected float maxStepHeight = 18.0f;
	[Export]
	// The height, in hammer units, of the player's collider while they are in the crouch state.
	// Default: 36.0
	protected float CrouchHeight = 36.0f;
	[Export]
	// How long, in seconds, that the crouching transition lasts.
	// Default: 0.4. See: www.jwchong.com/hl/duckjump.html#id11
	protected float CrouchTime = 0.4f;
	[Export]
	// How long, in seconds, that the uncrouching transition lasts.
	// Default: ?? Much quicker than the 0.4 crouch transition, however.
	protected float UncrouchTime = 0.1f;

	// TODO: Expose to console.
	protected bool drawDebug = true;
	protected bool autojump = true;

	private Vector3 velocity;
	private Vector3 wishDir;

	private float currentMaxGroundSpeed;
	private float currentEdgeFriction = 1.0f;
	private float floorY = 0.0f;
	private float colliderMargin;
	private bool noFrictionThisFrame = false;

	private CameraController camRef;
	private CollisionShape collider;
	private PhysicsDirectBodyState physBodyState;
	private StateMachine movementStates;
	private StateMachine groundedStates;
	
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

		// Convert hammer units to godot units according to our inverse scale.
		maxSpeed /= QODOT_INVERSE_SCALE;
		maxWalkSpeed /= QODOT_INVERSE_SCALE;
		maxCrouchSpeed /= QODOT_INVERSE_SCALE;
		maxAirSpeed /= QODOT_INVERSE_SCALE;
		stopSpeed /= QODOT_INVERSE_SCALE;
		gravity /= QODOT_INVERSE_SCALE;
		jumpForce /= QODOT_INVERSE_SCALE;
		maxStepHeight /= QODOT_INVERSE_SCALE;
		CrouchHeight /= QODOT_INVERSE_SCALE;

		// My current understanding of how this works - Both the floor and the ceiling contribute a margin
		// of 0.02f, while the player capsule contributes a margin of 0.04f. So we subtract this from
		// our crouch height to allow the collider to move under 36 unit tall spaces.
		CrouchHeight -= 0.08f;
	}

	/**
	 * @brief Checks to see if the player is touching the ground.
	 * 
	 * If the player is on the ground, the member variable floorY also becomes set to the global space 
	 * y-coordinate (world height) of the contact point between the player and the floor.
	 * 
	 * @return True if the player was on the ground this frame, false otherwise.
	 */
	protected bool TestGround()
	{
		if (physBodyState.GetContactCount() == 0) { return false; }
		if (velocity.y > 0.0f) { return false; }

		for (int i = 0; i < physBodyState.GetContactCount(); i++)
		{
			Vector3 surfNormal = physBodyState.GetContactLocalNormal(i);
			
			// Does this contact actually represent a walkable surface?
			float angle = Mathf.Rad2Deg(Mathf.Acos(surfNormal.Dot(Vector3.Up) / surfNormal.Length()));
			if (angle < maxWalkAngle) 
			{ 
				floorY = physBodyState.GetContactColliderPosition(i).y; 
				return true; 
			}
		}
		floorY = Mathf.NaN;
		return false;
	}

	protected bool CanUncrouch()
	{
		// Construct our collider shape to be narrower than we are, since we know the player 
		// must already be fit inside the space they are in. 
		// Construct the collider to also be shorter than the player. We then shift it's pos up so it
		// does not errantly detect the floor.
		CylinderShape traceShape = new CylinderShape();
		traceShape.Radius = ((CylinderShape)collider.Shape).Radius - 0.05f;
		traceShape.Height = ((CylinderShape)collider.Shape).Height - 0.2f;
		Vector3 position = GlobalTransform.origin;
		position.y += 0.2f/2f;
		DebugDraw.DrawCylinder(position, traceShape.Radius, traceShape.Height);

		Dictionary resultsdiscard = new Dictionary();
		return !Utils.TestIntersection(GetWorld().DirectSpaceState, traceShape, position, ref resultsdiscard, new Array(this));
	}

	public override void _IntegrateForces(PhysicsDirectBodyState state)
	{
		movementStates.ProcessStateTransitions();
		groundedStates.ProcessStateTransitions();
		movementStates.UpdateStates(state.Step);
		groundedStates.UpdateStates(state.Step);
		CanUncrouch();
		if (drawDebug && Input.IsKeyPressed((int)KeyList.Capslock)) { DebugDraw.Freeze3DRender = true; }
		DebugDraw.DrawCylinder(GlobalTransform.origin, ((CylinderShape)collider.Shape).Radius, ((CylinderShape)collider.Shape).Height, new Color(0, 0, 0));

		// See: AttemptStep. This is for helping prevent weird RigidBody movement on steep slopes.
		AxisLockLinearY = false;

		CalcWishDir();
		// While we calculate velocity every frame for internal use, 
		// we only send the updated velocity to the physics server if we DIDN'T find a slope/step
		// and teleport the player. Otherwise, we end up with excessive speed as the physics server
		// adds our new velocity on top of the teleport we did.
		state.LinearVelocity = CalcNewVel() ? Vector3.Zero : velocity;
	}

	/**
	 * @brief Checks if there is a slope/step we can move up/down onto.
	 * 
	 * @param moveDelta The amount of velocity we plan to move by during this frame.
	 * 
	 * Performs a series of shapecasts to detect if the velocity this frame will bring
	 * us into contact with world geometry such as slopes or steps, that require teleporting
	 * the player instead of applying velocity to the physics server. If a suitable step/slope is
	 * found, the player is manually teleported by updating their transform.
	 * Similar to the method described in thelowrooms.xyz/articledir/programming_stepclimbing.php
	 * 
	 * @return True if we teleported the player to a new position, false if no suitable step/slope was found.
	 */
	protected bool AttemptStep(Vector3 moveDelta)
	{
		if (velocity == Vector3.Zero) { return false; }
		Array ignoreThis = new Array(this);
		var space = GetWorld().DirectSpaceState;

		CylinderShape traceShape = new CylinderShape();
		traceShape.Radius = ((CylinderShape)collider.Shape).Radius;
		traceShape.Height = ((CylinderShape)collider.Shape).Height;
		Array results = new Array();

		// Cast our collider upward, to see what our ceiling clearance is.
		Utils.TestMotion(space, traceShape, collider.GlobalTransform.origin, Vector3.Up * maxStepHeight, ref results, ignoreThis);
		Vector3 maxUpPos = collider.GlobalTransform.origin + Utils.ScaleVector(Vector3.Up * maxStepHeight, (float)results[0]);
		// If we hit a ceiling, we need to track how far up we moved for later calculations.
		float missingTraverseAmount = Mathf.Abs((maxStepHeight * (float)results[0]) - maxStepHeight);
		results.Clear();

		// Cast forward by the delta velocity this frame.
		// Account for margin, otherwise we can't step up at slow speeds.
		Vector3 forwardMotion = (moveDelta * physBodyState.Step).Normalized() * ((moveDelta * physBodyState.Step).Length() + colliderMargin);
		Utils.TestMotion(space, traceShape, maxUpPos, forwardMotion, ref results, ignoreThis);
		Vector3 maxForwardPos = maxUpPos + Utils.ScaleVector(forwardMotion, (float)results[0]);
		results.Clear();

		// Now that we are at our "predicted position", cast down to see if there's a step or slope.
		Utils.TestMotion(space, traceShape, maxForwardPos, Vector3.Down * (maxStepHeight - missingTraverseAmount), ref results, ignoreThis);
		Vector3 finalPos = maxForwardPos + Utils.ScaleVector(Vector3.Down * (maxStepHeight - missingTraverseAmount), (float)results[0]);

		if (Mathf.IsEqualApprox((float)results[0], 1.0f))
		{
			// Didn't find a place to step...
			results.Clear();
			// Don't try to step down while in the air, it will cause weird snapping.
			if (groundedStates.IsInState<Air>()) return false;
			// Since we didn't find a step up, lets keep going to see if we can find a step down...
			Utils.TestMotion(space, traceShape, finalPos, Vector3.Down * maxStepHeight, ref results, ignoreThis);
			finalPos = finalPos + Utils.ScaleVector(Vector3.Down * maxStepHeight, (float)results[0]);
			// Found a spot to step down.
			// Confirm that what we are stepping onto is within our max slope allowance.
			Dictionary hitResult = new Dictionary();
			Utils.TestIntersection(space, traceShape, finalPos, ref hitResult, ignoreThis);
			if (hitResult.Count == 0) { return false; }
			// Don't attempt a teleport if we are detecting the same floor we are already standing on.
			float hitY = ((Vector3)hitResult["point"]).y;
			if (Mathf.Abs(hitY - floorY) < (0.1f / QODOT_INVERSE_SCALE)) { return false; }
			Vector3 resNormal = (Vector3)hitResult["normal"];
			float angle = Utils.GetVec3Angle(resNormal, Vector3.Up);
			if (angle <= maxWalkAngle)
			{
				// Teleport to the new step/slope pos.
				var oldTransform = physBodyState.Transform;
				oldTransform.origin = finalPos;
				physBodyState.Transform = oldTransform;
				return true;
			}
		} else
		{
			// Found a spot to step up!
			// Confirm that what we are stepping onto is within our max slope allowance.
			Dictionary hitResult = new Dictionary();
			Utils.TestIntersection(space, traceShape, finalPos, ref hitResult, ignoreThis);
			if (hitResult.Count == 0) { return false; }
			Vector3 resNormal = (Vector3)hitResult["normal"];
			float angle = Utils.GetVec3Angle(resNormal, Vector3.Up);
			if (angle <= maxWalkAngle)
			{
				// Teleport to the new step/slope pos.
				var oldTransform = physBodyState.Transform;
				oldTransform.origin = finalPos;
				physBodyState.Transform = oldTransform;
				return true;
			} else
			{
				// Hack to (mostly) prevent bumping up and down steep slopes. Doesn't work all the time (why?)
				if (groundedStates.IsInState<Ground>()) AxisLockLinearY = true;
			}
		}

		return false;
	}

	/** Calculates a unit vector representing the direction the player wishes to move. */
	protected void CalcWishDir()
	{
		wishDir = new Vector3();
		Vector3 forward2D = Utils.GetVec2D(camRef.GetForwardVector());
		Vector3 right2D = Utils.GetVec2D(camRef.GetRightVector());

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

	/**
	 * @brief Checks whether edge friction should be applied this frame.
	 * 
	 * Edge friction increases the amount of ground friction when the player approaches a sharp ledge.
	 */
	protected bool ShouldApplyEdgeFriction()
	{
		if (groundedStates.IsInState<Air>()) return false;
		
		var space = GetWorld().DirectSpaceState;
		// Source for 16 units: https://www.jwchong.com/hl/movement.html#edgefriction
		Vector3 traceBeginPos = GlobalTransform.origin + Utils.GetVec2D(velocity).Normalized() * 16.0f / QODOT_INVERSE_SCALE;
		traceBeginPos = traceBeginPos + Vector3.Down * ((CylinderShape)collider.Shape).Height / 2;
		Array throwaway = new Array();
		Dictionary throwawayDictionary = new Dictionary();
		Array excludeThis = new Array(this);

		if (Utils.TestIntersection(space, collider.Shape, traceBeginPos, ref throwawayDictionary, excludeThis)) { return false; }
		return Utils.TestMotion(space, collider.Shape, traceBeginPos, traceBeginPos + Vector3.Down * ((CylinderShape)collider.Shape).Height / 2, ref throwaway, excludeThis);
	}

	/** Applies ground friction, modulating the player's ground velocity. */
	protected void ApplyFriction()
	{
		// Ensure there exists a single frame after landing where we do not apply friction.
		if (noFrictionThisFrame) {  noFrictionThisFrame = false; return; }
		float lastSpeed = Utils.GetVec2D(velocity).Length();
		if (lastSpeed != 0)
		{
			// stopspeed only scales decel at low speeds.
			// www.jwchong.com/hl/movement.html#ground-friction
			float finalStopSpeedScale = Mathf.Max(stopSpeed, lastSpeed);
			Vector3 decelAmount = Utils.GetVec2D(velocity).Normalized() * friction * currentEdgeFriction * finalStopSpeedScale;
			velocity -= decelAmount * physBodyState.Step;

			// Fix for jittery back-and-forward velocity changes just before stopping.
			var signedDecel = decelAmount.Sign();
			var signedVel = velocity.Sign();
			if (!(Mathf.IsEqualApprox(signedDecel.x, signedVel.x) && Mathf.IsEqualApprox(signedDecel.z, signedVel.z)))
			{
				velocity = Vector3.Zero; return;
			}
		}
	}

	/** Applies a single frame worth of downward gravity acceleration. */
	protected void ApplyGravity()
	{
		velocity.y += (Vector3.Up * gravity * physBodyState.Step).y;
	}

	/**
	 * @brief Calculates whether our velocity vector should be "slid" along a wall normal.
	 * 
	 * @param nextVel The proposed next velocity delta. If we do need to slide our velocity vector,
	 * this vector will be modified as a ref.
	 * 
	 * Godot's rigidbody collision calculations does this for us automatically, but we still
	 * need to approximate it for certain calculations such as for AttemptStep().
	 *
	 * @return True if we modified the proposed velocity, false otherwise.
	 */
	protected bool CalcWallVelSlide(ref Vector3 nextVel)
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
			if (Utils.GetVec3Angle(nextVel, -normal) < 90.0f)
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

	/**
	 * Calculates the final player velocity for this physics step.
	 * 
	 * @return True if the velocity sent to the physics server should be zeroed, false otherwise.
	 */
	protected bool CalcNewVel()
	{
		// Resources for how this movement works:
		// projectborealis.com/movement.html
		// adrianb.io/2015/02/14/bunnyhop.html
		// www.youtube.com/watch?v=v3zT3Z5apaM
		// www.jwchong.com/hl/

		// Scale wishDir by our requested movement speed (similar to cl_forwardspeed)
		Vector3 accel = wishDir.Normalized() * currentMaxGroundSpeed;
		// Clamp to the maxspeed to prevent input directly contributing to speeds greater than maxspeed.
		accel = accel.Normalized() * Mathf.Clamp(accel.Length(), 0, maxSpeed);

		// Apply friction if necessary.
		if (ShouldApplyEdgeFriction()) { currentEdgeFriction = edgeFrictionMult; } else { currentEdgeFriction = 1.0f; }
		if (groundedStates.IsInState<Ground>()) ApplyFriction();

		// www.adrianb.io/2015/02/14/bunnyhop.html
		float projVel = velocity.Dot(accel.Normalized());
		// Clamp our requested movement speed in the air to account for the lack of friction. 
		float clampedSpeed = groundedStates.IsInState<Ground>() ? accel.Length() : (accel.Normalized() * Mathf.Clamp(accel.Length(), 0, maxAirSpeed)).Length();
		float additionalVel = clampedSpeed - projVel;

		// Don't add velocity if velocity has ended up negative, or it will slow us down, which is exclusively the job of friction.
		if (additionalVel >= 0)
		{
			// www.projectborealis.com/movement.html
			accel *= maxAccel * physBodyState.Step;
			accel = accel.Normalized() * Mathf.Clamp(accel.Length(), 0, additionalVel);
			velocity += accel;
		}

		// Test for wall collisions that would change our velocity direction.
		CalcWallVelSlide(ref velocity);
		// Check for slopes. If we do teleport as a result of a discovered slope, we should hold onto
		// out calculated velocity for this frame (since we instantly moved by that amount in AttemptStep()),
		// but we should not post this velocity info to the physics server for this frame, to avoid doubling 
		// our speed.
		bool shouldCancelVel = AttemptStep(velocity);

		// Apply gravity if we are airborne, after we've determined our horz velocity.
		if (groundedStates.IsInState<Air>()) ApplyGravity();

		// Return whether we should actually post this velocity to the physics server (if we didn't manually teleport the player).
		return shouldCancelVel;
	}

	public override void _Process(float delta)
	{
		// Note: Currently has a rendering issue with qodot, causes flickering.
		if (drawDebug)
		{
			Vector3 rayOrigin = camRef.GlobalTransform.origin;
			rayOrigin.y -=  0.75f;
			DebugDraw.DrawArrowRay3D(rayOrigin, wishDir.Normalized(), 1.25f, new Color(255, 0, 0));
			DebugDraw.DrawArrowRay3D(rayOrigin, Utils.GetVec2D(velocity).Normalized(), Utils.GetVec2D(velocity).Length() / 5, new Color(0, 255, 0));

			DebugDraw.TextBackgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.1f);
			DebugDraw.TextForegroundColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);
			DebugDraw.BeginTextGroup("Player Information");
			DebugDraw.SetText("Real Horz Velocity:", Utils.GetVec2D(velocity), 0);
			DebugDraw.SetText("Real Horz speed:", Utils.GetVec2D(velocity).Length(), 1);
			DebugDraw.SetText("Movement State Stack:", movementStates.GetStateStackAsString(), 3);
			DebugDraw.SetText("Grounded State Stack:", groundedStates.GetStateStackAsString(), 4);
			DebugDraw.EndTextGroup();
		}
	}

	/** Called as soon as the player transitions from the Air state to the Ground state. */
	protected virtual void OnPlayerLanded()
	{
		// Treat a single frame on the ground as if we are still in the air
		// See: adrianb.io/2015/02/14/bunnyhop.html
		noFrictionThisFrame = true;
		// Cancel out any gravity we were experiencing now that we are grounded, otherwise 
		// we will be moving into the floor at an angle, slowing us down.
		velocity.y = 0;
	}

	/** Called on the frame that the player initiates a jump */
	protected virtual void OnBeginJump()
	{
		velocity.y += jumpForce;
	}

	/** Called at the very top of a player jump. */
	protected virtual void OnPlayerJumpApex()
	{
	}

	/** Called when the player starts to fall. That is, the first frame that their velocity begins decreasing,
	 *  either from a jump or because they stepped off something. */
	protected virtual void OnPlayerBeginFalling()
	{
	}

	/* Movement states for the state machines */
	class Ground : StateWithOwner<PlayerMovee>
	{
		// When using autojump, there's a small chance the player will still be grounded next frame,
		// and IsActionPressed will return true, attempting to jump the player twice and doubling
		// vertical velocity as a result. This is a simple hack to avoid that. 
		bool stillAttemptingJump = false;
		public override Transition GetTransition()
		{
			if (!Owner.TestGround())
			{
				return Transition.Sibling<Air>();
			}

			return Transition.None();
		}

		public override void Update(float aDeltaTime)
		{
			// Check every frame while we are on the ground if the player is initiating a jump.
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
			if (Owner.TestGround())
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
			if (Input.IsActionJustPressed("Crouch") && !firstFrame && Owner.CanUncrouch())
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

		public override void OnExit()
		{
			((CylinderShape)Owner.collider.Shape).Height = Owner.CrouchHeight;
			Transform bab = Owner.physBodyState.Transform;
			bab.origin = new Vector3(bab.origin.x, Owner.floorY + Owner.CrouchHeight/2, bab.origin.z);
			Owner.physBodyState.Transform = bab;
			GD.Print(Owner.physBodyState.Transform.origin);
		}

		public override void Update(float aDeltaTime)
		{
			// Smoothly change the collider height.
			float colliderAdjustAmt = Mathf.Lerp(startHeight, Owner.CrouchHeight, Utils.Normalize(0.0f, Owner.CrouchTime, -(timer.TimeLeft - Owner.CrouchTime)));
			float deltaHeightChange = Mathf.Abs(((CylinderShape)Owner.collider.Shape).Height - colliderAdjustAmt);
			((CylinderShape)Owner.collider.Shape).Height = colliderAdjustAmt;

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
			// Smoothly adjust collider height.
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
			if (Input.IsActionJustPressed("Crouch") && Owner.CanUncrouch()) { return Transition.Sibling<CrouchOut>(false); }

			return Transition.None();
		}
	}
}
