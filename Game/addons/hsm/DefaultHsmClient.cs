using Godot;

namespace Hsm
{
    public static partial class Client
    {
        public static void Log(StateMachine aStateMachine, string aMessage)
        {
            GD.Print(aMessage);
        }

        public static void LogError(StateMachine aStateMachine, string aMessage)
        {
            GD.PushError(aMessage);
        }
    }
}
