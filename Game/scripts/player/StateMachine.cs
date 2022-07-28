using System;
using System.Collections.Generic;
using Godot;

public enum MovementStates
{
	Ground,
		Landed,
		Walk,
		Sprint,

	Air,
		Jump,
		Fall,
}

public class StateMachine
{
	Stack<MovementStates> states = new Stack<MovementStates>();

	public void PushState(MovementStates newState)
    {
		if ((states.Count == 0) || (!ContainsState(newState)))
        {
			states.Push(newState);
		}
    }

	public void PopState()
    {
		states.Pop();
    }

	public MovementStates GetCurrentState()
    {
		return states.Peek();
    }

	public bool ContainsState(MovementStates state)
    {
		return states.Contains(state);
    }

	public void RemoveHierarchy(MovementStates state)
    {
		while (ContainsState(state))
        {
			PopState();
        }
    }

	public override String ToString()
    {
		string output = "";

		Array arr = states.ToArray();

		Array.Reverse(arr);
		foreach (MovementStates state in arr)
        {
			output += state.ToString();
			output += " -> ";
        }
		output = output.Remove(output.Length - 4);
		return output;
    }
}
