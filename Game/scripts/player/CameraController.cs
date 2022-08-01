using Godot;
using System;

public class CameraController : Camera
{
	[Export]
	private float horzSens = 0.4f;
	[Export]
	private float vertSens = 0.4f;

	private float pitch = 0.0f;
	private float yaw = 0.0f;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		SetAsToplevel(true);
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

	public void SetWorldPosition(float x, float y, float z)
    {
		var transform = GlobalTransform;
		transform.origin = new Vector3(x, y, z);
		GlobalTransform = transform;
	}
}
