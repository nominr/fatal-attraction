using Godot;
using System;

// Ensure partial class matches filename
public partial class TestRunner : Node
{
	public override void _Ready()
	{
		GD.Print("TestRunner starting...");
		
		// Load GameWorld directly
		var gameWorldScene = GD.Load<PackedScene>("res://scenes/GameWorld.tscn");
		if (gameWorldScene == null)
		{
			GD.PrintErr("Failed to load GameWorld.tscn");
			return;
		}

		var gameWorld = gameWorldScene.Instantiate<GameWorld>();
		AddChild(gameWorld);
		
		// Wait a bit for _Ready
		var timer = GetTree().CreateTimer(0.5);
		timer.Timeout += () => 
		{
			GD.Print("[TestRunner] Simulating NPC Interaction...");
			
			// Find an NPC entity
			// We need to iterate over GameWorld children
			foreach(var child in gameWorld.GetChildren())
			{
				if (child is NPCEntity npc)
				{
					GD.Print($"[TestRunner] Found NPC: {npc.NpcId}");
					
					var connections = npc.GetSignalConnectionList(NPCEntity.SignalName.NPCClicked);
					GD.Print($"[TestRunner] Connection Count for NPCClicked: {connections.Count}");
					foreach(var conn in connections)
					{
						GD.Print($"[TestRunner] Connected to: {conn["callable"].AsCallable().Target}");
					}

					// Trigger the signal manually since we can't click physically
					// Use CallDeferred to ensure safe execution
					npc.EmitSignal(NPCEntity.SignalName.NPCClicked, npc.NpcId);
					GD.Print($"[TestRunner] Emitted NPCClicked for {npc.NpcId}");
					break; 
				}
			}
			
			// Wait for logs then quit
			var quitTimer = GetTree().CreateTimer(2.0);
			quitTimer.Timeout += () => 
			{
				GD.Print("[TestRunner] Test Complete. Quitting.");
				GetTree().Quit();
			};
		};
	}
}
