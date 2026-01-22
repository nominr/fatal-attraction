using Godot;
using System;

/// <summary>
/// Player controller for spatial movement in the game world.
/// Uses CharacterBody2D for physics-based movement.
/// </summary>
public partial class PlayerController : CharacterBody2D
{
	[Signal]
	public delegate void PositionChangedEventHandler(long playerId, Vector2 position);

	[Export]
	public float Speed { get; set; } = 200.0f;

	[Export]
	public Color PlayerColor { get; set; } = Colors.Green;

	[Export]
	public string PlayerRole { get; set; } = "Observer";

	[Export]
	public int PlayerIndex { get; set; } = 1; // 1, 2, or 3 for sprite selection

	// Visual elements
	private Sprite2D _sprite;
	private Label _nameLabel;
	private Label _roleLabel;

	// Networking
	private bool _isLocalPlayer = false;
	public bool IsLocalPlayer => _isLocalPlayer;
	private long _playerId = 0;
	private Vector2 _lastSentPosition = Vector2.Zero;
	private const float PositionSyncThreshold = 2.0f; // Only sync if moved more than this

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
		shape.Radius = 16; // 32x32 sprite / 2
		collisionShape.Shape = shape;
		AddChild(collisionShape);

		// Player sprite - will be loaded when role is set
		_sprite = new Sprite2D();
		AddChild(_sprite);
		
		// Load sprite based on role (will be called again when role is set)
		LoadSpriteForRole(PlayerRole);

		// Role label below player
		_roleLabel = new Label();
		_roleLabel.Text = PlayerRole;
		_roleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_roleLabel.Position = new Vector2(-40, 20);
		_roleLabel.CustomMinimumSize = new Vector2(80, 20);
		AddChild(_roleLabel);
	}

	private void LoadSpriteForRole(string role)
	{
		if (_sprite == null) return;
		
		// Map role to sprite number: Admirer=1, Prophet=2, Producer=3
		int spriteNum = role.ToLower() switch
		{
			"admirer" => 1,
			"prophet" => 2,
			"producer" => 3,
			_ => 1 // default to sprite 1
		};
		
		// Use correct path from assets folder
		string spritePath = $"res://assets/sprite-000{spriteNum}.png";
		var texture = GD.Load<Texture2D>(spritePath);
		
		if (texture != null)
		{
			_sprite.Texture = texture;
			GD.Print($"Loaded player sprite for {role}: {spritePath}");
		}
		else
		{
			GD.PrintErr($"Failed to load sprite: {spritePath}");
			// Fallback to generated texture
			var fallbackTexture = new GradientTexture2D();
			fallbackTexture.Width = 32;
			fallbackTexture.Height = 32;
			_sprite.Texture = fallbackTexture;
		}
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

		// Load the correct sprite for this role
		LoadSpriteForRole(role);
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

	/// <summary>
	/// Set the network player ID for this controller
	/// </summary>
	public void SetPlayerId(long playerId)
	{
		_playerId = playerId;
		_lastSentPosition = Position; // Initialize to current position
		GD.Print($"PlayerController: Set player ID {playerId} at position {Position}");
	}

	/// <summary>
	/// Update position from network sync (for remote players)
	/// </summary>
	public void UpdateRemotePosition(Vector2 newPosition)
	{
		if (_isLocalPlayer)
		{
			GD.Print($"[PlayerController] Ignoring UpdateRemotePosition for local player {_playerId}");
			return; // Don't override local player position
		}
		GD.Print($"[PlayerController] Updating remote player {_playerId} position from {Position} to {newPosition}");
		Position = newPosition;
	}

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

		// Emit position change if moved significantly
		if (Position.DistanceTo(_lastSentPosition) > PositionSyncThreshold)
		{
			_lastSentPosition = Position;
			GD.Print($"[PlayerController] Player {_playerId} emitting PositionChanged signal at {Position}");
			EmitSignal(SignalName.PositionChanged, _playerId, Position);
		}
	}
}
