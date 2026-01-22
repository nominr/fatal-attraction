using Godot;
using System;

/// <summary>
/// Player controller for spatial movement in the game world.
/// Uses CharacterBody2D for physics-based movement.
/// </summary>
public partial class PlayerController : CharacterBody2D
{
	[Export]
	public float Speed { get; set; } = 200.0f;

	[Export]
	public Color PlayerColor { get; set; } = Colors.Green;

	[Export]
	public string PlayerRole { get; set; } = "Observer";

	// Visual elements
	private Sprite2D _sprite;
	private Label _nameLabel;
	private Label _roleLabel;

	// Networking
	private bool _isLocalPlayer = false;
	public bool IsLocalPlayer => _isLocalPlayer;

	public override void _Ready()
	{
		SetupVisuals();
		
		// Add to players group so NPCs can detect us
		AddToGroup("players");
	}

	private void SetupVisuals()
	{
		// Create collision shape
		var collisionShape = new CollisionShape2D();
		var shape = new CircleShape2D();
		shape.Radius = 20;
		collisionShape.Shape = shape;
		AddChild(collisionShape);

		// Player sprite (colored circle)
		_sprite = new Sprite2D();
		var texture = new GradientTexture2D();
		texture.Width = 48;
		texture.Height = 48;
		texture.Fill = GradientTexture2D.FillEnum.Radial;
		texture.FillFrom = new Vector2(0.5f, 0.5f);
		texture.FillTo = new Vector2(1f, 0.5f);
		var gradient = new Gradient();
		gradient.SetColor(0, PlayerColor);
		gradient.SetColor(1, PlayerColor.Darkened(0.4f));
		texture.Gradient = gradient;
		_sprite.Texture = texture;
		AddChild(_sprite);

		// Role label below player
		_roleLabel = new Label();
		_roleLabel.Text = PlayerRole;
		_roleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_roleLabel.Position = new Vector2(-40, 25);
		_roleLabel.CustomMinimumSize = new Vector2(80, 20);
		AddChild(_roleLabel);
	}

	/// <summary>
	/// Set whether this is the local player (controlled by this client)
	/// </summary>
	public void SetLocalPlayer(bool isLocal)
	{
		_isLocalPlayer = isLocal;
		GD.Print($"SetLocalPlayer called: isLocal={isLocal}, _sprite exists={_sprite != null}");
		
		if (_sprite != null)
		{
			if (isLocal)
			{
				// Add visual indicator that this is the local player
				_sprite.Modulate = Colors.White;
			}
			else
			{
				// Other players are slightly dimmed
				_sprite.Modulate = new Color(0.8f, 0.8f, 0.8f, 1f);
			}
		}
	}

	/// <summary>
	/// Update the player's visual based on role
	/// </summary>
	public void SetRole(string role)
	{
		PlayerRole = role;
		GD.Print($"SetRole called: role={role}");
		
		if (_roleLabel != null)
		{
			_roleLabel.Text = role;
		}

		// Set color based on role
		Color roleColor = role.ToLower() switch
		{
			"admirer" => Colors.Red,
			"prophet" => Colors.Purple,
			"producer" => Colors.Gold,
			_ => Colors.Gray
		};

		SetColor(roleColor);
	}

	/// <summary>
	/// Set the player color
	/// </summary>
	public void SetColor(Color color)
	{
		PlayerColor = color;
		if (_sprite?.Texture is GradientTexture2D gradientTexture)
		{
			var gradient = gradientTexture.Gradient;
			gradient.SetColor(0, color);
			gradient.SetColor(1, color.Darkened(0.4f));
		}
	}

	// Input control
	public bool InputEnabled { get; set; } = true;

	public override void _PhysicsProcess(double delta)
	{
		// Only process input for the local player
		if (!_isLocalPlayer || !InputEnabled) return;

		var velocity = Vector2.Zero;

		// Handle input
		if (Input.IsActionPressed("ui_right") || Input.IsKeyPressed(Key.D))
			velocity.X += 1;
		if (Input.IsActionPressed("ui_left") || Input.IsKeyPressed(Key.A))
			velocity.X -= 1;
		if (Input.IsActionPressed("ui_down") || Input.IsKeyPressed(Key.S))
			velocity.Y += 1;
		if (Input.IsActionPressed("ui_up") || Input.IsKeyPressed(Key.W))
			velocity.Y -= 1;

		if (velocity.Length() > 0)
		{
			velocity = velocity.Normalized() * Speed;
		}

		Velocity = velocity;
		MoveAndSlide();
	}
}
