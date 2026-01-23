using Godot;
using System;

/// <summary>
/// Node2D-based NPC entity that players can approach and interact with.
/// Handles visual representation and click detection for NPC interactions.
/// </summary>
public partial class NPCEntity : Area2D
{
	[Signal]
	public delegate void NPCClickedEventHandler(string npcId);

	[Export]
	public string NpcId { get; set; } = "";

	[Export]
	public string NpcName { get; set; } = "NPC";

	[Export]
	public Color NpcColor { get; set; } = Colors.Blue;

	// Visual elements
	private Sprite2D _sprite;
	private Label _nameLabel;
	private CollisionShape2D _collisionShape;
	
	// State indicators
	private Sprite2D _convertedIndicator;
	private Sprite2D _deadOverlay;
	private Sprite2D _marriedIndicator;

	// Interaction range
	private Area2D _interactionArea;
	private bool _playerInRange = false;
	private Label _interactHint;

	public override void _Ready()
	{
		SetupVisuals();
		SetupCollision();
		SetupInteractionArea();
		
		// Connect input
		InputEvent += OnInputEvent;
	}

	private void SetupVisuals()
	{
		// Main NPC sprite - load from file based on NPC ID
		_sprite = new Sprite2D();
		LoadSpriteForNPC();
		AddChild(_sprite);

		// Name label above the NPC
		_nameLabel = new Label();
		_nameLabel.Text = NpcName;
		_nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_nameLabel.Position = new Vector2(-50, -50);
		_nameLabel.CustomMinimumSize = new Vector2(100, 20);
		AddChild(_nameLabel);

		// Status indicators (hidden by default)
		_convertedIndicator = CreateStatusIndicator(Colors.Purple, new Vector2(20, -20));
		_deadOverlay = CreateStatusIndicator(Colors.Black.Lerp(Colors.Transparent, 0.3f), Vector2.Zero);
		_deadOverlay.Scale = new Vector2(1.2f, 1.2f);
		_marriedIndicator = CreateStatusIndicator(Colors.Pink, new Vector2(-20, -20));

		// Interaction hint
		_interactHint = new Label();
		_interactHint.Text = "[Click to Interact]";
		_interactHint.HorizontalAlignment = HorizontalAlignment.Center;
		_interactHint.Position = new Vector2(-60, 40);
		_interactHint.CustomMinimumSize = new Vector2(120, 20);
		_interactHint.Visible = false;
		AddChild(_interactHint);
	}

	private void LoadSpriteForNPC()
	{
		// Map NPC IDs to sprite numbers (1-10)
		var npcSpriteMap = new System.Collections.Generic.Dictionary<string, int>
		{
			{ "katy", 1 },
			{ "john", 2 },
			{ "rebecca", 3 },
			{ "marcus", 4 },
			{ "sofia", 5 }
		};


		int spriteNum = 1; // default
		if (!npcSpriteMap.TryGetValue(NpcId.ToLower(), out spriteNum))
		{
			spriteNum = 1; // Use sprite 1 as fallback for unknown NPCs
		}
		string spritePath = $"res://assets/sprite-{spriteNum:D4}.png";

		
		var texture = GD.Load<Texture2D>(spritePath);
		
		if (texture != null)
		{
			_sprite.Texture = texture;
			// Scale sprite to match tile height (4x for better visibility)
			_sprite.Scale = new Vector2(4.0f, 4.0f);
			GD.Print($"Loaded NPC sprite for {NpcId}: {spritePath}");
		}
		else
		{
			GD.PrintErr($"Failed to load NPC sprite: {spritePath}, using fallback");
			// Fallback to gradient texture
			var fallbackTexture = new GradientTexture2D();
			fallbackTexture.Width = 64;
			fallbackTexture.Height = 64;
			fallbackTexture.Fill = GradientTexture2D.FillEnum.Radial;
			fallbackTexture.FillFrom = new Vector2(0.5f, 0.5f);
			fallbackTexture.FillTo = new Vector2(1f, 0.5f);
			var gradient = new Gradient();
			gradient.SetColor(0, NpcColor);
			gradient.SetColor(1, NpcColor.Darkened(0.3f));
			fallbackTexture.Gradient = gradient;
			_sprite.Texture = fallbackTexture;
			_sprite.Scale = new Vector2(4.0f, 4.0f);
		}
	}

	private Sprite2D CreateStatusIndicator(Color color, Vector2 offset)
	{
		var indicator = new Sprite2D();
		var texture = new GradientTexture2D();
		texture.Width = 16;
		texture.Height = 16;
		texture.Fill = GradientTexture2D.FillEnum.Radial;
		texture.FillFrom = new Vector2(0.5f, 0.5f);
		texture.FillTo = new Vector2(1f, 0.5f);
		var gradient = new Gradient();
		gradient.SetColor(0, color);
		gradient.SetColor(1, color.Darkened(0.5f));
		texture.Gradient = gradient;
		indicator.Texture = texture;
		indicator.Position = offset;
		indicator.Visible = false;
		AddChild(indicator);
		return indicator;
	}

	private void SetupCollision()
	{
		// Make the NPC clickable
		_collisionShape = new CollisionShape2D();
		var shape = new CircleShape2D();
		shape.Radius = 32;
		_collisionShape.Shape = shape;
		AddChild(_collisionShape);
		
		InputPickable = true;
	}

	private void SetupInteractionArea()
	{
		// Larger area for detecting when player is in range
		_interactionArea = new Area2D();
		_interactionArea.Name = "InteractionArea";
		// Monitor Layer 2 (Players) and Layer 1 (Default)
		_interactionArea.CollisionMask = 3;
		
		var collisionShape = new CollisionShape2D();
		var shape = new CircleShape2D();
		shape.Radius = 120; // Increased range
		collisionShape.Shape = shape;
		_interactionArea.AddChild(collisionShape);
		
		_interactionArea.BodyEntered += OnBodyEntered;
		_interactionArea.BodyExited += OnBodyExited;
		
		AddChild(_interactionArea);
	}

	private void OnInputEvent(Node viewport, InputEvent @event, long shapeIdx)
	{
		// Kept for specific physics interaction if working
		if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
		{
			TryInteract();
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		// Fallback: Check global mouse position distance if physics click failed
		if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
		{
			float dist = GetGlobalMousePosition().DistanceTo(GlobalPosition);
			if (dist < 80) // Visual click radius
			{
				GD.Print($"Fallback click detected! Dist: {dist}, InRange: {_playerInRange}. Emitting signal anyway.");
				EmitSignal(SignalName.NPCClicked, NpcId);
				GetViewport().SetInputAsHandled();
			}
		}
	}

	private void TryInteract()
	{
		if (_playerInRange)
		{
			GD.Print($"Interacting with {NpcId}");
			EmitSignal(SignalName.NPCClicked, NpcId);
		}
		else
		{
			GD.Print($"Must be closer to interact with {NpcName}");
		}
	}

	private void OnBodyEntered(Node2D body)
	{
		// Check if it's a player
		if (body.IsInGroup("players"))
		{
			GD.Print($"Body entered {NpcName}: {body.Name}");
			_playerInRange = true;
			
			// Auto-interact if it's the local player
			if (body is PlayerController player && player.IsLocalPlayer)
			{
				GD.Print($"Local player entered {NpcName} range. Auto-interacting.");
				EmitSignal(SignalName.NPCClicked, NpcId);
			}
			else
			{
				// Keep hint for remote players (optional, or just logic consistency)
				_interactHint.Visible = true;
			}
		}
	}

	private void OnBodyExited(Node2D body)
	{
		if (body.IsInGroup("players"))
		{
			GD.Print($"Body exited {NpcName}: {body.Name}");
			_playerInRange = false;
			_interactHint.Visible = false;
		}
	}

	/// <summary>
	/// Update the visual state of this NPC based on game state
	/// </summary>
	public void UpdateState(bool alive, bool converted, bool married)
	{
		_deadOverlay.Visible = !alive;
		_convertedIndicator.Visible = converted;
		_marriedIndicator.Visible = married;
		
		// Dim the sprite if dead
		_sprite.Modulate = alive ? Colors.White : Colors.DarkGray;
		
		// Disable interaction if dead
		InputPickable = alive;
	}

	/// <summary>
	/// Set the NPC color (useful for distinguishing different NPCs)
	/// </summary>
	public void SetColor(Color color)
	{
		NpcColor = color;
		if (_sprite?.Texture is GradientTexture2D gradientTexture)
		{
			var gradient = gradientTexture.Gradient;
			gradient.SetColor(0, color);
			gradient.SetColor(1, color.Darkened(0.3f));
		}
	}
}
