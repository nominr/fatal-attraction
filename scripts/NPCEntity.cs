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
	private static Font _sharedFont;
	private static readonly Dictionary<string, SpriteFrames> _spriteFramesCache = new();
	private static readonly Dictionary<string, Dictionary<string, float>> _animationScalesCache = new();
	private static readonly Dictionary<string, Texture2D> _baseTextureCache = new();
	private static readonly Dictionary<string, float> _frameWidthCache = new();
	
	// State indicators
	private Sprite2D _convertedIndicator;
	private Node2D _deadOverlay;
	private Sprite2D _marriedIndicator;
	private Sprite2D _targetIndicator;
	private Sprite2D _bloodBodyOverlay;
	private static Texture2D _midtermBloodSheet;

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
	private const float STUCK_DISTANCE_THRESHOLD = 5.0f;  // pixels — raised to avoid false triggers
	private const float STUCK_TIME_THRESHOLD    = 0.75f; // seconds — raised so brief wall contacts don't trigger
	
	// Backoff-escape: when stuck, move away from wall briefly before retargeting
	private double  _stuckBackoffTimer = 0.0;
	private Vector2 _stuckBackoffDir   = Vector2.Zero;
	private const float BACKOFF_DURATION = 0.35f; // seconds to back away from wall
	
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

	// Punch stun
	private bool _isStunned = false;
	private double _stunTimer = 0.0;

	// ── Mass Revelation march override ─────────────────────────────────────────
	private bool    _isMarchingToTarget = false;
	private Vector2 _marchTarget        = Vector2.Zero;
	private float   _marchSpeed         = 0f;

	// ── Flee from threat (Admirer knife) ──────────────────────────────────────
	private bool    _isFleeing       = false;
	private double  _fleeTimer       = 0.0;
	private Vector2 _fleeFromPos     = Vector2.Zero;
	private const float FLEE_SPEED   = 510f;   // 2x normal NPC speed
	private const float FLEE_DURATION = 3.0f;  // seconds to run

	// ── Admirer UI State ───────────────────────────────────────────────────────
	private Label   _stabLabel;
	private int     _stabCount        = 0;
	private double  _stabWindowTimer  = 0.0;
	private const int ADMIRER_STABS_TO_KILL = 3;

	// Animation state
	private Dictionary<string, float> _animationScales = new Dictionary<string, float>();
	private string _lastFacingDirection = "right"; // "up", "down", "left", "right"
	private Vector2 _smoothedVelocity = Vector2.Zero; // Anti-jitter filtering

	public override void _Ready()
	{
		// Enable Y-sort for proper overlap rendering (NPCs further down screen render in front)
		YSortEnabled = true;
		
		// Load custom font
		_sharedFont ??= ResourceLoader.Load<Font>("res://assets/Pixer-Regular.otf");
		_customFont = _sharedFont;
		
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
		
		// Deferred: push NPC out of any collision box it may have spawned inside.
		// Physics bodies are not fully registered until the next frame.
		CallDeferred(MethodName.ResolveInitialOverlap);
	}

	/// <summary>
	/// Tries to move the NPC out of any static collision body it may have spawned inside.
	/// Called one frame deferred from _Ready() so all physics bodies are registered.
	/// </summary>
	private void ResolveInitialOverlap()
	{
		const float NUDGE = 40f;      // pixels per nudge attempt
		const int   MAX_TRIES = 16;   // limit so we don't loop forever

		// Eight cardinal + diagonal directions to try
		var dirs = new Vector2[]
		{
			Vector2.Right, Vector2.Left, Vector2.Down, Vector2.Up,
			new Vector2( 1,  1).Normalized(),
			new Vector2(-1,  1).Normalized(),
			new Vector2( 1, -1).Normalized(),
			new Vector2(-1, -1).Normalized(),
		};

		for (int attempt = 0; attempt < MAX_TRIES; attempt++)
		{
			// TestMove with zero motion: returns true if the CURRENT position is overlapping something.
			// We detect overlap by testing a tiny move in each direction; if none move cleanly,
			// the NPC is embedded in geometry.
			var motion = new KinematicCollision2D();
			bool stuck = TestMove(GlobalTransform, Vector2.Zero, motion);
			if (!stuck)
			{
				// Clear — done
				if (attempt > 0)
				{
					GD.Print($"[NPCEntity] {NpcId} resolved initial overlap after {attempt} nudge(s). Final pos: {Position}");
					_targetPosition = Position;
					_lastPosition = Position;
				}
				return;
			}

			// Move in the collision normal direction (or cycle through dirs if normal is zero)
			Vector2 pushDir = motion.GetNormal();
			if (pushDir == Vector2.Zero)
				pushDir = dirs[attempt % dirs.Length];

			Position += pushDir * NUDGE;
			GD.Print($"[NPCEntity] {NpcId} overlap attempt {attempt + 1}: nudging {pushDir * NUDGE}, new pos={Position}");
		}

		GD.PrintErr($"[NPCEntity] {NpcId} could not resolve initial overlap after {MAX_TRIES} attempts — NPC may be stuck!");
	}

	public override void _Process(double delta)
	{
		// Update Stab Window Timer
		if (_stabWindowTimer > 0)
		{
			_stabWindowTimer -= delta;
			if (_stabWindowTimer <= 0 || !_isAlive)
			{
				_stabWindowTimer = 0;
				_stabCount = 0;
				if (_stabLabel != null) _stabLabel.Visible = false;
			}
			else if (_stabLabel != null)
			{
				int secs = (int)Math.Ceiling(_stabWindowTimer);
				_stabLabel.Text = $"🗡️ {_stabCount}/{ADMIRER_STABS_TO_KILL} | {secs}s";
				_stabLabel.Visible = true;
				// Continuously update position based on size changes just in case
				CenterStabLabel();
			}
		}

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

		if (_isStunned)
		{
			string stunAnim = _lastFacingDirection.Contains("up") ? "stun_back" : "stun_front";
			if (_sprite.SpriteFrames.HasAnimation(stunAnim))
			{
				if (_sprite.Animation != stunAnim)
				{
					_sprite.Play(stunAnim);
				}
				return;
			}
			else
			{
				_sprite.Pause();
				return;
			}
		}

		// Use smoothed velocity to prevent single-frame physics jitter (resolves infinite spin/oscillations)
		_smoothedVelocity = _smoothedVelocity.Lerp(Velocity, 0.2f);
		Vector2 velocity = _smoothedVelocity;
		string animToPlay = _sprite.Animation;
		
		// Use a small threshold to detect movement
		if (velocity.Length() > 5.0f)
		{
			float absX = Mathf.Abs(velocity.X);
			float absY = Mathf.Abs(velocity.Y);
			bool movingUp = velocity.Y < -5.0f;
			bool movingDown = velocity.Y > 5.0f;

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

	private void UpdateWandering(double delta)
	{
		/* Debug: Always log frozen state for target NPCs
		if ((NpcId == "john" || NpcId == "rebecca" || NpcId == "marcus") && Multiplayer.IsServer())
		{
			GD.Print($"[NPCEntity] UpdateWandering for {NpcId}: _isFrozen={_isFrozen}, _isSlipping={_isSlipping}");
		}
		*/
		
		// ── Flee from threat override (server-side only) ─────────────────────
		if (_isFleeing && _isAlive && !_isSlipping && !_isStunned &&
		    (Multiplayer.MultiplayerPeer == null || Multiplayer.IsServer()))
		{
			_fleeTimer -= delta;
			if (_fleeTimer <= 0)
			{
				StopFleeing();
			}
			else
			{
				// Run away from threat position
				Vector2 awayDir = (Position - _fleeFromPos);
				if (awayDir.Length() > 1f)
				{
					Velocity = awayDir.Normalized() * FLEE_SPEED;
					MoveAndSlide();
				}
				else
				{
					// If somehow on top of threat, pick random direction
					Velocity = new Vector2(_random.Next(-1, 2), _random.Next(-1, 2)).Normalized() * FLEE_SPEED;
					MoveAndSlide();
				}
			}
			return;
		}

		// ── Mass Revelation march override (server-side only) ──────────────────
		if (_isMarchingToTarget && _isAlive && !_isSlipping && !_isStunned &&
		    (Multiplayer.MultiplayerPeer == null || Multiplayer.IsServer()))
		{
			Vector2 dir = (_marchTarget - Position);
			if (dir.Length() > 8f)
			{
				Velocity = dir.Normalized() * _marchSpeed;
				MoveAndSlide();
			}
			else
			{
				Velocity = Vector2.Zero;
			}
			// Don't run normal AI tick but still update stun timer below.
			return;
		}

		// If slipping, frozen (interview), or stunned, don't move
		if (_isSlipping || _isFrozen || _isStunned)
		{
			// Handle stun timer countdown
			if (_isStunned)
			{
				_stunTimer -= delta;
				if (_stunTimer <= 0)
				{
					_isStunned = false;
					_stunTimer = 0.0;
					// Restore normal color if still alive
					if (_sprite != null)
					{
						_sprite.Modulate = _isAlive ? Colors.White : Colors.DarkGray;
					}
				}
			}
			/*
			if (_isFrozen && Multiplayer.IsServer())
			{
				// Debug: Log when frozen NPC tries to move
				GD.Print($"[NPCEntity] {NpcId} is frozen, skipping movement");
			}
			*/
			return;
		}

		// CLIENTS DO NOT RUN AI - they are synced by server
		if (Multiplayer.MultiplayerPeer == null || !Multiplayer.IsServer())
		{
			if (_hasReceivedFirstSync)
			{
				Vector2 oldPos = Position;
				// Interpolate towards target
				// Use a factor that depends on delta to be frame-rate independent
				// A factor of 10.0f * delta gives quick but smooth catch-up
				Position = Position.Lerp(_clientTargetPosition, 10.0f * (float)delta);
				
				// Calculate velocity for animation logic
				if (delta > 0)
				{
					Velocity = (Position - oldPos) / (float)delta;
				}
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
				// Corridor waypoint rule: if near entry points and needing to traverse, walk straight to exit
				Vector2 corridorTarget = Vector2.Zero;
				bool inCorridorTransit = false;
				
				// Corridor 0 (Left top-to-mid)
				if (Position.DistanceTo(new Vector2(177, 517)) < 50f && _targetPosition.Y > 700f)
				{
					corridorTarget = new Vector2(177, 946);
					inCorridorTransit = true;
				}
				else if (Position.DistanceTo(new Vector2(177, 946)) < 50f && _targetPosition.Y < 700f)
				{
					corridorTarget = new Vector2(177, 517);
					inCorridorTransit = true;
				}
				// Corridor 1 (Mid top-to-mid)
				else if (Position.DistanceTo(new Vector2(1579, 510)) < 50f && _targetPosition.Y > 700f)
				{
					corridorTarget = new Vector2(1579, 946);
					inCorridorTransit = true;
				}
				else if (Position.DistanceTo(new Vector2(1579, 946)) < 50f && _targetPosition.Y < 700f)
				{
					corridorTarget = new Vector2(1579, 510);
					inCorridorTransit = true;
				}
				// Corridor 2 (Right mid-to-bottom)
				else if (Position.DistanceTo(new Vector2(2741, 1418)) < 50f && _targetPosition.Y < 1200f)
				{
					corridorTarget = new Vector2(2741, 968);
					inCorridorTransit = true;
				}
				else if (Position.DistanceTo(new Vector2(2741, 968)) < 50f && _targetPosition.Y > 1200f)
				{
					corridorTarget = new Vector2(2741, 1418);
					inCorridorTransit = true;
				}
				// Corridor 3 (Left mid-to-bottom)
				else if (Position.DistanceTo(new Vector2(959, 1418)) < 50f && _targetPosition.Y < 1200f)
				{
					corridorTarget = new Vector2(959, 968);
					inCorridorTransit = true;
				}
				else if (Position.DistanceTo(new Vector2(959, 968)) < 50f && _targetPosition.Y > 1200f)
				{
					corridorTarget = new Vector2(959, 1418);
					inCorridorTransit = true;
				}
				
				if (inCorridorTransit)
				{
					// Move smoothly toward corridor target without arbitrary axis locking
					Vector2 transitDir = (corridorTarget - Position).Normalized();
					Velocity = transitDir * MOVE_SPEED;
					MoveAndSlide();
					
					// Once close to exit, resume normal movement
					if (Position.DistanceTo(corridorTarget) < 15f)
					{
						Position = corridorTarget;
						PickNewTarget(); // Pick new target in room
					}
					break;
				}

				// ── Backoff-escape phase ────────────────────────────────────────────────
				// If the NPC was stuck and is now backing away from the wall, honour that
				// movement until the timer expires, then pick a proper new target.
				if (_stuckBackoffTimer > 0)
				{
					_stuckBackoffTimer -= delta;
					Velocity = _stuckBackoffDir * MOVE_SPEED;
					MoveAndSlide();
					
					if (_stuckBackoffTimer <= 0)
					{
						// Done backing off — pick a fresh target from current (now-clear) position
						PickNewTarget();
						_stuckTimer    = 0;
						_lastPosition  = Position;
					}
					break;
				}

				// ── Normal room movement ─────────────────────────────────────────────────
				Vector2 direction = (_targetPosition - Position).Normalized();
				float distanceToTarget = Position.DistanceTo(_targetPosition);
				if (distanceToTarget <= TARGET_REACHED_THRESHOLD)
				{
					Position = _targetPosition;
					Velocity = Vector2.Zero;
					_wanderState       = WanderState.Pausing;
					_pauseTimer        = (float)(_random.NextDouble() * (MAX_PAUSE - MIN_PAUSE) + MIN_PAUSE);
					_stuckTimer        = 0;
					_stuckBackoffTimer = 0;
					_lastPosition      = Position;
				}
				else
				{
					// Set velocity toward target and let MoveAndSlide() handle wall sliding.
					// (Manual slide-assist was removed — it amplified near-zero slide vectors
					//  to full MOVE_SPEED in corner traps, causing the spinning/oscillation.)
					Velocity = direction * MOVE_SPEED;
					MoveAndSlide();

					// ── Stuck detection ─────────────────────────────────────────────────
					// Only accumulate if the NPC is barely moving relative to last frame.
					if (Position.DistanceTo(_lastPosition) < STUCK_DISTANCE_THRESHOLD)
					{
						_stuckTimer += delta;
					}
					else
					{
						_stuckTimer   = 0;
						_lastPosition = Position;
					}

					if (_stuckTimer >= STUCK_TIME_THRESHOLD)
					{
						// Determine backoff direction: use the last collision normal if we have one,
						// otherwise use the reverse of our current heading.
						Vector2 backDir = Vector2.Zero;
						if (GetSlideCollisionCount() > 0)
						{
							for (int ci = 0; ci < GetSlideCollisionCount(); ci++)
							{
								var col = GetSlideCollision(ci);
								// Only walls (not other NPCs or players)
								if (col.GetCollider() is not NPCEntity &&
									(col.GetCollider() as Node)?.IsInGroup("players") == false)
								{
									backDir += col.GetNormal();
								}
							}
						}
						if (backDir == Vector2.Zero)
							backDir = -direction; // reverse heading as fallback

						_stuckBackoffDir   = backDir.Normalized();
						_stuckBackoffTimer = BACKOFF_DURATION;
						_stuckTimer        = 0;
						// GD.Print($"[NPCEntity] {NpcId} stuck — backing off in dir {_stuckBackoffDir}");
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
			
			// Add padding to corridors to avoid walking directly on walls (stay in the center)
			float corridorPadding = 60.0f; 
			float cMinX = corridor.minX + corridorPadding;
			float cMaxX = corridor.maxX - corridorPadding;
			float cMinY = corridor.minY + corridorPadding;
			float cMaxY = corridor.maxY - corridorPadding;

			// Safety check for narrow corridors
			if (cMinX >= cMaxX) { cMinX = corridor.minX; cMaxX = corridor.maxX; }
			if (cMinY >= cMaxY) { cMinY = corridor.minY; cMaxY = corridor.maxY; }
			
			float x = (float)(_random.NextDouble() * (cMaxX - cMinX) + cMinX);
			float y = (float)(_random.NextDouble() * (cMaxY - cMinY) + cMinY);
			_targetPosition = new Vector2(x, y);
			
			GD.Print($"{NpcId} heading to corridor {corridorIndex} with wall-avoidance.");
		}
		else
		{
			// Pick a random position within current room with PADDING to avoid walls
			if (_currentRoomIndex >= 0 && _currentRoomIndex < _rooms.Count)
			{
				Room room = _rooms[_currentRoomIndex];
				// Increased padding to push NPCs further from wall edges (where blood clips)
				float padding = 150.0f; 

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

		// Stab Status Tracker (below punch hint, above name)
		_stabLabel = new Label();
		_stabLabel.Text = $"🗡️ 0/{ADMIRER_STABS_TO_KILL} | 30s";
		_stabLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_stabLabel.AddThemeColorOverride("font_color", Colors.White);
		_stabLabel.AddThemeFontOverride("font", _customFont);
		_stabLabel.AddThemeFontSizeOverride("font_size", 24);
		var stabBgStyle = new StyleBoxFlat();
		stabBgStyle.BgColor = new Color(0.8f, 0.1f, 0.1f, 0.85f); // Red
		stabBgStyle.SetCornerRadiusAll(4);
		stabBgStyle.SetContentMarginAll(4);
		_stabLabel.AddThemeStyleboxOverride("normal", stabBgStyle);
		_stabLabel.Visible = false;
		AddChild(_stabLabel);
		CallDeferred(MethodName.CenterStabLabel);

		// Converted (Halo) — commented out per request
		// _convertedIndicator = CreateStatusIndicator("res://assets/halo.png", new Vector2(0, -110));
		_convertedIndicator = new Sprite2D(); // dummy to avoid null refs
		_convertedIndicator.Visible = false;
		AddChild(_convertedIndicator);
		
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

		// ── Midterm Demo Blood Overlay (Body Splatter) ──
		_bloodBodyOverlay = new Sprite2D();
		_bloodBodyOverlay.Visible = false;
		_bloodBodyOverlay.TextureFilter = TextureFilterEnum.Nearest;
		_bloodBodyOverlay.ZIndex = 1; // On top of NPC sprite
		AddChild(_bloodBodyOverlay);
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

		if (_spriteFramesCache.TryGetValue(npcAsset, out var cachedFrames) &&
			_animationScalesCache.TryGetValue(npcAsset, out var cachedScales))
		{
			_sprite.SpriteFrames = cachedFrames;
			_animationScales = new Dictionary<string, float>(cachedScales);
			if (_baseTextureCache.TryGetValue(npcAsset, out var cachedBaseTexture))
			{
				_baseTexture = cachedBaseTexture;
			}
			if (_frameWidthCache.TryGetValue(npcAsset, out var cachedFrameWidth))
			{
				_frameWidth = cachedFrameWidth;
			}
			_sprite.Play("idle_right");
			if (_animationScales.TryGetValue("idle_right", out float cachedScale))
			{
				_sprite.Scale = new Vector2(cachedScale, cachedScale);
			}
			return;
		}

		var frames = new SpriteFrames();
		var generatedScales = new Dictionary<string, float>();
		string basePath = "res://assets/new-character-assets/";
		
		// Helper to load frames from a split texture (6x6 grid, limit to 35)
		void AddAnimationFrames(string animName, string path, bool skipFirstFrame = false, float scaleMultiplier = 1.0f, bool loop = true)
		{
			var tex = GD.Load<Texture2D>(path);
			if (tex == null) return;

			if (!frames.HasAnimation(animName))
				frames.AddAnimation(animName);
			
			float width = tex.GetWidth();
			float height = tex.GetHeight();
			
			int gridCols = 6;
			int gridRows = 6;

			float frameWidth = width / (float)gridCols;
			float frameHeight = height / (float)gridRows;

			if (animName == "idle_right") _frameWidth = frameWidth;

			int totalAdded = 0;
			for (int y = 0; y < gridRows; y++)
			{
				for (int x = 0; x < gridCols; x++)
				{
					if (skipFirstFrame && x == 0 && y == 0) continue;
					if (totalAdded >= 35) break;

					var atlasKey = new AtlasTexture();
					atlasKey.Atlas = tex;
					atlasKey.Region = new Rect2(x * frameWidth, y * frameHeight, frameWidth, frameHeight);
					frames.AddFrame(animName, atlasKey);
					totalAdded++;
				}
				if (totalAdded >= 35) break;
			}
			
			frames.SetAnimationLoop(animName, loop);
			frames.SetAnimationSpeed(animName, animName.Contains("idle") ? 10.0f : 15.0f); // 36 frames need higher speed

			// Calculate and store scale for this specific animation to ensure 243 world unit height
			float targetWorldHeight = 243.0f;
			generatedScales[animName] = (targetWorldHeight / frameHeight) * scaleMultiplier;

			// For portrait/base reference, use the front-idle texture
			if (animName == "idle_right") _baseTexture = tex;
		}

		AddAnimationFrames("walk_down", $"{basePath}{npcAsset}-front-walk.png");
		AddAnimationFrames("walk_up", $"{basePath}{npcAsset}-back-walk.png");
		AddAnimationFrames("walk_right", $"{basePath}{npcAsset}-front-walk.png");
		AddAnimationFrames("walk_left", $"{basePath}{npcAsset}-front-walk.png");
		
		// Add back-directional walk for up-diagonals
		AddAnimationFrames("walk_up_right", $"{basePath}{npcAsset}-back-walk.png");
		AddAnimationFrames("walk_up_left", $"{basePath}{npcAsset}-back-walk.png");
		// For John (npc6), limit back-walk animations to the first row (first 6 frames) and reuse them
		if (npcAsset == "npc6")
		{
			string[] backWalkAnims = { "walk_up", "walk_up_right", "walk_up_left" };
			foreach (var anim in backWalkAnims)
			{
				int frameCount = frames.GetFrameCount(anim);
				// Remove frames beyond the first 6 (indices 6 and above)
				for (int i = frameCount - 1; i >= 6; i--)
				{
					frames.RemoveFrame(anim, i);
				}
			}
		}
		// For John (npc6), skip every other frame in walking animations
		if (npcAsset == "npc6")
		{
			string[] walkAnims = { "walk_down", "walk_up", "walk_right", "walk_left", "walk_up_right", "walk_up_left" };
			foreach (var anim in walkAnims)
			{
				int frameCount = frames.GetFrameCount(anim);
				for (int i = frameCount - 1; i >= 0; i--)
				{
					if (i % 2 == 1)
					{
						frames.RemoveFrame(anim, i);
					}
				}
			}
		}

		AddAnimationFrames("idle_right", $"{basePath}{npcAsset}-front-idle.png");
		AddAnimationFrames("idle_left", $"{basePath}{npcAsset}-front-idle.png");
		
		// Admirer's and NPC6's (John) back-idle has a broken first frame (grey background)
		bool skipBrokenFrame = (npcAsset == "admirer2" || npcAsset == "npc6"); 
		AddAnimationFrames("idle_up", $"{basePath}{npcAsset}-back-idle.png", skipBrokenFrame, (npcAsset == "admirer2") ? 1.15f : 1.0f);
		if (npcAsset == "npc6")
		{
			// Keep only the first frame for a static idle
			if (frames.HasAnimation("idle_up"))
			{
				int count = frames.GetFrameCount("idle_up");
				for (int i = count - 1; i > 0; i--)
				{
					frames.RemoveFrame("idle_up", i);
				}
			}
		}


		
		// Add back-directional idle for up-diagonals
		AddAnimationFrames("idle_up_right", $"{basePath}{npcAsset}-back-idle.png", skipBrokenFrame, (npcAsset == "admirer2") ? 1.15f : 1.0f);
		AddAnimationFrames("idle_up_left", $"{basePath}{npcAsset}-back-idle.png", skipBrokenFrame, (npcAsset == "admirer2") ? 1.15f : 1.0f);

		AddAnimationFrames("idle_down", $"{basePath}{npcAsset}-front-idle.png");


		_animationScales = generatedScales;
		_spriteFramesCache[npcAsset] = frames;
		_animationScalesCache[npcAsset] = new Dictionary<string, float>(generatedScales);
		if (_baseTexture != null)
		{
			_baseTextureCache[npcAsset] = _baseTexture;
		}
		_frameWidthCache[npcAsset] = _frameWidth;
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
		// Taller rectangle covering mid-body to feet.
		// Sprite is ~200px tall, centered at origin → bottom is at y=+100.
		// A taller shape stops the NPC before the upper body clips into objects.
		_collisionShape = new CollisionShape2D();
		var shape = new RectangleShape2D();
		shape.Size = new Vector2(32, 40);
		_collisionShape.Shape = shape;
		_collisionShape.Position = new Vector2(0, 80); // lower-body centre
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
		// Dead NPCs cannot be interacted with
		if (!_isAlive) return;

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
		// Dead NPCs cannot be interacted with
		if (!_isAlive) return;

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
			
			// Only show interact hint for living NPCs
			if (_isAlive && body is PlayerController player && player.IsLocalPlayer)
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
			// New isometric assets are 6x6 grids
			float frameHeight = _baseTexture.GetHeight() / 6.0f;
			
			atlas.Region = new Rect2(0, 0, _frameWidth, frameHeight);
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
		_deadOverlay.Visible = false; // Dead overlay disabled per request
		// _convertedIndicator.Visible = converted; // halo commented out per request
		_convertedIndicator.Visible = false;
		_marriedIndicator.Visible = false; // Marriage removed from game
		_targetIndicator.Visible = isTarget;
		if (!alive && _punchHint != null)
		{
			_punchHint.Visible = false;
		}
		
		// Do not dim the sprite if dead per request, just keep it normal
		_sprite.Modulate = Colors.White;
		
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
		// Don't override stun visuals with flash
		if (_isStunned) return;
		_sprite.Modulate = Colors.Red;
		var timer = GetTree().CreateTimer(Math.Max(0.1, seconds));
		timer.Timeout += () =>
		{
			if (!IsInstanceValid(this) || _sprite == null) return;
			// Don't restore color if currently stunned
			if (_isStunned) return;
			_sprite.Modulate = _isAlive ? Colors.White : Colors.DarkGray;
		};
	}

	/// <summary>
	/// Stun the NPC for a given duration. Greys out the sprite and forces idle.
	/// </summary>
	public void ApplyStun(double seconds)
	{
		_isStunned = true;
		_stunTimer = seconds;
		Velocity = Vector2.Zero;

		// Grey out during stun if no collapse animation exists
		string stunAnim = _lastFacingDirection.Contains("up") ? "stun_back" : "stun_front";
		if (_sprite != null)
		{
			if (_sprite.SpriteFrames.HasAnimation(stunAnim))
			{
				_sprite.Play(stunAnim);
				_sprite.Modulate = Colors.White; // Ensure no grey out if we have animation
			}
			else
			{
				_sprite.Modulate = Colors.DarkGray;
				_sprite.Pause();
			}
		}
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
			_punchHint.Position = new Vector2(-labelWidth / 2, -270);
		}
	}

	/// <summary>
	/// Center the stab hint horizontally over the NPC based on its actual width.
	/// </summary>
	private void CenterStabLabel()
	{
		if (_stabLabel != null)
		{
			var labelWidth = _stabLabel.Size.X;
			_stabLabel.Position = new Vector2(-labelWidth / 2, -150);
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
		// GD.Print($"[NPCEntity] SetFrozen called on {NpcId}: {frozen} (was {_isFrozen})");
		if (!frozen && (NpcId == "john" || NpcId == "rebecca" || NpcId == "marcus"))
		{
			// Print where unfreeze is coming from
			// GD.Print($"[NPCEntity] WARNING: UNFREEZING target NPC {NpcId}");
		}
		_isFrozen = frozen;
		if (frozen)
		{
			// Optional: Stop current velocity
			Velocity = Vector2.Zero;
			// GD.Print($"[NPCEntity] {NpcId} velocity set to zero, _isFrozen is now {_isFrozen}");
		}
	}

	/// <summary>
	/// Overrides wandering AI: march this NPC toward <paramref name="target"/> at
	/// <paramref name="speed"/> pixels/sec. Called every server tick during Mass Revelation.
	/// </summary>
	public void SetMarchTarget(Vector2 target, float speed)
	{
		_isMarchingToTarget = true;
		_marchTarget        = target;
		_marchSpeed         = speed;
	}

	/// <summary>Clears the march override and returns the NPC to normal wandering AI.</summary>
	public void ClearMarchTarget()
	{
		_isMarchingToTarget = false;
		_marchSpeed         = 0f;
		Velocity            = Vector2.Zero;
		// Resume from the current position so the NPC picks a sensible next target
		_wanderState = WanderState.Pausing;
		_pauseTimer  = 0.5;
	}

	/// <summary>
	/// Makes this NPC flee away from the given position at high speed.
	/// Used when the Admirer stabs this NPC.
	/// </summary>
	public void StartFleeingFrom(Vector2 threatPosition)
	{
		if (!_isAlive) return;
		_isFleeing   = true;
		_fleeTimer   = FLEE_DURATION;
		_fleeFromPos = threatPosition;
		GD.Print($"[NPCEntity] {NpcId} is now fleeing from {threatPosition}!");
	}

	/// <summary>
	/// Updates the overhead label with current stab counts and starts the timer.
	/// </summary>
	public void UpdateStabStatus(int count, double windowRemaining)
	{
		_stabCount = count;
		_stabWindowTimer = windowRemaining;
		if (count <= 0 || count >= ADMIRER_STABS_TO_KILL || !_isAlive)
		{
			if (_stabLabel != null) _stabLabel.Visible = false; // Immediately hide UI regardless of process tick
		}

	}

	/// <summary>Stops the flee behavior and returns to normal wandering.</summary>
	private void StopFleeing()
	{
		_isFleeing  = false;
		_fleeTimer  = 0;
		Velocity    = Vector2.Zero;
		_wanderState = WanderState.Pausing;
		_pauseTimer  = 0.5;
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
