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
	private StyleBoxFlat _roleLabelBgStyle; // stored so SetRole can recolor it
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
	public string FacingDirection => _lastFacingDirection;
	public bool FlipH => _sprite?.FlipH ?? false;
	private bool _isMovingSync = false;
	public bool IsMoving => _isLocalPlayer ? Velocity.Length() > 0.1f : _isMovingSync;
	private bool _isPunching = false;
	private bool _isStabbing = false;
	private bool _isMassRevAnimating = false;

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
		// Create collision shape — taller rectangle covering mid-body to feet.
		// Sprite is ~204px tall, centered at origin → bottom is at y=+102.
		// A taller shape stops the player before the upper body clips into objects.
		var collisionShape = new CollisionShape2D();
		var shape = new RectangleShape2D();
		shape.Size = new Vector2(32, 40);
		collisionShape.Shape = shape;
		collisionShape.Position = new Vector2(0, 80); // lower-body centre

		AddChild(collisionShape);

		// Player sprite - will be loaded when role is set
		_sprite = new AnimatedSprite2D();
		AddChild(_sprite);
		
		// Load sprite based on role (will be called again when role is set)
		LoadSpriteForRole(PlayerRole);

		// Connect animation finished signal for punching
		_sprite.AnimationFinished += OnAnimationFinished;

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
		// PlayerRole is set before _Ready via SetRole being called before AddChild,
		// so we can already read it here to pick the correct colour.
		bgStyle.BgColor = PlayerRole?.ToLower() switch
		{
			"prophet"  => new Color(0.1f,  0.5f,  1.0f,  0.85f), // vivid blue
			"admirer"  => new Color(1.0f,  0.08f, 0.08f, 0.85f), // red
			"producer" => new Color(0.05f, 0.80f, 0.25f, 0.85f), // green
			_          => new Color(0.2f,  0.2f,  0.2f,  0.6f),  // grey fallback
		};
		bgStyle.SetCornerRadiusAll(4);
		bgStyle.SetContentMarginAll(4);
		_roleLabelBgStyle = bgStyle;
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

	private void OnAnimationFinished()
	{
		if (_sprite.Animation == "punch_front" || _sprite.Animation == "punch_back")
		{
			_isPunching = false;
		}
		if (_sprite.Animation == "stab_front")
		{
			_isStabbing = false;
		}
	}

	private void UpdateAnimation()
	{
		if (_sprite == null) return;
		
		if (_isMassRevAnimating)
		{
			return;
		}

		// Priority for stabbing (highest)
		if (_isStabbing)
		{
			// Stab animation is playing — don't override
			return;
		}

		// Priority for punching
		if (_isPunching) 
		{
			// Animation is already playing or handled in TriggerPunchAnimation
			return;
		}

		if (_isSlipping) 
		{
			_sprite.Pause();
			return;
		}

		string animToPlay = _sprite.Animation;
		
		// For remote players, use sync'd state directly
		if (!_isLocalPlayer)
		{
			if (_isMovingSync)
			{
				// Pick walk animation based on sync'd direction
				if (_lastFacingDirection == "up") animToPlay = "walk_up";
				else if (_lastFacingDirection == "up_right") animToPlay = "walk_up_right";
				else if (_lastFacingDirection == "up_left") animToPlay = "walk_up_left";
				else if (_lastFacingDirection == "down") animToPlay = "walk_down";
				else if (_lastFacingDirection == "right") animToPlay = "walk_right";
				else if (_lastFacingDirection == "left") animToPlay = "walk_left";
				
				// Apply flip state (already sync'd in UpdateRemotePosition)
			}
			else
			{
				// Pick idle animation based on sync'd direction
				if (_lastFacingDirection == "up") animToPlay = "idle_up";
				else if (_lastFacingDirection == "up_right") animToPlay = "idle_up_right";
				else if (_lastFacingDirection == "up_left") animToPlay = "idle_up_left";
				else if (_lastFacingDirection == "down") animToPlay = "idle_down";
				else if (_lastFacingDirection == "right") animToPlay = "idle_right";
				else if (_lastFacingDirection == "left") animToPlay = "idle_left";
			}

			if (_sprite.Animation != animToPlay)
			{
				_sprite.Play(animToPlay);
			}

			if (_animationScales.TryGetValue(animToPlay, out float s))
			{
				_sprite.Scale = new Vector2(s, s);
			}
			return;
		}

		// Local player logic follows
		Vector2 velocity = Velocity;
		
		if (velocity.Length() > 0.1f)
		{
			float absX = Mathf.Abs(velocity.X);
			float absY = Mathf.Abs(velocity.Y);
			bool movingUp = velocity.Y < -0.1f;
			bool movingDown = velocity.Y > 0.1f;

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
		void AddAnimationFrames(string animName, string texturePath, bool cropShadow = false, bool skipFirstFrame = false, float scaleMultiplier = 1.0f, int skipSpecificFrameIndex = -1, int startIndex = 0, int frameLimit = 35, float forcedFrameWidth = 0, float forcedFrameHeight = 0)
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
			int processedFrames = 0;
			for (int y = 0; y < gridRows; y++)
			{
				for (int x = 0; x < gridCols; x++)
				{
					int currentFrameIndex = processedFrames;
					processedFrames++;

					if (currentFrameIndex < startIndex) continue;
					if (skipFirstFrame && currentFrameIndex == 0) continue;
					if (currentFrameIndex == skipSpecificFrameIndex) continue;
					if (totalAdded >= frameLimit) break;

					var atlasKey = new AtlasTexture();
					atlasKey.Atlas = texture;
					
					float fw = forcedFrameWidth > 0 ? forcedFrameWidth : frameWidth;
					float fh = forcedFrameHeight > 0 ? forcedFrameHeight : frameHeight;
					
					// Center-justify the frame within the grid cell
					float rx = (x * frameWidth) + (frameWidth - fw) / 2.0f;
					float ry = (y * frameHeight) + (frameHeight - fh) / 2.0f;
					
					atlasKey.Region = new Rect2(rx, ry, fw, cropShadow ? fh * 0.9f : fh);
					frames.AddFrame(animName, atlasKey);
					totalAdded++;
				}
				if (totalAdded >= frameLimit) break;
			}
			
			frames.SetAnimationLoop(animName, true);
			float speed = animName.Contains("idle") ? 10.0f : (animName.Contains("punch") ? 20.0f : 15.0f);
			frames.SetAnimationSpeed(animName, speed);

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
		
		bool isAdmirer = (roleName == "admirer2");
		bool skipBrokenFrame = (isAdmirer || roleName == "npc6");
		AddAnimationFrames("idle_up", backIdle, false, skipBrokenFrame, isAdmirer ? 1.15f : 1.0f); 
		
		// Add back-directional idle for up-diagonals
		AddAnimationFrames("idle_up_right", backIdle, false, skipBrokenFrame, isAdmirer ? 1.15f : 1.0f);
		AddAnimationFrames("idle_up_left", backIdle, false, skipBrokenFrame, isAdmirer ? 1.15f : 1.0f);

		AddAnimationFrames("idle_down", frontIdle);

		// Punch animations
		string punchFront = $"{basePath}{roleName.Replace("2", "")}-punch-front.png";
		if (roleName == "producer2")
		{
			punchFront = $"{basePath}producer-punch-front-new.png";
		}
		string punchBack = $"{basePath}{roleName.Replace("2", "")}-punch-back.png";
		
		isAdmirer = (roleName == "admirer2");
		bool isProducer = (roleName == "producer2");
		// Use specific frame range for Producer punch to skip idle frames (Row 1)
		if (isProducer)
		{
			AddAnimationFrames("punch_front", punchFront, false, false, 1.0f, -1, 6);
		}
		else
		{
			AddAnimationFrames("punch_front", punchFront, false, false, 1.0f, -1, 0);
		}
		// Skip 7th frame (index 6) for Admirer back punch
		AddAnimationFrames("punch_back", punchBack, false, false, isAdmirer ? 1.3f : 1.0f, isAdmirer ? 6 : -1);
		
		// Load stab animation for Admirer
		if (isAdmirer)
		{
			string stabFront = $"{basePath}admirer2-stab-front.png";
			AddAnimationFrames("stab_front", stabFront, false, false, 1.5f, -1, 0, 36);
			frames.SetAnimationLoop("stab_front", false);
			frames.SetAnimationSpeed("stab_front", 15.0f);
		}

		// Set punch animations to NOT loop
		frames.SetAnimationLoop("punch_front", false);
		frames.SetAnimationLoop("punch_back", false);

		if (roleName == "prophet")
		{
			string massRevTexture = $"{basePath}prophet-mass-revalation.png";
			AddAnimationFrames("mass_revelation", massRevTexture, false, false, 1.0f, -1, 0, 36);
			frames.SetAnimationLoop("mass_revelation", true);
			frames.SetAnimationSpeed("mass_revelation", 15.0f);
		}

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

			// Re-apply a new stylebox so Godot redraws the background immediately
			string roleLower = role.ToLower();
			var roleBg = new StyleBoxFlat();
			roleBg.BgColor = roleLower switch
			{
				"prophet"  => new Color(0.1f,  0.5f,  1.0f,  0.85f), // vivid blue
				"admirer"  => new Color(1.0f,  0.08f, 0.08f, 0.85f), // red
				"producer" => new Color(0.05f, 0.80f, 0.25f, 0.85f), // green
				_          => new Color(0.2f,  0.2f,  0.2f,  0.6f),  // grey fallback
			};
			roleBg.SetCornerRadiusAll(4);
			roleBg.SetContentMarginAll(4);
			_roleLabel.AddThemeStyleboxOverride("normal", roleBg);
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
	public void UpdateRemotePosition(Vector2 newPosition, string facing = null, bool? flipH = null, bool? isMoving = null)
	{
		if (_isLocalPlayer) return; // Don't override local player position
		_remoteTargetPosition = newPosition;
		
		if (!_hasReceivedFirstSync)
		{
			// Snap strictly on first update to avoid flying in from (0,0)
			Position = newPosition;
			_hasReceivedFirstSync = true;
		}

		if (facing != null) _lastFacingDirection = facing;
		if (flipH.HasValue && _sprite != null) _sprite.FlipH = flipH.Value;
		if (isMoving.HasValue) _isMovingSync = isMoving.Value;
	}

	/// <summary>
	/// Centrally trigger the punching animation for this player (local or remote).
	/// </summary>
	public void TriggerPunchAnimation(string facing = null, bool? flipH = null)
	{
		if (_sprite == null) return;
		
		if (facing != null) _lastFacingDirection = facing;
		if (flipH.HasValue) _sprite.FlipH = flipH.Value;

		_isPunching = true;
		string punchAnim = _lastFacingDirection.Contains("up") ? "punch_back" : "punch_front";
		_sprite.Play(punchAnim);
		if (_animationScales.TryGetValue(punchAnim, out float s)) 
		{
			_sprite.Scale = new Vector2(s, s);
		}
	}

	/// <summary>
	/// Trigger the stabbing animation for this player (Admirer knife kill).
	/// </summary>
	public void TriggerStabAnimation(string facing = null, bool? flipH = null)
	{
		if (_sprite == null) return;
		if (!_sprite.SpriteFrames.HasAnimation("stab_front")) return;

		if (facing != null) _lastFacingDirection = facing;
		if (flipH.HasValue) _sprite.FlipH = flipH.Value;

		_isStabbing = true;
		_isPunching = false; // cancel any punch
		_sprite.Play("stab_front");
		if (_animationScales.TryGetValue("stab_front", out float s))
		{
			_sprite.Scale = new Vector2(s, s);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		// Only process input for the local player
		if (!_isLocalPlayer || !InputEnabled) return;

		// Get movement input
		var velocity = Vector2.Zero;
		if (Input.IsActionPressed("ui_right") || Input.IsKeyPressed(Key.D))
			velocity.X += 1;
		if (Input.IsActionPressed("ui_left") || Input.IsKeyPressed(Key.A))
			velocity.X -= 1;
		if (Input.IsActionPressed("ui_down") || Input.IsKeyPressed(Key.S))
			velocity.Y += 1;
		if (Input.IsActionPressed("ui_up") || Input.IsKeyPressed(Key.W))
			velocity.Y -= 1;

		// If slipping, disable movement entirely
		if (_isSlipping)
		{
			Velocity = Vector2.Zero;
			MoveAndSlide();
			return;
		}

		// If stabbing but a movement key was pressed, cancel the stab animation and let the player walk
		if (_isStabbing && velocity.Length() > 0)
		{
			_isStabbing = false;
			// Fall through to normal movement below
		}
		else if (_isStabbing)
		{
			// Still stabbing and no movement input — keep player frozen
			Velocity = Vector2.Zero;
			MoveAndSlide();
			return;
		}

		// If punching but a movement key was pressed, cancel the punch and let the player walk
		if (_isPunching && velocity.Length() > 0)
		{
			_isPunching = false;
			// Fall through to normal movement below
		}
		else if (_isPunching)
		{
			// Still punching and no movement input — keep player frozen
			Velocity = Vector2.Zero;
			MoveAndSlide();
			return;
		}

		// Handle input
		// (Already checked above for interruption)

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

	// ── Mass Revelation overlay ───────────────────────────────────────────────
	private Tween _massRevTween;

	/// <summary>
	/// Start a looping golden-glow modulate on the sprite to indicate the
	/// Prophet is channeling Mass Revelation. Safe to call on any peer.
	/// </summary>
	public void PlayMassRevelationAnimation()
	{
		if (_sprite == null) return;

		_isMassRevAnimating = true;
		_sprite.Play("mass_revelation");
		if (_animationScales.TryGetValue("mass_revelation", out float s))
		{
			_sprite.Scale = new Vector2(s, s);
		}

		// Kill any leftover tween first
		_massRevTween?.Kill();

		_massRevTween = CreateTween();
		_massRevTween.SetLoops();

		// Pulse between warm gold and bright white-gold
		_massRevTween
			.TweenProperty(_sprite, "modulate",
				new Color(1.4f, 1.1f, 0.2f, 1.0f), 0.35f)
			.SetTrans(Tween.TransitionType.Sine);
		_massRevTween
			.TweenProperty(_sprite, "modulate",
				new Color(1.0f, 0.85f, 0.4f, 1.0f), 0.35f)
			.SetTrans(Tween.TransitionType.Sine);
	}

	/// <summary>
	/// Stop the Mass Revelation glow and restore the sprite's normal modulate.
	/// </summary>
	public void StopMassRevelationAnimation()
	{
		_isMassRevAnimating = false;
		_massRevTween?.Kill();
		_massRevTween = null;

		if (_sprite == null) return;

		// Restore the correct base modulate (local = white, remote = slightly dimmed)
		float alpha = _isGhostMode ? 0.5f : 1.0f;
		_sprite.Modulate = _isLocalPlayer
			? new Color(1f, 1f, 1f, alpha)
			: new Color(0.8f, 0.8f, 0.8f, alpha);

		// Explicitly switch back to idle so the mass_revelation animation
		// never lingers, even if UpdateAnimation() hasn't run yet this frame.
		string idleAnim = _lastFacingDirection switch
		{
			"up"       => "idle_up",
			"up_right" => "idle_up_right",
			"up_left"  => "idle_up_left",
			"down"     => "idle_down",
			"left"     => "idle_left",
			_          => "idle_right",
		};
		if (_sprite.SpriteFrames != null && _sprite.SpriteFrames.HasAnimation(idleAnim))
		{
			_sprite.Play(idleAnim);
			if (_animationScales.TryGetValue(idleAnim, out float s))
				_sprite.Scale = new Vector2(s, s);
		}
	}

	// ── Producer Cash-Trail Aura ───────────────────────────────────────────────
	private Tween _auraSpriteTween;
	private Tween _auraRingTween;
	private Timer _sparkleTimer;

	/// <summary>
	/// Activates a hazy, sparkly green aura around the producer for the duration
	/// of the Cash Trail ultimate. Safe to call on any peer.
	/// </summary>
	public void ShowCashTrailAura()
	{
		StopCashTrailAura(); // clean up any previous (shouldn't exist, but be safe)

		// ── 1. Sprite green-pulse tween ──────────────────────────────────────
		if (_sprite != null)
		{
			_auraSpriteTween = CreateTween();
			_auraSpriteTween.SetLoops();
			_auraSpriteTween
				.TweenProperty(_sprite, "modulate",
					new Color(0.55f, 1.4f, 0.6f, 1.0f), 0.4f)   // saturated lime-green
				.SetTrans(Tween.TransitionType.Sine);
			_auraSpriteTween
				.TweenProperty(_sprite, "modulate",
					new Color(0.3f, 1.0f, 0.4f, 1.0f), 0.4f)    // deep green
				.SetTrans(Tween.TransitionType.Sine);
		}

		// ── 2. Multi-ring haze node ───────────────────────────────────────────
		// ZIndex +1 so rings render IN FRONT of the sprite → always visible from
		// all facing directions regardless of sprite transparency.
		// Radii are scaled to the 243-world-unit character height:
		//   inner  ≈  110  (just outside the silhouette)
		//   mid    ≈  180
		//   outer  ≈  280
		var auraNode = new Node2D();
		auraNode.Name    = "CashAura";
		auraNode.ZIndex  = 1;   // in front of sprite, behind labels (labels default to 0 but are added later)

		// Three layers of filled-disc + arc per ring so they look hazy/glowing.
		// disc = large semi-transparent fill; arc = bright stroke outline.
		var rings = new (float radius, Color discCol, Color arcCol, float arcWidth)[]
		{
			(280f, new Color(0.1f, 0.9f, 0.3f, 0.04f), new Color(0.3f, 1.0f, 0.4f, 0.10f), 22f),  // outer haze
			(180f, new Color(0.2f, 1.0f, 0.4f, 0.07f), new Color(0.4f, 1.0f, 0.5f, 0.16f), 14f),  // mid glow
			(110f, new Color(0.3f, 1.0f, 0.5f, 0.10f), new Color(0.6f, 1.0f, 0.6f, 0.22f), 8f),   // inner core
		};

		foreach (var (radius, discCol, arcCol, arcWidth) in rings)
		{
			var ring = new Node2D();
			float r = radius;
			Color dc = discCol; Color ac = arcCol; float aw = arcWidth;

			ring.Draw += () =>
			{
				ring.DrawCircle(Vector2.Zero, r, dc);          // hazy filled disc
				ring.DrawArc(Vector2.Zero, r, 0f, Mathf.Tau, 64, ac, aw); // bright stroke
			};
			ring.QueueRedraw();
			auraNode.AddChild(ring);

			// Pulse the ring scale so it breathes in and out
			var t = auraNode.CreateTween();
			t.SetLoops();
			float phaseOffset = radius / 500f; // stagger phase by ring size
			t.TweenProperty(ring, "scale", new Vector2(1.08f, 1.08f), 0.7f + phaseOffset)
				.SetTrans(Tween.TransitionType.Sine);
			t.TweenProperty(ring, "scale", new Vector2(0.93f, 0.93f), 0.7f + phaseOffset)
				.SetTrans(Tween.TransitionType.Sine);

			// QueueRedraw is needed every frame because Node2D.Draw doesn't
			// auto-fire when only the transform (scale tween) changes.
			// We attach a lightweight _Process-like timer to keep it live.
			var redrawTimer = new Timer();
			redrawTimer.WaitTime = 0.05; // 20 fps refresh — enough for smooth look
			redrawTimer.Autostart = true;
			redrawTimer.Timeout += () => { if (GodotObject.IsInstanceValid(ring)) ring.QueueRedraw(); };
			ring.AddChild(redrawTimer);
		}

		AddChild(auraNode);

		// ── 3. Sparkle emitter (Timer that spawns rising dots) ────────────────
		_sparkleTimer = new Timer();
		_sparkleTimer.WaitTime = 0.10;
		_sparkleTimer.Autostart = true;
		_sparkleTimer.Timeout += () =>
		{
			if (!IsInsideTree() || !GodotObject.IsInstanceValid(this)) return;
			SpawnAuraSparkle();
		};
		AddChild(_sparkleTimer);
	}

	private void SpawnAuraSparkle()
	{
		// Create a tiny glowing dot that floats upward and fades
		var dot = new Node2D();
		dot.ZIndex = 2; // above rings (ZIndex 1) and sprite

		// Spawn anywhere within the outer ring radius (~280 wu), but at least
		// 40 wu from centre so they don't appear inside the character body.
		float angle = (float)(GD.Randf() * Mathf.Tau);
		float dist  = (float)(GD.Randf() * 200f + 40f);
		dot.Position = new Vector2(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist);

		// Pick a sparkling green-gold hue
		var sparkColors = new Color[]
		{
			new Color(0.4f, 1.0f, 0.5f, 0.95f),  // bright green
			new Color(0.7f, 1.0f, 0.3f, 0.90f),  // yellow-green
			new Color(0.2f, 0.9f, 0.6f, 0.95f),  // teal-green
			new Color(1.0f, 1.0f, 0.4f, 0.85f),  // gold sparkle
		};
		Color chosenColor = sparkColors[(int)(GD.Randf() * sparkColors.Length)];
		float radius = GD.Randf() * 8f + 3f; // 3–11 px

		dot.Draw += () => dot.DrawCircle(Vector2.Zero, radius, chosenColor);
		dot.QueueRedraw();
		AddChild(dot);

		// Tween: float upward and fade out
		float upDist = (float)(GD.Randf() * 100f + 80f); // 80–180 wu rise
		var tween = dot.CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(dot, "position",
			dot.Position + new Vector2((GD.Randf() - 0.5f) * 60f, -upDist), 0.9f)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(dot, "modulate", new Color(1, 1, 1, 0), 0.9f)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		tween.Chain().TweenCallback(Callable.From(() => dot.QueueFree()));
	}

	/// <summary>
	/// Removes the cash-trail aura and restores normal sprite colour.
	/// </summary>
	public void StopCashTrailAura()
	{
		// Kill sprite tween
		_auraSpriteTween?.Kill();
		_auraSpriteTween = null;

		// Remove haze node
		var aura = GetNodeOrNull<Node2D>("CashAura");
		aura?.QueueFree();

		// Stop sparkle timer
		if (_sparkleTimer != null && GodotObject.IsInstanceValid(_sparkleTimer))
		{
			_sparkleTimer.Stop();
			_sparkleTimer.QueueFree();
			_sparkleTimer = null;
		}

		// Restore normal sprite modulate
		if (_sprite != null)
		{
			float alpha = _isGhostMode ? 0.5f : 1.0f;
			_sprite.Modulate = _isLocalPlayer
				? new Color(1f, 1f, 1f, alpha)
				: new Color(0.8f, 0.8f, 0.8f, alpha);
		}
	}

}
