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
	private Camera2D _camera;

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
		
		// Configure collision layers to prevent player-to-player collisions
		// Layer 1 (bit 0): Players exist here for detection by NPCs
		// Mask 0: Players don't collide with anything (pass through each other)
		CollisionLayer = 1; // Bit 0 = layer 1
		CollisionMask = 0;  // Don't collide with any layers
		
		GD.Print($"PlayerController: Set collision layer={CollisionLayer}, mask={CollisionMask}");
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

		// Add camera for local player
		_camera = new Camera2D();
		_camera.Enabled = false; // Will be enabled when SetLocalPlayer is called
		AddChild(_camera);

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
		
		// Map role to role-specific sprite file
		string spritePath = role.ToLower() switch
		{
			"admirer" => "res://assets/admirer-sprite.png",
			"prophet" => "res://assets/prophet-sprite.png",
			"producer" => "res://assets/producer-sprite.png",
			_ => "res://assets/admirer-sprite.png" // default to admirer
		};
		
		var texture = GD.Load<Texture2D>(spritePath);
		
		if (texture != null)
		{
			_sprite.Texture = texture;
			// Scale sprite to match tile height (4x for better visibility)
			_sprite.Scale = new Vector2(4.0f, 4.0f);
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
			_sprite.Scale = new Vector2(4.0f, 4.0f);
		}
	}


	/// <summary>
	/// Set whether this is the local player (controlled by this client)
	/// </summary>
	public void SetLocalPlayer(bool isLocal)
	{
		_isLocalPlayer = isLocal;
		GD.Print($"SetLocalPlayer called: isLocal={isLocal}, _sprite exists={_sprite != null}");
		
		// Enable camera for local player
		if (_camera != null)
		{
			_camera.Enabled = isLocal;
			if (isLocal)
			{
				_camera.MakeCurrent(); // Explicitly make this camera active
				GD.Print($"Camera enabled and made current for local player {_playerId}");
			}
		}
		else
		{
			GD.PrintErr("Camera reference is null!");
		}
		
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
	}

	/// <summary>
	/// Update position from network sync (for remote players)
	/// </summary>
	public void UpdateRemotePosition(Vector2 newPosition)
	{
		if (_isLocalPlayer) return; // Don't override local player position
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
			EmitSignal(SignalName.PositionChanged, _playerId, Position);
		}
	}
}
