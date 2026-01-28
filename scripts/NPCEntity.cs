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
	private Sprite2D _sprite;
	private Label _nameLabel;
	private CollisionShape2D _collisionShape;
	private Font _customFont;
	
	// State indicators
	private Sprite2D _convertedIndicator;
	private Sprite2D _deadOverlay;
	private Sprite2D _marriedIndicator;

	// Interaction range
	private Area2D _interactionArea;
	private bool _playerInRange = false;
	private Label _interactHint;

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
	private const float MOVE_SPEED = 150.0f; // Pixels per second (increased from 50)
	private const float MIN_PAUSE = 0.5f; // Minimum pause time in seconds (decreased from 2.0)
	private const float MAX_PAUSE = 2.0f; // Maximum pause time in seconds (decreased from 5.0)
	private const float TARGET_REACHED_THRESHOLD = 10.0f; // How close to target to consider "arrived"
	private static readonly Random _random = new Random();

	// Stuck detection
	private Vector2 _lastPosition = Vector2.Zero;
	private double _stuckTimer = 0.0;
	private const float STUCK_DISTANCE_THRESHOLD = 3.0f; // pixels
	private const float STUCK_TIME_THRESHOLD = 0.8f; // seconds
	
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

	public override void _Ready()
	{
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
	}

	private void UpdateWandering(double delta)
	{
		// If slipping, don't move
		if (_isSlipping) return;

		// CLIENTS DO NOT RUN AI - they are synced by server
		if (!Multiplayer.IsServer())
		{
			if (_hasReceivedFirstSync)
			{
				// Interpolate towards target
				// Use a factor that depends on delta to be frame-rate independent
				// A factor of 10.0f * delta gives quick but smooth catch-up
				Position = Position.Lerp(_clientTargetPosition, 10.0f * (float)delta);
			}
			return;
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
					// Walk straight to corridor exit
					Vector2 dir = (corridorTarget - Position).Normalized();
					Velocity = dir * MOVE_SPEED;
					MoveAndCollide(Velocity * (float)delta);
					
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
					var collision = MoveAndCollide(Velocity * (float)delta);
					if (collision != null)
					{
						var n = collision.GetNormal();
						Vector2 tangent = new Vector2(-n.Y, n.X);
						float sign = _random.Next(2) == 0 ? -1f : 1f;
						_targetPosition = Position + tangent.Normalized() * 120f * sign;
					}
					if (Position.DistanceTo(_lastPosition) < STUCK_DISTANCE_THRESHOLD) _stuckTimer += delta; else { _stuckTimer = 0; _lastPosition = Position; }
					if (_stuckTimer >= STUCK_TIME_THRESHOLD)
					{
						if (_currentRoomIndex >= 0 && _currentRoomIndex < _rooms.Count)
						{
							Room r = _rooms[_currentRoomIndex];
							float x = (float)(_random.NextDouble() * (r.maxX - r.minX) + r.minX);
							float y = (float)(_random.NextDouble() * (r.maxY - r.minY) + r.minY);
							_targetPosition = new Vector2(x, y);
						}
						_stuckTimer = 0.0;
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
			// Pick a random position within current room
			if (_currentRoomIndex >= 0 && _currentRoomIndex < _rooms.Count)
			{
				Room room = _rooms[_currentRoomIndex];
				float x = (float)(_random.NextDouble() * (room.maxX - room.minX) + room.minX);
				float y = (float)(_random.NextDouble() * (room.maxY - room.minY) + room.minY);
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
		_sprite = new Sprite2D();
		LoadSpriteForNPC();
		AddChild(_sprite);

		// Name label above the NPC (white color on transparent grey background)
		_nameLabel = new Label();
		_nameLabel.Text = NpcName;
		_nameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_nameLabel.AddThemeColorOverride("font_color", Colors.White);
		_nameLabel.AddThemeFontOverride("font", _customFont);
		_nameLabel.AddThemeFontSizeOverride("font_size", 28);
		// Add transparent grey background
		var bgStyle = new StyleBoxFlat();
		bgStyle.BgColor = new Color(0.2f, 0.2f, 0.2f, 0.6f); // Transparent grey
		bgStyle.SetCornerRadiusAll(4);
		bgStyle.SetContentMarginAll(4);
		_nameLabel.AddThemeStyleboxOverride("normal", bgStyle);
		AddChild(_nameLabel);
		// Center the label over the NPC based on text width
		CallDeferred(MethodName.CenterNameLabel);
		// Size will auto-adjust based on text content

		// Status indicators (hidden by default)
		// Converted (Halo) - Above head (e.g., -60 relative to center)
		_convertedIndicator = CreateStatusIndicator("res://assets/halo.png", new Vector2(0, -75));
		
		// Married (Heart) - Above head (slightly offset or overlapping halo if both?)
		// Let's put it slightly higher or same spot.
		_marriedIndicator = CreateStatusIndicator("res://assets/marry-heart.png", new Vector2(0, -95));

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
		// Map NPC IDs to sprite numbers (1-10)
		var npcSpriteMap = new System.Collections.Generic.Dictionary<string, int>
		{
			{ "katy", 1 },
			{ "john", 2 },
			{ "rebecca", 3 },
			{ "marcus", 4 },
			{ "sofia", 5 },
			{ "amir", 6 },
			{ "bella", 7 },
			{ "chris", 8 },
			{ "diana", 9 },
			{ "eli", 10 }
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

	private Sprite2D CreateStatusIndicator(string texturePath, Vector2 offset)
	{
		var indicator = new Sprite2D();
		var texture = ResourceLoader.Load<Texture2D>(texturePath);
		if (texture != null)
		{
			indicator.Texture = texture;
			// Scale sprites to match character pixel grid (4x)
			indicator.Scale = new Vector2(4.0f, 4.0f); 
		}
		indicator.TextureFilter = TextureFilterEnum.Nearest;
		indicator.Position = offset;
		indicator.Visible = false;
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
		indicator.Scale = new Vector2(4.0f, 4.0f); // Cover the whole sprite
		indicator.Visible = false;
		AddChild(indicator);
		return indicator;
	}

	private void SetupCollision()
	{
		// Create collision shape matching player controller (80x128 rectangle)
		_collisionShape = new CollisionShape2D();
		var shape = new RectangleShape2D();
		shape.Size = new Vector2(80, 128);
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

	/// <summary>
	/// Update the visual state of this NPC based on game state
	/// </summary>
	public void UpdateState(bool alive, bool converted, bool married)
	{
		_isAlive = alive;
		_deadOverlay.Visible = !alive;
		_convertedIndicator.Visible = converted;
		_marriedIndicator.Visible = married;
		
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
			_nameLabel.Position = new Vector2(-labelWidth / 2 - 3, -115);
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
			_interactHint.Position = new Vector2(-labelWidth / 2, 70);
		}
	}
}
