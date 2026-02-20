using Godot;
using System;
public partial class TestGif : Node {
    public override void _Ready() {
        var res = GD.Load("res://assets/Title_BG_1.gif");
        GD.Print(res != null ? res.GetType().Name : "null");
        GetTree().Quit();
    }
}
