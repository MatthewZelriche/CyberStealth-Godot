using Godot;
using System;

public class DevConsole : Node
{
    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {    
        // Player autojump
        (((GetNode("/root/Console").Call("add_command", "playerAutoJump", this, nameof(playerAutoJump)) as Godot.Object)
		.Call("set_description", "Enable to automatically jump while holding down space. Results in easier bunnyhopping.") as Godot.Object)
		.Call("add_argument", "enable", Variant.Type.Bool) as Godot.Object)
		.Call("register");

        // Draw player debug info.
        (((GetNode("/root/Console").Call("add_command", "playerDrawDebug", this, nameof(playerDrawDebug)) as Godot.Object)
        .Call("set_description", "Renders some debug information relating to the player.") as Godot.Object)
        .Call("add_argument", "enable", Variant.Type.Bool) as Godot.Object)
        .Call("register");
    }

    private void playerAutoJump(bool enable)
    {
        GetTree().CurrentScene.GetNode<PlayerMovee>("Player").autojump = enable;
    }

    private void playerDrawDebug(bool enable)
    {
        GetTree().CurrentScene.GetNode<PlayerMovee>("Player").drawDebug = enable;
    }

    //  // Called every frame. 'delta' is the elapsed time since the previous frame.
    //  public override void _Process(float delta)
    //  {
    //      
    //  }
}
