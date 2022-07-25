using Godot;
using Godot.Collections;

public class PlayerMovee : RigidBody
{
	[Export]
	// The top speed, in meters per second, that a player may initiate without movement tricks (bhopping, etc)
	// See: sv_maxspeed
	private float maxWalkSpeed = 10.0f;
	[Export]
	// How much friction is applied to decelerating the player. A measure of how "slippery" surfaces will feel.
	// See: sv_friction
	private float friction = 4.0f;
	[Export]
	// The maximum amount a player can accelerate in a single physics step.
	// See: sv_accelerate
	private float maxAccel = 10.0f;
	[Export]
	// A modifier to control how quickly the player decelerates to a stop at low speeds, in combination with friction.
	// See: sv_stopspeed
	private float stopSpeed = 1.1905f;
	[Export]
	// Speed of constant gravity force, in meters per second.
	private float gravity = -12.0f;
	[Export]
	// A limit on how steep a slope can be before a player cannot traverse it. 
	private float maxWalkAngle = 45.0f;
	[Export]
	// The maximum height a player can smoothly step up onto without jumping.
	private float maxStepHeight = 0.75f;

	[Export]
	private float horzSens = 0.5f;
	[Export]
	private float vertSens = 0.5f;

	// TODO: Expose to console.
	bool drawDebug = true;

	private Vector3 velocity;
	private Vector3 wishDir;
	private Camera camRef;
	private float pitch = 0.0f;
	private float yaw = 0.0f;

	CollisionShape collider;
	SpatialVelocityTracker velTracker = new SpatialVelocityTracker();

	bool isGrounded;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		collider = GetNode<CollisionShape>("CollisionShape");
		GetNode<Spatial>("ForwardHint").Visible = false;
		Input.SetMouseMode(Input.MouseMode.Captured);

		camRef = GetNode<Camera>("Camera");
		maxAccel = maxAccel * maxWalkSpeed;
	}

	void TestGround(PhysicsDirectBodyState state)
	{
		// TODO: Consider using get_rest_state instead.
		if (state.GetContactCount() == 0) { isGrounded = false; }

		for (int i = 0; i < state.GetContactCount(); i++)
		{
			Vector3 surfNormal = state.GetContactLocalNormal(i);

			float angle = Mathf.Rad2Deg(Mathf.Acos(surfNormal.Dot(Vector3.Up) / surfNormal.Length()));
			if (angle < maxWalkAngle)
			{
				isGrounded = true;
				break;
			}
			else
			{
				isGrounded = false;

			}
		}
	}

	void _integrate_forces(PhysicsDirectBodyState state)
	{
		TestGround(state);
		CalcWishDir();
		// While we calculate velocity every frame for internal use, 
		// we only send the updated velocity to the physics server if we DIDN'T find a slope/step
		// and teleport the player. Otherwise, we end up with excessive speed as the physics server
		// adds our new velocity on top of the teleport we did.
		if (!CalcNewVel(state)) {
			state.LinearVelocity = Vector3.Zero;
		} else
		{
			state.LinearVelocity = velocity;
		}
		velTracker.UpdatePosition(GlobalTransform.origin);
	}

	float CheckSlope(Vector3 moveDelta, PhysicsDirectBodyState state)
	{
		if (velocity == Vector3.Zero) { return 0; }
		
		var space = GetWorld().DirectSpaceState;
		PhysicsShapeQueryParameters query = new PhysicsShapeQueryParameters();
		// Set up our initial query to overlap our real collider.
		CylinderShape traceShape = ((CylinderShape)collider.Shape);
		query.SetShape(traceShape);
		query.Exclude = new Array(this);
		query.Transform = collider.GlobalTransform;

		// First, get the highest we can move up our step height without hitting a ceiling.
		var res = space.CastMotion(query, Vector3.Up * maxStepHeight);
		var temp = query.Transform;
		float traverseAmt = maxStepHeight * (float)res[0];
		temp.origin = new Vector3(temp.origin.x, temp.origin.y + traverseAmt, temp.origin.z);
		query.Transform = temp;
		DebugDraw.DrawCylinder(query.Transform.origin, traceShape.Radius, traceShape.Height);

		// If we hit a ceiling, we need to track how far up we moved for later calculations.
		float missingTraverseAmount = Mathf.Abs(traverseAmt - maxStepHeight);

		// Second, move horizontally along the velocity direction to "test" whether we will collide 
		// for our next simulation step.
		res = space.CastMotion(query, moveDelta * state.Step);
		temp = query.Transform;
		temp.origin = query.Transform.origin + (moveDelta * state.Step * (float)res[0]);
		query.Transform = temp;

		// Next, we cast down to see if we actually find a step we can move up onto.
		res = space.CastMotion(query, Vector3.Down * (maxStepHeight - missingTraverseAmount));
		temp = query.Transform;
		temp.origin = new Vector3(temp.origin.x, temp.origin.y - ((maxStepHeight - missingTraverseAmount) * (float)res[0]), temp.origin.z);
		query.Transform = temp;

		if (Mathf.IsEqualApprox((float)res[0], 1.0f))
		{
			// We didn't find an up step...Keep checking for a down step.
			// TODO
			return 0;
		} else
		{
			// Found a spot to step up!
			var oldTransform = state.Transform;
			Vector3 oldOrigin = oldTransform.origin;
			oldTransform.origin = query.Transform.origin;
			state.Transform = oldTransform;
			return (state.Transform.origin - oldOrigin).Length();
		}
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
		float lastSpeed = GetVec2D(velocity).Length();
		// Zero out small float values to ensure we come to a complete stop.
		if (lastSpeed <= 0.05)
		{
			velocity = Vector3.Zero;
		}
		if (lastSpeed != 0)
		{
			// stopspeed only scales decel at low speeds.
			float finalStopSpeedScale = Mathf.Max(stopSpeed, lastSpeed);
			Vector3 decelAmount = GetVec2D(velocity).Normalized() * friction * finalStopSpeedScale;
			velocity -= decelAmount * physDelta;
		}
	}

	void ApplyGravity()
	{
		// TODO: Currently a constant force. Should gravity be an accelerating force?
		velocity.y = (Vector3.Up * gravity).y;
	}

	bool CalcNewVel(PhysicsDirectBodyState state)
	{
		if (isGrounded)
		{
			// Friction is apparently not applied while airborne in quake-style movement systems
			// TODO: Should maxspeed be capped while moving in the air? How do other games do it?
			ApplyFriction(state.Step);
		} else
		{
			// When the user is grounded, gravity doesn't exist.
			// Helps avoid some issues with slopes, and applying constant
			// gravity while on the ground seems redundant anyway.
			ApplyGravity();
		}

		// Clamp velocity based on dot product of wishdir and current velocity, because that's how quake did it
		// for some reason. See: https://www.youtube.com/watch?v=v3zT3Z5apaM
		float curSpeed = velocity.Dot(wishDir);
		float addSpeed = Mathf.Clamp(maxWalkSpeed - curSpeed, 0, maxAccel * state.Step);

		// Increase our vel based on addSpeed in the wishdir.
		Vector3 predictedNextVel = velocity + addSpeed * wishDir;

		// Check for slopes. Modulate velocity by the amount we had to teleport, if any.
		float amtMoved = CheckSlope(predictedNextVel, state);
		velocity = predictedNextVel.Normalized() * (predictedNextVel.Length() - amtMoved);

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
		if (Input.IsKeyPressed((int)KeyList.Capslock))
		{
			DebugDraw.Freeze3DRender = true;
		}
		
		if (drawDebug)
		{
			DebugDraw.DrawArrowRay3D(GlobalTransform.origin, wishDir.Normalized(), 1.25f, new Color(255, 0, 0));
			DebugDraw.DrawArrowRay3D(GlobalTransform.origin, velocity.Normalized(), velocity.Length() / 5, new Color(0, 255, 0));

			DebugDraw.TextBackgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.1f);
			DebugDraw.TextForegroundColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);
			DebugDraw.BeginTextGroup("Player Information");
			DebugDraw.SetText("Real Horz Velocity:", GetVec2D(velTracker.GetTrackedLinearVelocity()), 0);
			DebugDraw.SetText("Real Horz speed:", GetVec2D(velTracker.GetTrackedLinearVelocity()).Length(), 1);
			DebugDraw.SetText("Is Grounded: ", isGrounded, 2);
			DebugDraw.EndTextGroup();
		}
	}

	Vector3 GetVec2D(Vector3 inVec)
	{
		return new Vector3(inVec.x, 0, inVec.z);
	}
}
