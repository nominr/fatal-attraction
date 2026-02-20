using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// CharacterBody2D-based NPC entity that players can approach and interact with.
/// Handles visual representation, click detection, and collision with world geometry.
/// </summary>
public partial class NPCEntity : CharacterBody2D
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
	private AnimatedSprite2D _sprite;
	private Texture2D _baseTexture; // Store the texture for portrait usage
	private float _frameWidth = 240.0f;
	private Label _nameLabel;
	private CollisionShape2D _collisionShape;
	private Font _customFont;
	
	// State indicators
	private Sprite2D _convertedIndicator;
	private Sprite2D _deadOverlay;
	private Sprite2D _marriedIndicator;
	private Sprite2D _targetIndicator;

	// Interaction range
	private Area2D _interactionArea;
	private bool _playerInRange = false;
	private Label _interactHint;
	private Label _punchHint;

	// Room and corridor definitions
	private struct Room
	{
		public float minX, maxX, minY, maxY;
		public Room(float minX, float maxX, float minY, float maxY)
		{
			this.minX = minX;
			this.maxX = maxX;
			this.minY = minY;
			this.maxY = maxY;
		}
	}

	private struct Corridor
	{
		public float minX, maxX, minY, maxY;
		public Corridor(float minX, float maxX, float minY, float maxY)
		{
			this.minX = minX;
			this.maxX = maxX;
			this.minY = minY;
			this.maxY = maxY;
		}
	}

	private List<Room> _rooms;
	private List<Corridor> _corridors;
	private int _currentRoomIndex = 0; // Track which room NPC is in

	// Wandering AI
	private enum WanderState { Moving, Pausing }
	private WanderState _wanderState = WanderState.Pausing;
	private Vector2 _targetPosition;
	private double _pauseTimer = 0.0;
	private const float MOVE_SPEED = 255.0f; // 1.7x original 150 (approx)
	private const float MIN_PAUSE = 0.5f; // Minimum pause time in seconds
	private const float MAX_PAUSE = 2.0f; // Maximum pause time in seconds
	private const float TARGET_REACHED_THRESHOLD = 17.0f; // 1.7x original 10.0f
	private static readonly Random _random = new Random();

	// Stuck detection
	private Vector2 _lastPosition = Vector2.Zero;
	private double _stuckTimer = 0.0;
	private const float STUCK_DISTANCE_THRESHOLD = 3.0f; // pixels
	private const float STUCK_TIME_THRESHOLD = 0.4f; // seconds (faster reaction)
	
	// Map bounds (encompass all NPC spawn zones globally)
	private Vector2 _mapMin = new Vector2(-300, 160);
	private Vector2 _mapMax = new Vector2(2850, 1740);
	
	// Alive status (dead NPCs don't wander)
	private bool _isAlive = true;

	// Client-side interpolation
	private Vector2 _clientTargetPosition;
	private bool _hasReceivedFirstSync = false;

	// Temporary movement disable (slip on banana)
	private bool _isSlipping = false;
	private double _slipTimer = 0.0;
	
	// Interview immobilization
	private bool _isFrozen = false;

	// Animation state
	private Dictionary<string, float> _animationScales = new Dictionary<string, float>();
	private string _lastFacingHorizontal = "right"; // "left" or "right"

	public override void _Ready()
	{
		// Enable Y-sort for proper overlap rendering (NPCs further down screen render in front)
		YSortEnabled = true;
		
		// Load custom font
		_customFont = ResourceLoader.Load<Font>("res://assets/Pixer-Regular.otf");
		
		// Initialize room and corridor definitions
		_rooms = new List<Room>
		{
			new Room(50, 1000, 175, 400),       // Room 0
			new Room(-300, 2700, 930, 950),     // Room 1
			new Room(2500, 2850, 1450, 1750),   // Room 2 (Fixed height)
			new Room(580, 2030, 1450, 1740),    // Room 3
			new Room(1600, 2300, 160, 440)      // Room 4
		};

		_corridors = new List<Corridor>
		{
			new Corridor(1564, 1612, 547, 900),   // Corridor 0
			new Corridor(156, 204, 547, 900),     // Corridor 1
			new Corridor(924, 972, 1056, 1370),   // Corridor 2
			new Corridor(2716, 2764, 1056, 1370)  // Corridor 3
		};

		SetupVisuals();
		SetupCollision();
		SetupInteractionArea();
		
		// Connect input
		InputEvent += OnInputEvent;
		
		// Configure collision layers
		// Layer 4 (bit 2): NPCs (separate from players)
		// Mask 1 (bit 0): Walls/World only — NPCs pass through each other and players
		CollisionLayer = 4;
		CollisionMask = 1;
		
		GD.Print($"NPCEntity: Set collision layer={CollisionLayer}, mask={CollisionMask}");
		
		// Determine starting room
		_currentRoomIndex = GetRoomAtPosition(Position);

		// Init last position for stuck detection
		_lastPosition = Position;
		
		// Start with a random pause before first movement for all NPCs
		_pauseTimer = (float)(_random.NextDouble() * (MAX_PAUSE - MIN_PAUSE) + MIN_PAUSE);
		_targetPosition = Position;
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
				// Restore upright rotation only if not dead
				if (_isAlive && _sprite != null)
				{
					_sprite.RotationDegrees = 0;
				}
			}
		}

		UpdateWandering(delta);
		UpdateAnimation();
	}

	private void UpdateAnimation()
	{
		if (_sprite == null) return;
		if (_isSlipping || !_isAlive) 
		{
			_sprite.Pause();
			return;
		}

		Vector2 velocity = Velocity;
		string animToPlay = _sprite.Animation;
		
		// Use a small threshold to detect movement
		if (velocity.Length() > 5.0f)
		{
			if (Mathf.Abs(velocity.X) > Mathf.Abs(velocity.Y))
			{
				// Horizontal movement
				if (velocity.X > 0)
				{
					animToPlay = "walk_right";
					_sprite.FlipH = true; // Swap
					_lastFacingHorizontal = "right";
				}
				else
				{
					animToPlay = "walk_left";
					_sprite.FlipH = false; // Swap
					_lastFacingHorizontal = "left";
				}
			}
			else
			{
				// Vertical movement
				if (velocity.Y > 0)
				{
					animToPlay = "walk_down";
					_sprite.FlipH = false;
				}
				else
				{
					animToPlay = "walk_up";
					_sprite.FlipH = false;
				}
			}
		}
		else
		{
			// Idle
			if (_lastFacingHorizontal == "right")
			{
				animToPlay = "idle_right";
				_sprite.FlipH = true; // Swap
			}
			else
			{
				animToPlay = "idle_left";
				_sprite.FlipH = false; // Swap
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

	private void UpdateWandering(double delta)
	{
		// Debug: Always log frozen state for target NPCs
		if ((NpcId == "john" || NpcId == "rebecca" || NpcId == "marcus") && Multiplayer.IsServer())
		{
			GD.Print($"[NPCEntity] UpdateWandering for {NpcId}: _isFrozen={_isFrozen}, _isSlipping={_isSlipping}");
		}
		
		// If slipping or frozen (interview), don't move
		if (_isSlipping || _isFrozen)
		{
			if (_isFrozen && Multiplayer.IsServer())
			{
				// Debug: Log when frozen NPC tries to move
				GD.Print($"[NPCEntity] {NpcId} is frozen, skipping movement");
			}
			return;
		}

		// CLIENTS DO NOT RUN AI - they are synced by server
		if (Multiplayer.MultiplayerPeer == null || !Multiplayer.IsServer())
		{
			if (_hasReceivedFirstSync)
			{
				// Interpolate towards target
				// Use a factor that depends on delta to be frame-rate independent
				// A factor of 10.0f * delta gives quick but smooth catch-up
				Position = Position.Lerp(_clientTargetPosition, 10.0f * (float)delta);
			}
			return; // Clients only interpolate, they don't run AI
		}

		// Dead NPCs don't wander
		if (!_isAlive) return;
		
		// No special hallway handling; all NPCs use corridor/room rules
		
		switch (_wanderState)
		{
			case WanderState.Pausing:
				_pauseTimer -= delta;
				if (_pauseTimer <= 0)
				{
					// Done pausing, pick a new target and start moving
					PickNewTarget();
					_wanderState = WanderState.Moving;
				}
				break;

			case WanderState.Moving:
				// Corridor waypoint rule: if near entry points, walk straight to exit
				Vector2 corridorTarget = Vector2.Zero;
				bool inCorridorTransit = false;
				
				// Check proximity to each entry point (within 50px)
				if (Position.DistanceTo(new Vector2(177, 517)) < 50f)
				{
					corridorTarget = new Vector2(177, 946);
					inCorridorTransit = true;
				}
				else if (Position.DistanceTo(new Vector2(1579, 510)) < 50f)
				{
					corridorTarget = new Vector2(1579, 946);
					inCorridorTransit = true;
				}
				else if (Position.DistanceTo(new Vector2(2741, 1418)) < 50f)
				{
					corridorTarget = new Vector2(2741, 968);
					inCorridorTransit = true;
				}
				else if (Position.DistanceTo(new Vector2(959, 1418)) < 50f)
				{
					corridorTarget = new Vector2(959, 968);
					inCorridorTransit = true;
				}
				
				if (inCorridorTransit)
				{
					// Axis-Aligned Movement for Corridors
					// First align X (center in corridor), then move Y (traverse).
					float xDiff = corridorTarget.X - Position.X;
					float yDiff = corridorTarget.Y - Position.Y;

					// Threshold for X alignment
					if (Mathf.Abs(xDiff) > 5.0f)
					{
						// Move Horizontally
						Velocity = new Vector2(Mathf.Sign(xDiff), 0) * MOVE_SPEED;
					}
					else
					{
						// Move Vertically
						Velocity = new Vector2(0, Mathf.Sign(yDiff)) * MOVE_SPEED;
					}

					MoveAndSlide();
					
					// Once close to exit, resume normal movement
					if (Position.DistanceTo(corridorTarget) < 10f)
					{
						Position = corridorTarget;
						PickNewTarget(); // Pick new target in room
					}
					break;
				}

				// Normal room movement
				Vector2 direction = (_targetPosition - Position).Normalized();
				float distanceToTarget = Position.DistanceTo(_targetPosition);
				if (distanceToTarget <= TARGET_REACHED_THRESHOLD)
				{
					Position = _targetPosition;
					Velocity = Vector2.Zero;
					_wanderState = WanderState.Pausing;
					_pauseTimer = (float)(_random.NextDouble() * (MAX_PAUSE - MIN_PAUSE) + MIN_PAUSE);
				}
				else
				{
					Velocity = direction * MOVE_SPEED;

					// Slide Assist: If we hit a wall last frame, redirect velocity along the wall
					// to prevent "sticking" or slowing down.
					if (GetSlideCollisionCount() > 0)
					{
						var collision = GetSlideCollision(0);
						// Only if hitting a wall (Layer 1)
						if ((collision.GetCollider() as Node)?.IsInGroup("players") == false && !(collision.GetCollider() is NPCEntity))
						{
							// Project velocity onto the wall plane to get "slide" vector
							Vector2 normal = collision.GetNormal();
							Vector2 slide = Velocity.Slide(normal);
							// Preserve speed (sprint along the wall)
							Velocity = slide.Normalized() * MOVE_SPEED;
						}
					}

					MoveAndSlide();

					// INTEGRATED STUCK CHECK:
					// 1. Immediate "Vibration" Check: Touching wall + Low Speed = Jammed
					if (GetSlideCollisionCount() > 0 && Velocity.Length() < 5.0f)
					{
						// We are pushing a wall and not moving -> VIBRATING
						_stuckTimer = STUCK_TIME_THRESHOLD; // Force stuck trigger immediately
					}

					// 2. Positional Stuck Check (Corner Trap)
					if (Position.DistanceTo(_lastPosition) < STUCK_DISTANCE_THRESHOLD)
					{
						_stuckTimer += delta; 
					}
					else 
					{ 
						_stuckTimer = 0; 
						_lastPosition = Position; 
					}

					// Trigger Retargeting
					if (_stuckTimer >= STUCK_TIME_THRESHOLD)
					{
						// Pick a new random target instantly to break the loop
						PickNewTarget();
						_stuckTimer = 0.0;
						// Also reset state to Pause briefly to let physics settle? No, keep moving to break free.
						// actually, let's just pick and go.
					}
				}
				break;
		}
	}

	private void PickNewTarget()
	{
		// Update current room based on position
		_currentRoomIndex = GetRoomAtPosition(Position);

		// 30% chance to move to a corridor (if not already in one)
		if (_random.NextDouble() < 0.3 && _currentRoomIndex >= 0 && _currentRoomIndex < _rooms.Count)
		{
			// Pick a random corridor
			int corridorIndex = _random.Next(_corridors.Count);
			Corridor corridor = _corridors[corridorIndex];
			
			// Pick a random point in the corridor
			float x = (float)(_random.NextDouble() * (corridor.maxX - corridor.minX) + corridor.minX);
			float y = (float)(_random.NextDouble() * (corridor.maxY - corridor.minY) + corridor.minY);
			_targetPosition = new Vector2(x, y);
			
			GD.Print($"{NpcId} heading to corridor {corridorIndex}");
		}
		else
		{
			// Pick a random position within current room with PADDING to avoid walls
			if (_currentRoomIndex >= 0 && _currentRoomIndex < _rooms.Count)
			{
				Room room = _rooms[_currentRoomIndex];
				float padding = 100.0f; 

				float rMinX = room.minX + padding;
				float rMaxX = room.maxX - padding;
				float rMinY = room.minY + padding;
				float rMaxY = room.maxY - padding;

				// Safety check if room is too small for padding
				if (rMinX >= rMaxX) { rMinX = room.minX; rMaxX = room.maxX; }
				if (rMinY >= rMaxY) { rMinY = room.minY; rMaxY = room.maxY; }

				float x = (float)(_random.NextDouble() * (rMaxX - rMinX) + rMinX);
				float y = (float)(_random.NextDouble() * (rMaxY - rMinY) + rMinY);
				_targetPosition = new Vector2(x, y);
			}
			else
			{
				// Fallback if position is in no room (shouldn't happen)
				float x = (float)(_random.NextDouble() * (_mapMax.X - _mapMin.X) + _mapMin.X);
				float y = (float)(_random.NextDouble() * (_mapMax.Y - _mapMin.Y) + _mapMin.Y);
				_targetPosition = new Vector2(x, y);
			}
		}
	}

	private int GetRoomAtPosition(Vector2 pos)
	{
		for (int i = 0; i < _rooms.Count; i++)
		{
			Room room = _rooms[i];
			if (pos.X >= room.minX && pos.X <= room.maxX &&
				pos.Y >= room.minY && pos.Y <= room.maxY)
			{
				return i;
			}
		}
		return -1; // Not in any room
	}

	private int GetCorridorAtPosition(Vector2 pos)
	{
		for (int i = 0; i < _corridors.Count; i++)
		{
			Corridor c = _corridors[i];
			if (pos.X >= c.minX && pos.X <= c.maxX &&
				pos.Y >= c.minY && pos.Y <= c.maxY)
			{
				return i;
			}
		}
		return -1; // Not in any corridor
	}

	private void SetupVisuals()
	{
		// Main NPC sprite - load from file based on NPC ID
		_sprite = new AnimatedSprite2D();
		_sprite.YSortEnabled = true; // Participate in Y-sort
		LoadSpriteForNPC();
		AddChild(_sprite);

		// Name label above the NPC (white color on transparent grey background)
		_nameLabel = new Label();
		_nameLabel.Text = NpcName;
		_nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_nameLabel.AddThemeColorOverride("font_color", Colors.Black);
		_nameLabel.AddThemeFontOverride("font", _customFont);
		_nameLabel.AddThemeFontSizeOverride("font_size", 28);
		// Add transparent grey background
		var bgStyle = new StyleBoxFlat();
		bgStyle.BgColor = new Color(1.0f, 1.0f, 1.0f, 0.7f); // Transparent white/light
		bgStyle.SetCornerRadiusAll(4);
		bgStyle.SetContentMarginAll(4);
		_nameLabel.AddThemeStyleboxOverride("normal", bgStyle);
		AddChild(_nameLabel);
		// Center the label over the NPC based on text width
		CallDeferred(MethodName.CenterNameLabel);
		// Size will auto-adjust based on text content

		// Punch hint above NPC name (for Admirer targets)
		_punchHint = new Label();
		_punchHint.Text = "Press P to Punch";
		_punchHint.HorizontalAlignment = HorizontalAlignment.Center;
		_punchHint.AddThemeColorOverride("font_color", Colors.Yellow);
		_punchHint.AddThemeFontOverride("font", _customFont);
		_punchHint.AddThemeFontSizeOverride("font_size", 22);
		// Add transparent grey background
		var punchBgStyle = new StyleBoxFlat();
		punchBgStyle.BgColor = new Color(0.2f, 0.2f, 0.2f, 0.6f);
		punchBgStyle.SetCornerRadiusAll(4);
		punchBgStyle.SetContentMarginAll(4);
		_punchHint.AddThemeStyleboxOverride("normal", punchBgStyle);
		_punchHint.Visible = false;
		AddChild(_punchHint);
		CallDeferred(MethodName.CenterPunchHint);

		// Status indicators (hidden by default)
		// Converted (Halo) - Above head (approx -110)
		_convertedIndicator = CreateStatusIndicator("res://assets/halo.png", new Vector2(0, -110));
		
		// Married (Heart) - Above head (approx -110)
		_marriedIndicator = CreateStatusIndicator("res://assets/marry-heart.png", new Vector2(0, -110));

		// Target indicator (for Admirer targets) - DISABLED to avoid visual confusion with camera UI
		// _targetIndicator = CreateStatusIndicator("res://assets/editorial-focus.png", new Vector2(0, -110));
		_targetIndicator = new Sprite2D(); // Create dummy sprite to avoid null reference
		_targetIndicator.Visible = false;
		AddChild(_targetIndicator);

		// Dead Overlay (Darkens sprite)
		_deadOverlay = CreateDeadOverlay();

		// Interact hint below NPC (white color on transparent grey background)
		_interactHint = new Label();
		_interactHint.Text = "Click to interact";
		_interactHint.HorizontalAlignment = HorizontalAlignment.Center;
		_interactHint.AddThemeColorOverride("font_color", Colors.White);
		_interactHint.AddThemeFontOverride("font", _customFont);
		_interactHint.AddThemeFontSizeOverride("font_size", 24);
		// Add transparent grey background
		var interactBgStyle = new StyleBoxFlat();
		interactBgStyle.BgColor = new Color(0.2f, 0.2f, 0.2f, 0.6f); // Transparent grey
		interactBgStyle.SetCornerRadiusAll(4);
		interactBgStyle.SetContentMarginAll(4);
		_interactHint.AddThemeStyleboxOverride("normal", interactBgStyle);
		_interactHint.Visible = false;
		AddChild(_interactHint);
		// Center the label over the NPC based on text width
		CallDeferred(MethodName.CenterInteractHint);
		// Size will auto-adjust based on text content
	}

	private void LoadSpriteForNPC()
	{
		if (_sprite == null) return;

		string id = NpcId.ToLower();
		string npcAsset = "npc1"; // Default

		// Map NPC Names to npc1-10 assets
		if (id == "katy") npcAsset = "npc1";
		else if (id == "bella") npcAsset = "npc2";
		else if (id == "rebecca") npcAsset = "npc3";
		else if (id == "diana") npcAsset = "npc4";
		else if (id == "sofia") npcAsset = "npc5";
		else if (id == "john") npcAsset = "npc6";
		else if (id == "chris") npcAsset = "npc7";
		else if (id == "marcus") npcAsset = "npc8";
		else if (id == "eli") npcAsset = "npc9";
		else if (id == "amir") npcAsset = "npc10";
		else
		{
			// Use hash to deterministically assign one of the 10 NPCs for others
			int hash = Math.Abs(NpcId.GetHashCode());
			npcAsset = $"npc{(hash % 10) + 1}";
		}

		var frames = new SpriteFrames();
		string basePath = "res://assets/new-character-assets/";
		
		// Helper to load frames from a split texture (6x6 grid, limit to 35)
		void AddAnimationFrames(string animName, string path)
		{
			var tex = GD.Load<Texture2D>(path);
			if (tex == null) return;

			if (!frames.HasAnimation(animName))
				frames.AddAnimation(animName);
			
			float width = tex.GetWidth();
			float height = tex.GetHeight();
			
			// New isometric assets are 6x6 grids (36 frames total)
			int gridCols = 6;
			int gridRows = 6;
			float frameWidth = width / gridCols;
			float frameHeight = height / gridRows;

			if (animName == "idle_right") _frameWidth = frameWidth;

			int totalAdded = 0;
			for (int y = 0; y < gridRows; y++)
			{
				for (int x = 0; x < gridCols; x++)
				{
					if (totalAdded >= 35) break;

					var atlasKey = new AtlasTexture();
					atlasKey.Atlas = tex;
					atlasKey.Region = new Rect2(x * frameWidth, y * frameHeight, frameWidth, frameHeight);
					frames.AddFrame(animName, atlasKey);
					totalAdded++;
				}
				if (totalAdded >= 35) break;
			}
			
			frames.SetAnimationLoop(animName, true);
			frames.SetAnimationSpeed(animName, animName.Contains("idle") ? 10.0f : 15.0f); // 36 frames need higher speed

			// Calculate and store scale for this specific animation to ensure 243 world unit height
			float targetWorldHeight = 243.0f;
			frameHeight = height / gridRows;
			_animationScales[animName] = targetWorldHeight / frameHeight;

			// For portrait/base reference, use the front-idle texture
			if (animName == "idle_right") _baseTexture = tex;
		}

		AddAnimationFrames("walk_down", $"{basePath}{npcAsset}-front-walk.png");
		AddAnimationFrames("walk_up", $"{basePath}{npcAsset}-back-walk.png");
		AddAnimationFrames("walk_right", $"{basePath}{npcAsset}-front-walk.png");
		AddAnimationFrames("walk_left", $"{basePath}{npcAsset}-front-walk.png");
		
		AddAnimationFrames("idle_right", $"{basePath}{npcAsset}-front-idle.png");
		AddAnimationFrames("idle_left", $"{basePath}{npcAsset}-front-idle.png");
		AddAnimationFrames("idle_up", $"{basePath}{npcAsset}-back-idle.png");

		_sprite.SpriteFrames = frames;
		
		_sprite.Play("idle_right");
		// Apply initial scale
		if (_animationScales.TryGetValue("idle_right", out float s)) _sprite.Scale = new Vector2(s, s);

		GD.Print($"Loaded split isometric animated sprites for NPC {NpcId} as {npcAsset}");
	}

	private Sprite2D CreateStatusIndicator(string texturePath, Vector2 offset)
	{
		var indicator = new Sprite2D();
		var texture = ResourceLoader.Load<Texture2D>(texturePath);
		if (texture != null)
		{
			indicator.Texture = texture;
			// Scale sprites to match character pixel grid (6.8x for 1.7 scale)
			indicator.Scale = new Vector2(6.8f, 6.8f); 
		}
		indicator.TextureFilter = TextureFilterEnum.Nearest;
		indicator.Position = offset;
		indicator.YSortEnabled = true; // Participate in parent's Y-sortPC
		indicator.ZIndex = 1; // Render above the NPC sprite but respect parent layering
		AddChild(indicator);
		return indicator;
	}

	private Sprite2D CreateDeadOverlay()
	{
		var indicator = new Sprite2D();
		var texture = new GradientTexture2D();
		texture.Width = 32;
		texture.Height = 32;
		texture.Fill = GradientTexture2D.FillEnum.Radial;
		texture.FillFrom = new Vector2(0.5f, 0.5f);
		texture.FillTo = new Vector2(1f, 0.5f);
		var gradient = new Gradient();
		gradient.SetColor(0, Colors.Black.Lerp(Colors.Transparent, 0.3f));
		gradient.SetColor(1, Colors.Black.Lerp(Colors.Transparent, 0.8f));
		texture.Gradient = gradient;
		indicator.Texture = texture;
		indicator.Scale = new Vector2(6.8f, 6.8f); // Cover the whole sprite
		indicator.Visible = false;
		AddChild(indicator);
		return indicator;
	}

	private void SetupCollision()
	{
		// Create collision shape matching player controller (37x200 rectangle)
		_collisionShape = new CollisionShape2D();
		var shape = new RectangleShape2D();
		// Reduced width to 37 to fit in corridors while increased height to 200
		shape.Size = new Vector2(37, 200);
		_collisionShape.Shape = shape;
		AddChild(_collisionShape);
		
		InputPickable = true;
	}

	private void SetupInteractionArea()
	{
		// Larger area for detecting when player is in range
		_interactionArea = new Area2D();
		_interactionArea.Name = "InteractionArea";
		// Monitor Layer 2 (NPCs/Players)
		_interactionArea.CollisionMask = 2;
		
		var collisionShape = new CollisionShape2D();
		var shape = new CircleShape2D();
		shape.Radius = 200; // 1.7x original
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
			// Check both visual distance AND proximity flag
			if (dist < 80 && _playerInRange)
			{
				GD.Print($"Fallback click detected! Dist: {dist}, InRange: {_playerInRange}");
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
			
			// Just show hint, don't auto-interact (player must click)
			if (body is PlayerController player && player.IsLocalPlayer)
			{
				_interactHint.Visible = true;
				GD.Print($"Local player entered {NpcName} range, showing hint");
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
	
	public Texture2D GetPortraitTexture()
	{
		if (_baseTexture != null)
		{
			// Return an atlas texture of the first frame (Idle Right 0)
			// effectively a "mugshot"
			var atlas = new AtlasTexture();
			atlas.Atlas = _baseTexture;
			atlas.Region = new Rect2(0, 0, _frameWidth, _baseTexture.GetHeight());
			return atlas;
		}
		return null;
	}

	/// <summary>
	/// Update the visual state of this NPC based on game state
	/// </summary>
	public void UpdateState(bool alive, bool converted, bool married, bool isTarget = false)
	{
		_isAlive = alive;
		_deadOverlay.Visible = !alive;
		_convertedIndicator.Visible = converted;
		_marriedIndicator.Visible = false; // Marriage removed from game
		_targetIndicator.Visible = isTarget;
		if (!alive && _punchHint != null)
		{
			_punchHint.Visible = false;
		}
		
		// Dim the sprite if dead
		_sprite.Modulate = alive ? Colors.White : Colors.DarkGray;
		
		// Rotate sprite 90 degrees clockwise if dead. If alive and currently slipping, preserve slip rotation.
		if (!alive)
		{
			_sprite.RotationDegrees = 90;
		}
		else if (!_isSlipping)
		{
			_sprite.RotationDegrees = 0;
		}
		
		// Disable interaction if dead
		InputPickable = alive;
	}

	/// <summary>
	/// Show or hide the punch hint label.
	/// </summary>
	public void SetPunchHintVisible(bool visible)
	{
		if (_punchHint != null)
		{
			_punchHint.Visible = visible;
		}
	}

	/// <summary>
	/// Flash the NPC red briefly to indicate damage.
	/// </summary>
	public void FlashDamage(double seconds = 0.5)
	{
		if (_sprite == null) return;
		_sprite.Modulate = Colors.Red;
		var timer = GetTree().CreateTimer(Math.Max(0.1, seconds));
		timer.Timeout += () =>
		{
			if (!IsInstanceValid(this) || _sprite == null) return;
			_sprite.Modulate = _isAlive ? Colors.White : Colors.DarkGray;
		};
	}

	/// <summary>
	/// Set the NPC color (useful for distinguishing different NPCs)
	/// </summary>
	public void SetColor(Color color)
	{
		NpcColor = color;
		// AnimatedSprite2D doesn't easily support gradient override without shaders. 
		// We can just tint it if needed, but for now we skip specific color overrides to preserve sprite colors.
	}

	/// <summary>
	/// Called by GameWorld on clients to update target position for interpolation
	/// </summary>
	public void SyncPosition(Vector2 pos)
	{
		_clientTargetPosition = pos;
		if (!_hasReceivedFirstSync)
		{
			// Snap strictly on first update to avoid flying in from (0,0)
			Position = pos;
			_hasReceivedFirstSync = true;
		}
	}

	/// <summary>
	/// Temporarily disable movement and rotate 90 degrees clockwise for the specified duration.
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
	/// Center the name label horizontally over the NPC based on its actual width.
	/// Called deferred to ensure the label has been sized.
	/// </summary>
	private void CenterNameLabel()
	{
		if (_nameLabel != null)
		{
			var labelWidth = _nameLabel.Size.X;
			// Position higher (-200) to be above the status icons which are at -110
			_nameLabel.Position = new Vector2(-labelWidth / 2 - 3, -200);
		}
	}

	/// <summary>
	/// Center the punch hint horizontally over the NPC based on its actual width.
	/// Called deferred to ensure the label has been sized.
	/// </summary>
	private void CenterPunchHint()
	{
		if (_punchHint != null)
		{
			var labelWidth = _punchHint.Size.X;
			_punchHint.Position = new Vector2(-labelWidth / 2, -330);
		}
	}

	/// <summary>
	/// Center the interact hint horizontally over the NPC based on its actual width.
	/// Called deferred to ensure the label has been sized.
	/// </summary>
	private void CenterInteractHint()
	{
		if (_interactHint != null)
		{
			var labelWidth = _interactHint.Size.X;
			_interactHint.Position = new Vector2(-labelWidth / 2, 120);
		}
	}

	/// <summary>
	/// Freezes the NPC (stops wandering) for interviews or other events.
	/// </summary>
	public void SetFrozen(bool frozen)
	{
		GD.Print($"[NPCEntity] SetFrozen called on {NpcId}: {frozen} (was {_isFrozen})");
		if (!frozen && (NpcId == "john" || NpcId == "rebecca" || NpcId == "marcus"))
		{
			// Print where unfreeze is coming from
			GD.Print($"[NPCEntity] WARNING: UNFREEZING target NPC {NpcId}");
		}
		_isFrozen = frozen;
		if (frozen)
		{
			// Optional: Stop current velocity
			Velocity = Vector2.Zero;
			GD.Print($"[NPCEntity] {NpcId} velocity set to zero, _isFrozen is now {_isFrozen}");
		}
	}
	public void UpdateNameTagColor(Color bgColor)
	{
		if (_nameLabel == null) return;
		
		// Update Background
		var style = _nameLabel.GetThemeStylebox("normal") as StyleBoxFlat;
		if (style != null)
		{
			style.BgColor = bgColor;
		}
		else
		{
			// If style is missing or not flat, create new
			var bgStyle = new StyleBoxFlat();
			bgStyle.BgColor = bgColor;
			bgStyle.SetCornerRadiusAll(4);
			bgStyle.SetContentMarginAll(4);
			_nameLabel.AddThemeStyleboxOverride("normal", bgStyle);
		}
		
		// Always use Black text
		_nameLabel.AddThemeColorOverride("font_color", Colors.Black);
	}
}
