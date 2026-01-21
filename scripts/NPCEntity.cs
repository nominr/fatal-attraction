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
		// Main NPC sprite (simple colored circle for now)
		_sprite = new Sprite2D();
		// Create a simple placeholder texture
		var texture = new GradientTexture2D();
		texture.Width = 64;
		texture.Height = 64;
		texture.Fill = GradientTexture2D.FillEnum.Radial;
		texture.FillFrom = new Vector2(0.5f, 0.5f);
		texture.FillTo = new Vector2(1f, 0.5f);
		var gradient = new Gradient();
		gradient.SetColor(0, NpcColor);
		gradient.SetColor(1, NpcColor.Darkened(0.3f));
		texture.Gradient = gradient;
		_sprite.Texture = texture;
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
		
		var collisionShape = new CollisionShape2D();
		var shape = new CircleShape2D();
		shape.Radius = 80; // Larger than clickable area
		collisionShape.Shape = shape;
		_interactionArea.AddChild(collisionShape);
		
		_interactionArea.BodyEntered += OnBodyEntered;
		_interactionArea.BodyExited += OnBodyExited;
		
		AddChild(_interactionArea);
	}

	private void OnInputEvent(Node viewport, InputEvent @event, long shapeIdx)
	{
		if (@event is InputEventMouseButton mouseEvent)
		{
			if (mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
			{
				if (_playerInRange)
				{
					GD.Print($"NPC {NpcId} clicked!");
					EmitSignal(SignalName.NPCClicked, NpcId);
				}
				else
				{
					GD.Print($"Must be closer to interact with {NpcName}");
				}
			}
		}
	}

	private void OnBodyEntered(Node2D body)
	{
		// Check if it's a player (you'll tag your player CharacterBody2D)
		if (body.IsInGroup("players"))
		{
			_playerInRange = true;
			_interactHint.Visible = true;
		}
	}

	private void OnBodyExited(Node2D body)
	{
		if (body.IsInGroup("players"))
		{
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
