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
	public float Speed { get; set; } = 250.0f;

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
	private Font _customFont;

	// Networking
	private bool _isLocalPlayer = false;
	public bool IsLocalPlayer => _isLocalPlayer;
	private long _playerId = 0;
	private Vector2 _lastSentPosition = Vector2.Zero;
	private const float PositionSyncThreshold = 2.0f; // Only sync if moved more than this

	public override void _Ready()
	{
		// Load custom font
		_customFont = ResourceLoader.Load<Font>("res://assets/Pixer-Regular.otf");
		
		SetupVisuals();
		
		// Add to players group so NPCs can detect us
		AddToGroup("players");
		
		// Configure collision layers
		// Layer 2 (bit 1): Players
		// Mask 1 (bit 0): Walls/World
		CollisionLayer = 2; 
		CollisionMask = 1; 
		
		GD.Print($"PlayerController: Set collision layer={CollisionLayer}, mask={CollisionMask}");
	}

	private void SetupVisuals()
	{
		// Create collision shape
		// Create collision shape
		var collisionShape = new CollisionShape2D();
		var shape = new RectangleShape2D();
		
		// Sprite is scaled 4x (approx 128px height)
		// Set collision to match height (128) and reduced width (80) for playability
		shape.Size = new Vector2(60, 120); 
		collisionShape.Shape = shape;
		// Position centered (0,0) matches sprite center
		
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

		// Role label below player (white color on transparent grey background)
		_roleLabel = new Label();
		_roleLabel.Text = PlayerRole;
		_roleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_roleLabel.AddThemeColorOverride("font_color", Colors.White);
		_roleLabel.AddThemeFontOverride("font", _customFont);
		_roleLabel.AddThemeFontSizeOverride("font_size", 28);
		// Add transparent grey background
		var bgStyle = new StyleBoxFlat();
		bgStyle.BgColor = new Color(0.2f, 0.2f, 0.2f, 0.6f); // Transparent grey
		bgStyle.SetCornerRadiusAll(4);
		bgStyle.SetContentMarginAll(4);
		_roleLabel.AddThemeStyleboxOverride("normal", bgStyle);
		AddChild(_roleLabel);
		// Center the label over the player based on text width
		CallDeferred(MethodName.CenterRoleLabel);
		// Size will auto-adjust based on text content
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

	/// <summary>
	/// Set the ghost mode for eliminated players (Admirer)
	/// </summary>
	public void SetGhostMode(bool enabled)
	{
		if (_sprite != null)
		{
			// Semi-transparent if ghost
			var color = _sprite.Modulate;
			color.A = enabled ? 0.5f : 1.0f;
			_sprite.Modulate = color;
		}

		// Update collision layers
		// Normal: Layer 2 (Bit 1) = Players
		// Ghost: Remove Layer 2 so they don't trigger interaction areas (Mask 2)
		if (enabled)
		{
			CollisionLayer &= ~(uint)2; // Clear bit 1
			GD.Print($"Player {_playerId} entered GHOST MODE. CollisionLayer: {CollisionLayer}");
		}
		else
		{
			CollisionLayer |= (uint)2; // Set bit 1
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

	/// <summary>
	/// Center the role label horizontally over the player based on its actual width.
	/// Called deferred to ensure the label has been sized.
	/// </summary>
	private void CenterRoleLabel()
	{
		if (_roleLabel != null)
		{
			var labelWidth = _roleLabel.Size.X;
			_roleLabel.Position = new Vector2(-labelWidth / 2, -115);
		}
	}
}
