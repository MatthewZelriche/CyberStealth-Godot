using Godot;
using System;

public class CameraController : Camera
{
	const float QODOT_INVERSE_SCALE = 32.0f;

	[Export]
	private float horzSens = 0.4f;
	[Export]
	private float vertSens = 0.4f;
	[Export]
	// How many hammer units between the top of the player collider, and the camera position. 
	// In HL this distance is always 8.0 hammer units, regardless of crouching/standing.
	private float eyeHeightDistanceFromTop = 8.0f;

	public float EyeHeightDistanceFromTop { get => eyeHeightDistanceFromTop; }

	private float pitch = 0.0f;
	private float yaw = 0.0f;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		eyeHeightDistanceFromTop /= QODOT_INVERSE_SCALE;
	}

	public Vector3 GetForwardVector()
    {
		return GlobalTransform.basis.z;
	}

	public Vector3 GetRightVector()
	{
		return GlobalTransform.basis.x;
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

			Vector3 cameraRotDeg = RotationDegrees;
			cameraRotDeg.y = yaw;
			cameraRotDeg.x = pitch;
			RotationDegrees = cameraRotDeg;
		}
	}

	public void SetEyePos(float eyeHeight)
    {
		var transform = Transform;
		transform.origin = new Vector3(Transform.origin.x, eyeHeight, Transform.origin.z);
		Transform = transform;
	}
}
