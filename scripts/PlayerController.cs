using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Player controller for spatial movement in the game world.
/// Uses CharacterBody2D for physics-based movement.
/// </summary>
public partial class PlayerController : CharacterBody2D
{
	[Signal]
	public delegate void PositionChangedEventHandler(long playerId, Vector2 position);

	[Export]
	public float Speed { get; set; } = 425.0f; // 1.7x original 250

	[Export]
	public Color PlayerColor { get; set; } = Colors.Green;

	[Export]
	public string PlayerRole { get; set; } = "Observer";

	[Export]
	public int PlayerIndex { get; set; } = 1; // 1, 2, or 3 for sprite selection

	// Visual elements
	private AnimatedSprite2D _sprite;
	private Label _nameLabel;
	private Label _roleLabel;
	private Camera2D _camera;
	private Font _customFont;
	private static Font _sharedFont;
	private static readonly Dictionary<string, SpriteFrames> _spriteFramesCache = new();
	private static readonly Dictionary<string, Dictionary<string, float>> _animationScalesCache = new();

	// Networking
	private bool _isLocalPlayer = false;
	public bool IsLocalPlayer => _isLocalPlayer;
	private long _playerId = 0;
	private Vector2 _lastSentPosition = Vector2.Zero;
	private const float PositionSyncThreshold = 2.0f; // Only sync if moved more than this
	
	// Slip state (for banana trap)
	private bool _isSlipping = false;
	private double _slipTimer = 0.0;

	// Flash damage state
	private bool _isFlashing = false;
	private double _flashTimer = 0.0;

	// Ghost mode state (for eliminated players)
	private bool _isGhostMode = false;

	// Client-side interpolation (for remote players)
	private Vector2 _remoteTargetPosition = Vector2.Zero;
	private bool _hasReceivedFirstSync = false;
	
	// Animation state
	private Dictionary<string, float> _animationScales = new Dictionary<string, float>();
	private string _lastFacingDirection = "right"; // "up", "down", "left", "right"

	public override void _Ready()
	{
		// Load custom font
		_sharedFont ??= ResourceLoader.Load<Font>("res://assets/Pixer-Regular.otf");
		_customFont = _sharedFont;
		
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
		var collisionShape = new CollisionShape2D();
		var shape = new RectangleShape2D();
		
		// Sprite is scaled (300 * scale = height)
		// Set collision to match height (204) and width to fit corridors (37)
		shape.Size = new Vector2(37, 204); 
		collisionShape.Shape = shape;
		// Position centered (0,0) matches sprite center
		
		AddChild(collisionShape);

		// Player sprite - will be loaded when role is set
		_sprite = new AnimatedSprite2D();
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

	public override void _Process(double delta)
	{
		// Update slip timer and resume when elapsed
		if (_isSlipping)
		{
			_slipTimer -= delta;
			if (_slipTimer <= 0)
			{
				_isSlipping = false;
				_slipTimer = 0;
				// Restore upright rotation
				if (_sprite != null)
				{
					_sprite.RotationDegrees = 0;
				}
			}
		}

		// Update flash timer
		if (_isFlashing)
		{
			_flashTimer -= delta;
			if (_flashTimer <= 0)
			{
				_isFlashing = false;
				_flashTimer = 0;
				// Restore normal color, preserving ghost mode alpha
				float alpha = _isGhostMode ? 0.5f : 1.0f;
				if (_sprite != null && _isLocalPlayer)
				{
					_sprite.Modulate = new Color(1, 1, 1, alpha);
				}
				else if (_sprite != null)
				{
					_sprite.Modulate = new Color(0.8f, 0.8f, 0.8f, alpha);
				}
			}
		}

		// Client-side interpolation for remote players
		if (!_isLocalPlayer && _hasReceivedFirstSync)
		{
			Vector2 oldPos = Position;
			// Lerp towards target position for smooth movement
			Position = Position.Lerp(_remoteTargetPosition, 10.0f * (float)delta);
			
			// Calculate velocity for animation logic
			if (delta > 0)
			{
				Velocity = (Position - oldPos) / (float)delta;
			}
		}

		UpdateAnimation();
	}

	private void UpdateAnimation()
	{
		if (_sprite == null) return;
		if (_isSlipping) 
		{
			_sprite.Pause();
			return;
		}

		Vector2 velocity = Velocity;
		string animToPlay = _sprite.Animation;
		
		if (velocity.Length() > 0.1f)
		{
			float absX = Mathf.Abs(velocity.X);
			float absY = Mathf.Abs(velocity.Y);
			bool movingUp = velocity.Y < -0.1f;
			bool movingDown = velocity.Y > 0.1f;
			bool movingHorizontal = absX > 0.1f;

			if (movingUp)
			{
				if (absX < absY * 0.5f)
				{
					animToPlay = "walk_up";
					_sprite.FlipH = false;
					_lastFacingDirection = "up";
				}
				else if (velocity.X > 0)
				{
					animToPlay = "walk_up_right";
					_sprite.FlipH = false;
					_lastFacingDirection = "up_right";
				}
				else
				{
					animToPlay = "walk_up_left";
					_sprite.FlipH = true;
					_lastFacingDirection = "up_left";
				}
			}
			else if (movingDown)
			{
				if (absX < absY * 0.5f)
				{
					animToPlay = "walk_down";
					_sprite.FlipH = false;
					_lastFacingDirection = "down";
				}
				else if (velocity.X > 0)
				{
					animToPlay = "walk_right"; // Front-facing
					_sprite.FlipH = true;
					_lastFacingDirection = "right";
				}
				else
				{
					animToPlay = "walk_left"; // Front-facing
					_sprite.FlipH = false;
					_lastFacingDirection = "left";
				}
			}
			else // Pure horizontal
			{
				if (velocity.X > 0)
				{
					animToPlay = "walk_right";
					_sprite.FlipH = true;
					_lastFacingDirection = "right";
				}
				else
				{
					animToPlay = "walk_left";
					_sprite.FlipH = false;
					_lastFacingDirection = "left";
				}
			}
		}
		else
		{
			// Idle
			if (_lastFacingDirection == "right")
			{
				animToPlay = "idle_right";
				_sprite.FlipH = true; // Swap
			}
			else if (_lastFacingDirection == "left")
			{
				animToPlay = "idle_left";
				_sprite.FlipH = false; // Swap
			}
			else if (_lastFacingDirection == "up")
			{
				animToPlay = "idle_up";
				_sprite.FlipH = false;
			}
			else if (_lastFacingDirection == "up_right")
			{
				animToPlay = "idle_up_right";
				_sprite.FlipH = false;
			}
			else if (_lastFacingDirection == "up_left")
			{
				animToPlay = "idle_up_left";
				_sprite.FlipH = true;
			}
			else // down
			{
				animToPlay = "idle_down";
				_sprite.FlipH = false;
			}
		}

		if (_sprite.Animation != animToPlay)
		{
			_sprite.Play(animToPlay);
		}

		// Apply per-animation scale to standardize size
		if (_animationScales.TryGetValue(animToPlay, out float targetScale))
		{
			_sprite.Scale = new Vector2(targetScale, targetScale);
		}
	}


	private void LoadSpriteForRole(string role)
	{
		if (_sprite == null) return;
		
		string roleLower = role.ToLower();
		string basePath = "res://assets/new-character-assets/";
		string roleName = roleLower;
		
		// Map role name to asset prefix
		if (roleLower == "admirer")
		{
			roleName = "admirer2";
		}
		else if (roleLower == "producer")
		{
			roleName = "producer2";
		}
		else if (roleLower != "prophet")
		{
			roleName = "admirer2"; // Default
		}

		if (_spriteFramesCache.TryGetValue(roleName, out var cachedFrames) &&
			_animationScalesCache.TryGetValue(roleName, out var cachedScales))
		{
			_sprite.SpriteFrames = cachedFrames;
			_animationScales = new Dictionary<string, float>(cachedScales);
			_sprite.Play("idle_right");
			if (_animationScales.TryGetValue("idle_right", out float cachedScale))
			{
				_sprite.Scale = new Vector2(cachedScale, cachedScale);
			}
			return;
		}

		var frames = new SpriteFrames();
		var generatedScales = new Dictionary<string, float>();
		
		// Helper to load frames from a split texture (6x6 grid, but we limit to 35 frames)
		void AddAnimationFrames(string animName, string texturePath, bool cropShadow = false, bool skipFirstFrame = false, float scaleMultiplier = 1.0f)
		{
			var texture = GD.Load<Texture2D>(texturePath);
			if (texture == null)
			{
				GD.PrintErr($"Failed to load texture for {animName}: {texturePath}");
				return;
			}

			if (!frames.HasAnimation(animName))
				frames.AddAnimation(animName);
			
			float width = texture.GetWidth();
			float height = texture.GetHeight();
			
			int gridCols = 6;
			int gridRows = 6;
			
			float frameWidth = width / (float)gridCols;
			float frameHeight = height / (float)gridRows;

			int totalAdded = 0;
			for (int y = 0; y < gridRows; y++)
			{
				for (int x = 0; x < gridCols; x++)
				{
					if (skipFirstFrame && x == 0 && y == 0) continue;
					if (totalAdded >= 35) break;

					var atlasKey = new AtlasTexture();
					atlasKey.Atlas = texture;
					
					// If cropping shadow, we take 90% of height from top
					float h = cropShadow ? frameHeight * 0.9f : frameHeight;
					atlasKey.Region = new Rect2(x * frameWidth, y * frameHeight, frameWidth, h);
					frames.AddFrame(animName, atlasKey);
					totalAdded++;
				}
				if (totalAdded >= 35) break;
			}
			
			frames.SetAnimationLoop(animName, true);
			frames.SetAnimationSpeed(animName, animName.Contains("idle") ? 10.0f : 15.0f);

			// Standardized height units
			float targetWorldHeight = 243.0f; // Standard size for all isometric characters
			
			// Use the full frame height for scale calculation to keep consistency
			generatedScales[animName] = (targetWorldHeight / frameHeight) * scaleMultiplier;
		}

		string frontIdle = $"{basePath}{roleName}-front-idle.png";
		string frontWalk = $"{basePath}{roleName}-front-walk.png";
		string backIdle = $"{basePath}{roleName}-back-idle.png";
		string backWalk = $"{basePath}{roleName}-back-walk.png";

		// Load split animations
		// For isometric: 
		// Down = Front
		// Up = Back
		// Right = Front
		// Left = Front (flipped in UpdateAnimation)
		AddAnimationFrames("walk_down", frontWalk);
		AddAnimationFrames("walk_up", backWalk);
		AddAnimationFrames("walk_right", frontWalk);
		AddAnimationFrames("walk_left", frontWalk);
		
		// Add back-directional walk for up-diagonals
		AddAnimationFrames("walk_up_right", backWalk);
		AddAnimationFrames("walk_up_left", backWalk);

		AddAnimationFrames("idle_right", frontIdle);
		AddAnimationFrames("idle_left", frontIdle);
		
		// Admirer's back-idle has a broken first frame and is exported smaller than other sides
		bool isAdmirer = (roleName == "admirer2");
		AddAnimationFrames("idle_up", backIdle, false, isAdmirer, isAdmirer ? 1.15f : 1.0f); 
		
		// Add back-directional idle for up-diagonals
		AddAnimationFrames("idle_up_right", backIdle, false, isAdmirer, isAdmirer ? 1.15f : 1.0f);
		AddAnimationFrames("idle_up_left", backIdle, false, isAdmirer, isAdmirer ? 1.15f : 1.0f);

		AddAnimationFrames("idle_down", frontIdle);

		_animationScales = generatedScales;
		_spriteFramesCache[roleName] = frames;
		_animationScalesCache[roleName] = new Dictionary<string, float>(generatedScales);
		_sprite.SpriteFrames = frames;
		
		_sprite.Play("idle_right");
		// Apply initial scale
		if (_animationScales.TryGetValue("idle_right", out float s)) _sprite.Scale = new Vector2(s, s);

		GD.Print($"Loaded split isometric animated sprites for {role}");
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
		// AnimatedSprite2D doesn't support GradientTexture2D easily like Sprite2D did for colorization
		// We might need a shader or separate sprites if we want color overrides, but generic requirements usually just mean tint is enough?
		// For now, we'll skip the gradient replacement.
	}

	/// <summary>
	/// Set the ghost mode for eliminated players (Admirer)
	/// </summary>
	public void SetGhostMode(bool enabled)
	{
		_isGhostMode = enabled;
		
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
		_remoteTargetPosition = newPosition;
		
		if (!_hasReceivedFirstSync)
		{
			// Snap strictly on first update to avoid flying in from (0,0)
			Position = newPosition;
			_hasReceivedFirstSync = true;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		// Only process input for the local player
		if (!_isLocalPlayer || !InputEnabled) return;
		
		// If slipping, disable movement
		if (_isSlipping)
		{
			Velocity = Vector2.Zero;
			return;
		}

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
	/// Temporarily disable movement and rotate 90 degrees clockwise for the specified duration.
	/// Used when player slips on a banana trap.
	/// </summary>
	/// <param name="seconds">Duration in seconds.</param>
	public void StartSlip(double seconds)
	{
		_isSlipping = true;
		_slipTimer = Math.Max(0, seconds);
		if (_sprite != null)
		{
			_sprite.RotationDegrees = 90;
		}
	}

	/// <summary>
	/// Flash the player sprite red for visual damage feedback.
	/// </summary>
	/// <param name="duration">Duration in seconds.</param>
	public void FlashDamage(double duration)
	{
		_isFlashing = true;
		_flashTimer = Math.Max(0, duration);
		if (_sprite != null)
		{
			// Preserve ghost mode alpha when flashing
			float alpha = _isGhostMode ? 0.5f : 1.0f;
			_sprite.Modulate = new Color(1, 0, 0, alpha); // Red flash with preserved alpha
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
			_roleLabel.Position = new Vector2(-labelWidth / 2, -195);
		}
	}

}
