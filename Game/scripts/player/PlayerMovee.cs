using Godot;

public class PlayerMovee : Spatial
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
	private float gravity = 12.0f;
	[Export]
	private float maxWalkAngle = 45.0f;

	[Export]
	private float horzSens = 0.5f;
	[Export]
	private float vertSens = 0.5f;

	private Vector3 velocity;
	private Camera camRef;
	private float pitch = 0.0f;
	private float yaw = 0.0f;
	float realSpeed = 0.0f;

	bool isGrounded;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		GetNode<Spatial>("ForwardHint").Visible = false;
		Input.SetMouseMode(Input.MouseMode.Captured);

		camRef = GetNode<Camera>("Camera");
		maxAccel = maxAccel * maxWalkSpeed;
	}

	void _integrate_forces(PhysicsDirectBodyState state)
	{
		// Check if player is grounded.
		if (state.GetContactCount() == 0)
		{
			isGrounded = false;
		}
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

		Vector3 wishDir = CalcWishDir();
		CalcNewVel(wishDir, state.Step);
		state.LinearVelocity = velocity;
	}

	Vector3 CalcWishDir()
	{
		Vector3 wishDir = new Vector3();
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
		return wishDir;
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
		velocity.y = (Vector3.Down * gravity).y;
	}

	void CalcNewVel(Vector3 wishDir, float physDelta)
	{
		if (isGrounded)
		{
			ApplyFriction(physDelta);
		}
		ApplyGravity();

		// Clamp velocity based on dot product of wishdir and current velocity, because that's how quake did it
		// for some reason. See: https://www.youtube.com/watch?v=v3zT3Z5apaM
		float curSpeed = velocity.Dot(GetVec2D(wishDir));
		float addSpeed = Mathf.Clamp(maxWalkSpeed - curSpeed, 0, maxAccel * physDelta);

		// Increase our vel based on addSpeed in the wishdir.
		velocity = velocity + addSpeed * wishDir;
		realSpeed = GetVec2D(velocity).Length();
		//GD.Print(realSpeed);
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

	Vector3 GetVec2D(Vector3 inVec)
	{
		return new Vector3(inVec.x, 0, inVec.z);
	}
}
