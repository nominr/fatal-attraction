using Godot;
using System;

public partial class DebugRooms : Node
{
	public override void _Ready()
	{
		var gameWorld = GetNode("/root/GameWorld");
		if (gameWorld == null)
		{
			GD.Print("GameWorld not found");
			return;
		}

		string[] roomNames = { "Room1", "Room2", "Room3", "Room4", "Room5", "Hallways" };
		foreach (var name in roomNames)
		{
			var area = gameWorld.GetNodeOrNull<Area2D>(name);
			if (area != null)
			{
				foreach (var child in area.GetChildren())
				{
					if (child is CollisionShape2D shape)
					{
						GD.Print($"[DEBUG_ROOM] {name} GlobalPos: {shape.GlobalPosition} Rect: {shape.Shape.GetRect()} AreaScale: {area.Scale} GlobalScale: {shape.GlobalScale}");
					}
				}
			}
		}
		GetTree().Quit();
	}
}
