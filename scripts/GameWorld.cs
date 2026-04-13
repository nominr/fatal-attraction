using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FatalAttraction.Engine;

/// <summary>
/// Spatial game world that replaces the UI-based MainGame.
/// Handles spawning NPCs, players, and the interaction panel.
/// Works with the existing networking layer.
/// </summary>
public partial class GameWorld : Node2D
{
	// ==================== NOTIFICATION BOX TOGGLE (line 16) ====================
	// Set to true to show the notification box, false to hide it entirely.
	private bool _notificationBoxEnabled = false;
	// ============================================================================

	// Triangle Scene Reference
	private TriangleScene _triangleScene;

	// Network
	private NetworkManager _networkManager;

	// Game Logic (server only)
	private GameEngine _gameEngine;
	private bool _gameActive = false;
	private double _timeRemaining;
	private double _broadcastTimer = 0.0;
	private const double BROADCAST_INTERVAL = 0.1; // Broadcast game state every 0.1s (10Hz) for smoother movement
	private bool _countdownTriggered = false;

	// Local State Cache
	private JObject _localGameState;
	private string _myRole = "";

	// Spawned entities
	private Dictionary<string, NPCEntity> _npcEntities = new();
	private Dictionary<long, PlayerController> _playerControllers = new(); // remote player puppets
	private PlayerController _localPlayer;
	
	// UI Components
	private NPCDialogueUI _npcDialogueUI;
	// Elimination UI
	private Control _eliminationOverlay;
	private Label _eliminationLabel;
	private double _admirerEliminatedTimer = 0;
	private bool _admirerEliminatedShown = false;
	private CanvasLayer _uiLayer;
	// private GoalsMenu _goalsMenu; // (Removed)
	private TextureButton _goalsButton;
	private TextureButton _aiLeaderboardButton;
	private TextureRect _aiLeaderboardPanel;
	private TriangleScene _leaderboardTriangle;
	private Label _leaderboardInfluenceLabel;
	private Label _leaderboardDetailsLabel;
	// Editorial asset content node (holds room Area2D children)
	private Node2D _editorialContentNode;
	
	// Leaderboard Enhancements
	private ColorRect _leaderboardBgDim;
	private VBoxContainer _leaderboardDetailsContainer;
	private GridContainer _leaderboardGrid;
	private bool _isLeaderboardOpen = false;
	private float _leaderboardPanelWidth;
	
	// Interaction tracking
	private string _currentInteractingNpcId = null;
	private int _lastGameStateHash = 0; // Track when game state changes to prevent unnecessary refreshes
	private bool _goalsShownAtStart = false;
	
	private Label _timerLabel;
	private int _lastDisplayedSecond = -1; // for per-second pulse detection
	private Label _roleLabel;
	private Label _convertedLabel;
	private VBoxContainer _metersContainer;
	private RichTextLabel _notificationText;

	private PanelContainer _notificationPanel;
	private Button _collapseNotificationButton;
	private bool _notificationCollapsed = false;
	private MarginContainer _notificationMargin;
	private Vector2 _notificationPanelExpandedPosition;
	private int _lastNotificationCount = 0; // Track which notifications have been displayed

	// Punch logic
	private const float PUNCH_RANGE = 120f;
	private bool _wasPunchPressed = false;

	// ── Producer Ultimate: Cash Trail ──────────────────────────────────────────
	private bool   _ultimateActive      = false;
	private bool   _ultimateUsed        = false; // can only be used once
	private double _ultimateTimer       = 0.0;   // counts down while active
	private double _cashSpawnTimer      = 0.0;   // time until next coin spawns
	private bool   _wasUltimatePressed  = false;
	private const double ULTIMATE_DURATION    = 10.0;  // seconds trail lasts
	private const double CASH_SPAWN_INTERVAL  = 0.35;  // seconds between coins
	private const float  CASH_COIN_RADIUS     = 28f;   // NPC/player pick-up radius
	private const float  CASH_INFLUENCE_GAIN  = 0.5f;  // producer state bonus per coin
	private Label  _ultimateCooldownLabel;             // HUD label under the button
	private Button _ultimateButton;                    // "Cash Trail" button

	// ── Admirer Ultimate: Knife Kill ─────────────────────────────────────────────
	private bool   _wasAdmirerUltPressed     = false;
	private const float ADMIRER_KNIFE_RANGE  = 160f;   // increased for reliability
	private const int   ADMIRER_STABS_TO_KILL = 3;     // stabs needed to kill an NPC
	private const double ADMIRER_STAB_COOLDOWN = 1.0;  // reduced for fluid resetting feel
	private const double ADMIRER_STAB_WINDOW  = 10.0;  // 10s window to land next stab (refreshes)
	private double _admirerStabCooldownTimer = 0.0;    // current cooldown remaining
	private double _admirerStabWindowTimer   = 0.0;    // time left in current stab window
	private string _admirerStabWindowTarget  = null;   // NPC being actively hunted
	private bool   _knifePermanentlyLost     = false;  // Lost if hunt timer expires
	private Node2D _admirerAuraVisual;                 // private red circle
	private Dictionary<string, float> _bleedingNpcTimers = new Dictionary<string, float>();
	private Dictionary<string, float> _npcBloodDropTimers = new Dictionary<string, float>();
	private const float BLOOD_TRAIL_DURATION = 8.0f; // Seconds to bleed after a stab
	private const float BLOOD_DROP_INTERVAL = 0.6f; // Seconds between trail drops
	private Dictionary<string, Vector2> _lastBloodPositions = new(); // Track last puddle for de-duplication
	private Node2D _floorLayer; // Reference to the floor layer node
	private Texture2D[] _splashTextures;
	private Dictionary<string, int> _npcStabCounts = new(); // server-side stab counts per NPC
	private Dictionary<string, ulong> _npcStabLastTime = new(); // server-side timestamp of the last stab per NPC
	private Button _admirerKnifeButton;                // "🔪 Knife" HUD button
	private Label  _admirerKnifeLabel;                 // hint label under the button

	// ── Prophet Ultimate: Mass Revelation ──────────────────────────────────────
	private bool   _massRevActive            = false;
	private bool   _massRevUsed              = false;
	private double _massRevTimer             = 0.0;
	private bool   _wasMassRevPressed        = false;
	private bool   _massRevServerActive      = false;  // server-side flag
	private long   _massRevProphetPeerId     = 0;
	private Dictionary<string, float> _massRevNpcTimers = new();
	private Dictionary<string, float> _massRevNpcPoints = new();      // which peer is channeling
	private const double MASS_REV_DURATION   = 10.0;
	private const float  MASS_REV_RADIUS     = 480f;   // NPC pull radius (world units)
	private const float  MASS_REV_NPC_SPEED  = 65f;    // NPC march-toward-prophet speed
	private const float  MASS_REV_PLAYER_SPD = 80f;    // Prophet reduced speed while chanting
	private const float  MASS_REV_FADE       = 1.0f;   // audio fade duration (seconds)
	private Button  _massRevButton;
	private Label   _massRevLabel;
	private Node2D  _massRevAuraVisual;                // pulsing aura circle node
	private bool    _massRevClientAuraActive = false;  // non-prophet clients: aura is showing


	private Font _customFont;
	
	// Hover color for buttons
	private Color _normalColor = new Color(1, 1, 1, 1); // White
	private Color _hoverColor = new Color(1, 0.9f, 0.2f, 1); // Yellowish

	// World bounds
	private Vector2 _worldSize = new Vector2(1200, 800);
	private const int PROPHET_CONVERT_GOAL = 5;


	// Producer HUD Elements
	private VBoxContainer _producerStatsContainer;
	private Label _activeCamerasLabel;

	private int _lastProcessedNotificationCount = 0;
	
	// Track where panels were opened to auto-close on distance
	private Vector2 _cameraSelectPanelOpenPos;

	// How many seconds to keep the panel open after a chat-game answer
	private double _responsePreviewTimer = 0.0;
	private const double RESPONSE_PREVIEW_DURATION = 3.0;

	// Suppresses self-healing re-open between intentional Close() and server confirmation
	private bool _suppressSelfHeal = false;
	private bool _isGameOver = false; // Set permanently once game ends — prevents panel re-open
	
	// +1 Rating Visual Feedback
	private double _previousProducerRating = 0;

	// Global Influence Counter (Triangle UI)
	private TriangleScene _globalInfluenceTriangle;
	private TextureRect _globalAdmirerIcon;
	private TextureRect _globalProphetIcon;
	private TextureRect _globalProducerIcon;
	private ColorRect _globalAdmirerGlow;
	private ColorRect _globalProphetGlow;
	private ColorRect _globalProducerGlow;
	private Tween _prophetIconPulse;
	private Tween _producerIconPulse;
	private Tween _admirerIconPulse;

	// Bottom-Left Status Container (for role-specific stats)
	private PanelContainer _bottomLeftStatusPanel;
	private VBoxContainer _bottomLeftStatusContainer;

	// Notification System Enhancements
	private Dictionary<string, string> _previousNpcZones = new();
	private Dictionary<string, int> _previousZoneCounts = new() { { "prophet", 0 }, { "admirer", 0 }, { "producer", 0 } };
	private List<NotificationItem> _activeNotificationItems = new();
	private const int WIN_THRESHOLD = 5;

	// Helper for Trap-Like UI Style
	private StyleBoxFlat CreateTrapStyle(Color bgColor, Color borderColor)
	{
		var style = new StyleBoxFlat();
		style.BgColor = bgColor;
		style.BorderColor = borderColor;
		style.SetBorderWidthAll(2);
		style.SetCornerRadiusAll(4);
		return style;
	}

	// Helper to check if the local player is dead
	private bool IsLocalPlayerDead()
	{
		if (string.IsNullOrEmpty(_myRole)) return false;
		
		// On server, check game engine directly
		if (Multiplayer.IsServer() && _gameEngine != null)
		{
			if (Enum.TryParse<Role>(_myRole, true, out var role))
			{
				var playerState = _gameEngine.GameState.GetPlayerState(role);
				return playerState != null && !playerState.Alive;
			}
		}
		
		// On client, check cached player states
		var playerStates = _localGameState?["player_states"] as JObject;
		if (playerStates != null)
		{
			// Keys are role names like "Prophet", "Producer", "Admirer"
			var roleKey = _myRole; // Already capitalized from network manager
			var stateObj = playerStates[roleKey];
			if (stateObj != null)
			{
				bool alive = stateObj["alive"]?.Value<bool>() ?? true;
				return !alive;
			}
		}
		
		return false;
	}

	public override void _Ready()
	{
		// Enable Y-sort for proper NPC/player overlap rendering
		YSortEnabled = true;
		
		// RUN DEBUG TESTS
		// FatalAttraction.Tests.MurderTest.RunTests();

		_networkManager = GetNode<NetworkManager>("/root/NetworkManager");
		// Listen for network player events to keep controllers in sync
		_networkManager.PlayerConnected += OnNetworkPlayerConnected;
		_networkManager.PlayerDisconnected += OnNetworkPlayerDisconnected;
		_networkManager.PlayerInteraction += OnNetworkPlayerInteraction;
		
		// Connect Room Signals
		ConnectRoomSignals();
		
		SetupUI();
		// TileMap is now defined in GameWorld.tscn scene file
		
		// Isometric map: find the instanced IsometricWorldMap scene and apply transform
		var isoMap = GetNodeOrNull("IsometricWorldMap");
		if (isoMap != null && isoMap is Node2D isoMapNode)
		{
			GD.Print($"[Isometric] IsometricWorldMap found!");
			
			Vector2 targetPos = new Vector2(600, 250);
			Vector2 targetScale = new Vector2(4.0f, 4.0f);
			
			isoMapNode.Scale = targetScale;
			isoMapNode.Position = targetPos;
			isoMapNode.ZIndex = 0; // Must be 0 so furniture tiles Y-sort against character nodes (both at ZIndex=0)
			GD.Print($"[Isometric] IsometricWorldMap scaled to {isoMapNode.Scale} and positioned at {isoMapNode.Position}");
			
			// Layer z-index setup — target TileMapLayer6 by name so index drift never breaks it.
			int layerIdx = 0;
			foreach (var child in isoMapNode.GetChildren())
			{
				if (child is Node2D childLayer)
				{
					if (layerIdx <= 1)
					{
						// Base Floor / Carpet layers
						childLayer.ZIndex = -10 + layerIdx; // Floor = -10, Carpet = -9
						if (layerIdx == 0) _floorLayer = childLayer; // Store base floor for blood parenting
					}
					else if (layerIdx == 2)
					{
						// Wall layer — MUST be above blood (which is at -8)
						childLayer.ZIndex = -7; 
					}
					else
					{
						// Furniture / upper layers — Y-sort with characters.
						childLayer.ZIndex = 0;
					}
					childLayer.YSortEnabled = true;
					GD.Print($"[Isometric] ZIndex={childLayer.ZIndex} YSort={childLayer.YSortEnabled} on {childLayer.Name}");
					layerIdx++;
				}
			}

			// Enable Y-sort on the map node so character nodes and furniture tiles sort together.
			isoMapNode.YSortEnabled = true;
			
			// --- ALIGN ROOM AREAS TO MATCH SCALED WORLD ---
			string[] roomNames = { "Room1", "Room2", "Room3", "Room4", "Room5", "Hallways" };
			foreach (var rName in roomNames)
			{
				var area = GetNodeOrNull<Area2D>(rName);
				if (area != null)
				{
					Vector2 relPos = area.Position;
					Vector2 newRelPos = relPos * targetScale;
					area.Position = targetPos + newRelPos;
					area.Scale = targetScale;
					GD.Print($"[GameWorld] Aligned {rName} to World: Pos {area.Position}, Scale {area.Scale}");
				}
			}
			
			// Add invisible boundary walls around the isometric map perimeter
			CallDeferred(MethodName.SetupMapBoundaries);
		}
		else
		{
			GD.PrintErr("[Isometric] IsometricWorldMap NOT found in scene!");
		}

		if (Multiplayer.IsServer())
		{
			InitializeServer();
		}
		else
		{
			InitializeClient();
		}
	}

	/// <summary>
	/// Builds 4 invisible StaticBody2D walls tracing the isometric map's diamond perimeter.
	/// Called deferred so the TileMapLayer is fully ready before MapToLocal() is invoked.
	/// </summary>
	private void SetupMapBoundaries()
	{
		var isoMapNode = GetNodeOrNull<Node2D>("IsometricWorldMap");
		if (isoMapNode == null) { GD.PrintErr("[Boundaries] IsometricWorldMap not found"); return; }

		var tileLayer = isoMapNode.GetNodeOrNull<TileMapLayer>("TileMapLayer");
		if (tileLayer == null) { GD.PrintErr("[Boundaries] TileMapLayer not found inside IsometricWorldMap"); return; }

		Rect2I usedRect = tileLayer.GetUsedRect();
		if (usedRect.Size == Vector2I.Zero) { GD.PrintErr("[Boundaries] TileMapLayer has no tiles"); return; }

		// Add 1-tile padding so walls sit just outside the visible perimeter
		const int PAD = 1;
		Vector2I minT = usedRect.Position - new Vector2I(PAD, PAD);
		Vector2I maxT = usedRect.End     + new Vector2I(PAD, PAD); // End is exclusive, so this equals last tile + PAD + 1

		// In isometric projection the 4 extreme screen-space tips correspond to:
		//   tipLeft   = tile (minCol, maxRow)  — leftmost screen point
		//   tipRight  = tile (maxCol, minRow)  — rightmost screen point
		//   tipTop    = tile (minCol, minRow)  — topmost screen point
		//   tipBottom = tile (maxCol, maxRow)  — bottommost screen point
		Vector2 tipLeft   = isoMapNode.ToGlobal(tileLayer.MapToLocal(new Vector2I(minT.X, maxT.Y)));
		Vector2 tipRight  = isoMapNode.ToGlobal(tileLayer.MapToLocal(new Vector2I(maxT.X, minT.Y)));
		Vector2 tipTop    = isoMapNode.ToGlobal(tileLayer.MapToLocal(new Vector2I(minT.X, minT.Y)));
		Vector2 tipBottom = isoMapNode.ToGlobal(tileLayer.MapToLocal(new Vector2I(maxT.X, maxT.Y)));

		GD.Print($"[Boundaries] Diamond tips — Top:{tipTop} Right:{tipRight} Bottom:{tipBottom} Left:{tipLeft}");

		// Wall thickness — large enough to prevent tunnelling at full speed
		const float THICKNESS = 600f;

		// 4 edges of the diamond (wound clockwise in screen space so outward is "right" perp)
		AddEdgeWall("WallTopLeft",     tipLeft,   tipTop,    THICKNESS);
		AddEdgeWall("WallTopRight",    tipTop,    tipRight,  THICKNESS);
		AddEdgeWall("WallBottomRight", tipRight,  tipBottom, THICKNESS);
		AddEdgeWall("WallBottomLeft",  tipBottom, tipLeft,   THICKNESS);

		GD.Print("[Boundaries] Map boundary walls created.");
	}

	/// <summary>
	/// Creates a single invisible wall segment from <paramref name="from"/> to <paramref name="to"/>.
	/// The wall is a rectangle of the given <paramref name="thickness"/> placed on the OUTSIDE
	/// of a clockwise-wound polygon (outward normal = right-hand perpendicular of edge direction).
	/// </summary>
	private void AddEdgeWall(string wallName, Vector2 from, Vector2 to, float thickness)
	{
		Vector2 edge   = to - from;
		float   length = edge.Length();
		if (length < 1f) return;

		// For a CW polygon the outward normal is the RIGHT-side perpendicular of the edge.
		Vector2 edgeNorm     = edge.Normalized();
		Vector2 outwardNorm  = new Vector2(edgeNorm.Y, -edgeNorm.X);

		var body = new StaticBody2D();
		body.Name          = wallName;
		body.CollisionLayer = 1;  // Players (Mask=1) collide with this
		body.CollisionMask  = 0;  // Wall doesn't need to detect anything

		var shape = new CollisionShape2D();
		var rect  = new RectangleShape2D();
		// Extend length a bit on both sides so walls overlap at diamond corners
		rect.Size = new Vector2(length + thickness, thickness);
		shape.Shape    = rect;
		shape.Rotation = edge.Angle();
		// Centre of shape = midpoint of edge + half-thickness in outward direction
		shape.Position = (from + to) * 0.5f + outwardNorm * (thickness * 0.5f);

		body.AddChild(shape);
		AddChild(body);
	}

	/// <summary>
	/// Returns null so all entities spawn as direct children of GameWorld (world-space positions).
	/// This keeps NPC/player coordinate logic working correctly. Y-sorting against IsometricWorldMap
	/// tiles is handled by GameWorld.YSortEnabled + IsometricWorldMap.YSortEnabled + per-layer YSort.
	/// </summary>
	private Node2D GetLayer6() => null;

	private void ConnectRoomSignals()
	{
		string[] roomNames = { "Room1", "Room2", "Room3", "Room4", "Room5", "Hallways" };
		foreach (var rName in roomNames)
		{
			var area = GetNodeOrNull<Area2D>(rName);
			if (area != null)
			{
				// We need to capture the room name variable for the lambda
				string capturedRoomName = rName;
				
				// Ensure Area monitors the NPC layer (Layer 3/Value 4 based on NPCEntity.cs)
				// NPCEntity uses CollisionLayer = 4. 
				// We'll set Mask to include 4 (NPCs) and 2 (Players) for future-proofing
				area.CollisionMask = 4 | 2; 
				area.Monitorable = false; // Room areas don't need to be detected by others
				area.Monitoring = true;
				
				// Disconnect existing if any to avoid duplicates (safeguard)
				if (area.IsConnected(Area2D.SignalName.BodyEntered, Callable.From<Node>((body) => OnBodyEnteredRoom(body, capturedRoomName))))
				{
					area.Disconnect(Area2D.SignalName.BodyEntered, Callable.From<Node>((body) => OnBodyEnteredRoom(body, capturedRoomName)));
				}

				area.BodyEntered += (body) => OnBodyEnteredRoom(body, capturedRoomName);
				
				GD.Print($"[GameWorld] Connected signals for {rName} (using existing collision shapes)");
			}
			else
			{
				GD.PrintErr($"Room Area not found: {rName}");
			}
		}
	}

	private string GetRoomIdAtPosition(Vector2 pos)
	{
		// PHYSICS ENGINE DETECTION STRATEGY:
		// Use the physics engine to determine exactly which room Area2D contains the point.
		
		var spaceState = GetWorld2D().DirectSpaceState;
		var query = new PhysicsPointQueryParameters2D
		{
			Position = pos,
			CollideWithAreas = true,
			CollideWithBodies = false,
			CollisionMask = int.MaxValue // Check all layers, we verify names manually
		};

		var results = spaceState.IntersectPoint(query);
		
		string bestId = null;
		
		// Priority: Specific Rooms > Hallways
		foreach (var result in results)
		{
			var collider = result["collider"].As<Node>();
			if (collider is Area2D area)
			{
				string name = area.Name;
				// Check if it's one of our monitored rooms
				if (name == "Room1" || name == "Room2" || name == "Room3" || 
					name == "Room4" || name == "Room5")
				{
					// Found a specific room, return immediately (highest priority)
					return name;
				}
				else if (name == "Hallways")
				{
					// Found Hallways, keep as candidate but keep searching for specific room
					bestId = "Hallways";
				}
			}
		}

		return bestId;
	}

	private void OnBodyEnteredRoom(Node body, string roomId)
	{
		GD.Print($"[GameWorld] Body {body.Name} entered {roomId}");
		if (Multiplayer.IsServer() && body is NPCEntity npcEntity)
		{
			// Update GameEngine NPC location
			var npc = _gameEngine.GameState.GetNPC(npcEntity.NpcId);
			if (npc != null)
			{
				npc.CurrentRoomId = roomId;
				GD.Print($"[GameWorld] Server updated NPC {npc.Name} (ID: {npcEntity.NpcId}) location to: {roomId}");
			}
		}
	}

	private void SetupUI()
	{
		_uiLayer = new CanvasLayer();
		AddChild(_uiLayer);

		// Load custom font
		_customFont = ResourceLoader.Load<Font>("res://assets/Pixer-Regular.otf");

		// Add Coordinate Display
		// var coordinateDisplay = new CoordinateDisplay();
		// AddChild(coordinateDisplay);

		// ── Outer VBox: stacks timer box directly on top of triangle box, same width ──
		var hudOuterVBox = new VBoxContainer();
		hudOuterVBox.Position = new Vector2(8, 0);
		hudOuterVBox.AddThemeConstantOverride("separation", 6);
		hudOuterVBox.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
		_uiLayer.AddChild(hudOuterVBox);

		// Timer Panel — dark bg, same 2px black border as triangle box
		var timerPanel = new PanelContainer();
		timerPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; // match width of VBox
		timerPanel.MouseFilter = Control.MouseFilterEnum.Ignore;

		var timerBgStyle = new StyleBoxFlat();
		timerBgStyle.BgColor = new Color(0.05f, 0.05f, 0.05f, 0.72f);
		timerBgStyle.BorderColor = new Color(0f, 0f, 0f, 0.85f);
		timerBgStyle.SetBorderWidthAll(2); // match triangle box
		timerBgStyle.SetCornerRadiusAll(6);
		timerBgStyle.SetContentMarginAll(8);
		timerPanel.AddThemeStyleboxOverride("panel", timerBgStyle);
		hudOuterVBox.AddChild(timerPanel);

		_timerLabel = new Label();
		_timerLabel.Text = "180";
		_timerLabel.AddThemeFontOverride("font", _customFont);
		_timerLabel.AddThemeFontSizeOverride("font_size", 48);
		_timerLabel.AddThemeColorOverride("font_color", Colors.White);
		_timerLabel.AddThemeConstantOverride("outline_size", 6);
		_timerLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
		_timerLabel.HorizontalAlignment = HorizontalAlignment.Center;
		timerPanel.AddChild(_timerLabel);

		// Triangle+Icons Panel — white semi-transparent, same border
		var hudPanel = new PanelContainer();
		hudPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		hudPanel.MouseFilter = Control.MouseFilterEnum.Pass;

		var hudBgStyle = new StyleBoxFlat();
		hudBgStyle.BgColor = new Color(1f, 1f, 1f, 0.55f);
		hudBgStyle.BorderColor = new Color(0f, 0f, 0f, 0.85f);
		hudBgStyle.SetBorderWidthAll(2);
		hudBgStyle.SetCornerRadiusAll(6);
		hudBgStyle.SetContentMarginAll(6);
		hudPanel.AddThemeStyleboxOverride("panel", hudBgStyle);
		hudOuterVBox.AddChild(hudPanel);

		// VBox inside white panel: center-aligned
		var hudContainer = new VBoxContainer();
		hudContainer.AddThemeConstantOverride("separation", 6);
		hudContainer.Alignment = BoxContainer.AlignmentMode.Center;
		hudContainer.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		hudPanel.AddChild(hudContainer);
		
		_roleLabel = new Label();
		_roleLabel.Text = "";
		_roleLabel.Visible = false;
		_roleLabel.AddThemeFontOverride("font", _customFont);
		_roleLabel.AddThemeFontSizeOverride("font_size", 26);
		_roleLabel.AddThemeColorOverride("font_color", Colors.White);
		hudContainer.AddChild(_roleLabel);


		// Elimination Overlay (Shared for "You Died" and "Admirer Eliminated")
		_eliminationOverlay = new PanelContainer();
		_eliminationOverlay.Name = "EliminationOverlay";
		_eliminationOverlay.SetAnchorsPreset(Control.LayoutPreset.Center);
		// _eliminationOverlay.AnchorsPreset = (int)Control.LayoutPreset.Center; // Godot 4 style
		_eliminationOverlay.GrowHorizontal = Control.GrowDirection.Both;
		_eliminationOverlay.GrowVertical = Control.GrowDirection.Both;
		_eliminationOverlay.Visible = false;
		
		var elimStyle = new StyleBoxFlat();
		elimStyle.BgColor = new Color(0, 0, 0, 0.8f);
		elimStyle.SetCornerRadiusAll(10);
		elimStyle.SetContentMarginAll(20);
		_eliminationOverlay.AddThemeStyleboxOverride("panel", elimStyle);
		
		_eliminationLabel = new Label();
		_eliminationLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_eliminationLabel.VerticalAlignment = VerticalAlignment.Center;
		_eliminationLabel.AddThemeFontOverride("font", _customFont);
		_eliminationLabel.AddThemeFontSizeOverride("font_size", 42); // Big and centered
		_eliminationLabel.AddThemeColorOverride("font_color", Colors.Red);
		_eliminationOverlay.AddChild(_eliminationLabel);
		
		_uiLayer.AddChild(_eliminationOverlay);


		// Game Over Overlay (For eliminated player)
		var overlay = new PanelContainer();
		overlay.Name = "GameOverOverlay";
		overlay.AnchorsPreset = (int)Control.LayoutPreset.FullRect;
		overlay.Visible = false;
		var style = new StyleBoxFlat();
		style.BgColor = new Color(0, 0, 0, 0.8f);
		overlay.AddThemeStyleboxOverride("panel", style);
		var center = new CenterContainer();
		overlay.AddChild(center);
		var overLabel = new Label();
		overLabel.Text = "YOU HAVE BEEN ELIMINATED\n(Wait for next game)";
		overLabel.HorizontalAlignment = HorizontalAlignment.Center;
		overLabel.AddThemeFontSizeOverride("font_size", 32);
		overLabel.AddThemeColorOverride("font_color", Colors.Red);
		center.AddChild(overLabel);
		_uiLayer.AddChild(overlay);

		// Bottom Left Status Container (positioned above buttons)
		_bottomLeftStatusPanel = new PanelContainer();
		_bottomLeftStatusPanel.Position = new Vector2(20, 500); // Above buttons at y=600
		_uiLayer.AddChild(_bottomLeftStatusPanel);

		var bottomLeftStyle = new StyleBoxFlat();
		bottomLeftStyle.BgColor = new Color(0, 0, 0, 0.6f); // Dark translucent background
		bottomLeftStyle.SetCornerRadiusAll(4);
		bottomLeftStyle.SetContentMarginAll(8);
		_bottomLeftStatusPanel.AddThemeStyleboxOverride("panel", bottomLeftStyle);
		_bottomLeftStatusPanel.Visible = false;

		_bottomLeftStatusContainer = new VBoxContainer();
		_bottomLeftStatusPanel.AddChild(_bottomLeftStatusContainer);

		// _convertedLabel — commented out per request
		// _convertedLabel = new Label();
		// _convertedLabel.Text = "Converted: 0";
		// _convertedLabel.AddThemeFontOverride("font", _customFont);
		// _convertedLabel.AddThemeFontSizeOverride("font_size", 26);
		// _convertedLabel.AddThemeColorOverride("font_color", Colors.White);
		// _convertedLabel.Visible = false;
		// _bottomLeftStatusContainer.AddChild(_convertedLabel);

		// _metersContainer removed

		// Global Influence Counter — Triangle UI card (no background)
		var globalInfluenceCard = new PanelContainer();
		globalInfluenceCard.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
		globalInfluenceCard.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		hudContainer.AddChild(globalInfluenceCard);

		var globalInfluenceVBox = new VBoxContainer();
		globalInfluenceVBox.AddThemeConstantOverride("separation", 0); // icons flush with triangle
		globalInfluenceVBox.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
		globalInfluenceCard.AddChild(globalInfluenceVBox);

		// SubViewportContainer — this is the visible window into the triangle render
		var globalSvContainer = new SubViewportContainer();
		globalSvContainer.CustomMinimumSize = new Vector2(270, 225);
		globalSvContainer.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
		globalSvContainer.Stretch = true;
		// Nearest filtering keeps the viewport texture pixel-sharp; default Linear softens it.
		globalSvContainer.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
		globalInfluenceVBox.AddChild(globalSvContainer);

		var globalSv = new SubViewport();
		globalSv.Size = new Vector2I(270, 225);
		globalSv.Disable3D = true;
		globalSv.TransparentBg = true;
		globalSv.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		globalSvContainer.AddChild(globalSv);

		// Camera centred at triangle image centre (77,82) in TriangleScene local space.
		// Zoom=1.5: viewport shows 120×100 world units around centre, giving ~18px padding
		// around the outermost vertices (±30 wide, ±28 tall from centre).
		var globalTriangleCam = new Camera2D();
		globalTriangleCam.Position = new Vector2(77, 82);
		globalTriangleCam.Zoom = new Vector2(1.8f, 1.8f);
		globalSv.AddChild(globalTriangleCam);

		var globalTrianglePrefab = GD.Load<PackedScene>("res://scenes/TriangleScene.tscn");
		_globalInfluenceTriangle = globalTrianglePrefab.Instantiate<TriangleScene>();
		_globalInfluenceTriangle.Visible = true;
		globalSv.AddChild(_globalInfluenceTriangle);
		_globalInfluenceTriangle.ShowAllPoints();

		// Icon-only row below triangle — one icon per role, glows when winning.
		var globalIconsHBox = new HBoxContainer();
		globalIconsHBox.AddThemeConstantOverride("separation", 10); // tight between icons
		globalIconsHBox.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		globalInfluenceVBox.AddChild(globalIconsHBox);

		// Each icon is a Control stack: shader glow (behind) + TextureRect (front)
		// Stack is 90×90 so the radial glow can visually bleed beyond the icon bounds.
		void MakeRoleIcon(string iconPath, Color glowColor, out TextureRect iconOut, out ColorRect glowOut)
		{
			var stack = new Control();
			stack.CustomMinimumSize = new Vector2(82, 82);
			globalIconsHBox.AddChild(stack);

			var glowRect = new ColorRect();
			glowRect.Color = Colors.White;
			glowRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			glowRect.MouseFilter = Control.MouseFilterEnum.Ignore;
			glowRect.Visible = false;

			var glowShader = ResourceLoader.Load<Shader>("res://assets/glow_circle.gdshader");
			if (glowShader != null)
			{
				var mat = new ShaderMaterial();
				mat.Shader = glowShader;
				mat.SetShaderParameter("glow_color", glowColor);
				glowRect.Material = mat;
			}
			stack.AddChild(glowRect);

			// Icon on top — slightly inset so the glow spreads around it
			var icon = new TextureRect();
			icon.Texture = ResourceLoader.Load<Texture2D>(iconPath);
			icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			icon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			icon.OffsetLeft   =  16;
			icon.OffsetRight  = -16;
			icon.OffsetTop    =  16;
			icon.OffsetBottom = -16;
			icon.MouseFilter = Control.MouseFilterEnum.Ignore;
			stack.AddChild(icon);

			iconOut = icon;
			glowOut = glowRect;
		}

		MakeRoleIcon("res://scenes/prophet-btn.png",
			new Color(1.0f, 0.92f, 0.15f, 1f),  // yellow
			out _globalProphetIcon,  out _globalProphetGlow);
		MakeRoleIcon("res://scenes/admirer-bttn.png",
			new Color(1.0f, 0.92f, 0.15f, 1f),  // yellow
			out _globalAdmirerIcon,  out _globalAdmirerGlow);
		MakeRoleIcon("res://scenes/producer-btn.png",
			new Color(1.0f, 0.92f, 0.15f, 1f),  // yellow
			out _globalProducerIcon, out _globalProducerGlow);

		// Producer Stats Container (Active Cameras / Police)

		// Producer Stats Container (Active Cameras / Police)
		_producerStatsContainer = new VBoxContainer();
		hudContainer.AddChild(_producerStatsContainer);

		_activeCamerasLabel = new Label();
		_activeCamerasLabel.Text = "Active Security Cameras:\nNone";
		_activeCamerasLabel.AddThemeFontOverride("font", _customFont);
		_activeCamerasLabel.AddThemeFontSizeOverride("font_size", 26);
		_activeCamerasLabel.Visible = false;
		_bottomLeftStatusContainer.AddChild(_activeCamerasLabel);


		// Notification Panel (bottom right)
		var viewportSize = GetViewportRect().Size; // Use actual viewport to avoid clipping on smaller windows
		_notificationPanel = new PanelContainer();
		_notificationPanel.CustomMinimumSize = new Vector2(380, 280);
		_notificationPanelExpandedPosition = new Vector2(
			Mathf.Max(20, viewportSize.X - _notificationPanel.CustomMinimumSize.X - 30),
			Mathf.Max(20, viewportSize.Y - _notificationPanel.CustomMinimumSize.Y - 30));
		_notificationPanel.Position = _notificationPanelExpandedPosition;
		_notificationPanel.Visible = _notificationBoxEnabled;
		_uiLayer.AddChild(_notificationPanel);

		// Container for notification content with collapse button
		var notifContainer = new VBoxContainer();
		notifContainer.AddThemeConstantOverride("separation", 5);
		_notificationPanel.AddChild(notifContainer);

		// Top bar with collapse button
		var topBar = new HBoxContainer();
		topBar.AddThemeConstantOverride("separation", 5);
		topBar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		notifContainer.AddChild(topBar);

		var titleLabel = new Label();
		titleLabel.Text = "Notifications";
		titleLabel.AddThemeFontOverride("font", _customFont);
		titleLabel.AddThemeFontSizeOverride("font_size", 28);
		titleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		topBar.AddChild(titleLabel);

		_collapseNotificationButton = new Button();
		_collapseNotificationButton.Text = "−";
		_collapseNotificationButton.AddThemeFontOverride("font", _customFont);
		_collapseNotificationButton.AddThemeFontSizeOverride("font_size", 20);
		_collapseNotificationButton.CustomMinimumSize = new Vector2(36, 36);
		_collapseNotificationButton.Pressed += OnCollapseNotificationPressed;
		topBar.AddChild(_collapseNotificationButton);

		_notificationMargin = new MarginContainer();
		_notificationMargin.AddThemeConstantOverride("margin_left", 10);
		_notificationMargin.AddThemeConstantOverride("margin_top", 5);
		_notificationMargin.AddThemeConstantOverride("margin_right", 10);
		_notificationMargin.AddThemeConstantOverride("margin_bottom", 10);
		_notificationMargin.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		notifContainer.AddChild(_notificationMargin);

		_notificationText = new RichTextLabel();
		_notificationText.BbcodeEnabled = true;
		_notificationText.ScrollFollowing = true;
		_notificationText.AddThemeFontOverride("normal_font", _customFont);
		_notificationText.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_notificationMargin.AddChild(_notificationText);
		_notificationText.AddThemeFontSizeOverride("normal_font_size", 22);

		// Interaction Panel (Replaced by NPCDialogueUI)
		var dialogScene = GD.Load<PackedScene>("res://scenes/NpcDialogueBoxScene.tscn");
		_npcDialogueUI = dialogScene.Instantiate<NPCDialogueUI>();
		_npcDialogueUI.ActionSelected += OnActionSelected;
		_npcDialogueUI.PanelClosed += OnInteractionPanelClosed;
		_uiLayer.AddChild(_npcDialogueUI);

		// Prophet Trap Button
		var trapButton = new Button();
		trapButton.Text = "Set Trap";
		trapButton.AddThemeFontOverride("font", _customFont);
		trapButton.AddThemeFontSizeOverride("font_size", 26); // Increased font size
		trapButton.Position = new Vector2(20, 600);
		trapButton.CustomMinimumSize = new Vector2(180, 60); // Bigger to accommodate icon
		
		// Add Banana Icon
		var bananaTexture = ResourceLoader.Load<Texture2D>("res://assets/banana.png");
		trapButton.Icon = bananaTexture;
		trapButton.ExpandIcon = true;
		trapButton.IconAlignment = HorizontalAlignment.Left;
		trapButton.AddThemeConstantOverride("h_separation", 10);
		trapButton.AddThemeConstantOverride("icon_max_width", 40); // Change 40 to your desired width

		// Style settings
		var normalStyle = CreateTrapStyle(Colors.White, Colors.Black);
		var hoverStyle = CreateTrapStyle(new Color(0.85f, 0.85f, 0.85f, 1), Colors.Black);
		var pressedStyle = CreateTrapStyle(new Color(0.85f, 0.85f, 0.85f, 1), Colors.Black);
		var disabledStyle = CreateTrapStyle(new Color(0.6f, 0.6f, 0.6f, 1), Colors.Black);
		disabledStyle.SetBorderWidthAll(0); // Trap disabled has no border (from original code)

		trapButton.AddThemeStyleboxOverride("normal", normalStyle);
		trapButton.AddThemeStyleboxOverride("hover", hoverStyle);
		trapButton.AddThemeStyleboxOverride("pressed", pressedStyle);
		trapButton.AddThemeStyleboxOverride("disabled", disabledStyle);
		
		// Black text that stays black
		trapButton.AddThemeColorOverride("font_color", new Color(0, 0, 0, 1));
		trapButton.AddThemeColorOverride("font_hover_color", new Color(0, 0, 0, 1));
		trapButton.AddThemeColorOverride("font_pressed_color", new Color(0, 0, 0, 1));
		trapButton.AddThemeColorOverride("font_focus_color", new Color(0, 0, 0, 1));
		trapButton.AddThemeColorOverride("font_disabled_color", new Color(0, 0, 0, 1));
		
		// trapButton.Pressed += OnTrapButtonPressed; // Set Trap disabled per request
		_uiLayer.AddChild(trapButton);
		// Only visible if Prophet (handled in UpdateUI or default hidden?)
		// Ideally we verify role in UpdateUI.
		trapButton.Name = "TrapButton";
		trapButton.Visible = false;




		// Producer UI Elements
		SetupProducerUI();
		
		// Admirer UI Elements
		SetupAdmirerUI();

		// Prophet UI Elements
		SetupProphetUI();
		
		// Triangle Scene (Left Middle)
		var triangleScenePrefab = GD.Load<PackedScene>("res://scenes/TriangleScene.tscn");
		_triangleScene = triangleScenePrefab.Instantiate<TriangleScene>();
		_triangleScene.Visible = false; // Hidden by default
		// Scale up by 2x as requested
		_triangleScene.Scale = new Vector2(2.0f, 2.0f);
		
		// Position Bottom Right:
		// Content is roughly 150x150, scaled by 2 = 300x300.
		// Place with some margin from the edges.
		var vpSize = GetViewportRect().Size;
		_triangleScene.Position = new Vector2(vpSize.X - 350, vpSize.Y - 280);
		_triangleScene.ZIndex = 100; // Ensure it's on top
		_uiLayer.AddChild(_triangleScene);
		
		// Goals Menu System
		SetupGoalsMenu();
		
		// TEST NOTIFICATION
		CallDeferred(MethodName.AddSlidingNotification, "Notification System Online!");
	}

	private void SetupAdmirerUI()
	{
		// ── Knife Ultimate Button (Admirer only) ───────────────────────────────
		_admirerKnifeButton = new Button();
		_admirerKnifeButton.Name = "AdmirerKnifeButton";
		_admirerKnifeButton.Text = "🔪 Stab [Q]";
		_admirerKnifeButton.AddThemeFontOverride("font", _customFont);
		_admirerKnifeButton.AddThemeFontSizeOverride("font_size", 26);
		_admirerKnifeButton.Position          = new Vector2(20, 540);
		_admirerKnifeButton.CustomMinimumSize = new Vector2(220, 55);
		_admirerKnifeButton.Visible           = false; // shown only for Admirer

		var knifeStyle = CreateTrapStyle(new Color(0.85f, 0.1f, 0.1f, 1f), Colors.Black); // red
		_admirerKnifeButton.AddThemeStyleboxOverride("normal",   knifeStyle);
		_admirerKnifeButton.AddThemeStyleboxOverride("hover",    CreateTrapStyle(new Color(1.0f, 0.2f, 0.2f, 1f), Colors.Black));
		_admirerKnifeButton.AddThemeStyleboxOverride("pressed",  CreateTrapStyle(new Color(0.6f, 0.05f, 0.05f, 1f), Colors.Black));
		_admirerKnifeButton.AddThemeStyleboxOverride("disabled", CreateTrapStyle(new Color(0.4f, 0.4f, 0.4f, 0.8f), Colors.DarkGray));
		_admirerKnifeButton.AddThemeColorOverride("font_color",          Colors.White);
		_admirerKnifeButton.AddThemeColorOverride("font_hover_color",    Colors.White);
		_admirerKnifeButton.AddThemeColorOverride("font_pressed_color",  Colors.White);
		_admirerKnifeButton.AddThemeColorOverride("font_disabled_color", new Color(0.6f, 0.6f, 0.6f, 1f));
		_admirerKnifeButton.Pressed += OnAdmirerKnifeButtonPressed;
		_uiLayer.AddChild(_admirerKnifeButton);

		_admirerKnifeLabel = new Label();
		_admirerKnifeLabel.Name = "AdmirerKnifeLabel";
		_admirerKnifeLabel.AddThemeFontOverride("font", _customFont);
		_admirerKnifeLabel.AddThemeFontSizeOverride("font_size", 22);
		_admirerKnifeLabel.AddThemeColorOverride("font_color", Colors.White);
		_admirerKnifeLabel.Position = new Vector2(20, 600);
		_admirerKnifeLabel.Text     = "Press Q near a contestant";
		_admirerKnifeLabel.Visible  = false;
		_uiLayer.AddChild(_admirerKnifeLabel);
		SetupAdmirerAura();
	}

	private void SetupAdmirerAura()
	{
		// Build regardless of role check here, handle visibility in HandleAdmirerKnifeInput
		if (_admirerAuraVisual != null && IsInstanceValid(_admirerAuraVisual)) return;

		_admirerAuraVisual = new Node2D();
		_admirerAuraVisual.Name = "AdmirerWitnessAura";
		_admirerAuraVisual.ZIndex = -1; // behind characters, above floor
		_admirerAuraVisual.Visible = false; // only show during hunt
		AddChild(_admirerAuraVisual);

		var visual = new Node2D();
		visual.Name = "AuraCircle";
		_admirerAuraVisual.AddChild(visual);

		visual.Draw += () =>
		{
			// Draw subtle red pulsing radius (fixed 350f world units)
			visual.DrawCircle(Vector2.Zero, KNIFE_WITNESS_RADIUS, new Color(1.0f, 0.1f, 0.1f, 0.15f));
			visual.DrawArc(Vector2.Zero, KNIFE_WITNESS_RADIUS, 0, Mathf.Tau, 64, new Color(1.0f, 0.2f, 0.2f, 0.6f), 3.0f, true);
		};
	}

	private void SetupProducerUI()
	{
		// ── Cash Trail Ultimate Button (Producers only) ──────────────────────────
		_ultimateButton = new Button();
		_ultimateButton.Name  = "UltimateButton";
		_ultimateButton.Text  = "⚡ Cash Trail";
		_ultimateButton.AddThemeFontOverride("font", _customFont);
		_ultimateButton.AddThemeFontSizeOverride("font_size", 26);
		_ultimateButton.Position          = new Vector2(20, 540);
		_ultimateButton.CustomMinimumSize = new Vector2(220, 55);
		_ultimateButton.Visible           = false; // shown only for Producer

		var ulStyle = CreateTrapStyle(new Color(1.0f, 0.85f, 0.0f, 1f), Colors.Black); // gold
		_ultimateButton.AddThemeStyleboxOverride("normal",   ulStyle);
		_ultimateButton.AddThemeStyleboxOverride("hover",    CreateTrapStyle(new Color(0.9f, 0.75f, 0.0f, 1f), Colors.Black));
		_ultimateButton.AddThemeStyleboxOverride("pressed",  CreateTrapStyle(new Color(0.7f, 0.6f,  0.0f, 1f), Colors.Black));
		_ultimateButton.AddThemeStyleboxOverride("disabled", CreateTrapStyle(new Color(0.4f, 0.4f,  0.4f, 0.8f), Colors.DarkGray));
		_ultimateButton.AddThemeColorOverride("font_color",          Colors.Black);
		_ultimateButton.AddThemeColorOverride("font_hover_color",    Colors.Black);
		_ultimateButton.AddThemeColorOverride("font_pressed_color",  Colors.Black);
		_ultimateButton.AddThemeColorOverride("font_disabled_color", new Color(0.6f, 0.6f, 0.6f, 1f));
		_ultimateButton.Pressed += OnUltimateButtonPressed;
		_uiLayer.AddChild(_ultimateButton);

		_ultimateCooldownLabel = new Label();
		_ultimateCooldownLabel.Name = "UltimateCooldownLabel";
		_ultimateCooldownLabel.AddThemeFontOverride("font", _customFont);
		_ultimateCooldownLabel.AddThemeFontSizeOverride("font_size", 22);
		_ultimateCooldownLabel.AddThemeColorOverride("font_color", Colors.White);
		_ultimateCooldownLabel.Position = new Vector2(20, 600);
		_ultimateCooldownLabel.Text     = "[Q] to activate";
		_ultimateCooldownLabel.Visible  = false;
		_uiLayer.AddChild(_ultimateCooldownLabel);

		
		// Manage Cameras Button (Restyled and Repositioned)
		var manageCamsBtn = new Button();
		manageCamsBtn.Name = "ManageCamerasButton"; // Explicit name for visibility toggling
		manageCamsBtn.Text = "Manage Cameras";
		manageCamsBtn.ToggleMode = true;
		manageCamsBtn.AddThemeFontOverride("font", _customFont);
		manageCamsBtn.AddThemeFontSizeOverride("font_size", 26);
		// Position above Marry Button (Swapped with Marry)
		manageCamsBtn.Position = new Vector2(20, 600);
		manageCamsBtn.CustomMinimumSize = new Vector2(240, 60);
		manageCamsBtn.Visible = false;
		
		// Apply same style to Manage Cameras
		manageCamsBtn.AddThemeStyleboxOverride("normal", CreateTrapStyle(Colors.White, Colors.Black));
		manageCamsBtn.AddThemeStyleboxOverride("hover", CreateTrapStyle(new Color(0.85f, 0.85f, 0.85f, 1), Colors.Black));
		
		// Pressed matches Trap DISABLED
		var mcPressed = CreateTrapStyle(new Color(0.6f, 0.6f, 0.6f, 1), Colors.Black);
		mcPressed.SetBorderWidthAll(0);
		manageCamsBtn.AddThemeStyleboxOverride("pressed", mcPressed);
		
		var mcDisabled = CreateTrapStyle(new Color(0.6f, 0.6f, 0.6f, 1), Colors.Black);
		mcDisabled.SetBorderWidthAll(0);
		manageCamsBtn.AddThemeStyleboxOverride("disabled", mcDisabled);
		
		manageCamsBtn.AddThemeColorOverride("font_color", Colors.Black);
		manageCamsBtn.AddThemeColorOverride("font_hover_color", Colors.Black);
		manageCamsBtn.AddThemeColorOverride("font_pressed_color", Colors.Black);
		manageCamsBtn.AddThemeColorOverride("font_focus_color", Colors.Black);
		manageCamsBtn.MouseFilter = Control.MouseFilterEnum.Stop;

			// Use Toggled to bind visibility directly to button state
		// manageCamsBtn.Toggled += (pressed) => 
		// {
		// 	var panel = _uiLayer.GetNodeOrNull<Control>("CameraSelectPanel");
		// 	if (panel != null) panel.Visible = pressed;
		// 	
		// 	if (pressed)
		// 	{
		// 		// Capture open position for distance check
		// 		if (_localPlayer != null) _cameraSelectPanelOpenPos = _localPlayer.Position;
		// 	}
		// };
		_uiLayer.AddChild(manageCamsBtn);

		// _uiLayer.AddChild(cPanel); // REMOVED


		// Editorial Attention Section (Visible)
		var ePanel = new PanelContainer();
		ePanel.Name = "EditorialAttentionPanel";
		ePanel.Position = new Vector2(950, 340); // Shifted down to avoid overlap
		ePanel.CustomMinimumSize = new Vector2(180, 150);
		ePanel.Visible = false;
		var eVBox = new VBoxContainer();
		eVBox.Name = "EditorialContainer";
		eVBox.AddThemeConstantOverride("separation", 5);
		ePanel.AddChild(eVBox);
		var eLabel = new Label();
		eLabel.Text = "Editorial Attention";
		eLabel.AddThemeFontOverride("font", _customFont);
		eLabel.AddThemeFontSizeOverride("font_size", 14);
		eVBox.AddChild(eLabel);
		var eInfo = new Label();
		eInfo.Name = "EditorialInfo";
		eInfo.Text = "Focus: None";
		eInfo.AddThemeFontOverride("font", _customFont);
		eInfo.AutowrapMode = TextServer.AutowrapMode.Word;
		eVBox.AddChild(eInfo);
		var eBtn = new Button();
		eBtn.Text = "Set Focus";
		eBtn.AddThemeFontOverride("font", _customFont);
		eBtn.Pressed += () => TogglePanel("FocusPanel");
		eVBox.AddChild(eBtn);
		_uiLayer.AddChild(ePanel);

		// Camera Selection Panel (Hidden)
		var csPanel = new PanelContainer();
		csPanel.Name = "CameraSelectPanel";
		csPanel.Name = "CameraSelectPanel";
		// Center panel on screen
		csPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		csPanel.GrowHorizontal = Control.GrowDirection.Both;
		csPanel.GrowVertical = Control.GrowDirection.Both;
		csPanel.Visible = false;
		
		var csVBox = new VBoxContainer();
		csVBox.AddThemeConstantOverride("separation", 5); // Tighter spacing
		csPanel.AddChild(csVBox);
		var csLabel = new Label();
		csLabel.Text = "Toggle Cameras (Max 2):";
		csLabel.HorizontalAlignment = HorizontalAlignment.Center; // Center title
		csLabel.AddThemeFontSizeOverride("font_size", 18);
		csLabel.AddThemeFontOverride("font", _customFont);
		csVBox.AddChild(csLabel);
		
		// Editorial Room Asset Integration
		var svContainer = new SubViewportContainer();
		// Minimal Map Size (Aggressively cropped)
		svContainer.CustomMinimumSize = new Vector2(400, 260);
		svContainer.Stretch = true;
		csVBox.AddChild(svContainer);

		var subViewport = new SubViewport();
		subViewport.Size = new Vector2I(400, 260); 
		subViewport.Disable3D = true;
		subViewport.TransparentBg = true;
		subViewport.PhysicsObjectPicking = true;
		svContainer.AddChild(subViewport);

		// Adjusted Camera:
		// Position: 590, 360 (Shifted left to center map in viewport)
		// Zoom: 0.7 (Zoomed in slightly more to fill space)
		var camera = new Camera2D();
		camera.Position = new Vector2(590, 360); 
		camera.Zoom = new Vector2(0.7f, 0.7f); 
		subViewport.AddChild(camera);

		var assetScene = ResourceLoader.Load<PackedScene>("res://scenes/editorial_room_asset.tscn");
		if (assetScene != null)
		{
			var assetInstance = assetScene.Instantiate();
			subViewport.AddChild(assetInstance);

			// Connect signals from the asset buttons
			// Structure: root -> Node2D -> [ButtonRoom1, ButtonRoom2, ...]
			var contentNode = assetInstance.GetNodeOrNull("Node2D");
			if (contentNode != null)
			{
				// keep reference to the content node so we can update room visuals later
				_editorialContentNode = contentNode as Node2D;

				foreach (var child in contentNode.GetChildren())
				{
					if (child.HasSignal("room_clicked"))
					{
						child.Connect("room_clicked", Callable.From<string>((roomName) =>
						{
							// Remove spaces to match command format (e.g. "Room 1" -> "Room1")
							string cleanName = roomName.Replace(" ", "");
							// GD.Print($"[GameWorld] Producer selected camera: {cleanName}");
							// OnActionSelected("producer_global", $"toggle_camera_{cleanName}"); // Camera logic disabled per request
						}));
					}
				}
			}
		}
		else
		{
			GD.PrintErr("Failed to load editorial_room_asset.tscn");
		}
		
		var closeBtn = new Button();
		closeBtn.Text = "Close Map";
		closeBtn.CustomMinimumSize = new Vector2(0, 50); // Taller button
		closeBtn.AddThemeFontOverride("font", _customFont);
		closeBtn.AddThemeFontSizeOverride("font_size", 24); // Larger text
		
		// Apply consistent Trap Style to Close Button
		closeBtn.AddThemeStyleboxOverride("normal", CreateTrapStyle(Colors.White, Colors.Black));
		closeBtn.AddThemeStyleboxOverride("hover", CreateTrapStyle(new Color(0.85f, 0.85f, 0.85f, 1), Colors.Black));
		closeBtn.AddThemeStyleboxOverride("pressed", CreateTrapStyle(new Color(0.85f, 0.85f, 0.85f, 1), Colors.Black));
		closeBtn.AddThemeColorOverride("font_color", Colors.Black);
		closeBtn.AddThemeColorOverride("font_hover_color", Colors.Black);
		closeBtn.AddThemeColorOverride("font_pressed_color", Colors.Black);
		closeBtn.MouseFilter = Control.MouseFilterEnum.Stop;

		// Sync state: Unpress the toggle button, which updates visibility (if signal emitted, but safer to do both)
		closeBtn.Pressed += () => 
		{
			manageCamsBtn.ButtonPressed = false; // Reset toggle visual
			csPanel.Visible = false; // Hide panel
		};
		csVBox.AddChild(closeBtn);

		_uiLayer.AddChild(csPanel);

		// Focus Panel (Hidden)
		var fPanel = new PanelContainer();
		fPanel.Name = "FocusPanel";
		fPanel.Position = new Vector2(500, 300);
		fPanel.Visible = false;
		var fVBox = new VBoxContainer();
		fVBox.AddThemeConstantOverride("separation", 10);
		fPanel.AddChild(fVBox);
		var fLabel = new Label();
		fLabel.Text = "Select Editorial Focus:";
		fLabel.AddThemeFontSizeOverride("font_size", 14);
		fLabel.AddThemeFontOverride("font", _customFont);
		fVBox.AddChild(fLabel);
		var fGrid = new GridContainer();
		fGrid.Columns = 2; // 2x2
		fVBox.AddChild(fGrid);
		
		// Define Focus Options
		string[] focusOptions = { "Drama", "Romance", "Suspense", "Action" };
		string[] focusIds = { "drama", "romance", "suspense", "action" };

		for (int i = 0; i < focusOptions.Length; i++)
		{
			var qBtn = new Button();
			qBtn.Text = focusOptions[i];
			qBtn.AddThemeFontOverride("font", _customFont);
			qBtn.CustomMinimumSize = new Vector2(120, 80);
			int qIdx = i;
			string focusId = focusIds[i];
			qBtn.Pressed += () => {
				OnActionSelected("producer_global", $"set_editorial_focus_{focusId}");
				// Update editorial attention display
				var editorialInfo = _uiLayer.GetNodeOrNull<Label>("EditorialAttentionPanel/EditorialContainer/EditorialInfo");
				if (editorialInfo != null) editorialInfo.Text = $"Focus: {focusOptions[qIdx]}";
				// Hide panel
				var fp = _uiLayer.GetNodeOrNull<Control>("FocusPanel");
				if (fp != null) fp.Visible = false;
			};
			fGrid.AddChild(qBtn);
		}
		_uiLayer.AddChild(fPanel);
	}

	// Goals Menu System (Replaced by Action Table)
	private ActionTable _actionTable;

	private void SetupGoalsMenu()
	{
		// Action Table (Goals Menu replacement)
		_actionTable = new ActionTable();
		_uiLayer.AddChild(_actionTable);

		// Goals Button (upper right corner) - Info Icon
		var viewportSize = GetViewportRect().Size;
		var infoIconTexture = ResourceLoader.Load<Texture2D>("res://assets/ai_info_icon.png");
		var phoneTexture = ResourceLoader.Load<Texture2D>("res://assets/phone-menu.png");

		// buttonWidth based on original phone texture for layout reference
		float buttonWidth = phoneTexture != null ? phoneTexture.GetWidth() * 2 : 40;
		float aiButtonWidth = buttonWidth * 0.7f;

		_goalsButton = new TextureButton();
		_goalsButton.TextureNormal = infoIconTexture;
		_goalsButton.IgnoreTextureSize = true;
		_goalsButton.StretchMode = TextureButton.StretchModeEnum.Scale;
		// Match the AI leaderboard button size
		float infoScale = infoIconTexture != null ? aiButtonWidth / (float)infoIconTexture.GetWidth() : 1;
		float infoButtonHeight = infoIconTexture != null ? infoIconTexture.GetHeight() * infoScale : aiButtonWidth;
		_goalsButton.CustomMinimumSize = new Vector2(aiButtonWidth, infoButtonHeight);
		_goalsButton.Size = new Vector2(aiButtonWidth, infoButtonHeight);
		
		// Position in upper right corner
		float phoneButtonHeight = infoButtonHeight;
		_goalsButton.Position = new Vector2(
			Mathf.Max(20, viewportSize.X - aiButtonWidth - 30),
			20);
		_goalsButton.Pressed += OnGoalsButtonPressed;
		_uiLayer.AddChild(_goalsButton);

		// AI Leaderboard Button (below phone button, same width)
		var aiLeaderboardTexture = ResourceLoader.Load<Texture2D>("res://assets/ai_leaderboard_button.png");
		if (aiLeaderboardTexture != null)
		{
			_aiLeaderboardButton = new TextureButton();
			_aiLeaderboardButton.TextureNormal = aiLeaderboardTexture;
			_aiLeaderboardButton.IgnoreTextureSize = true;
			_aiLeaderboardButton.StretchMode = TextureButton.StretchModeEnum.Scale;
			// Uses aiButtonWidth from outer scope (70% of phone button width)
			float aiScale = aiButtonWidth / (float)aiLeaderboardTexture.GetWidth();
			_aiLeaderboardButton.CustomMinimumSize = new Vector2(aiButtonWidth, aiLeaderboardTexture.GetHeight() * aiScale);
			_aiLeaderboardButton.Size = new Vector2(aiButtonWidth, aiLeaderboardTexture.GetHeight() * aiScale);
			// Position below the info button with minimal gap
			_aiLeaderboardButton.Position = new Vector2(
				Mathf.Max(20, viewportSize.X - aiButtonWidth - 30),
				20 + phoneButtonHeight + 2);
			_aiLeaderboardButton.Pressed += OnAiLeaderboardButtonPressed;
			_uiLayer.AddChild(_aiLeaderboardButton);
		}
		else
		{
			GD.PrintErr("[GameWorld] Failed to load ai_leaderboard_button.png");
		}

		// AI Leaderboard Panel (right side of screen, initially hidden)
		var leaderboardBgTexture = ResourceLoader.Load<Texture2D>("res://assets/ai_leaderboard_background.png");
		if (leaderboardBgTexture != null)
		{
			// Background Dimming Overlay
			_leaderboardBgDim = new ColorRect();
			_leaderboardBgDim.Color = new Color(0, 0, 0, 0.4f);
			_leaderboardBgDim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			_leaderboardBgDim.Visible = false;
			_leaderboardBgDim.MouseFilter = Control.MouseFilterEnum.Stop;
			_leaderboardBgDim.GuiInput += (ev) => {
				if (ev is InputEventMouseButton mb && mb.Pressed)
					OnAiLeaderboardButtonPressed(); // Close on click outside
			};
			_uiLayer.AddChild(_leaderboardBgDim);

			_aiLeaderboardPanel = new TextureRect();
			_aiLeaderboardPanel.Texture = leaderboardBgTexture;
			_aiLeaderboardPanel.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			_aiLeaderboardPanel.StretchMode = TextureRect.StretchModeEnum.Scale;
			
			float panelHeight = 720;
			float aspectRatio = (float)leaderboardBgTexture.GetWidth() / (float)leaderboardBgTexture.GetHeight();
			_leaderboardPanelWidth = panelHeight * aspectRatio;
			_aiLeaderboardPanel.Size = new Vector2(_leaderboardPanelWidth, panelHeight);
			
			// Initial position: off-screen
			_aiLeaderboardPanel.Position = new Vector2(viewportSize.X, 0);
			_aiLeaderboardPanel.Visible = false;
			_uiLayer.AddChild(_aiLeaderboardPanel);

			float borderLeft  = _leaderboardPanelWidth  * 0.15f + 5;
			float borderRight = _leaderboardPanelWidth  * 0.15f + 5;
			float contentWidth = _leaderboardPanelWidth - borderLeft - borderRight;

			// 1. INFLUENCE TEXT (Moving up by 5% from previous 11%)
			float textTop = panelHeight * 0.06f; 
			_leaderboardInfluenceLabel = new Label();
			_leaderboardInfluenceLabel.Text = "";
			_leaderboardInfluenceLabel.AddThemeFontOverride("font", _customFont);
			_leaderboardInfluenceLabel.AddThemeFontSizeOverride("font_size", 23);
			_leaderboardInfluenceLabel.AddThemeColorOverride("font_color", Colors.White);
			_leaderboardInfluenceLabel.AddThemeColorOverride("font_shadow_color", Colors.Black);
			_leaderboardInfluenceLabel.AddThemeConstantOverride("shadow_offset_x", 2);
			_leaderboardInfluenceLabel.AddThemeConstantOverride("shadow_offset_y", 2);
			_leaderboardInfluenceLabel.AutowrapMode = TextServer.AutowrapMode.Word;
			_leaderboardInfluenceLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_leaderboardInfluenceLabel.Position = new Vector2(borderLeft, textTop);
			_leaderboardInfluenceLabel.Size = new Vector2(contentWidth, panelHeight * 0.10f);
			_aiLeaderboardPanel.AddChild(_leaderboardInfluenceLabel);

			// 2. TRIANGLE (RESTORING to 0.02f as it was before)
			var lbTrianglePrefab = GD.Load<PackedScene>("res://scenes/TriangleScene.tscn");
			_leaderboardTriangle = lbTrianglePrefab.Instantiate<TriangleScene>();
			_leaderboardTriangle.Scale = new Vector2(3.125f, 3.125f);
			_leaderboardTriangle.Position = new Vector2(_leaderboardPanelWidth / 2 - 77 * 3.125f, panelHeight * 0.02f); 
			_leaderboardTriangle.Visible = true;
			_aiLeaderboardPanel.AddChild(_leaderboardTriangle);
			_leaderboardTriangle.ShowAllPoints();

			// 3. DETAILS (Separated from triangle)
			float triangleHeight = (82 + 24) * 3.125f;
			float detailsTop = panelHeight * 0.02f + triangleHeight + 15; 
			var detailsWrapper = new ScrollContainer();
			detailsWrapper.Position = new Vector2(borderLeft, detailsTop);
			float borderBottom = panelHeight * 0.03f + 5;
			detailsWrapper.Size = new Vector2(contentWidth, panelHeight - detailsTop - borderBottom - 80);
			_aiLeaderboardPanel.AddChild(detailsWrapper);

			_leaderboardDetailsContainer = new VBoxContainer();
			_leaderboardDetailsContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			_leaderboardDetailsContainer.AddThemeConstantOverride("separation", 10);
			detailsWrapper.AddChild(_leaderboardDetailsContainer);

			_leaderboardDetailsLabel = new Label();
			_leaderboardDetailsLabel.Text = "DETAILS";
			_leaderboardDetailsLabel.AddThemeFontOverride("font", _customFont);
			_leaderboardDetailsLabel.AddThemeFontSizeOverride("font_size", 24);
			_leaderboardDetailsLabel.AddThemeColorOverride("font_color", Colors.SkyBlue);
			_leaderboardDetailsLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_leaderboardDetailsContainer.AddChild(_leaderboardDetailsLabel);

			_leaderboardGrid = new GridContainer();
			_leaderboardGrid.Columns = 2;
			_leaderboardGrid.AddThemeConstantOverride("h_separation", 15);
			_leaderboardGrid.AddThemeConstantOverride("v_separation", 10);
			_leaderboardDetailsContainer.AddChild(_leaderboardGrid);

			// Close button
			var closeLeaderboardBtn = new Button();
			closeLeaderboardBtn.Text = "Close";
			closeLeaderboardBtn.AddThemeFontOverride("font", _customFont);
			closeLeaderboardBtn.AddThemeFontSizeOverride("font_size", 27);
			closeLeaderboardBtn.CustomMinimumSize = new Vector2(120, 40);
			var closeBtnStyle = new StyleBoxFlat();
			closeBtnStyle.BgColor = new Color(0.15f, 0.15f, 0.3f, 0.9f);
			closeBtnStyle.SetCornerRadiusAll(6);
			closeBtnStyle.SetContentMarginAll(8);
			closeLeaderboardBtn.AddThemeStyleboxOverride("normal", closeBtnStyle);
			var closeBtnHover = new StyleBoxFlat();
			closeBtnHover.BgColor = new Color(0.25f, 0.25f, 0.45f, 0.9f);
			closeBtnHover.SetCornerRadiusAll(6);
			closeBtnHover.SetContentMarginAll(8);
			closeLeaderboardBtn.AddThemeStyleboxOverride("hover", closeBtnHover);
			closeLeaderboardBtn.AddThemeColorOverride("font_color", Colors.White);
			float closeBtnX = (_leaderboardPanelWidth - 120) / 2;
			float closeBtnY = panelHeight - 60 - (panelHeight * 0.05f);
			closeLeaderboardBtn.Position = new Vector2(closeBtnX, closeBtnY);
			closeLeaderboardBtn.Pressed += OnAiLeaderboardButtonPressed;
			closeLeaderboardBtn.Visible = true;
			_aiLeaderboardPanel.AddChild(closeLeaderboardBtn);
		}
		else
		{
			GD.PrintErr("[GameWorld] Failed to load ai_leaderboard_background.png");
		}
	}

	private void OnGoalsButtonPressed()
	{
		if (_actionTable != null)
		{
			// Toggle visibility
			_actionTable.Visible = !_actionTable.Visible;
		}

	}

	private void OnGoalsMenuClosed()
	{
		// Optional: Handle any logic when goals menu is closed
	}

	private void OnAiLeaderboardButtonPressed()
	{
		if (_aiLeaderboardPanel == null) return;

		_isLeaderboardOpen = !_isLeaderboardOpen;
		bool opening = _isLeaderboardOpen;
		
		var tween = GetTree().CreateTween();
		tween.SetParallel(true);
		tween.SetTrans(Tween.TransitionType.Cubic);
		tween.SetEase(Tween.EaseType.Out);

		var viewportSize = GetViewportRect().Size;
		float targetX = opening ? (viewportSize.X - _leaderboardPanelWidth + (_leaderboardPanelWidth * 0.1f)) : viewportSize.X;

		if (opening)
		{
			_aiLeaderboardPanel.Visible = true;
			_leaderboardBgDim.Visible = true;
			_leaderboardBgDim.Modulate = new Color(1, 1, 1, 0);
			tween.TweenProperty(_leaderboardBgDim, "modulate:a", 1.0f, 0.3f);
			_leaderboardTriangle?.ShowAllPoints();
			UpdateLeaderboardText();
		}

		else
		{
			tween.TweenProperty(_leaderboardBgDim, "modulate:a", 0.0f, 0.3f);
		}

		tween.TweenProperty(_aiLeaderboardPanel, "position:x", targetX, 0.4f);
		
		if (!opening)
		{
			tween.Chain().TweenCallback(OfCallable(() => {
				_aiLeaderboardPanel.Visible = false;
				_leaderboardBgDim.Visible = false;
			}));
		}
	}

	// Helper for clean syntax in Tween callbacks
	private Callable OfCallable(Action action) => Callable.From(action);

	private void UpdateLeaderboardText()
	{
		if (_leaderboardInfluenceLabel == null || _leaderboardTriangle == null || _leaderboardGrid == null) return;

		// Get counts per role
		int admirerCount  = _leaderboardTriangle.AdmirerZoneCount;
		int prophetCount  = _leaderboardTriangle.ProphetZoneCount;
		int producerCount = _leaderboardTriangle.ProducerZoneCount;

		// Build sorted list for ranking
		var rankings = new List<(string role, int count)>
		{
			("admirer",  admirerCount),
			("prophet",  prophetCount),
			("producer", producerCount)
		};
		rankings.Sort((a, b) => b.count.CompareTo(a.count));

		string myRole = _myRole?.ToLower() ?? "";
		int myCount = myRole switch
		{
			"admirer"  => admirerCount,
			"prophet"  => prophetCount,
			"producer" => producerCount,
			_ => 0
		};

		int myPlace = 1;
		foreach (var r in rankings)
		{
			if (r.count > myCount) myPlace++;
		}

		var tiedWith = new List<string>();
		foreach (var r in rankings)
		{
			if (r.role != myRole && r.count == myCount)
				tiedWith.Add(Capitalize(r.role));
		}

		string placeStr = myPlace switch { 1 => "1st", 2 => "2nd", _ => "3rd" };
		string text = $"You have influenced {myCount} contestants. ";

		if (tiedWith.Count > 0)
			text += $"You are tied for {placeStr} place with {string.Join(" and ", tiedWith)}.";
		else
			text += $"You are in {placeStr} place.";

		_leaderboardInfluenceLabel.Text = text;

		// Update grid with icons and aligned neutral count
		foreach (var child in _leaderboardGrid.GetChildren())
		{
			child.QueueFree();
		}

		int neutralCount = _leaderboardTriangle.NeutralZoneCount;
		
		// Add rows for each role
		CreateDetailRow("prophet", prophetCount, myRole == "prophet");
		CreateDetailRow("producer", producerCount, myRole == "producer");
		CreateDetailRow("admirer", admirerCount, myRole == "admirer");
		
		// Neutral row (aligned using a dummy icon space)
		var dummyIcon = new Control();
		dummyIcon.CustomMinimumSize = new Vector2(40, 40);
		_leaderboardGrid.AddChild(dummyIcon);

		var neutralLabel = new Label();
		neutralLabel.Text = $"Neutral: {neutralCount}";
		neutralLabel.AddThemeFontOverride("font", _customFont);
		neutralLabel.AddThemeFontSizeOverride("font_size", 22);
		neutralLabel.AddThemeColorOverride("font_color", Colors.White);
		_leaderboardGrid.AddChild(neutralLabel);
	}

	private void CreateDetailRow(string role, int count, bool isLocalPlayer)
	{
		var iconRect = new TextureRect();
		string iconPath = role switch {
			"prophet" => "res://assets/prophet-btn.png",
			"producer" => "res://assets/producer-btn.png",
			_ => "res://assets/admirer-bttn.png"
		};
		iconRect.Texture = ResourceLoader.Load<Texture2D>(iconPath);
		iconRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		iconRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		iconRect.CustomMinimumSize = new Vector2(40, 40);
		_leaderboardGrid.AddChild(iconRect);

		var label = new Label();
		label.Text = $"{Capitalize(role)}: {count}";
		if (isLocalPlayer) label.Text += " (YOU)";
		label.AddThemeFontOverride("font", _customFont);
		label.AddThemeFontSizeOverride("font_size", 22);
		label.AddThemeColorOverride("font_color", isLocalPlayer ? Colors.Yellow : Colors.White);
		_leaderboardGrid.AddChild(label);
	}

	private void TogglePanel(string name)
	{
		var node = _uiLayer.GetNodeOrNull<Control>(name);
		if (node != null) node.Visible = !node.Visible;
	}


	private void InitializeServer()
	{
		var configPath = ProjectSettings.GlobalizePath("res://data/game_configuration.json");
		_gameEngine = new GameEngine(configPath);
		
		// Subscribe to score changes
		_gameEngine.GameState.OnScoreChange += HandleScoreChange;
		
		_gameActive = true;
		_timeRemaining = 180.0;

		SpawnNPCs();
		SpawnAllPlayers();
		BroadcastGameState();
	}

	private void InitializeClient()
	{
		// Request state from server - players will be spawned when state arrives
		RpcId(1, MethodName.RequestGameState);
	}

	/// <summary>
	/// Returns a spawn position that does not overlap any layer-1 (wall/world) collision body.
	/// Tries up to <paramref name="maxAttempts"/> random candidates; returns the last candidate
	/// if none are completely clear (best-effort fallback).
	/// </summary>
	private Vector2 FindFreeNPCSpawnPosition(int maxAttempts = 30)
	{
		// The isometric map is positioned at world (600, 250) with 4x scale.
		const float CX = 600f;
		const float CY = 250f;

		var spawnRanges = new List<(float minX, float maxX, float minY, float maxY)>
		{
			(CX - 300, CX + 300, CY - 150, CY + 150),   // Central cluster
			(CX - 150, CX + 450, CY + 100, CY + 350),   // Slightly south
			(CX - 450, CX + 150, CY - 300, CY + 100),   // Slightly north/west
			(CX + 150, CX + 500, CY - 200, CY + 200),   // East corridor
			(CX - 500, CX - 100, CY - 100, CY + 250),   // West corridor
		};

		var random = new Random();
		var spaceState = GetWorld2D()?.DirectSpaceState;

		Vector2 candidate = Vector2.Zero;
		for (int attempt = 0; attempt < maxAttempts; attempt++)
		{
			var range = spawnRanges[random.Next(spawnRanges.Count)];
			float x = (float)(random.NextDouble() * (range.maxX - range.minX) + range.minX);
			float y = (float)(random.NextDouble() * (range.maxY - range.minY) + range.minY);
			candidate = new Vector2(x, y);

			if (spaceState == null)
				break; // Physics not ready yet, just use the candidate

			// Query for any *static bodies* (layer 1 = walls/world geometry) at this point.
			// Use a small circle to account for the NPC collision shape half-width (≈19 px).
			var shape = new CircleShape2D { Radius = 20f };
			var shapeParams = new PhysicsShapeQueryParameters2D
			{
				Shape = shape,
				Transform = new Transform2D(0f, candidate),
				CollisionMask = 1,          // Only layer 1 (world/walls)
				CollideWithBodies = true,
				CollideWithAreas = false,
			};

			var hits = spaceState.IntersectShape(shapeParams, maxResults: 1);
			if (hits.Count == 0)
			{
				// Position is clear — use it
				GD.Print($"[SpawnNPC] Found free position {candidate} on attempt {attempt + 1}");
				return candidate;
			}

			GD.Print($"[SpawnNPC] Attempt {attempt + 1}: {candidate} blocked, retrying…");
		}

		GD.PrintErr($"[SpawnNPC] Could not find clear spawn after {maxAttempts} attempts; using last candidate {candidate}");
		return candidate;
	}


	private void SpawnNPCs()
	{
		if (_gameEngine == null) return;

		var npcColors = new Dictionary<string, Color>
		{
			{ "katy", Colors.DeepPink },
			{ "john", Colors.DodgerBlue },
			{ "rebecca", Colors.Orange },
			{ "marcus", Colors.LimeGreen },
			{ "sofia", Colors.Orchid },
			{ "amir", Colors.Gold },
			{ "bella", Colors.MediumPurple },
			{ "chris", Colors.Teal },
			{ "diana", Colors.Salmon },
			{ "eli", Colors.SlateBlue }
		};

		var layer6 = GetLayer6();

		foreach (var npc in _gameEngine.GameState.NPCs.Values)
		{
			var entity = new NPCEntity();
			entity.NpcId = npc.Id;
			entity.NpcName = npc.Name;
			entity.NpcColor = npcColors.GetValueOrDefault(npc.Id, Colors.Blue);
			Vector2 worldPos = FindFreeNPCSpawnPosition();
			entity.NPCClicked += OnNPCClicked;
			if (layer6 != null)
			{
				entity.Position = layer6.ToLocal(worldPos);
				entity.Scale = new Vector2(0.25f, 0.25f); // Counteract 4x inherited scale
				layer6.AddChild(entity);
			}
			else
			{
				entity.Position = worldPos;
				AddChild(entity);
			}
			_npcEntities[npc.Id] = entity;
		}
	}

	private void SpawnAllPlayers()
	{
		// Clear existing players
		foreach (var kvp in _playerControllers)
		{
			kvp.Value.QueueFree();
		}
		_playerControllers.Clear();

		var startPositions = new Vector2[]
		{
			new Vector2(100, 700),
			new Vector2(600, 700),
			new Vector2(1100, 700)
		};

		var layer6 = GetLayer6();

		int idx = 0;
		foreach (var kvp in _networkManager.Players)
		{
			var player = new PlayerController();
			player.Name = $"Player_{kvp.Key}"; // Set unique name for debugging
			Vector2 worldPos = startPositions[idx % startPositions.Length];
			player.PlayerIndex = (idx % 3) + 1; // 1, 2, or 3 for sprite selection
			player.SetRole(kvp.Value.Role);
			player.SetPlayerId(kvp.Key); // Set the network player ID
			
			// Check if this is our local player
			bool isLocal = kvp.Key == Multiplayer.GetUniqueId();
			player.SetLocalPlayer(isLocal);
			if (isLocal)
			{
				player.PositionChanged += OnPlayerPositionChanged;
				GD.Print($"[GameWorld] Connected PositionChanged signal for local player {kvp.Key}");
			}

			GD.Print($"[GameWorld] Spawning player {kvp.Key} as {kvp.Value.Role}, sprite={player.PlayerIndex}, isLocal={isLocal}, pos={worldPos}");

			// Add to Layer6 so the player appears on the correct tilemap layer
			if (layer6 != null)
			{
				player.Position = layer6.ToLocal(worldPos);
				player.Scale = new Vector2(0.25f, 0.25f); // Counteract 4x inherited scale
				layer6.AddChild(player);
			}
			else
			{
				player.Position = worldPos;
				AddChild(player);
			}
			
			// Re-apply local player status (camera must exist after AddChild)
			player.SetLocalPlayer(isLocal);
			
			_playerControllers[kvp.Key] = player;
			idx++;
		}
		
		// Also set our role from network manager for UI
		long myId = Multiplayer.GetUniqueId();
		if (_networkManager.Players.ContainsKey(myId))
		{
			_myRole = _networkManager.Players[myId].Role;
			// Role label hidden per user request
			// _roleLabel.Text = $"Role: {_myRole?.ToUpper()}";

			// Show ultimate button only for Producer
			bool isProducer = string.Equals(_myRole, "Producer", StringComparison.OrdinalIgnoreCase);
			if (_ultimateButton != null)        _ultimateButton.Visible        = isProducer;
			if (_ultimateCooldownLabel != null)  _ultimateCooldownLabel.Visible = isProducer;
		}
	}

	private void OnNPCClicked(string npcId)
	{
		if (IsLocalPlayerDead())
		{
			GD.Print($"[GameWorld] Dead player tried to interact with NPC {npcId}, ignoring.");
			return;
		}

		// Block interaction with dead NPCs (server-authoritative check)
		if (TryGetNpcTargetState(npcId, out _, out bool npcAlive) && !npcAlive)
		{
			GD.Print($"[GameWorld] NPC {npcId} is dead, ignoring click.");
			return;
		}

		GD.Print($"GameWorld received OnNPCClicked for {npcId}. Current Role: '{_myRole}'");
		if (string.IsNullOrEmpty(_myRole))
		{
			GD.Print("Role is empty, ignoring click.");
			return;
		}

		var allActions = _localGameState?["all_actions"] as JObject;
		var myActions = allActions?[_myRole.ToLower()] as JObject;
		var npcActions = myActions?[npcId];

		var npcEntity = _npcEntities.GetValueOrDefault(npcId);
		string npcName = npcEntity?.NpcName ?? npcId;

		var npcConfig = _localGameState?["npcs"]?[npcId];
		string desc = npcConfig?["interactionTree"]?["root"]?["text"]?.Value<string>()
			?? "An NPC awaits your action.";

		var activeInterviews = _localGameState?["active_interviews"] as JObject;
		string roleKey = Capitalize(_myRole);
		if (activeInterviews != null && activeInterviews.ContainsKey(roleKey))
		{
			var interviewInfo = activeInterviews[roleKey];
			if (interviewInfo["npcId"]?.Value<string>() == npcId)
			{
				desc = interviewInfo["lastResponse"]?.Value<string>() ?? desc;
			}
		}

		// CONVERSION UI OVERRIDE (Prophet)
		var activeConversionsClick = _localGameState?["active_conversions"] as JObject;
		if (activeConversionsClick != null && activeConversionsClick.ContainsKey(roleKey))
		{
			var convInfo = activeConversionsClick[roleKey];
			if (convInfo["npcId"]?.Value<string>() == npcId)
			{
				desc = convInfo["lastResponse"]?.Value<string>() ?? desc;
			}
		}

		var actionsList = npcActions?.ToObject<List<JToken>>() ?? new List<JToken>();

		if (!string.IsNullOrEmpty(_currentInteractingNpcId) && _currentInteractingNpcId != npcId)
		{
			if (_npcEntities.TryGetValue(_currentInteractingNpcId, out var prevNpc))
			{
				prevNpc.SetFrozen(false);
			}
		}

		if (_npcEntities.TryGetValue(npcId, out var npc))
		{
			npc.SetFrozen(true);
		}

		_currentInteractingNpcId = npcId;

		Vector3 npcState = Vector3.Zero;
		var npcStates = _localGameState?["npc_states"] as JObject;
		if (npcStates != null && npcStates.ContainsKey(npcId))
		{
			var s = npcStates[npcId];
			float x = s["state_x"]?.Value<float>() ?? 0;
			float y = s["state_y"]?.Value<float>() ?? 0;
			float z = s["state_z"]?.Value<float>() ?? 0;
			float tx = s["tri_x"]?.Value<float>() ?? 0;
			float ty = s["tri_y"]?.Value<float>() ?? 0;
			npcState = new Vector3(x, y, z);

			if (_triangleScene != null)
			{
				_triangleScene.Show(npcName, new Vector2(tx, ty));
			}
		}

		Texture2D npcPortrait = null;
		if (_npcEntities.ContainsKey(npcId))
		{
			npcPortrait = _npcEntities[npcId].GetPortraitTexture();
		}

		_npcDialogueUI.ShowForNPC(npcId, npcName, desc, actionsList, npcState, npcPortrait);

		// Play chat-begin sound effect
		GetNode<SfxManager>("/root/SfxManager").PlayChatBegin();

		// PROPHET AUTO-CONVERT: trigger start_convert immediately on click (no button needed)
		if (_myRole?.ToLower() == "prophet")
		{
			var npcStateData = _localGameState?["npc_states"]?[npcId];
			bool isAlive = npcStateData?["alive"]?.Value<bool>() ?? true;
			// bool isConverted = npcStateData?["converted"]?.Value<bool>() ?? false;

			var activeConvs = _localGameState?["active_conversions"] as JObject;
			string rKey = Capitalize(_myRole);
			bool alreadyConverting = activeConvs != null && activeConvs.ContainsKey(rKey)
				&& activeConvs[rKey]["npcId"]?.Value<string>() == npcId;

			if (isAlive && !alreadyConverting)
			{
				RpcId(1, MethodName.SubmitAction, npcId, "start_convert");
			}
		}

		// ADMIRER AUTO-FLIRT: start flirt immediately on click (no button needed)
		if (_myRole?.ToLower() == "admirer")
		{
			var npcStateData = _localGameState?["npc_states"]?[npcId];
			bool isAlive = npcStateData?["alive"]?.Value<bool>() ?? true;

			var activeInterviewsMap = _localGameState?["active_interviews"] as JObject;
			string aKey = Capitalize(_myRole);
			bool alreadyFlirting = activeInterviewsMap != null && activeInterviewsMap.ContainsKey(aKey);

			if (isAlive && !alreadyFlirting)
			{
				RpcId(1, MethodName.SubmitAction, npcId, "start_flirt");
			}
		}

		// PRODUCER AUTO-INTERVIEW: start interview immediately on click (no button needed)
		if (_myRole?.ToLower() == "producer")
		{
			var npcStateData = _localGameState?["npc_states"]?[npcId];
			bool isAlive = npcStateData?["alive"]?.Value<bool>() ?? true;

			var activeProdInterviews = _localGameState?["active_producer_interviews"] as JObject;
			string pKey = Capitalize(_myRole);
			bool alreadyInterviewing = activeProdInterviews != null && activeProdInterviews.ContainsKey(pKey);

			if (isAlive && !alreadyInterviewing)
			{
				RpcId(1, MethodName.SubmitAction, npcId, "start_producer_interview");
			}
		}
	}

	private void OnActionSelected(string npcId, string actionId)
	{
		RpcId(1, MethodName.SubmitAction, npcId, actionId);

		// After picking a chat-game answer, keep the panel open for RESPONSE_PREVIEW_DURATION
		// seconds so the player can read the NPC's response, then auto-close.
		if (actionId.StartsWith("interview_option_") || actionId.StartsWith("producer_interview_option_") || actionId.StartsWith("convert_option_"))
		{
			_responsePreviewTimer = RESPONSE_PREVIEW_DURATION;
		}
	}

	private void OnTrapButtonPressed()
	{
		// Set Trap disabled per request.
		// var trapBtn = _uiLayer.GetNodeOrNull<Button>("TrapButton");
		// if (trapBtn != null)
		// {
		// 	if (!trapBtn.Disabled)
		// 	{
		// 		trapBtn.Disabled = true;
		// 		var timer = new Timer();
		// 		timer.Name = $"TrapCooldownTimer_{Time.GetTicksMsec()}";
		// 		timer.OneShot = true;
		// 		timer.WaitTime = 10.0;
		// 		timer.Timeout += () =>
		// 		{
		// 			if (IsInstanceValid(trapBtn)) trapBtn.Disabled = false;
		// 			timer.QueueFree();
		// 		};
		// 		AddChild(timer);
		// 		timer.Start();
		// 	}
		// }
		// OnActionSelected("global", "set_trap");
	}



	private void OnNetworkPlayerInteraction(long senderId, string npcId, string actionId)
	{
		if (!Multiplayer.IsServer()) return;

		if (_networkManager.Players.TryGetValue(senderId, out var info))
		{
			if (Enum.TryParse<Role>(info.Role, true, out var role))
			{
				var result = _gameEngine.PerformAction(npcId, actionId, role);
				if (!result.success)
				{
					GD.Print($"[GameWorld] Interaction failed: {result.failReason}");
				}
				BroadcastGameState();
			}
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestPunchAnimation(string facing, bool flipH)
	{
		if (!Multiplayer.IsServer()) return;
		long senderId = Multiplayer.GetRemoteSenderId();
		if (senderId == 0) senderId = Multiplayer.GetUniqueId();
		
		// Broadcast animation to all clients
		Rpc(MethodName.SyncPunchAnimation, senderId, facing, flipH);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SyncPunchAnimation(long playerId, string facing, bool flipH)
	{
		if (_playerControllers.TryGetValue(playerId, out var playerCtrl))
		{
			playerCtrl.TriggerPunchAnimation(facing, flipH);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void PunchTargetNPC(string targetNpcId)
	{
		if (!Multiplayer.IsServer()) return;
		if (_gameEngine == null) return;

		var npc = _gameEngine.GameState.GetNPC(targetNpcId);
		if (npc == null || !npc.Alive) return;

		long senderId = Multiplayer.GetRemoteSenderId();
		if (senderId == 0) senderId = Multiplayer.GetUniqueId();

		if (!_networkManager.Players.TryGetValue(senderId, out var playerInfo)) return;

		// Check if sender is dead (server-side verification)
		if (Enum.TryParse<Role>(playerInfo.Role, ignoreCase: true, out var senderRole))
		{
			var senderState = _gameEngine.GameState.GetPlayerState(senderRole);
			if (senderState != null && !senderState.Alive) return;
		}

		if (!_playerControllers.TryGetValue(senderId, out var playerCtrl)) return;
		if (!_npcEntities.TryGetValue(targetNpcId, out var npcEntity)) return;

		float distance = playerCtrl.Position.DistanceTo(npcEntity.Position);
		if (distance > PUNCH_RANGE) return;

		// Stun the NPC for 5 seconds (greyed out, idle)
		Rpc(MethodName.RpcStunNpc, targetNpcId, 5.0f);

		// --- Zero-Sum Punch Vectors ---
		// Collect all OTHER roles actively conversing with this NPC
		var conversingRoles = new HashSet<Role>();
		foreach (var (role, ctx) in _gameEngine.GameState.ActiveInterviews)
		{
			if (ctx.NpcId == targetNpcId && role != senderRole)
				conversingRoles.Add(role);
		}
		foreach (var (role, ctx) in _gameEngine.GameState.ActiveConversions)
		{
			if (ctx.NpcId == targetNpcId && role != senderRole)
				conversingRoles.Add(role);
		}
		foreach (var (role, ctx) in _gameEngine.GameState.ActiveProducerInterviews)
		{
			if (ctx.NpcId == targetNpcId && role != senderRole)
				conversingRoles.Add(role);
		}

		if (conversingRoles.Count == 0)
		{
			// Punch alone NPC: puncher loses (-1, others +0.5 each)
			var points = ScoringRules.GetLossPoints(senderRole);
			npc.State = ScoringRules.ClampState(npc.State + points);
			_gameEngine.GameState.TriggerScoreChange(senderRole, -1);
			GD.Print($"[PunchNPC] {senderRole} punched alone {npc.Name}, State += {points} -> {npc.State}");
		}
		else
		{
			// Punch NPC while someone is conversing:
			// Both the puncher and the conversing player(s) get -1, third role gets +2
			foreach (var conversingRole in conversingRoles)
			{
				var points = ScoringRules.GetPunchInteractingPoints(senderRole, conversingRole);
				npc.State = ScoringRules.ClampState(npc.State + points);

				// Notify score changes: puncher -1, conversing -1, third +2
				_gameEngine.GameState.TriggerScoreChange(senderRole, -1);
				_gameEngine.GameState.TriggerScoreChange(conversingRole, -1);
				var thirdRole = ScoringRules.GetThirdRole(senderRole, conversingRole);
				_gameEngine.GameState.TriggerScoreChange(thirdRole, 2);
				GD.Print($"[PunchNPC] {senderRole} punched {npc.Name} while {conversingRole} conversing, State += {points} -> {npc.State}");
			}
		}

		BroadcastGameState();
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	private void RpcFlashNpcDamage(string npcId)
	{
		if (_npcEntities.TryGetValue(npcId, out var npcEntity))
		{
			npcEntity.FlashDamage(0.5);
			GetNode<SfxManager>("/root/SfxManager").PlayWilhelmScream();
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	private void RpcStunNpc(string npcId, float duration)
	{
		if (_npcEntities.TryGetValue(npcId, out var npcEntity))
		{
			npcEntity.ApplyStun(duration);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void PunchPlayer(long targetPlayerId)
	{
		if (!Multiplayer.IsServer()) return;
		if (_gameEngine == null) return;

		long senderId = Multiplayer.GetRemoteSenderId();
		if (senderId == 0) senderId = Multiplayer.GetUniqueId();

		// Verify sender exists
		if (!_networkManager.Players.TryGetValue(senderId, out var senderInfo)) return;
		string senderRole = senderInfo.Role;

		// Verify target exists
		if (!_networkManager.Players.TryGetValue(targetPlayerId, out var targetInfo)) return;
		string targetRole = targetInfo.Role;

		// Validate punch is allowed:
		// Admirer can punch Prophet/Producer
		// Prophet can punch Admirer/Producer
		// Producer can punch Admirer/Prophet
		// (All roles can punch any other role, just not themselves)
		bool isValidPunch = !string.Equals(senderRole, targetRole, StringComparison.OrdinalIgnoreCase);
		
		if (!isValidPunch) return;

		// Check if sender is dead (server-side verification)
		Role senderRoleEnum;
		if (!Enum.TryParse<Role>(senderRole, ignoreCase: true, out senderRoleEnum))
		{
			GD.Print($"[PunchPlayer] Failed to parse sender role: {senderRole}");
			return;
		}
		var senderPlayerState = _gameEngine.GameState.GetPlayerState(senderRoleEnum);
		if (senderPlayerState != null && !senderPlayerState.Alive)
		{
			GD.Print($"[PunchPlayer] Sender {senderRole} is dead");
			return;
		}

		// Get player controllers
		if (!_playerControllers.TryGetValue(senderId, out var senderCtrl))
		{
			GD.Print($"[PunchPlayer] No controller for sender {senderId}");
			return;
		}
		if (!_playerControllers.TryGetValue(targetPlayerId, out var targetCtrl))
		{
			GD.Print($"[PunchPlayer] No controller for target {targetPlayerId}");
			return;
		}

		// Check range
		float distance = senderCtrl.Position.DistanceTo(targetCtrl.Position);
		if (distance > PUNCH_RANGE)
		{
			GD.Print($"[PunchPlayer] Out of range: {distance} > {PUNCH_RANGE}");
			return;
		}

		// Get target player state
		Role targetRoleEnum;
		if (!Enum.TryParse<Role>(targetRole, ignoreCase: true, out targetRoleEnum))
		{
			GD.Print($"[PunchPlayer] Failed to parse target role: {targetRole}");
			return;
		}
		var targetPlayerState = _gameEngine.GameState.GetPlayerState(targetRoleEnum);
		if (targetPlayerState == null)
		{
			GD.Print($"[PunchPlayer] No player state for target role: {targetRole}");
			return;
		}

		// Don't punch already-dead players
		if (!targetPlayerState.Alive)
		{
			GD.Print($"[PunchPlayer] Target {targetRole} is dead");
			return;
		}

		GD.Print($"[PunchPlayer] SUCCESS: {senderRole} punching {targetRole}");

		// ── Mass Revelation: shatters if Prophet is punched during channeling ──
		if (_massRevServerActive &&
			string.Equals(targetRole, "Prophet", StringComparison.OrdinalIgnoreCase))
		{
			GD.Print("[MassRev] Prophet punched during channeling — shattering Mass Revelation!");
			ShatterMassRevelationServer();
		}

		Rpc(MethodName.RpcFlashPlayerDamage, targetPlayerId);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	private void RpcFlashPlayerDamage(long playerId)
	{
		if (_playerControllers.TryGetValue(playerId, out var playerCtrl))
		{
			playerCtrl.FlashDamage(0.5);
		}
	}
	
	
	private bool IsNPCVisible(NPCEntity npc)
	{
		// Check if NPC node is in tree and visible
		if (npc == null || !IsInstanceValid(npc)) return false;
		
		// Simple visibility check: if the NPC is in the tree and not hidden, consider it visible
		// A more sophisticated check could use camera bounds, but for now we'll check if it's spawned
		return npc.Visible && npc.IsInsideTree();
	}
	
	private void OnInteractionPanelClosed()
	{
		// Unfreeze the NPC
		if (!string.IsNullOrEmpty(_currentInteractingNpcId))
		{
			if (_npcEntities.TryGetValue(_currentInteractingNpcId, out var npc))
			{
				npc.SetFrozen(false);
			}
		}

		_currentInteractingNpcId = null;
		GD.Print("Interaction panel closed, requesting end of interaction");
		
		// Hide Triangle Scene
		if (_triangleScene != null)
		{
			_triangleScene.HideScene();
		}

		// If on client, tell server to clear the active interview/interaction state
		if (!Multiplayer.IsServer())
		{
			RpcId(1, MethodName.RequestEndInteraction);
		}
		else
		{
			// If we are server (hosting player), do it directly
			EndInteractionForSelf();
		}
	}

	private void EndInteractionForSelf()
	{
		// Hosting player end interaction
		if (_gameActive && _gameEngine != null)
		{
			// Assuming host is Producer for now, or lookup?
			// _networkManager.Players[1] -> Role
			// Simplify: Just try to end for all local player roles if possible, or use _myRole
			if (Enum.TryParse<Role>(_myRole, true, out var role))
			{
				_gameEngine.EndActiveInteraction(role);
			}
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
	private void RequestEndInteraction()
	{
		if (!Multiplayer.IsServer()) return;

		var senderId = Multiplayer.GetRemoteSenderId();
		string roleStr = _networkManager.Players.ContainsKey(senderId) ? _networkManager.Players[senderId].Role : "Observer";
		
		if (Enum.TryParse<Role>(roleStr, true, out var role))
		{
			GD.Print($"[GameWorld] Ending interaction for {role} (Peer {senderId})");
			_gameEngine.EndActiveInteraction(role);
		}
	}

	private bool TryGetNpcTargetState(string npcId, out bool isTarget, out bool alive)
	{
		isTarget = false;
		alive = true;

		if (Multiplayer.IsServer() && _gameEngine != null)
		{
			var npc = _gameEngine.GameState.GetNPC(npcId);
			if (npc == null) return false;
			isTarget = npc.IsTarget;
			alive = npc.Alive;
			return true;
		}

		var npcStates = _localGameState?["npc_states"] as JObject;
		var state = npcStates?[npcId];
		if (state == null) return false;
		isTarget = state["is_target"]?.Value<bool>() ?? false;
		alive = state["alive"]?.Value<bool>() ?? true;
		return true;
	}

	private void UpdatePunchHints()
	{
		// Hide all hints if no role assigned
		if (string.IsNullOrEmpty(_myRole))
		{
			foreach (var kvp in _npcEntities)
			{
				kvp.Value.SetPunchHintVisible(false);
			}
			return;
		}

		if (_localPlayer == null)
		{
			var myId = Multiplayer.GetUniqueId();
			if (_playerControllers.TryGetValue(myId, out var localCtrl))
			{
				_localPlayer = localCtrl;
			}
		}

		if (_localPlayer == null)
		{
			foreach (var kvp in _npcEntities)
			{
				kvp.Value.SetPunchHintVisible(false);
			}
			return;
		}

		// All roles: show punch hint for any alive NPC in range
		foreach (var kvp in _npcEntities)
		{
			if (!TryGetNpcTargetState(kvp.Key, out bool isTarget, out bool alive))
			{
				kvp.Value.SetPunchHintVisible(false);
				continue;
			}

			if (!alive)
			{
				kvp.Value.SetPunchHintVisible(false);
				continue;
			}

			float distance = _localPlayer.Position.DistanceTo(kvp.Value.Position);
			bool inRange = distance <= PUNCH_RANGE;
			kvp.Value.SetPunchHintVisible(inRange);
		}
	}

	private void HandlePunchInput()
	{
		bool pPressed = Input.IsKeyPressed(Key.P);
		bool spacePressed = Input.IsActionJustPressed("ui_select");
		
		// All roles can punch now
		if (string.IsNullOrEmpty(_myRole))
		{
			_wasPunchPressed = pPressed;
			return;
		}

		// Don't allow dead players to punch
		if (IsLocalPlayerDead())
		{
			_wasPunchPressed = pPressed;
			return;
		}

		if (_localPlayer == null)
		{
			var myId = Multiplayer.GetUniqueId();
			if (_playerControllers.TryGetValue(myId, out var localCtrl))
			{
				_localPlayer = localCtrl;
			}
		}

		if (_localPlayer == null)
		{
			_wasPunchPressed = pPressed;
			return;
		}

		// Punch input: P key (just pressed via pPressed && !_wasPunchPressed) OR Space bar (just pressed via spacePressed)
		if ((pPressed && !_wasPunchPressed) || spacePressed)
		{
			// Request punch animation for all screens, sending current facing/flip
			RpcId(1, MethodName.RequestPunchAnimation, _localPlayer.FacingDirection, _localPlayer.FlipH);

			// Play punch sound effect
			GetNode<SfxManager>("/root/SfxManager").PlayPunch();

			string closestNpcId = null;
			float closestDistance = float.MaxValue;

			// All roles: can only punch NPCs (no player-vs-player punching)
			foreach (var kvp in _npcEntities)
			{
				if (!TryGetNpcTargetState(kvp.Key, out bool isTarget, out bool alive)) continue;
				if (!alive) continue;

				float distance = _localPlayer.Position.DistanceTo(kvp.Value.Position);
				if (distance <= PUNCH_RANGE && distance < closestDistance)
				{
					closestDistance = distance;
					closestNpcId = kvp.Key;
				}
			}

			// Punch the closest NPC
			if (!string.IsNullOrEmpty(closestNpcId))
			{
				RpcId(1, MethodName.PunchTargetNPC, closestNpcId);
			}

			// ── Extra: Producer / Admirer can punch the Prophet to shatter Mass Revelation ──
			// Only attempt player-punch when the local role is NOT the Prophet (can't shatter own ritual).
			string myRoleLower = _myRole?.ToLower();
			if (myRoleLower == "producer" || myRoleLower == "admirer")
			{
				// Find the Prophet player controller within punch range
				long prophetPeerId = 0;
				float closestPlayerDist = float.MaxValue;
				foreach (var kvp in _playerControllers)
				{
					var ctrl = kvp.Value;
					// Skip self and non-Prophet players
					if (ctrl == _localPlayer) continue;

					// Determine this controller's role
					if (_networkManager.Players.TryGetValue(kvp.Key, out var pInfo) &&
						string.Equals(pInfo.Role, "Prophet", StringComparison.OrdinalIgnoreCase))
					{
						float dist = _localPlayer.Position.DistanceTo(ctrl.Position);
						if (dist <= PUNCH_RANGE && dist < closestPlayerDist)
						{
							closestPlayerDist = dist;
							prophetPeerId = kvp.Key;
						}
					}
				}

				if (prophetPeerId != 0)
				{
					// Trigger the server-side PunchPlayer which already handles Mass Revelation shattering
					RpcId(1, MethodName.PunchPlayer, prophetPeerId);
					GD.Print($"[PunchInput] {_myRole} punching Prophet (peer {prophetPeerId}) to shatter Mass Revelation");
				}
			}
		}

		_wasPunchPressed = pPressed;
	}

	private void OnCollapseNotificationPressed()
	{
		_notificationCollapsed = !_notificationCollapsed;
		var viewportSize = GetViewportRect().Size;
		
		if (_notificationCollapsed)
		{
			_notificationMargin.Hide();
			_collapseNotificationButton.Text = "+";
			// Move to bottom of screen
			_notificationPanel.Position = new Vector2(
				Mathf.Max(20, viewportSize.X - _notificationPanel.CustomMinimumSize.X - 30),
				viewportSize.Y - 50);
		}
		else
		{
			_notificationMargin.Show();
			_collapseNotificationButton.Text = "−";
			// Move back to expanded position
			_notificationPanel.Position = _notificationPanelExpandedPosition;
		}
	}

	// ---- SCORE FEEDBACK ----

	private void HandleScoreChange(Role role, int score)
	{
		GD.Print($"[GameWorld] HandleScoreChange called for {role} with score {score}");
		// Called on Server (or wherever GameEngine is running)
		
		// Broadcast RPC to all clients to show the score feedback
		Rpc(MethodName.ClientShowScoreFeedback, role.ToString(), score);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	private void ClientShowScoreFeedback(string roleStr, int score)
	{
		if (_isGameOver) return; // Don't show score popups on end screen

		// Called on Client
		GD.Print($"[GameWorld] Received Score Feedback RPC: {score} for role {roleStr} on peer {Multiplayer.GetUniqueId()}");
		
		string assetName = null;
		string roleLower = roleStr.ToLower();
		
		if (score >= 1) assetName = $"ai_{roleLower}_plusone.png";
		else if (score <= -1) assetName = $"ai_{roleLower}_minusone.png";

		// Play success/failure SFX when score feedback is for the local player's role
		if (!string.IsNullOrEmpty(_myRole) && roleLower == _myRole.ToLower())
		{
			var sfx = GetNode<SfxManager>("/root/SfxManager");
			if (score >= 1) sfx.PlayChatSuccess();
			else if (score <= -1) sfx.PlayChatFailure();
		}
		
		if (assetName != null)
		{
			ShowScoreFeedback(roleLower, assetName);
		}
		else
		{
			GD.Print($"[GameWorld] No asset defined for score {score}");
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	private void RpcShowNpcScoreFeedback(string npcId, string roleStr, int score)
	{
		if (_isGameOver || HasNode("GameOverOverlay")) return;

		string roleLower = roleStr.ToLower();
		
		if (roleLower == "witness")
		{
			// Special case: just a floating red "-" sign for witnessing stabs
			if (_npcEntities.TryGetValue(npcId, out var witnessNpc))
			{
				var label = new Label();
				label.Text = "-";
				label.AddThemeFontOverride("font", _customFont);
				label.AddThemeFontSizeOverride("font_size", 48);
				label.AddThemeColorOverride("font_color", Colors.Red);
				label.AddThemeConstantOverride("outline_size", 4);
				label.AddThemeColorOverride("font_outline_color", Colors.Black);
				label.ZIndex = 100;
				
				// Position floating above NPC
				label.Position = witnessNpc.Position - new Vector2(10, 80);
				AddChild(label);

				var tween = CreateTween();
				tween.SetParallel(true);
				Vector2 targetPos = label.Position - new Vector2(0, 100);
				tween.TweenProperty(label, "position", targetPos, 1.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
				tween.TweenProperty(label, "modulate", new Color(1, 1, 1, 0), 1.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
				tween.Chain().TweenCallback(OfCallable(() => label.QueueFree()));
			}
			return;
		}

		string assetName = score >= 1 ? $"ai_{roleLower}_plusone.png" : $"ai_{roleLower}_minusone.png";
		string path = $"res://assets/{assetName}";
		var texture = ResourceLoader.Load<Texture2D>(path);

		if (texture != null && _npcEntities.TryGetValue(npcId, out var npc))
		{
			var floatingRect = new TextureRect();
			floatingRect.Texture = texture;
			floatingRect.ExpandMode = TextureRect.ExpandModeEnum.KeepSize;
			floatingRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			floatingRect.ZIndex = 100;
			floatingRect.Scale = new Vector2(0.05f, 0.05f); // smaller

			Vector2 texSize = texture.GetSize() * floatingRect.Scale;
			// Position floating above NPC
			floatingRect.Position = npc.Position - new Vector2(texSize.X / 2, 80);
			
			AddChild(floatingRect);

			var tween = CreateTween();
			tween.SetParallel(true);
			Vector2 targetPos = floatingRect.Position - new Vector2(0, 100);
			tween.TweenProperty(floatingRect, "position", targetPos, 1.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
			tween.TweenProperty(floatingRect, "modulate", new Color(1, 1, 1, 0), 1.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
			tween.Chain().TweenCallback(Callable.From(() => floatingRect.QueueFree()));
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	private void RpcShowMassRevNpcFeedback(string npcId)
	{
		if (_isGameOver || HasNode("GameOverOverlay")) return;

		string path = "res://assets/ai_prophet_one.png";
		var texture = ResourceLoader.Load<Texture2D>(path);

		if (texture != null && _npcEntities.TryGetValue(npcId, out var npc))
		{
			var floatingRect = new TextureRect();
			floatingRect.Texture = texture;
			floatingRect.ExpandMode = TextureRect.ExpandModeEnum.KeepSize;
			floatingRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			floatingRect.ZIndex = 100;
			floatingRect.Scale = new Vector2(0.025f, 0.025f);

			Vector2 texSize = texture.GetSize() * floatingRect.Scale;
			floatingRect.Position = npc.Position - new Vector2(texSize.X / 2, 80);

			AddChild(floatingRect);

			var tween = CreateTween();
			tween.SetParallel(true);
			Vector2 targetPos = floatingRect.Position - new Vector2(0, 100);
			tween.TweenProperty(floatingRect, "position", targetPos, 1.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
			tween.TweenProperty(floatingRect, "modulate", new Color(1, 1, 1, 0), 1.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
			tween.Chain().TweenCallback(Callable.From(() => floatingRect.QueueFree()));
		}
		else
		{
			GD.PrintErr($"[MassRev] Failed to load asset: {path}");
		}
	}
	
	private void ShowScoreFeedback(string roleLower, string assetName)
	{
		// Bail out if game is already over (covers race conditions where _isGameOver isn't set yet)
		if (_isGameOver || HasNode("GameOverOverlay")) return;

		string path = $"res://assets/{assetName}";
		GD.Print($"[GameWorld] Loading score asset: {path}");
		var texture = ResourceLoader.Load<Texture2D>(path);
		
		if (texture != null)
		{
			var floatingRect = new TextureRect();
			floatingRect.Texture = texture;
			floatingRect.ExpandMode = TextureRect.ExpandModeEnum.KeepSize;
			floatingRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			floatingRect.ZIndex = 100;
			floatingRect.AddToGroup("ScoreFeedback"); // tagged so we can purge on game over
			
			// Make points appear MUCH smaller (8% of original size)
			floatingRect.Scale = new Vector2(0.08f, 0.08f);
			
			// Initial center screen position, accounting for scale
			var vpSize = GetViewportRect().Size;
			Vector2 texSize = texture.GetSize() * floatingRect.Scale;
			
			// Separate horizontal positions so points don't overlap when they spawn simultaneously
			float xOffset = 0;
			if (roleLower == "prophet") xOffset = -100f;
			else if (roleLower == "producer") xOffset = 0f;
			else if (roleLower == "admirer") xOffset = 100f;
			
			// Start higher up on the screen by subtracting an additional Y offset (e.g., 200 pixels)
			floatingRect.Position = new Vector2(((vpSize.X - texSize.X) / 2) + xOffset, ((vpSize.Y - texSize.Y) / 2) - 200f);
			
			_uiLayer.AddChild(floatingRect);
			
			// Tween upwards and fade out
			var tween = CreateTween();
			tween.SetParallel(true);
			Vector2 targetPos = floatingRect.Position - new Vector2(0, 150);
			tween.TweenProperty(floatingRect, "position", targetPos, 2.0f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
			tween.TweenProperty(floatingRect, "modulate", new Color(1, 1, 1, 0), 2.0f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
			
			// Chain callback to remove after
			tween.Chain().TweenCallback(Callable.From(() => floatingRect.QueueFree()));
		}
		else
		{
			GD.PrintErr($"[GameWorld] Failed to load score asset: {path}");
		}
	}

	public override void _Process(double delta)
	{

		// Handle Admirer Eliminated timer for non-Admirer players
		if (_admirerEliminatedTimer > 0)
		{
			_admirerEliminatedTimer -= (float)delta;
			if (_admirerEliminatedTimer <= 0)
			{
				_eliminationOverlay.Visible = false;
				// Do NOT reset _admirerEliminatedShown here, or it will loop forever in UpdateUI
			}
		}

		if (Multiplayer.IsServer() && _gameActive)
		{
			// Check for Win Condition
			if (_gameEngine.GameState.IsGameOver)
			{
				_gameActive = false;
				string winner = _gameEngine.GameState.Winner;
				string prevNotif = winner != null ? $"{winner.ToUpper()} WINS!" : "GAME OVER";
				if (winner == null) _gameEngine.GameState.AddNotification("GAME OVER - TIME UP!"); // Only add if not already won
				
				GetNode<MusicManager>("/root/MusicManager")?.ForceLobbyMusic();
				GetNode<SfxManager>("/root/SfxManager")?.StopCountdown();
				BroadcastGameState();
				return;
			}
			
			// Update Game Engine (Traps, etc.)
			_gameEngine.Update(delta);
			
			_timeRemaining -= delta;

			// Trigger countdown voice at last ~13 seconds
			if (_timeRemaining <= 13.0 && !_countdownTriggered)
			{
				_countdownTriggered = true;
				GetNode<SfxManager>("/root/SfxManager")?.PlayCountdown();
			}

			if (_timeRemaining <= 0)
			{
				_timeRemaining = 0;
				_gameActive = false;
				
				// Determine winner based on most influence
				var endCounts = new Dictionary<string, int> { { "Prophet", 0 }, { "Admirer", 0 }, { "Producer", 0 } };
				if (_leaderboardTriangle != null)
				{
					var npcStates = _gameEngine.GameState.NPCs;
					foreach (var npc in npcStates.Values)
					{
						string zone = _leaderboardTriangle.GetInfluenceZone(new Vector2(npc.TrianglePosition.X, npc.TrianglePosition.Y));
						string role = zone == "neutral" ? "Neutral" : Capitalize(zone);
						if (endCounts.ContainsKey(role)) endCounts[role]++;
					}
				}
				
				string finalWinner = endCounts.OrderByDescending(x => x.Value).First().Key;
				_gameEngine.GameState.Winner = finalWinner;
				_gameEngine.GameState.AddNotification($"GAME OVER - TIME UP! {finalWinner} Wins with {endCounts[finalWinner]} influenced contestants!");
				
				GetNode<MusicManager>("/root/MusicManager")?.StopMusic();
				GetNode<SfxManager>("/root/SfxManager")?.StopCountdown();
				BroadcastGameState();
			}
			else
			{
				// Periodically broadcast game state to keep timer updated on clients
				_broadcastTimer += delta;
				if (_broadcastTimer >= BROADCAST_INTERVAL)
				{
					_broadcastTimer = 0.0;

					// --- THROTTLED UPDATES (10Hz) ---
					// Update NPC Quadrants in Game Engine (for Editorial Focus)
					// AND Freeze NPCs if they are being interviewed
			
					// 1. Get set of frozen NPCs (currently in interview)
					var frozenNpcIds = new HashSet<string>();
					if (_gameEngine.GameState.ActiveInterviews != null)
					{
						foreach (var kvp in _gameEngine.GameState.ActiveInterviews)
						{
							if (!string.IsNullOrEmpty(kvp.Value.NpcId))
							frozenNpcIds.Add(kvp.Value.NpcId);
						}
					}

					// Also freeze NPCs in active conversions (Prophet)
					if (_gameEngine.GameState.ActiveConversions != null)
					{
						foreach (var kvp in _gameEngine.GameState.ActiveConversions)
						{
							if (!string.IsNullOrEmpty(kvp.Value.NpcId))
								frozenNpcIds.Add(kvp.Value.NpcId);
						}
					}	
			
					// Also freeze the NPC we are currently interacting with locally
					if (!string.IsNullOrEmpty(_currentInteractingNpcId))
					{
						frozenNpcIds.Add(_currentInteractingNpcId);
					}
			
					foreach (var kvp in _npcEntities)
					{
						var npcEntity = kvp.Value;
						var npcId = kvp.Key;
						var npcData = _gameEngine.GameState.GetNPC(kvp.Key);
				
						// Freeze/Unfreeze
						npcEntity.SetFrozen(frozenNpcIds.Contains(npcId));

						if (npcData != null)
						{
							// Quadrants: 0:TL, 1:TR, 2:BL, 3:BR
							float midX = _worldSize.X / 2;
							float midY = _worldSize.Y / 2;
							int q = 0;
							if (npcEntity.Position.X >= midX) q += 1;
							if (npcEntity.Position.Y >= midY) q += 2;
					
							npcData.Quadrant = q;
						}
					}

					BroadcastGameState();

					// ── Mass Revelation server tick ──────────────────────────────────
					if (_massRevServerActive && _massRevProphetPeerId != 0 &&
						_playerControllers.TryGetValue(_massRevProphetPeerId, out var prophetCtrl))
					{
						Vector2 prophetPos = prophetCtrl.GlobalPosition;
						
						// Identify NPCs that left the radius or were removed
						var keysToRemove = new List<string>();
						foreach (var npcId in _massRevNpcTimers.Keys)
						{
							if (!_npcEntities.ContainsKey(npcId) || 
								_npcEntities[npcId].Position.DistanceTo(prophetPos) > MASS_REV_RADIUS)
							{
								keysToRemove.Add(npcId);
							}
						}
						foreach (var id in keysToRemove)
						{
							_massRevNpcTimers.Remove(id);
							_massRevNpcPoints.Remove(id);
						}

						foreach (var kvp in _npcEntities)
						{
							var npcData   = _gameEngine.GameState.GetNPC(kvp.Key);
							if (npcData == null || !npcData.Alive) continue;

							float dist = kvp.Value.Position.DistanceTo(prophetPos);
							if (dist <= MASS_REV_RADIUS)
							{
								// March toward prophet
								kvp.Value.SetMarchTarget(prophetPos, MASS_REV_NPC_SPEED);

								// Sever any active flirt dialog involving this NPC
								BreakNpcDialog(kvp.Key);
								
								// Process Continuous Mass Revelation Points
								if (!_massRevNpcTimers.ContainsKey(kvp.Key))
								{
									_massRevNpcTimers[kvp.Key] = 0f;
									_massRevNpcPoints[kvp.Key] = 0f;
								}
								// Since this loop runs every BROADCAST_INTERVAL
								_massRevNpcTimers[kvp.Key] += (float)BROADCAST_INTERVAL;
								
								if (_massRevNpcTimers[kvp.Key] >= 2.5f)
								{
									_massRevNpcTimers[kvp.Key] -= 2.5f;
									// Give 0.5 points to Prophet
									npcData.State = FatalAttraction.Engine.ScoringRules.ClampState(
										npcData.State + new System.Numerics.Vector3(0, 0.5f, 0)
									);
									
									// Animate every tick (every +0.5 pt) using the dedicated Prophet plus asset
									Rpc(MethodName.RpcShowMassRevNpcFeedback, kvp.Key);
								}
							}
						}
					}
				}
			}
		}

		// If interaction panel is visible, check if player is still in range of NPC
		if (_npcDialogueUI != null && _npcDialogueUI.Visible && _currentInteractingNpcId != null)
		{
			// Try to find local player if not set
			if (_localPlayer == null)
			{
				var myId = Multiplayer.GetUniqueId();
				if (_playerControllers.TryGetValue(myId, out var localCtrl))
				{
					_localPlayer = localCtrl;
				}
			}

			var npc = _npcEntities.GetValueOrDefault(_currentInteractingNpcId);
			if (npc != null && _localPlayer != null)
			{
				float distance = _localPlayer.Position.DistanceTo(npc.Position);
				// Close menu if player is too far (150 = interaction range + buffer)
				if (distance > 150)
				{
					GD.Print($"Player moved too far from NPC {_currentInteractingNpcId} (distance: {distance}), closing menu");
					_responsePreviewTimer = 0.0;   // Cancel any pending preview timer
					_suppressSelfHeal = true;       // Don't let self-healing re-open the panel
					_npcDialogueUI.Close();
					OnInteractionPanelClosed();     // Notify server to clear state
				}
			}

			// Auto-close after response preview timer expires
			if (_responsePreviewTimer > 0.0)
			{
				_responsePreviewTimer -= delta;
				if (_responsePreviewTimer <= 0.0)
				{
					_responsePreviewTimer = 0.0;
					_suppressSelfHeal = true;  // Don't let self-healing re-open the panel
					_npcDialogueUI?.Close();
					OnInteractionPanelClosed();
				}
			}
		}
		

		// Check Producer Panels Auto-Close on Move
		CheckProducerPanelsOnMove();

		// Punch hints and input handling
		UpdatePunchHints();
		HandlePunchInput();

		// Producer Ultimate: cash trail
		HandleUltimateInput(delta);

		// Admirer Ultimate: knife kill
		HandleAdmirerKnifeInput();

		// Prophet Ultimate: Mass Revelation
		HandleProphetUltimateInput(delta);

		// Handle bleeding trails for NPCs (Server only)
		if (Multiplayer.IsServer() && _gameActive)
		{
			var keysToUpdate = _bleedingNpcTimers.Keys.ToList();
			foreach (var npcId in keysToUpdate)
			{
				_bleedingNpcTimers[npcId] -= (float)delta;
				if (_bleedingNpcTimers[npcId] <= 0)
				{
					_bleedingNpcTimers.Remove(npcId);
					_npcBloodDropTimers.Remove(npcId);
					continue;
				}

				// Handle periodic blood drops for trail
				_npcBloodDropTimers[npcId] -= (float)delta;
				if (_npcBloodDropTimers[npcId] <= 0)
				{
					_npcBloodDropTimers[npcId] = (float)BLOOD_DROP_INTERVAL;
					if (_npcEntities.TryGetValue(npcId, out var npcEnt))
					{
						// DE-DUPLICATION: Only spawn if NPC moved enough
						Vector2 currentPos = npcEnt.GlobalPosition;
						if (!_lastBloodPositions.ContainsKey(npcId) || currentPos.DistanceTo(_lastBloodPositions[npcId]) > 80.0f)
						{
							Rpc(MethodName.RpcSpawnBloodPool, currentPos, true);
							_lastBloodPositions[npcId] = currentPos;
						}
					}
				}
			}
		}

		// Non-prophet clients: keep the aura visual on the prophet puppet each frame
		HandleNonProphetMassRevAura();
	}

	// ── Admirer Ultimate: Knife Kill ──────────────────────────────────────────────

	private void OnAdmirerKnifeButtonPressed()
	{
		ActivateAdmirerKnife();
	}

	private void ActivateAdmirerKnife()
	{
		if (_myRole?.ToLower() != "admirer") return;
		if (IsLocalPlayerDead()) return;
		if (_knifePermanentlyLost)
		{
			AddSlidingNotification("🔪 Your knife is gone...");
			return;
		}
		if (_admirerStabCooldownTimer > 0) return; // still on cooldown

		// Resolve local player
		if (_localPlayer == null)
		{
			var myId = Multiplayer.GetUniqueId();
			_playerControllers.TryGetValue(myId, out _localPlayer);
		}
		if (_localPlayer == null) return;

		// Find closest alive NPC within knife range
		string closestNpcId = null;
		float closestDist   = float.MaxValue;
		foreach (var kvp in _npcEntities)
		{
			if (!TryGetNpcTargetState(kvp.Key, out _, out bool alive)) continue;
			if (!alive) continue;
			float dist = _localPlayer.Position.DistanceTo(kvp.Value.Position);
			if (dist <= ADMIRER_KNIFE_RANGE && dist < closestDist)
			{
				closestDist   = dist;
				closestNpcId  = kvp.Key;
			}
		}

		if (string.IsNullOrEmpty(closestNpcId))
		{
			AddSlidingNotification("🔪 No contestant close enough to use the knife!");
			return;
		}

		// ── DEFINITIVE FIX: Target Locking & Refreshing Timer ────────────────
		// Once you start a hunt, you MUST finish it on the same NPC.
		if (!string.IsNullOrEmpty(_admirerStabWindowTarget))
		{
			if (closestNpcId != _admirerStabWindowTarget)
			{
				AddSlidingNotification("🔪 You are already hunting someone else! Finish the job!");
				return;
			}
			// Refresh the timer on every subsequent stab
			_admirerStabWindowTimer  = ADMIRER_STAB_WINDOW;
		}
		else
		{
			// First stab: Lock in the target and start the 10s refreshing window
			_admirerStabWindowTarget = closestNpcId;
			_admirerStabWindowTimer  = ADMIRER_STAB_WINDOW;
			AddSlidingNotification("🔪 Target Locked! Land the next stab within 10s...");
		}

		// Start cooldown
		_admirerStabCooldownTimer = ADMIRER_STAB_COOLDOWN;

		// Track count locally for UI display
		ulong now = Time.GetTicksMsec();
		if (!_npcStabCounts.ContainsKey(closestNpcId) || 
			(_npcStabLastTime.ContainsKey(closestNpcId) && now - _npcStabLastTime[closestNpcId] > ADMIRER_STAB_WINDOW * 1000))
		{
			_npcStabCounts[closestNpcId] = 0;
		}
		// (Stab count increment removed here to fix double-increment on server/host. The Rpc sets the truth.)
		// _npcStabCounts[closestNpcId]++;
		_npcStabLastTime[closestNpcId] = now;

		GD.Print($"[AdmirerKnife] Admirer stabbing NPC {closestNpcId} (UI tracking: {_npcStabCounts[closestNpcId]}/{ADMIRER_STABS_TO_KILL})");

		// Play stab animation on the Admirer (synced to all clients)
		{
			string facing = _localPlayer.FacingDirection;
			bool flipH = _localPlayer.FlipH;
			if (Multiplayer.IsServer())
				Rpc(MethodName.SyncStabAnimation, (long)Multiplayer.GetUniqueId(), facing, flipH);
			else
				RpcId(1, MethodName.RequestStabAnimation, facing, flipH);
		}

		// Tell server to stab the NPC (may or may not kill)
		if (Multiplayer.IsServer())
			AdmirerStabNpc(closestNpcId);
		else
			RpcId(1, MethodName.RequestAdmirerKillNpc, closestNpcId);
	}

	private void HandleAdmirerKnifeInput()
	{
		if (_myRole?.ToLower() != "admirer") return;
		if (IsLocalPlayerDead()) return;
		if (_knifePermanentlyLost)
		{
			if (_admirerKnifeButton != null) _admirerKnifeButton.Visible = false;
			if (_admirerKnifeLabel != null) _admirerKnifeLabel.Visible = false;
			if (_admirerAuraVisual != null) _admirerAuraVisual.Visible = false;
			return;
		}

		double dt = GetProcessDeltaTime();

		// Tick down cooldown
		if (_admirerStabCooldownTimer > 0)
		{
			_admirerStabCooldownTimer -= dt;
			if (_admirerStabCooldownTimer < 0) _admirerStabCooldownTimer = 0;
		}

		// Tick down stab window timer
		if (_admirerStabWindowTimer > 0)
		{
			_admirerStabWindowTimer -= dt;
			if (_admirerStabWindowTimer <= 0)
			{
				_admirerStabWindowTimer  = 0;
				// Ability Loss logic
				if (!string.IsNullOrEmpty(_admirerStabWindowTarget))
				{
					_knifePermanentlyLost = true;
					AddSlidingNotification("🔪 Hunt failed! The knife has been lost forever.");
				}
				_admirerStabWindowTarget = null;
			}
		}

		// Keep aura centered on local player and visible only during a hunt
		if (_admirerStabWindowTimer > 0)
		{
			if (_admirerAuraVisual == null) SetupAdmirerAura();
			if (_admirerAuraVisual != null)
			{
				_admirerAuraVisual.Visible = true;
				if (_localPlayer != null) _admirerAuraVisual.GlobalPosition = _localPlayer.GlobalPosition;
			}
		}
		else if (_admirerAuraVisual != null)
		{
			_admirerAuraVisual.Visible = false;
		}

		// Resolve local player
		if (_localPlayer == null)
		{
			var myId = Multiplayer.GetUniqueId();
			_playerControllers.TryGetValue(myId, out _localPlayer);
		}

		// Update button state
		if (_admirerKnifeButton != null)
			_admirerKnifeButton.Disabled = _admirerStabCooldownTimer > 0;

		// Check if any NPC is already dead (Admirer can only kill once)
		bool anyDead = false;
		foreach (var kvp in _npcEntities)
		{
			if (TryGetNpcTargetState(kvp.Key, out _, out bool alive) && !alive)
			{
				anyDead = true;
				break;
			}
		}
		if (anyDead) return;

		// ── Q key detection ───────────────────────────────────────────────────
		bool qPressed = Input.IsKeyPressed(Key.Q);
		if (qPressed && !_wasAdmirerUltPressed)
		{
			ActivateAdmirerKnife();
		}
		_wasAdmirerUltPressed = qPressed;
	}

	// ── Server RPC: client asks server to kill an NPC with the knife ──────────
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestAdmirerKillNpc(string npcId)
	{
		if (!Multiplayer.IsServer()) return;

		long senderId = Multiplayer.GetRemoteSenderId();
		if (!_networkManager.Players.TryGetValue(senderId, out var info)) return;
		if (!string.Equals(info.Role, "Admirer", StringComparison.OrdinalIgnoreCase)) return;

		// Verify sender is alive
		var senderState = _gameEngine.GameState.GetPlayerState(Role.Admirer);
		if (senderState != null && !senderState.Alive) return;

		// Verify admirer is close enough
		if (!_playerControllers.TryGetValue(senderId, out var admirerCtrl)) return;
		if (!_npcEntities.TryGetValue(npcId, out var npcEnt)) return;
		if (admirerCtrl.Position.DistanceTo(npcEnt.Position) > ADMIRER_KNIFE_RANGE)
		{
			GD.Print($"[AdmirerKnife] Server rejected knife: Admirer too far from {npcId}");
			return;
		}

		AdmirerStabNpc(npcId);
	}

	/// <summary>
	/// Server-side: apply one stab to an NPC. Kills on the Nth stab and makes the NPC flee.
	/// </summary>
	private void AdmirerStabNpc(string npcId)
	{
		if (!Multiplayer.IsServer()) return;
		if (_gameEngine == null) return;

		var npc = _gameEngine.GameState.GetNPC(npcId);
		if (npc == null || !npc.Alive)
		{
			GD.Print($"[AdmirerKnife] NPC {npcId} not found or already dead.");
			return;
		}

		// Increment stab counter or reset if window expired
		ulong now = Time.GetTicksMsec();
		if (!_npcStabCounts.ContainsKey(npcId) || 
			(_npcStabLastTime.ContainsKey(npcId) && now - _npcStabLastTime[npcId] > ADMIRER_STAB_WINDOW * 1000))
		{
			_npcStabCounts[npcId] = 0;
		}

		_npcStabCounts[npcId]++;
		_npcStabLastTime[npcId] = now;
		int stabCount = _npcStabCounts[npcId];

		GD.Print($"[AdmirerKnife] NPC {npcId} ({npc.Name}) stabbed {stabCount}/{ADMIRER_STABS_TO_KILL} times.");

		// Visual flash on all clients
		Rpc(MethodName.RpcFlashNpcDamage, npcId);

		// Spawn blood pool and start bleeding trail
		if (_npcEntities.TryGetValue(npcId, out var npcEnt))
		{
			Rpc(MethodName.RpcSpawnBloodPool, npcEnt.GlobalPosition, false);
			_bleedingNpcTimers[npcId] = (float)BLOOD_TRAIL_DURATION;
			_npcBloodDropTimers[npcId] = (float)BLOOD_DROP_INTERVAL;
		}

		// Find admirer position for flee direction
		Vector2 admirerPos = Vector2.Zero;
		foreach (var kvp in _playerControllers)
		{
			if (_networkManager.Players.TryGetValue(kvp.Key, out var pInfo) &&
				string.Equals(pInfo.Role, "Admirer", StringComparison.OrdinalIgnoreCase))
			{
				admirerPos = kvp.Value.Position;
				break;
			}
		}

		// Witness AoE: nearby contestants (NPCs/Players) drop their view of the Admirer for every witnessed stab
		ApplyKnifeWitnessAoE(npcId, npc.Name);

		if (stabCount >= ADMIRER_STABS_TO_KILL)
		{
			// Kill the NPC on the final stab
			npc.Alive = false;
			Rpc(MethodName.RpcSyncNpcStabStatus, npcId, ADMIRER_STABS_TO_KILL, 0f);
			_gameEngine.GameState.AddNotification($"🔪 {npc.Name} has been eliminated by the Admirer!");
			GD.Print($"[AdmirerKnife] NPC {npcId} ({npc.Name}) killed by Admirer after {stabCount} stabs.");
		}
		else
		{
			Rpc(MethodName.RpcSyncNpcStabStatus, npcId, stabCount, ADMIRER_STAB_WINDOW);
			// NPC is still alive — make them flee from the admirer
			if (_npcEntities.TryGetValue(npcId, out var npcEntity))
			{
				npcEntity.StartFleeingFrom(admirerPos);
			}

			int remaining = ADMIRER_STABS_TO_KILL - stabCount;
			_gameEngine.GameState.AddNotification($"🔪 {npc.Name} was stabbed! ({remaining} more to eliminate)");
		}

		BroadcastGameState();
	}

	// ── Witness AoE applied once per knife kill (server-only) ──────────────────
	private const float KNIFE_WITNESS_RADIUS   = 350f;  // world-space radius around corpse
	private const float KNIFE_WITNESS_PENALTY  = -0.5f; // Admirer score delta per witnessed STAB

	private void ApplyKnifeWitnessAoE(string deadNpcId, string deadNpcName)
	{
		if (!_npcEntities.TryGetValue(deadNpcId, out var corpseEntity)) return;
		Vector2 corpsePos = corpseEntity.GlobalPosition;

		int witnessCount = 0;
		foreach (var kvp in _npcEntities)
		{
			if (kvp.Key == deadNpcId) continue; // skip the corpse itself

			var witnessNpc = _gameEngine.GameState.GetNPC(kvp.Key);
			if (witnessNpc == null || !witnessNpc.Alive) continue;

			float dist = kvp.Value.GlobalPosition.DistanceTo(corpsePos);
			if (dist > KNIFE_WITNESS_RADIUS) continue;

			// Penalise Admirer's component (X axis in the state vector)
			var penalty = new System.Numerics.Vector3(KNIFE_WITNESS_PENALTY, 0f, 0f);
			witnessNpc.State = ScoringRules.ClampState(witnessNpc.State + penalty);
			witnessCount++;

			// ── DEFINITIVE FIX: Witness Feedback Signs (NPCs) ─────────────────
			Rpc(MethodName.RpcShowNpcScoreFeedback, kvp.Key, "witness", -1);

			GD.Print($"[KnifeWitness] {witnessNpc.Name} witnessed murder of {deadNpcName} (dist {dist:F0}). Admirer penalty {KNIFE_WITNESS_PENALTY}");
		}

		// ── DEFINITIVE FIX: Witness Feedback Signs (Players) ────────────────
		foreach (var kvp in _playerControllers)
		{
			// Skip the Admirer themselves
			if (_networkManager.Players.TryGetValue(kvp.Key, out var pInfo) && 
				string.Equals(pInfo.Role, "Admirer", StringComparison.OrdinalIgnoreCase)) continue;

			float dist = kvp.Value.GlobalPosition.DistanceTo(corpsePos);
			if (dist <= KNIFE_WITNESS_RADIUS)
			{
				witnessCount++;
				Rpc(MethodName.RpcShowPlayerScoreFeedback, kvp.Key, "witness", -1);
				GD.Print($"[KnifeWitness] Player (Peer {kvp.Key}) witnessed murder. distance: {dist:F0}");
			}
		}

		if (witnessCount > 0)
		{
			_gameEngine.GameState.AddNotification(
				$"👀 {witnessCount} contestant{(witnessCount > 1 ? "s" : "")} witnessed the murder! Admirer's reputation has plummeted!");
			// Also push a visual score-drop notification for the admirer
			_gameEngine.GameState.TriggerScoreChange(Role.Admirer, -witnessCount);
		}
	}

	// ── Stab Animation RPCs (Admirer Knife Kill) ────────────────────────────────
	
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcSyncNpcStabStatus(string npcId, int stabCount, double windowRemaining)
	{
		if (_npcEntities.TryGetValue(npcId, out var npcEnt))
		{
			npcEnt.UpdateStabStatus(stabCount, windowRemaining);
		}

		// ── DEFINITIVE FIX: Aura Cleanup ──────────────────────────────────────
		// If the target is eliminated, clear the local Admirer's hunt state immediately
		if (stabCount >= ADMIRER_STABS_TO_KILL && npcId == _admirerStabWindowTarget)
		{
			_admirerStabWindowTimer  = 0;
			_admirerStabWindowTarget = null;
			GD.Print($"[AdmirerKnife] Hunt concluded on {npcId}. Aura cleared.");
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestStabAnimation(string facing, bool flipH)
	{
		if (!Multiplayer.IsServer()) return;
		long senderId = Multiplayer.GetRemoteSenderId();
		if (senderId == 0) senderId = Multiplayer.GetUniqueId();

		// Broadcast stab animation to all clients
		Rpc(MethodName.SyncStabAnimation, senderId, facing, flipH);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SyncStabAnimation(long playerId, string facing, bool flipH)
	{
		if (_playerControllers.TryGetValue(playerId, out var playerCtrl))
		{
			playerCtrl.TriggerStabAnimation(facing, flipH);
			// Play the Admirer's signature line
			GetNode<SfxManager>("/root/SfxManager").PlayDontIgnoreMe();
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	public void RpcSpawnBloodPool(Vector2 position, bool isSmall)
	{
		// ── DEFINITIVE FIX: Physics-Based Wall Detection ──
		// Before spawning, we check if this point overlaps a wall (Collision Layer 1)
		var spaceState = GetWorld2D()?.DirectSpaceState;
		if (spaceState != null)
		{
			var query = new PhysicsPointQueryParameters2D();
			query.Position = position;
			query.CollisionMask = 1; // Walls / Static Geometry
			query.CollideWithAreas = false;
			query.CollideWithBodies = true;
			
			var result = spaceState.IntersectPoint(query, 1);
			if (result.Count > 0)
			{
				// Position is inside a wall — do not spawn blood here
				return;
			}
		}

		// 1. Load the splash assets (1-5)
		if (_splashTextures == null || _splashTextures.Length == 0)
		{
			_splashTextures = new Texture2D[5];
			for (int i = 0; i < 5; i++)
			{
				string path = $"res://assets/new-character-assets/splash-{i + 1}.png";
				_splashTextures[i] = GD.Load<Texture2D>(path);
				if (_splashTextures[i] == null)
				{
					GD.PrintErr($"[BloodSystem] ERROR: Failed to load splash asset: {path}");
				}
			}
		}

		// Pick a random splash texture
		int splashIdx = GD.RandRange(0, 4);
		var splashTex = _splashTextures[splashIdx];

		if (splashTex == null) return;

		// 2. Create Sprite
		var blood = new Sprite2D();
		blood.Texture = splashTex;
		blood.TextureFilter = TextureFilterEnum.Nearest;
		blood.ZAsRelative = false; // Ensure Z-index is absolute (-8) not relative to parent floor (-10)
		blood.ZIndex = -8; // Render above carpet (-9) but under walls (-7)
		
		// Add the blood as a child of the floor layer or world first
		// This ensures GlobalPosition and Scale are calculated relative to the correct parent
		if (_floorLayer != null)
		{
			_floorLayer.AddChild(blood);
		}
		else
		{
			AddChild(blood);
		}

		// Set GlobalPosition AFTER adding to the scene tree
		blood.GlobalPosition = position + new Vector2(0, 15f);

		// Scaling: We must compensate for the IsometricWorldMap's 4.0x scale
		// if we are parenting directly to the floor layer.
		float worldScale = (_floorLayer != null) ? 4.0f : 1.0f;
		float baseScale = (isSmall ? 0.12f : 0.28f) / worldScale;
		float randomScale = baseScale * (0.8f + (float)GD.RandRange(0, 0.4));
		blood.Scale = new Vector2(randomScale, randomScale);

		// 3. Fade out and remove
		var tween = CreateTween();
		float lingerTime = isSmall ? 4.0f : 8.0f;
		float fadeTime = 3.0f;
		tween.TweenInterval(lingerTime);
		tween.TweenProperty(blood, "modulate:a", 0.0f, fadeTime);
		tween.TweenCallback(Callable.From(() => blood.QueueFree()));
	}

	// ── Producer Ultimate: Cash Trail ───────────────────────────────────────────

	private void OnUltimateButtonPressed()
	{
		if (_ultimateActive || _ultimateUsed) return;
		ActivateCashTrailUltimate();
	}

	private void ActivateCashTrailUltimate()
	{
		if (_myRole?.ToLower() != "producer") return;
		if (_ultimateActive || _ultimateUsed) return;

		_ultimateActive = true;
		_ultimateUsed   = true;
		_ultimateTimer  = ULTIMATE_DURATION;
		_cashSpawnTimer = 0.0; // spawn first coin immediately
		if (_ultimateButton != null) _ultimateButton.Disabled = true;
		AddSlidingNotification("⚡ Cash Trail activated for 10 seconds!");
		GetNode<SfxManager>("/root/SfxManager")?.PlayBehold();
		GD.Print("[CashTrail] Producer activated ultimate");

		// Show green aura on the local Producer
		_localPlayer?.ShowCashTrailAura();
	}

	private void HandleUltimateInput(double delta)
	{
		// Only the local Producer can trigger this
		if (_myRole?.ToLower() != "producer") return;
		if (IsLocalPlayerDead()) return;

		// Resolve local player reference
		if (_localPlayer == null)
		{
			var myId = Multiplayer.GetUniqueId();
			_playerControllers.TryGetValue(myId, out _localPlayer);
		}

		// Show the ultimate button/label only while unused
		if (_ultimateButton != null)       _ultimateButton.Visible       = !_ultimateUsed || _ultimateActive;
		if (_ultimateCooldownLabel != null) _ultimateCooldownLabel.Visible = !_ultimateUsed || _ultimateActive;

		// ── Q key detection (one-shot) ──────────────────────────────────────────
		bool qPressed = Input.IsKeyPressed(Key.Q);
		if (qPressed && !_wasUltimatePressed)
		{
			if (!_ultimateActive && !_ultimateUsed)
				ActivateCashTrailUltimate();
		}
		_wasUltimatePressed = qPressed;

		// ── Active trail: spawn coins + countdown ───────────────────────────────
		if (!_ultimateActive) return;

		_ultimateTimer  -= delta;
		_cashSpawnTimer -= delta;

		if (_ultimateCooldownLabel != null)
			_ultimateCooldownLabel.Text = $"Trail active: {_ultimateTimer:F0}s";

		if (_cashSpawnTimer <= 0 && _localPlayer != null)
		{
			_cashSpawnTimer = CASH_SPAWN_INTERVAL;
			// Ask the server to spawn a cash coin at our current world position
			string coinId = $"Cash_{Multiplayer.GetUniqueId()}_{Time.GetTicksMsec()}";
			if (Multiplayer.IsServer())
			{
				// Server-hosted Producer: broadcast directly
				Rpc(MethodName.SpawnCashCoinVisual, _localPlayer.GlobalPosition, coinId, Multiplayer.GetUniqueId());
			}
			else
			{
				RpcId(1, MethodName.RequestSpawnCashCoin, _localPlayer.GlobalPosition, coinId);
			}
		}

		if (_ultimateTimer <= 0)
		{
			_ultimateActive = false;
			_ultimateTimer  = 0;
			// Hide button and label permanently — ult is single-use
			if (_ultimateButton        != null) _ultimateButton.Visible        = false;
			if (_ultimateCooldownLabel != null) _ultimateCooldownLabel.Visible = false;
			// Remove green aura from the Producer
			_localPlayer?.StopCashTrailAura();
			GD.Print("[CashTrail] Producer ultimate ended (one-use consumed)");
		}
	}

	// ── Server RPC: client asks server to spawn a coin ────────────────────────
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestSpawnCashCoin(Vector2 position, string coinId)
	{
		if (!Multiplayer.IsServer()) return;

		long senderId = Multiplayer.GetRemoteSenderId();
		if (senderId == 0) senderId = Multiplayer.GetUniqueId();

		// Must be the Producer
		if (!_networkManager.Players.TryGetValue(senderId, out var info)) return;
		if (!string.Equals(info.Role, "Producer", StringComparison.OrdinalIgnoreCase)) return;

		// Broadcast to all peers (including server itself)
		Rpc(MethodName.SpawnCashCoinVisual, position, coinId, senderId);
	}

	// ── Spawn the cash-coin node on ALL peers ─────────────────────────────────
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void SpawnCashCoinVisual(Vector2 position, string coinId, long producerPeerId)
	{
		if (HasNode(coinId)) return; // duplicate guard

		var coinRoot = new Node2D();
		coinRoot.Name     = coinId;
		coinRoot.Position = position;
		coinRoot.ZIndex   = -4; // above banana (-5), below NPCs/players
		coinRoot.AddToGroup("cash_coins");

		// ── Visual: animated spinning coin sprite sheet ───────────────────────
		const int COIN_COLS       = 4;
		const int COIN_ROWS       = 4;
		const int COIN_FRAME_W    = 67;   // 268 / 4
		const int COIN_FRAME_H    = 69;   // 276 / 4
		const int COIN_FRAMES     = COIN_COLS * COIN_ROWS; // 16
		const float COIN_FPS      = 12.0f;
		const float COIN_SCALE    = 0.45f; // world-space size

		var coinTexture = GD.Load<Texture2D>("res://assets/ai-spinning-coin.png");

		var frames = new SpriteFrames();
		frames.AddAnimation("spin");
		frames.SetAnimationLoop("spin", true);
		frames.SetAnimationSpeed("spin", COIN_FPS);

		for (int fi = 0; fi < COIN_FRAMES; fi++)
		{
			int col = fi % COIN_COLS;
			int row = fi / COIN_COLS;
			var atlas = new AtlasTexture();
			atlas.Atlas  = coinTexture;
			atlas.Region = new Rect2(col * COIN_FRAME_W, row * COIN_FRAME_H, COIN_FRAME_W, COIN_FRAME_H);
			frames.AddFrame("spin", atlas);
		}

		var visual = new AnimatedSprite2D();
		visual.Name         = "Visual";
		visual.SpriteFrames = frames;
		visual.Scale        = new Vector2(COIN_SCALE, COIN_SCALE);
		visual.Centered     = true;
		coinRoot.AddChild(visual);
		visual.Play("spin");

		// Pulsing scale animation via Tween
		var tween = visual.CreateTween();
		tween.SetLoops();
		tween.TweenProperty(visual, "scale", new Vector2(COIN_SCALE * 1.15f, COIN_SCALE * 1.15f), 0.4f).SetTrans(Tween.TransitionType.Sine);
		tween.TweenProperty(visual, "scale", new Vector2(COIN_SCALE, COIN_SCALE), 0.4f).SetTrans(Tween.TransitionType.Sine);

		// ── Collision area (detects NPCs + players) ───────────────────────────
		var area = new Area2D();
		area.Name           = "CollisionArea";
		area.CollisionMask  = 6;  // Layer 2: Players | Layer 4: NPCs
		area.Monitoring    = true;
		area.Monitorable   = false;
		var shape = new CollisionShape2D();
		var circle = new CircleShape2D { Radius = CASH_COIN_RADIUS };
		shape.Shape = circle;
		area.AddChild(shape);
		coinRoot.AddChild(area);

		// Only the server reacts to overlaps
		if (Multiplayer.IsServer())
		{
			area.BodyEntered += (Node2D body) =>
			{
				if (!IsInstanceValid(coinRoot) || !coinRoot.IsInsideTree()) return;

				if (body is NPCEntity npcEnt)
				{
					// NPC collects coin → Producer gains influence on this NPC
					var npcData = _gameEngine?.GameState.GetNPC(npcEnt.NpcId);
					if (npcData != null && npcData.Alive)
					{
						// Increase Producer component (Z) of the NPC's state
						var delta_v = new System.Numerics.Vector3(0f, 0f, CASH_INFLUENCE_GAIN);
						npcData.State = ScoringRules.ClampState(npcData.State + delta_v);
						_gameEngine.GameState.TriggerScoreChange(Role.Producer, 1);
						_gameEngine.GameState.AddNotification($"{npcEnt.NpcName} picked up the Producer's cash!");
						GD.Print($"[CashTrail] NPC {npcEnt.NpcId} collected coin {coinId}. Producer +{CASH_INFLUENCE_GAIN}");
						BroadcastGameState();
					}
					// Remove coin on all peers
					Rpc(MethodName.RemoveCashCoin, coinId, true);
				}
				else if (body is PlayerController playerCtrl)
				{
					// Another player steps on it — coin disappears (no NPC benefit)
					// Don't let the Producer collect their own coins
					long bodyPeerId = 0;
					foreach (var kvp in _playerControllers)
					{
						if (kvp.Value == playerCtrl) { bodyPeerId = kvp.Key; break; }
					}
					if (bodyPeerId == producerPeerId) return; // Producer doesn't collect own coins

					GD.Print($"[CashTrail] Player {bodyPeerId} picked up cash coin {coinId}");
					Rpc(MethodName.RemoveCashCoin, coinId, false);
				}
			};
		}

		AddChild(coinRoot);

		// Coins expire after the ultimate duration if not collected
		var lifeTimer = GetTree().CreateTimer(ULTIMATE_DURATION + 2.0);
		lifeTimer.Timeout += () =>
		{
			if (Multiplayer.IsServer() && IsInstanceValid(coinRoot) && coinRoot.IsInsideTree())
				Rpc(MethodName.RemoveCashCoin, coinId, false);
		};
	}

	// ── Remove a cash-coin on ALL peers ──────────────────────────────────────
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RemoveCashCoin(string coinId, bool wasCollectedByNpc)
	{
		var coinNode = GetNodeOrNull(coinId);
		if (coinNode == null) return;

		if (wasCollectedByNpc)
		{
			// Flash yellow before disappearing
			var visualNode = coinNode.GetNodeOrNull<Node2D>("Visual");
			if (visualNode != null)
			{
				var flashTween = visualNode.CreateTween();
				flashTween.TweenProperty(visualNode, "modulate", new Color(1, 1, 1, 0), 0.25f);
				flashTween.TweenCallback(Callable.From(() => coinNode.QueueFree()));
				return;
			}
		}
		coinNode.QueueFree();
	}

	private void CheckProducerPanelsOnMove()
	{
		if (!GodotObject.IsInstanceValid(_localPlayer) || _localPlayer.Velocity.LengthSquared() < 100) return; // Not moving significantly



		// Check Camera Panel
		var cPanel = _uiLayer.GetNodeOrNull<Control>("CameraSelectPanel");
		if (cPanel != null && cPanel.Visible)
		{
			cPanel.Visible = false;
			var btn = _uiLayer.GetNodeOrNull<Button>("ManageCamerasButton");
			if (btn != null) btn.SetPressedNoSignal(false);
		}

	}

	// ---- NETWORKING ----

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestGameState()
	{
		if (!Multiplayer.IsServer()) return;
		long senderId = Multiplayer.GetRemoteSenderId();
		string json = GenerateGameStateJson();
		RpcId(senderId, MethodName.UpdateGameState, json);
	}

	private void BroadcastGameState()
	{
		if (!Multiplayer.IsServer()) return;
		string json = GenerateGameStateJson();
		Rpc(MethodName.UpdateGameState, json);
	}

	private string GenerateGameStateJson()
	{
		var status = _gameEngine.GetGameStatus();
		status["time_remaining"] = _timeRemaining;
		status["game_active"] = _gameActive;
		status["notifications"] = JToken.FromObject(_gameEngine.GetNotifications());

		// All NPC IDs
		var allNpcIds = _gameEngine.GameState.NPCs.Keys.ToList();
		status["active_npcs"] = JToken.FromObject(allNpcIds);

		// NPC states
		var npcStates = new JObject();
		var npcsMetadata = new JObject(); // For goals display on clients
		foreach (var npc in _gameEngine.GameState.NPCs.Values)
		{
			var stateObj = new JObject
			{
				{ "alive", npc.Alive },
				{ "converted", npc.Converted },
				{ "married", npc.Married },
				{ "is_love_interest", npc.IsLoveInterest }, // Expose for UI filtering
				{ "is_target", npc.IsTarget }, // Expose target status for Admirer
				{ "state_x", npc.State.X }, // Admirer Score
				{ "state_y", npc.State.Y }, // Prophet Score
				{ "state_z", npc.State.Z }, // Producer Score
				{ "tri_x", npc.TrianglePosition.X },
				{ "tri_y", npc.TrianglePosition.Y }
			};
			// Console.WriteLine($"[DEBUG] Serializing {npc.Name}: Norm={npc.NormalizedState} Tri={npc.TrianglePosition} (Raw: {npc.State})");
			
			// Include Position (SERVER AUTHORITY)
			if (_npcEntities.TryGetValue(npc.Id, out var entity))
			{
				stateObj["pos_x"] = entity.Position.X;
				stateObj["pos_y"] = entity.Position.Y;
			}
			
			npcStates[npc.Id] = stateObj;

			// Add metadata for goals display
			var metaObj = new JObject
			{
				{ "name", npc.Name },
				{ "role", npc.IsLoveInterest ? "love_interest" : (npc.IsTarget ? "target" : "npc") },
				{ "isTarget", npc.IsTarget },
				{ "isLoveInterest", npc.IsLoveInterest }
			};
			npcsMetadata[npc.Id] = metaObj;
		}
		status["npc_states"] = npcStates;
		status["npcs"] = npcsMetadata;

		// Actions for all roles
		var allActions = new JObject();
		foreach (string roleName in new[] { "admirer", "prophet", "producer" })
		{
			Role roleEnum = Enum.Parse<Role>(roleName, true);
			var roleActions = new JObject();

			foreach (var npcId in allNpcIds)
			{
				var actions = _gameEngine.GetAvailableActions(npcId, roleEnum);
				roleActions[npcId] = JToken.FromObject(actions);
			}
			allActions[roleName] = roleActions;
		}
		status["all_actions"] = allActions;

		// Sync Network Players (Critical for UI Role Identity)
		var netPlayers = new JObject();
		foreach (var kvp in _networkManager.Players)
		{
			netPlayers[kvp.Key.ToString()] = new JObject
			{
				{ "id", kvp.Value.Id },
				{ "name", kvp.Value.Name },
				{ "role", kvp.Value.Role }
			};
		}
		status["network_players"] = netPlayers;

		// Player States
		var playerStates = new JObject();
		foreach (var kvp in _gameEngine.GameState.Players)
		{
			var playerState = kvp.Value;
			playerStates[kvp.Key.ToString()] = new JObject
			{
				{ "alive", playerState.Alive }
			};
		}
		status["player_states"] = playerStates;

		// Active Interview State (for UI)
		var interviews = new JObject();
		foreach (var kvp in _gameEngine.GameState.ActiveInterviews)
		{
			var ctx = kvp.Value;
			interviews[kvp.Key.ToString()] = new JObject
			{
				{ "npcId", ctx.NpcId },
				{ "lastResponse", ctx.LastResponse }
			};
		}
		status["active_interviews"] = interviews;

		// Active Conversion State (for UI - Prophet)
		var conversions = new JObject();
		foreach (var kvp in _gameEngine.GameState.ActiveConversions)
		{
			var ctx = kvp.Value;
			conversions[kvp.Key.ToString()] = new JObject
			{
				{ "npcId", ctx.NpcId },
				{ "lastResponse", ctx.LastResponse }
			};
		}
		status["active_conversions"] = conversions;

		// Active Producer Interview State (for UI)
		var producerInterviews = new JObject();
		foreach (var kvp in _gameEngine.GameState.ActiveProducerInterviews)
		{
			var ctx = kvp.Value;
			producerInterviews[kvp.Key.ToString()] = new JObject
			{
				{ "npcId", ctx.NpcId },
				{ "lastResponse", ctx.LastResponse }
			};
		}
		status["active_producer_interviews"] = producerInterviews;

		return status.ToString();
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	private void UpdateGameState(string json)
	{
		_localGameState = JObject.Parse(json);
		
		// sync network players from server state (Fix for missing roles)
		var netPlayers = _localGameState["network_players"] as JObject;
		if (netPlayers != null)
		{
			foreach (var prop in netPlayers.Properties())
			{
				long pid = long.Parse(prop.Name);
				string role = prop.Value["role"]?.Value<string>();
				string name = prop.Value["name"]?.Value<string>();
				
				// Force update NetworkManager if missing/mismatched
				if (!_networkManager.Players.ContainsKey(pid))
				{
					_networkManager.Players[pid] = new NetworkManager.PlayerInfo 
					{ 
						Id = (int)pid, 
						Name = name, 
						Role = role 
					};
				}
				else
				{
					var info = _networkManager.Players[pid];
					if (info.Role != role)
					{
						info.Role = role;
						_networkManager.Players[pid] = info;
					}
				}
			}
		}

		// Spawn NPCs if not spawned yet (clients)
		if (_npcEntities.Count == 0)
		{
			SpawnNPCsFromState();
		}
		
		// Spawn/update players
		if (_playerControllers.Count == 0 || _playerControllers.Count != _networkManager.Players.Count)
		{
			SpawnAllPlayers();
		}
		
		UpdateUI();
		UpdateTrianglePosition(); // Realtime update for triangle scene
		UpdateNPCVisuals();
		UpdateGhostMode();

		// If the player is previewing a chat-game response, refresh the panel text immediately
		// so the NPC's answer is visible for the duration of the timer.
		if (_responsePreviewTimer > 0.0 && _npcDialogueUI != null && _npcDialogueUI.Visible
			&& !string.IsNullOrEmpty(_currentInteractingNpcId))
		{
			RefreshInteractionPanel();
		}


		// Update Leaderboard Data
		if (_leaderboardTriangle != null)
		{
			var npcStates = _localGameState?["npc_states"] as JObject;
			if (npcStates != null)
			{
				var states = new Dictionary<string, Vector2>();
				foreach (var prop in npcStates.Properties())
				{
					float tx = prop.Value["tri_x"]?.Value<float>() ?? 0;
					float ty = prop.Value["tri_y"]?.Value<float>() ?? 0;
					states[prop.Name] = new Vector2(tx, ty);
				}
				_leaderboardTriangle.UpdateActiveStates(states);
				
				// Update Global Influence Counter (Triangle UI)
				if (_globalInfluenceTriangle != null)
				{
					_globalInfluenceTriangle.UpdateActiveStates(states);
					_globalInfluenceTriangle.ShowAllPoints();

					int admirerCount  = _globalInfluenceTriangle.AdmirerZoneCount;
					int prophetCount  = _globalInfluenceTriangle.ProphetZoneCount;
					int producerCount = _globalInfluenceTriangle.ProducerZoneCount;

					// Show glow under icon(s) of whoever leads. If tied, multiple glow.
					int maxCount = Math.Max(prophetCount, Math.Max(producerCount, admirerCount));
					bool prophetLeads  = maxCount > 0 && prophetCount  == maxCount;
					bool producerLeads = maxCount > 0 && producerCount == maxCount;
					bool admirerLeads  = maxCount > 0 && admirerCount  == maxCount;

					if (_globalProphetGlow  != null) _globalProphetGlow.Visible  = prophetLeads;
					if (_globalProducerGlow != null) _globalProducerGlow.Visible = producerLeads;
					if (_globalAdmirerGlow  != null) _globalAdmirerGlow.Visible  = admirerLeads;

					// Icon scale-pulse: loop when glowing, stop+reset when not
					void SetIconPulse(ref Tween tw, TextureRect icon, bool leading)
					{
						if (icon == null) return;
						if (leading)
						{
							if (tw != null && tw.IsRunning()) return; // already pulsing
							tw?.Kill();
							tw = CreateTween().SetLoops();
							icon.PivotOffset = icon.Size / 2f;
							tw.TweenProperty(icon, "scale", new Vector2(1.12f, 1.12f), 0.45f)
								.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
							tw.TweenProperty(icon, "scale", Vector2.One, 0.45f)
								.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
						}
						else
						{
							tw?.Kill();
							tw = null;
							icon.Scale = Vector2.One;
						}
					}
					SetIconPulse(ref _prophetIconPulse,  _globalProphetIcon,  prophetLeads);
					SetIconPulse(ref _producerIconPulse, _globalProducerIcon, producerLeads);
					SetIconPulse(ref _admirerIconPulse,  _globalAdmirerIcon,  admirerLeads);
				}

				ProcessInfluenceNotifications(states);

				// If leaderboard is open, refresh visuals

				if (_isLeaderboardOpen)
				{
					_leaderboardTriangle.ShowAllPoints();
					UpdateLeaderboardText();
				}
			}
		}


		// Show goals menu at game start (first time game state is received)
		if (!_goalsShownAtStart && !string.IsNullOrEmpty(_myRole))
		{
			_goalsShownAtStart = true;
			// Delay slightly to ensure UI is ready
			// CallDeferred(MethodName.ShowGoalsMenuAtStart); // (Removed)
		}
		// } // (Removed usage of premature closing brace)



		// Refresh Interaction Panel logic with "Self-Healing" for Interviews
		// If server says we are in an interview, we ensure the panel is open.
		var activeInterviews = _localGameState?["active_interviews"] as JObject;
		var activeConversions2 = _localGameState?["active_conversions"] as JObject;
		var activeProdInterviews2 = _localGameState?["active_producer_interviews"] as JObject;
		string myRoleKey = Capitalize(_myRole ?? "");

		// Detect whether the server still has ANY active interaction for us
		bool serverHasActive =
			(!string.IsNullOrEmpty(_myRole)) &&
			(
				(activeInterviews != null && activeInterviews.ContainsKey(myRoleKey)) ||
				(activeConversions2 != null && activeConversions2.ContainsKey(myRoleKey)) ||
				(activeProdInterviews2 != null && activeProdInterviews2.ContainsKey(myRoleKey))
			);

		// Once the server confirms no active interaction, lift the suppress flag
		// Do NOT lift _suppressSelfHeal once the game is over
		if (_suppressSelfHeal && !serverHasActive && !_isGameOver)
		{
			_suppressSelfHeal = false;
		}

		// --- Self-healing: only runs when we have NOT intentionally closed the panel ---
		if (!_suppressSelfHeal)
		{
			bool isInInterview = false;
			string interviewNpcId = null;

			if (!string.IsNullOrEmpty(_myRole) && activeInterviews != null)
			{
				if (activeInterviews.ContainsKey(myRoleKey))
				{
					var interviewInfo = activeInterviews[myRoleKey];
					string nId = interviewInfo["npcId"]?.Value<string>();
					if (!string.IsNullOrEmpty(nId)) { isInInterview = true; interviewNpcId = nId; }
				}
			}

			if (isInInterview && interviewNpcId != null)
			{
				if (!_npcDialogueUI.Visible || _currentInteractingNpcId != interviewNpcId)
				{
					_currentInteractingNpcId = interviewNpcId;
					RefreshInteractionPanel();
				}
			}
			else
			{
				bool isInConversion = false;
				string conversionNpcId = null;

				if (!string.IsNullOrEmpty(_myRole) && activeConversions2 != null)
				{
					if (activeConversions2.ContainsKey(myRoleKey))
					{
						var convInfo = activeConversions2[myRoleKey];
						string nId = convInfo["npcId"]?.Value<string>();
						if (!string.IsNullOrEmpty(nId)) { isInConversion = true; conversionNpcId = nId; }
					}
				}

				if (isInConversion && conversionNpcId != null)
				{
					if (!_npcDialogueUI.Visible || _currentInteractingNpcId != conversionNpcId)
					{
						_currentInteractingNpcId = conversionNpcId;
						RefreshInteractionPanel();
					}
				}
				else if (_npcDialogueUI.Visible && !string.IsNullOrEmpty(_currentInteractingNpcId))
				{
					int currentHash = _localGameState?.GetHashCode() ?? 0;
					if (currentHash != _lastGameStateHash)
					{
						_lastGameStateHash = currentHash;
						RefreshInteractionPanel();
					}
				}
			}
		}
	}

	private void RefreshInteractionPanel()
	{
		string npcId = _currentInteractingNpcId;
		if (string.IsNullOrEmpty(npcId)) return;

		// Get available actions from cached state
		var allActions = _localGameState?["all_actions"] as JObject;
		var myActions = allActions?[_myRole.ToLower()] as JObject;
		var npcActions = myActions?[npcId];

		var npcEntity = _npcEntities.GetValueOrDefault(npcId);
		if (npcEntity != null)
		{
			npcEntity.SetFrozen(true);
		}
		string npcName = npcEntity?.NpcName ?? npcId;

		var npcConfig = _localGameState?["npcs"]?[npcId];
		string desc = npcConfig?["interactionTree"]?["root"]?["text"]?.Value<string>() 
			?? "An NPC awaits your action.";

		// INTERVIEW UI OVERRIDE
		var activeInterviews = _localGameState?["active_interviews"] as JObject;
		string roleKey = Capitalize(_myRole); 
		if (activeInterviews != null && activeInterviews.ContainsKey(roleKey))
		{
			var interviewInfo = activeInterviews[roleKey];
			if (interviewInfo["npcId"]?.Value<string>() == npcId)
			{
				desc = interviewInfo["lastResponse"]?.Value<string>() ?? desc;
			}
		}

		// CONVERSION UI OVERRIDE (Prophet)
		var activeConversions = _localGameState?["active_conversions"] as JObject;
		if (activeConversions != null && activeConversions.ContainsKey(roleKey))
		{
			var convInfo = activeConversions[roleKey];
			if (convInfo["npcId"]?.Value<string>() == npcId)
			{
				desc = convInfo["lastResponse"]?.Value<string>() ?? desc;
			}
		}

		// PRODUCER INTERVIEW UI OVERRIDE
		var producerInterviews = _localGameState?["active_producer_interviews"] as JObject;
		if (producerInterviews != null && !string.IsNullOrEmpty(_myRole) && producerInterviews.ContainsKey(Capitalize(_myRole)))
		{
			var piInfo = producerInterviews[Capitalize(_myRole)];
			if (piInfo["npcId"]?.Value<string>() == npcId)
			{
				desc = piInfo["lastResponse"]?.Value<string>() ?? desc;
			}
		}

		var actionsList = npcActions?.ToObject<List<JToken>>() ?? new List<JToken>();
		
		Vector3 npcState = Vector3.Zero;
		var npcStates = _localGameState?["npc_states"] as JObject;
		if (npcStates != null && npcStates.ContainsKey(npcId))
		{
			var s = npcStates[npcId];
			float x = s["state_x"]?.Value<float>() ?? 0;
			float y = s["state_y"]?.Value<float>() ?? 0;
			float z = s["state_z"]?.Value<float>() ?? 0;
			npcState = new Vector3(x, y, z);
		}

		Texture2D npcPortrait = null;
		
		// Try to find the actual NPCEntity to get the portrait
		// We have _npcEntities dictionary but it might be server-only?
		// GameWorld tracks spawned entities in _npcEntities.
		if (_npcEntities.ContainsKey(npcId))
		{
			npcPortrait = _npcEntities[npcId].GetPortraitTexture();
		}

		_npcDialogueUI.ShowForNPC(npcId, npcName, desc, actionsList, npcState, npcPortrait);
	}

	private void SpawnNPCsFromState()
	{
		var npcColors = new Dictionary<string, Color>
		{
			{ "katy", Colors.DeepPink },
			{ "john", Colors.DodgerBlue },
			{ "rebecca", Colors.Orange },
			{ "marcus", Colors.LimeGreen },
			{ "sofia", Colors.Orchid },
			{ "amir", Colors.Gold },
			{ "bella", Colors.MediumPurple },
			{ "chris", Colors.Teal },
			{ "diana", Colors.Salmon },
			{ "eli", Colors.SlateBlue }
		};

		var activeNpcs = _localGameState?["active_npcs"];
		if (activeNpcs == null) return;

		var layer6 = GetLayer6();

		foreach (string npcId in activeNpcs)
		{
			if (_npcEntities.ContainsKey(npcId)) continue;
			
			var entity = new NPCEntity();
			entity.NpcId = npcId;
			entity.NpcName = npcId.Substring(0, 1).ToUpper() + npcId.Substring(1); // Capitalize
			entity.NpcColor = npcColors.GetValueOrDefault(npcId, Colors.Blue);
			
			// Try to get initial position from state
			Vector2 initPos = FindFreeNPCSpawnPosition();
			var npcStates = _localGameState["npc_states"] as JObject;
			if (npcStates != null && npcStates[npcId] != null)
			{
				float? px = npcStates[npcId]["pos_x"]?.Value<float>();
				float? py = npcStates[npcId]["pos_y"]?.Value<float>();
				if (px.HasValue && py.HasValue)
				{
					initPos = new Vector2(px.Value, py.Value);
				}
			}

			entity.NPCClicked += OnNPCClicked;
			if (layer6 != null)
			{
				entity.Position = layer6.ToLocal(initPos);
				entity.Scale = new Vector2(0.25f, 0.25f); // Counteract 4x inherited scale
				entity.SyncPosition(entity.Position);
				layer6.AddChild(entity);
			}
			else
			{
				entity.Position = initPos;
				entity.SyncPosition(initPos);
				AddChild(entity);
			}
			_npcEntities[npcId] = entity;
		}
	}

	private void UpdateUI()
	{
		if (_localGameState == null) return;

		// Timer — MM:SS, color gradient, per-second bounce in last 20s
		double time = _localGameState["time_remaining"]?.Value<double>() ?? 0;
		int secs = Mathf.CeilToInt((float)time);
		TimeSpan ts = TimeSpan.FromSeconds(time);
		_timerLabel.Text = $"{ts.Minutes:D2}:{ts.Seconds:D2}";

		// Timer color = leading role's color (matches glow icons), white if no NPCs placed yet
		Color timerColor = Colors.White;
		if (_globalInfluenceTriangle != null)
		{
			int prophetN  = _globalInfluenceTriangle.ProphetZoneCount;
			int producerN = _globalInfluenceTriangle.ProducerZoneCount;
			int admirerN  = _globalInfluenceTriangle.AdmirerZoneCount;
			int maxN = Math.Max(prophetN, Math.Max(producerN, admirerN));

			if (maxN > 0)
			{
				bool pLead = prophetN  == maxN;
				bool rLead = producerN == maxN;
				bool aLead = admirerN  == maxN;

				// Role colours (match glow shader uniforms)
				var cProphet  = new Color(0.1f,  0.5f,  1.0f,  1f); // vivid blue
				var cAdmirer  = new Color(1.0f,  0.08f, 0.08f, 1f); // red
				var cProducer = new Color(0.05f, 1.0f,  0.25f, 1f); // lime-green

				if (pLead && !rLead && !aLead)       timerColor = cProphet;
				else if (rLead && !pLead && !aLead)  timerColor = cProducer;
				else if (aLead && !pLead && !rLead)  timerColor = cAdmirer;
				// Tie blends
				else if (pLead && rLead && !aLead)   timerColor = new Color(0.0f,  0.85f, 0.80f, 1f); // teal
				else if (pLead && aLead && !rLead)   timerColor = new Color(0.55f, 0.1f,  0.85f, 1f); // purple
				else if (rLead && aLead && !pLead)   timerColor = new Color(1.0f,  0.65f, 0.0f,  1f); // orange-yellow
				// All three tied — stay white
			}
		}
		_timerLabel.AddThemeColorOverride("font_color", timerColor);

		// Per-second bounce pulse in last 20 seconds
		if (secs != _lastDisplayedSecond)
		{
			_lastDisplayedSecond = secs;
			if (time <= 20 && time > 0)
			{
				var tw = CreateTween();
				_timerLabel.PivotOffset = _timerLabel.Size / 2f;
				tw.TweenProperty(_timerLabel, "scale", new Vector2(1.35f, 1.35f), 0.08f)
					.SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
				tw.TweenProperty(_timerLabel, "scale", Vector2.One, 0.18f)
					.SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
			}
		}

		// Check for new CAMERA ALERTS to reset local timer if needed
		var notifications = _localGameState["notifications"]?.ToObject<List<string>>() ?? new List<string>();
		// GD.Print($"[CallPoliceDebug] Notifs: {notifications.Count}, Last: {_lastProcessedNotificationCount}");
		
		// CRITICAL FIX: If notifications were cleared (Count dropped), reset our pointer so we don't miss new ones.
		if (notifications.Count < _lastProcessedNotificationCount)
		{
			_lastProcessedNotificationCount = 0;
		}

		if (notifications.Count > _lastProcessedNotificationCount)
		{
			for (int i = _lastProcessedNotificationCount; i < notifications.Count; i++)
			{
				if (notifications[i].Contains("[CAMERA ALERT]"))
				{
					// New detection! Reset local fallback timer to NOW
					// _localCaughtTimeFallback = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

				}
			}
			_lastProcessedNotificationCount = notifications.Count;
		}

		// Role
		long myId = Multiplayer.GetUniqueId();
		if (_networkManager.Players.ContainsKey(myId))
		{
			_myRole = _networkManager.Players[myId].Role;
		}
		// Role label hidden per user request
		// _roleLabel.Text = $"Role: {_myRole?.ToUpper()}";

		bool isProphet = (_myRole?.ToLower() == "prophet");
		bool isAdmirer = (_myRole?.ToLower() == "admirer");

		// Check if any player or NPC has been killed (one kill per game limit for Admirer UI)
		bool anyNPCDead = false;
		foreach (var kvp in _npcEntities)
		{
			if (TryGetNpcTargetState(kvp.Key, out _, out bool alive) && !alive)
			{
				anyNPCDead = true;
				break;
			}
		}

		// Update Admirer Knife Visibility (Only show if admirer AND nobody is dead)
		bool showKnife = isAdmirer && !anyNPCDead;
		if (_admirerKnifeButton != null) _admirerKnifeButton.Visible = showKnife;
		if (_admirerKnifeLabel != null) _admirerKnifeLabel.Visible = showKnife;

		// Update Trap Button Visibility (Prophet only)
		if (_uiLayer.GetNodeOrNull<Button>("TrapButton") is Button trapBtn)
		{
			// trapBtn.Visible = isProphet; // Set Trap disabled per request
			trapBtn.Visible = false;
		}

		// Update bottom-left status panel visibility — hidden since converted label removed
		if (_bottomLeftStatusPanel != null)
		{
			_bottomLeftStatusPanel.Visible = false;
		}

		// Prophet conversion progress — commented out per request
		// if (_convertedLabel != null)
		// {
		// 	var npcStates = _localGameState?["npc_states"] as JObject;
		// 	int convertedCount = npcStates?.Properties()
		// 		.Where(p => p.Value["converted"]?.Value<bool>() == true)
		// 		.Count() ?? 0;
		// 	_convertedLabel.Text = $"Converted: {convertedCount}";
		// 	_convertedLabel.Visible = isProphet;
		// }


		bool isProducer = (_myRole?.ToLower() == "producer");
		var manageCamsBtn = _uiLayer.GetNodeOrNull<Button>("ManageCamerasButton");
		if (manageCamsBtn != null) manageCamsBtn.Visible = false; // camera logic disabled per request
		if (_activeCamerasLabel != null) _activeCamerasLabel.Visible = false; // camera logic disabled per request
		if (isProducer)
		{
			// Camera logic disabled per request.
			// // Update Active Cameras Text in HUD
			// var activeCameras = _localGameState?["active_camera_room_ids"]?.ToObject<List<string>>() ?? new List<string>();
			// if (_activeCamerasLabel != null)
			// {
			// 	_activeCamerasLabel.Visible = true;
			// 	_activeCamerasLabel.Text = activeCameras.Count > 0 
			// 		? $"Active Security Cameras:\n{string.Join(", ", activeCameras)}"
			// 		: "Active Security Cameras:\nNone";
			// }
			// 
			// // Update Camera Select Panel Buttons (if open)
			// var csPanel = _uiLayer.GetNodeOrNull<Control>("CameraSelectPanel");
			// if (csPanel != null && csPanel.Visible)
			// {
			// 	foreach (var camRoom in new[] { "Room1", "Room2", "Room3", "Room4", "Room5", "Hallways" })
			// 	{
			// 		var btn = csPanel.FindChild($"Btn_{camRoom}", true, false) as CheckButton;
			// 		if (btn != null)
			// 		{
			// 			bool isActive = activeCameras.Contains(camRoom);
			// 			btn.ButtonPressed = isActive; // Set check state
			// 		}
			// 	}
			// 
			// 	// Also update the visual overlay on the editorial asset (rooms greyed)
			// 	if (_editorialContentNode != null)
			// 	{
			// 		foreach (var child in _editorialContentNode.GetChildren())
			// 		{
			// 			if (child is Node roomNode)
			// 			{
			// 				var nameProp = roomNode.Get("room_name");
			// 				string roomName = nameProp.Obj != null ? nameProp.ToString().Replace(" ", "") : roomNode.Name.ToString().Replace(" ", "");
			// 				bool shouldBeActive = activeCameras.Contains(roomName);
			// 				if (roomNode.HasMethod("set_active"))
			// 				{
			// 					roomNode.Call("set_active", shouldBeActive);
			// 				}
			// 				else 
			// 				{
			// 					try
			// 					{
			// 						roomNode.Set("is_active", shouldBeActive);
			// 						if (roomNode.HasMethod("update_visual")) roomNode.Call("update_visual");
			// 					}
			// 					catch (Exception) { /* catch */ }
			// 				}
			// 			}
			// 		}
			// 	}
			// }
		}


		// Refresh Interaction Panel if open (for dynamic content like Interview)
		// Only refresh when we haven't intentionally closed the panel (suppress flag prevents re-opening)
		if (!_suppressSelfHeal && _npcDialogueUI.Visible && !string.IsNullOrEmpty(_currentInteractingNpcId))
		{
			RefreshInteractionPanel();
		}


		// Meters removed


		// Notifications
		var notifs = _localGameState["notifications"];
		if (notifs != null)
		{
			var notifList = notifs.ToObject<List<string>>();
			int oldNotificationCount = _lastNotificationCount; // Save old count for checking new notifications
			
			// Only add new notifications (those we haven't shown yet)
			for (int i = _lastNotificationCount; i < notifList.Count; i++)
			{
				string msg = notifList[i];
				_notificationText.AddText(msg + "\n");
				
			}
			_lastNotificationCount = notifList.Count;
			
			// Check for +1 rating increase for Producer after interview
			bool isProducerRole = (_myRole?.ToLower() == "producer");
			if (isProducerRole)
			{
				// Get current producer rating
				var players = _localGameState["players"];
				var producerState = players?["producer"];
				if (producerState != null)
				{
					var meters = producerState["meters"];
					var ratingsData = meters?["ratings"];
					if (ratingsData != null)
					{
						double currentRating = ratingsData["value"]?.Value<double>() ?? 0;
						
						GD.Print($"[+1 Debug] Current rating: {currentRating}, Previous rating: {_previousProducerRating}");
						
						// Check if rating increased (Relaxed check to include +2 or other sources)
						if (currentRating > _previousProducerRating)
						{
							double diff = currentRating - _previousProducerRating;
							GD.Print($"[+1 Debug] Rating increased by +{diff}! Showing visual feedback...");
							
							// Show +1 visual regardless of source (Interview, MiniGame, etc)
							if (!_isGameOver)
								ShowScoreFeedback("producer", "ai_producer_plusone.png");
							GD.Print("[GameWorld] ✅ Showing rating increase visual feedback!");
						}
						
						// Update previous rating for next check
						_previousProducerRating = currentRating;
					}
				}
			}
		}

		// Game Over check
		bool isGameOver = _localGameState["game_over"]?.Value<bool>() ?? false;
		if (isGameOver)
		{
			// 1. DISABLE PLAYER INPUT
			if (_localPlayer != null)
			{
				_localPlayer.InputEnabled = false;
				_localPlayer.Velocity = Vector2.Zero; // Stop moving immediately
			}

			// Permanently suppress interaction panel for the rest of the session
			_isGameOver = true;
			_suppressSelfHeal = true;
			_currentInteractingNpcId = null;

			// Always force-hide the dialogue panel on game over (in case it got re-opened)
			if (_npcDialogueUI != null && _npcDialogueUI.Visible)
			{
				_npcDialogueUI.Close();
			}

			string winner = _localGameState["winner"]?.Value<string>();
			string winText = !string.IsNullOrEmpty(winner) ? $"{winner.ToUpper()} WINS!" : "GAME OVER";
			
			// 2. BLOCK UI CLICKS & SHOW OVERLAY
			if (!HasNode("GameOverOverlay"))
			{
				_currentInteractingNpcId = null;

				// Purge any floating score-feedback nodes that are already in the scene
				foreach (Node node in GetTree().GetNodesInGroup("ScoreFeedback"))
				{
					node.QueueFree();
				}

				// Full screen blocking rect with title background
				var overlay = new TextureRect();
				overlay.Name = "GameOverOverlay";
				overlay.Texture = ResourceLoader.Load<Texture2D>("res://assets/Title_BG_1.png");
				overlay.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
				overlay.StretchMode = TextureRect.StretchModeEnum.Scale;
				overlay.Size = _worldSize * 1.2f; // Cover entire world (20% bigger)
				overlay.MouseFilter = Control.MouseFilterEnum.Stop; // BLOCK ALL CLICKS
				overlay.ZIndex = 200; // Well above score floats (ZIndex 100) and everything else
				_uiLayer.AddChild(overlay);

				// Switch to lobby music for the win screen
				GetNode<MusicManager>("/root/MusicManager")?.ForceLobbyMusic();

				// Centered Label
				var label = new Label();
				label.Name = "GameOverLabel";
				label.Text = winText;
				label.AddThemeFontOverride("font", _customFont);
				label.AddThemeFontSizeOverride("font_size", 64);
				label.HorizontalAlignment = HorizontalAlignment.Center;
				label.VerticalAlignment = VerticalAlignment.Center;
				label.AnchorsPreset = (int)Control.LayoutPreset.Center;
				// Center in overlay
				label.Position = _worldSize / 2 - new Vector2(200, 150);
				label.ZIndex = 201; // Above overlay
				_uiLayer.AddChild(label);

				// Return to Lobby Button
				var lobbyButton = new Button();
				lobbyButton.Name = "ReturnToLobbyButton";
				lobbyButton.Text = "Return to Lobby";
				lobbyButton.AddThemeFontOverride("font", _customFont);
				lobbyButton.AddThemeFontSizeOverride("font_size", 32);
				lobbyButton.CustomMinimumSize = new Vector2(250, 60);
				// Make button transparent (no black box)
				var emptyStyle = new StyleBoxEmpty();
				lobbyButton.AddThemeStyleboxOverride("normal", emptyStyle);
				lobbyButton.AddThemeStyleboxOverride("hover", emptyStyle);
				lobbyButton.AddThemeStyleboxOverride("pressed", emptyStyle);
				lobbyButton.AddThemeStyleboxOverride("focus", emptyStyle);
				// Position below the label
				lobbyButton.Position = _worldSize / 2 - new Vector2(100, 50);
				lobbyButton.ZIndex = 201; // Above overlay
				lobbyButton.Pressed += OnReturnToLobbyPressed;
				
				// Add hover effect
				lobbyButton.MouseEntered += () => lobbyButton.AddThemeColorOverride("font_color", _hoverColor);
				lobbyButton.MouseExited += () => lobbyButton.AddThemeColorOverride("font_color", _normalColor);
				
				_uiLayer.AddChild(lobbyButton);
			}
		}


		// Elimination UI Logic
		bool admirerEliminated = _localGameState?["admirer_eliminated"]?.Value<bool>() ?? false;
		
		if (admirerEliminated)
		{
			// Verify if we haven't shown it yet
			if (!_admirerEliminatedShown)
			{
				_admirerEliminatedShown = true;
				_eliminationOverlay.Visible = true;
				_admirerEliminatedTimer = 5.0f; // Start 5s timer for EVERYONE

				if (_myRole.ToLower() == "admirer")
				{
					_eliminationLabel.Text = "YOU HAVE BEEN ELIMINATED";
				}
				else
				{
					_eliminationLabel.Text = "ADMIRER ELIMINATED";
				}
			}
			// Visibility is controlled by _Process via timer
		}
		else
		{
			_eliminationOverlay.Visible = false;
			_admirerEliminatedShown = false;
		}
		if (!isGameOver) // Only hide if game isn't over otherwise
		{
			// Reset if new game
			// Reset if new game
		}
	}

	private Control CreateMeterControl(string name, double val, double max, bool isProducer)
	{
		var label = new Label();
		label.Text = $"{name.ToUpper()}: {val:F1}/{max:F0}";
		label.AddThemeFontOverride("font", _customFont);
		label.AddThemeFontSizeOverride("font_size", 26);
		label.AddThemeColorOverride("font_color", Colors.White);
		return label;
	}



	private void UpdateTrianglePosition()
	{
		if (_triangleScene == null || !_triangleScene.Visible || string.IsNullOrEmpty(_currentInteractingNpcId)) return;

		var npcStates = _localGameState?["npc_states"] as JObject;
		if (npcStates != null && npcStates.ContainsKey(_currentInteractingNpcId))
		{
			var s = npcStates[_currentInteractingNpcId];
			float tx = s["tri_x"]?.Value<float>() ?? 0;
			float ty = s["tri_y"]?.Value<float>() ?? 0;
			
			// Directly update position without full re-show
			// We need to expose a method in TriangleScene or just call Show again (it handles updates)
			_triangleScene.Show(_triangleScene.GetNodeOrNull<Label>("NpcNameLabel")?.Text ?? "", new Vector2(tx, ty));
		}
	}

	private void UpdateNPCVisuals()
	{
		var npcStates = _localGameState?["npc_states"] as JObject;
		if (npcStates == null) return;

		foreach (var kvp in _npcEntities)
		{
			var state = npcStates[kvp.Key];
			if (state != null)
			{
				bool alive = state["alive"]?.Value<bool>() ?? true;
				bool converted = state["converted"]?.Value<bool>() ?? false;
				bool married = state["married"]?.Value<bool>() ?? false;
				bool isTarget = state["is_target"]?.Value<bool>() ?? false;
				
				// Sync Position (If client)
				if (!Multiplayer.IsServer())
				{
					float? px = state["pos_x"]?.Value<float>();
					float? py = state["pos_y"]?.Value<float>();
					if (px.HasValue && py.HasValue)
					{
						// Smooth interpolation via SyncPosition
						kvp.Value.SyncPosition(new Vector2(px.Value, py.Value));
					}
				}

				kvp.Value.UpdateState(alive, converted, married, isTarget);
				
				// DYNAMIC NAME TAG COLOR (GRADIENT)
				if (_triangleScene != null)
				{
					float tx = state["tri_x"]?.Value<float>() ?? 0;
					float ty = state["tri_y"]?.Value<float>() ?? 0;
					Color gradientColor = _triangleScene.GetGradientColor(new Vector2(tx, ty));
					kvp.Value.UpdateNameTagColor(gradientColor);
				}
			}
		}
	}

	private void UpdateGhostMode()
	{
		// Check for Admirer Elimination (legacy flag)
		bool admirerEliminated = _localGameState?["admirer_eliminated"]?.Value<bool>() ?? false;
		
		// Get player states for all roles
		var playerStates = _localGameState?["player_states"] as JObject;
		
		// Iterate through all players
		foreach (var kvp in _playerControllers)
		{
			long pid = kvp.Key;
			var controller = kvp.Value;
			
			// Determine role
			string role = "Observer";
			if (_localGameState != null)
			{
				var netPlayers = _localGameState["network_players"] as JObject;
				role = netPlayers?[pid.ToString()]?["role"]?.Value<string>() ?? controller.PlayerRole;
			}
			
			// Check if this player is dead
			bool isPlayerDead = false;
			
			// For Admirer, use the legacy flag or player states
			if (role.ToLower() == "admirer")
			{
				isPlayerDead = admirerEliminated;
			}
			
			// Also check player_states for any role
			if (playerStates != null)
			{
				var roleState = playerStates[role];
				if (roleState != null)
				{
					bool alive = roleState["alive"]?.Value<bool>() ?? true;
					isPlayerDead = !alive;
				}
			}
			
			// Enable Ghost Mode if the player is eliminated
			controller.SetGhostMode(isPlayerDead);
		}
	}

	// ---- PLAYER CONTROLLER LIFECYCLE ----

	private void SpawnPlayer(long playerId)
	{
		if (_playerControllers.ContainsKey(playerId)) return;

		var startPositions = new Vector2[]
		{
			new Vector2(100, 700),
			new Vector2(600, 700),
			new Vector2(1100, 700)
		};

		var player = new PlayerController();
		player.Name = $"Player_{playerId}";
		int idx = _playerControllers.Count % startPositions.Length;
		Vector2 worldPos = startPositions[idx];
		player.PlayerIndex = (idx % 3) + 1;
		player.SetPlayerId(playerId);

		string role = _networkManager.Players.ContainsKey(playerId) ? _networkManager.Players[playerId].Role : "Observer";
		player.SetRole(role);

		bool isLocal = playerId == Multiplayer.GetUniqueId();
		player.SetLocalPlayer(isLocal);
		if (isLocal)
		{
			_localPlayer = player;
			player.PositionChanged += OnPlayerPositionChanged;
			GD.Print($"[GameWorld] Connected PositionChanged for local player {playerId}");
		}

		var layer6 = GetLayer6();
		if (layer6 != null)
		{
			player.Position = layer6.ToLocal(worldPos);
			player.Scale = new Vector2(0.25f, 0.25f); // Counteract 4x inherited scale
			layer6.AddChild(player);
		}
		else
		{
			player.Position = worldPos;
			AddChild(player);
		}
		_playerControllers[playerId] = player;
		GD.Print($"[GameWorld] Spawned player controller for {playerId}, role={role}, isLocal={isLocal}");
	}

	private void OnNetworkPlayerConnected(long id, string name)
	{
		GD.Print($"[GameWorld] OnNetworkPlayerConnected id={id}, name={name}");
		SpawnPlayer(id);
	}

	private void OnNetworkPlayerDisconnected(long id)
	{
		GD.Print($"[GameWorld] OnNetworkPlayerDisconnected id={id}");
		if (_playerControllers.TryGetValue(id, out var controller))
		{
			controller.QueueFree();
			_playerControllers.Remove(id);
		}
	}

	// ---- POSITION SYNCHRONIZATION ----

	private void OnPlayerPositionChanged(long playerId, Vector2 position)
	{
		if (!_playerControllers.TryGetValue(playerId, out var controller)) return;

		string facing = controller.FacingDirection;
		bool flipH = controller.FlipH;
		bool isMoving = controller.IsMoving;

		// This is called when the LOCAL player moves on this machine
		GD.Print($"[GameWorld] OnPlayerPositionChanged: Player {playerId} at {position}, facing={facing}, flip={flipH}, isMoving={isMoving}");
		
		// Send the position update to all other peers
		if (Multiplayer.IsServer())
		{
			controller.Position = position; // Update server's own position
			Rpc(MethodName.SyncPlayerPosition, playerId, position, facing, flipH, isMoving);
		}
		else
		{
			RpcId(1, MethodName.SendPlayerPosition, playerId, position, facing, flipH, isMoving);
		}
	}

	// Called by clients to send their position to the server
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	private void SendPlayerPosition(long playerId, Vector2 position, string facing, bool flipH, bool isMoving)
	{
		if (!Multiplayer.IsServer()) return;
		
		if (_playerControllers.TryGetValue(playerId, out var controller))
		{
			controller.Position = position;
			// Also update server's view of the remote player's animation state
			controller.UpdateRemotePosition(position, facing, flipH, isMoving);
		}
		
		// Broadcast to ALL clients
		Rpc(MethodName.SyncPlayerPosition, playerId, position, facing, flipH, isMoving);
	}

	// Called by the server to sync player position to all clients
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	private void SyncPlayerPosition(long playerId, Vector2 position, string facing, bool flipH, bool isMoving)
	{
		if (_playerControllers.TryGetValue(playerId, out var controller))
		{
			controller.UpdateRemotePosition(position, facing, flipH, isMoving);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	public void SubmitAction(string npcId, string actionId)
	{
		if (!Multiplayer.IsServer()) return;

		long senderId = Multiplayer.GetRemoteSenderId();
		if (senderId == 0) senderId = Multiplayer.GetUniqueId();

		if (!_networkManager.Players.ContainsKey(senderId))
		{
			GD.PrintErr($"[SubmitAction] Sender {senderId} not found in player list.");
			return;
		}

		string senderRole = _networkManager.Players[senderId].Role.ToLower();
		Role roleEnum = Enum.Parse<Role>(senderRole, true);

		// Special Handling: Global Actions (e.g. Set Trap)
		if (npcId == "global")
		{
			// if (actionId == "set_trap" && roleEnum == Role.Prophet)
			// {
			// 	// TODO: Check cooldown or limits if needed
			// 	_gameEngine.CreateTrap(roleEnum);
			// 	_gameEngine.GameState.AddNotification("A trap has been placed by the Prophet.");
			// 	// Spawn a banana at the Prophet's current location (server authoritative), replicate to all clients
			// 	if (_playerControllers.TryGetValue(senderId, out var prophetController))
			// 	{
			// 		var pos = prophetController.Position;
			// 		string trapId = $"Trap_{Time.GetTicksMsec()}_{senderId}";
			// 		GD.Print($"[SubmitAction] Prophet {senderId} placed trap {trapId} at {pos}. Broadcasting to all.");
			// 		Rpc(MethodName.SpawnBananaVisual, pos, trapId, senderId);
			// 	}
			// 	else
			// 	{
			// 		GD.PrintErr($"[SubmitAction] Could not find controller for Prophet {senderId}");
			// 	}
			// 	BroadcastGameState();
			// }
			return;
		}

		// CRITICAL FIX: Camera Detection Consistency
		// Intercept "kill_" actions to ensure the NPC's room location is 100% accurate before processing.
		if (actionId.StartsWith("kill_"))
		{
			// Find the NPC entity
			if (_npcEntities.TryGetValue(npcId, out var npcEntity))
			{
				// Determine which room they are ACTUALLY in right now
				string currentRoomId = GetRoomIdAtPosition(npcEntity.Position);
				if (!string.IsNullOrEmpty(currentRoomId))
				{
					// Force update the Game Engine state
					var npc = _gameEngine.GameState.GetNPC(npcId);
					if (npc != null && npc.CurrentRoomId != currentRoomId)
					{
						GD.Print($"[GameWorld] Force-updating NPC {npcId} room from '{npc.CurrentRoomId}' to '{currentRoomId}' before kill.");
						npc.CurrentRoomId = currentRoomId;
					}
				}
				else
				{
					GD.Print($"[GameWorld] Warning: Could not determine room for NPC {npcId} at {npcEntity.Position} during kill.");
				}
			}
		}

		var (success, failReason) = _gameEngine.PerformAction(npcId, actionId, roleEnum);

		if (success)
		{
			_gameEngine.GameState.AddNotification($"{senderRole.ToUpper()} performed {actionId} on {npcId}");
		}
		else
		{
			string reasonMsg = failReason ?? "unknown reason";
			_gameEngine.GameState.AddNotification($"{senderRole.ToUpper()}: {reasonMsg}");
		}

		BroadcastGameState();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	public void SpawnBananaVisual(Vector2 position, string trapId, long placerId)
	{
		GD.Print($"[SpawnBananaVisual] Spawning banana {trapId} at {position} placed by {placerId} on peer {Multiplayer.GetUniqueId()}");
		// Create a BananaTrap node with sprite and collision to detect NPCs
		var bananaTexture = ResourceLoader.Load<Texture2D>("res://assets/banana.png");
		if (bananaTexture == null)
		{
			GD.PrintErr("[GameWorld] Failed to load banana texture");
			return;
		}

		var bananaRoot = new Node2D();
		bananaRoot.Name = trapId;
		bananaRoot.Position = position;
		bananaRoot.AddToGroup("traps");
		bananaRoot.ZIndex = -5; // Behind players/NPCs (tilemap is at -10)
	
	// Store placer ID and placement time as metadata for immunity checks
	bananaRoot.SetMeta("placerId", placerId);
	bananaRoot.SetMeta("placementTime", Time.GetTicksMsec());

		var sprite = new Sprite2D();
		sprite.Texture = bananaTexture;
		sprite.Scale = new Vector2(1.5f, 1.5f); // Reverted to original size
		bananaRoot.AddChild(sprite);

		// Collision detector
		var area = new Area2D();
		area.Monitoring = true;
		area.Monitorable = true;
		area.CollisionMask = 6; // Detect bodies on NPC layer (4) and Player layer (2)
			var shape = new CollisionShape2D();
			var circle = new CircleShape2D();
			circle.Radius = 12; // Smaller trigger radius around banana
		shape.Shape = circle;
		area.AddChild(shape);
		bananaRoot.AddChild(area);

		// Only the server should react to triggers
		if (Multiplayer.IsServer())
		{
			area.BodyEntered += (Node2D body) =>
			{
				// Get trap metadata for immunity checks
				long trapPlacerId = (long)bananaRoot.GetMeta("placerId", 0L);
				ulong placementTime = (ulong)bananaRoot.GetMeta("placementTime", 0UL);
				ulong currentTime = Time.GetTicksMsec();
				ulong immunityDuration = 2000; // 2 seconds immunity in milliseconds
				
				if (body is NPCEntity npc)
				{
					// Notify and apply slip globally
					_gameEngine.GameState.AddNotification($"A trap has been triggered! {npc.NpcName} was caught in the banana trap!");
					
					// Sync the slip and removal to ALL clients
					Rpc(MethodName.SyncTrapTriggered, trapId, npc.NpcId);
					

					
					// Broadcast updated state (for notifications)
					BroadcastGameState();
				}
				else if (body is PlayerController player)
				{
					// Find the actual player ID from the controller
					long playerId = 0;
					foreach (var kvp in _playerControllers)
					{
						if (kvp.Value == player)
						{
							playerId = kvp.Key;
							break;
						}
					}
					
					// Skip if this is the placer and within immunity period
					if (playerId == trapPlacerId && (currentTime - placementTime) < immunityDuration)
					{
						GD.Print($"[BananaTrap] Player {playerId} is immune to their own trap (placed {currentTime - placementTime}ms ago)");
						return;
					}
					
					// Get player role for notification
					string playerRole = player.PlayerRole;
					
					// Notify and apply slip globally
					_gameEngine.GameState.AddNotification($"A trap has been triggered! {playerRole.ToUpper()} was caught in the banana trap!");
					
					// Sync the player slip and trap removal to ALL clients
					Rpc(MethodName.SyncPlayerTrapTriggered, trapId, playerId);
					

					
					// Broadcast updated state (for notifications)
					BroadcastGameState();
				}
			};
		}

		AddChild(bananaRoot);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	public void SyncTrapTriggered(string trapId, string npcId)
	{
		GD.Print($"[SyncTrapTriggered] Trap {trapId} triggered by NPC {npcId}");
		
		// 1. Remove the visual banana trap on all clients
		var trapNode = GetNodeOrNull(trapId);
		if (trapNode != null)
		{
			trapNode.QueueFree();
			GD.Print($"[SyncTrapTriggered] Removed trap node {trapId}");
		}

		// 2. Make the NPC slip visually on all clients
		if (_npcEntities.TryGetValue(npcId, out var npc))
		{
			npc.StartSlip(3.0);
			GD.Print($"[SyncTrapTriggered] NPC {npcId} started slipping");
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	public void SyncPlayerTrapTriggered(string trapId, long playerId)
	{
		GD.Print($"[SyncPlayerTrapTriggered] Trap {trapId} triggered by Player {playerId}");
		
		// 1. Remove the visual banana trap on all clients
		var trapNode = GetNodeOrNull(trapId);
		if (trapNode != null)
		{
			trapNode.QueueFree();
			GD.Print($"[SyncPlayerTrapTriggered] Removed trap node {trapId}");
		}

		// 2. Make the player slip visually on all clients
		if (_playerControllers.TryGetValue(playerId, out var player))
		{
			player.StartSlip(3.0);
			GD.Print($"[SyncPlayerTrapTriggered] Player {playerId} started slipping");
		}
	}

	private string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

	private void OnReturnToLobbyPressed()
	{
		GD.Print("[GameWorld] Return to Lobby button pressed");
		
		if (Multiplayer.IsServer())
		{
			// Send the return signal to all connected clients first,
			// then the server closes its own peer after a short delay so the
			// RPC has time to reach clients before the connection drops.
			Rpc(MethodName.ReturnToLobbyClient);
			// Use a timer so the RPC packet is flushed before we close the peer
			var timer = GetTree().CreateTimer(0.3);
			timer.Timeout += () =>
			{
				GD.Print("[GameWorld] Server returning to lobby...");
				_networkManager.Players.Clear();
				if (Multiplayer.HasMultiplayerPeer())
				{
					Multiplayer.MultiplayerPeer.Close();
					Multiplayer.MultiplayerPeer = null;
				}
				GetTree().ChangeSceneToFile("res://scenes/Lobby.tscn");
			};
		}
		else
		{
			// Client asks server to initiate return to lobby for everyone
			RpcId(1, MethodName.RequestReturnToLobby);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
	private void RequestReturnToLobby()
	{
		if (!Multiplayer.IsServer()) return;
		GD.Print("[GameWorld] Server received request to return to lobby");
		// Reuse the same pressed handler so the timer logic is shared
		OnReturnToLobbyPressed();
	}

	// Called on all clients (not the server) to return them to the lobby.
	// Clients disconnect their own peer and change scene immediately.
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
	private void ReturnToLobbyClient()
	{
		GD.Print("[GameWorld] Client returning to lobby...");
		_networkManager.Players.Clear();
		if (Multiplayer.HasMultiplayerPeer())
		{
			Multiplayer.MultiplayerPeer.Close();
			Multiplayer.MultiplayerPeer = null;
		}
		GetTree().ChangeSceneToFile("res://scenes/Lobby.tscn");
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.N)
		{
			AddSlidingNotification("MANUAL TEST: Influence Gained!");
		}
	}

	private void ProcessInfluenceNotifications(Dictionary<string, Vector2> states)
	{
		if (string.IsNullOrEmpty(_myRole)) 
		{
			return;
		}
		if (states.Count == 0) return;

		string myRoleLower = _myRole.ToLower();
		var currentCounts = new Dictionary<string, int> { { "prophet", 0 }, { "admirer", 0 }, { "producer", 0 }, { "neutral", 0 } };

		foreach (var kvp in states)
		{
			string npcId = kvp.Key;
			Vector2 pos = kvp.Value;
			
			if (_leaderboardTriangle == null) continue;
			string zone = _leaderboardTriangle.GetInfluenceZone(pos);
			
			// Track counts
			if (currentCounts.ContainsKey(zone)) currentCounts[zone]++;

			// Check for transitions
			if (_previousNpcZones.TryGetValue(npcId, out string prevZone))
			{
				if (zone != prevZone)
				{
					string npcName = _npcEntities.TryGetValue(npcId, out var entity) ? entity.NpcName : npcId;
					
					// Local player notifications
					// if (zone == myRoleLower)
					// 	AddSlidingNotification($"YOU are influencing {npcName} now.");
					// else if (prevZone == myRoleLower)
					// 	AddSlidingNotification($"YOU have lost influence over {npcName}!");
					
					// Other player notifications
					// else if (zone != "neutral" && zone != myRoleLower)
					// 	AddSlidingNotification($"{Capitalize(zone)} has gained influence over {npcName}!");
					// else if (prevZone != "neutral" && prevZone != myRoleLower)
					// 	AddSlidingNotification($"{Capitalize(prevZone)} has lost influence over {npcName}!");
				}
			}
			_previousNpcZones[npcId] = zone;
		}

		// Check proximity to win
		foreach (var role in new[] { "prophet", "admirer", "producer" })
		{
			int currentCount = currentCounts[role];
			if (!_previousZoneCounts.ContainsKey(role)) _previousZoneCounts[role] = 0;
			int prevCount = _previousZoneCounts[role];

			if (currentCount != prevCount)
			{
				if (currentCount == WIN_THRESHOLD - 1 && prevCount < currentCount)
				{
					if (role == myRoleLower)
						AddSlidingNotification("YOU are one contestant away from winning!");
					else
						AddSlidingNotification($"{Capitalize(role)} is one contestant away from winning!");
				}
				else if (currentCount == WIN_THRESHOLD - 2 && prevCount < currentCount)
				{
					if (role == myRoleLower)
						AddSlidingNotification("YOU are two contestants away from winning!");
					else
						AddSlidingNotification($"{Capitalize(role)} is two contestants away from winning!");
				}
				_previousZoneCounts[role] = currentCount;
			}
		}

		// Server side: Check for actual win condition
		if (Multiplayer.IsServer() && _gameActive)
		{
			foreach (var role in new[] { "prophet", "admirer", "producer" })
			{
				if (currentCounts[role] >= WIN_THRESHOLD)
				{
					_gameActive = false;
					_gameEngine.GameState.Winner = Capitalize(role);
					_gameEngine.GameState.AddNotification($"GAME OVER - {Capitalize(role)} has won by influencing {WIN_THRESHOLD} contestants!");
					GetNode<MusicManager>("/root/MusicManager")?.StopMusic();
					GetNode<SfxManager>("/root/SfxManager")?.StopCountdown();
					BroadcastGameState();
					break;
				}
			}
		}
	}

	public void AddSlidingNotification(string message, double duration = 3.0)
	{
		GD.Print($"[Notification] {message}");
		
		// Position logic: Slide down from top-middle
		Vector2 viewportSize = GetViewportRect().Size;
		float itemWidth = 630; // Matching NotificationItem width
		float itemHeight = 110; // Shorter height
		
		Vector2 startPos = new Vector2((viewportSize.X - itemWidth) / 2, -itemHeight);
		
		// Calculate target Y based on active notifications (near very top)
		float targetY = 10 + (_activeNotificationItems.Count * 15);
		Vector2 targetPos = new Vector2(startPos.X, targetY);

		var item = NotificationItem.Create(message, _customFont, startPos, targetPos, duration);
		_uiLayer.AddChild(item);
		_activeNotificationItems.Add(item);
		
		item.OnFinished += (finishedItem) => {
			_activeNotificationItems.Remove(finishedItem);
		};
	}



}

// ═══════════════════════════════════════════════════════════════════════════
// Prophet Ultimate: Mass Revelation — all methods inside partial class below
// (Added as a separate block to keep diffs clean)
// ═══════════════════════════════════════════════════════════════════════════
public partial class GameWorld
{
	// ── UI Setup ──────────────────────────────────────────────────────────────
	private void SetupProphetUI()
	{
		_massRevButton = new Button();
		_massRevButton.Name = "MassRevButton";
		_massRevButton.Text = "🌀 Mass Revelation";
		_massRevButton.AddThemeFontOverride("font", _customFont);
		_massRevButton.AddThemeFontSizeOverride("font_size", 26);
		_massRevButton.Position          = new Vector2(20, 540);
		_massRevButton.CustomMinimumSize = new Vector2(240, 55);
		_massRevButton.Visible           = false; // shown only for Prophet

		// Deep blue style
		_massRevButton.AddThemeStyleboxOverride("normal",   CreateTrapStyle(new Color(0.1f, 0.3f, 0.9f, 1f), Colors.Black));
		_massRevButton.AddThemeStyleboxOverride("hover",    CreateTrapStyle(new Color(0.2f, 0.45f, 1.0f, 1f), Colors.Black));
		_massRevButton.AddThemeStyleboxOverride("pressed",  CreateTrapStyle(new Color(0.05f, 0.2f, 0.6f, 1f), Colors.Black));
		_massRevButton.AddThemeStyleboxOverride("disabled", CreateTrapStyle(new Color(0.4f, 0.4f, 0.4f, 0.8f), Colors.DarkGray));
		_massRevButton.AddThemeColorOverride("font_color",          Colors.White);
		_massRevButton.AddThemeColorOverride("font_hover_color",    Colors.White);
		_massRevButton.AddThemeColorOverride("font_pressed_color",  Colors.White);
		_massRevButton.AddThemeColorOverride("font_disabled_color", new Color(0.6f, 0.6f, 0.6f, 1f));
		_massRevButton.Pressed += OnMassRevButtonPressed;
		_uiLayer.AddChild(_massRevButton);

		_massRevLabel = new Label();
		_massRevLabel.Name = "MassRevLabel";
		_massRevLabel.AddThemeFontOverride("font", _customFont);
		_massRevLabel.AddThemeFontSizeOverride("font_size", 22);
		_massRevLabel.AddThemeColorOverride("font_color", Colors.White);
		_massRevLabel.Position = new Vector2(20, 600);
		_massRevLabel.Text     = "[Q] activate aura";
		_massRevLabel.Visible  = false;
		_uiLayer.AddChild(_massRevLabel);
	}

	private void OnMassRevButtonPressed()
	{
		if (_massRevActive || _massRevUsed) return;
		ActivateMassRevelation();
	}

	// ── Client-side activation ─────────────────────────────────────────────
	private void ActivateMassRevelation()
	{
		if (_myRole?.ToLower() != "prophet") return;
		if (_massRevActive || _massRevUsed) return;
		if (IsLocalPlayerDead()) return;

		// Resolve local player reference
		if (_localPlayer == null)
		{
			var myId = Multiplayer.GetUniqueId();
			_playerControllers.TryGetValue(myId, out _localPlayer);
		}
		if (_localPlayer == null) return;

		_massRevActive = true;
		_massRevUsed   = true;
		_massRevTimer  = MASS_REV_DURATION;

		// Slow the Prophet down
		_localPlayer.Speed = MASS_REV_PLAYER_SPD;

		if (_massRevButton != null) _massRevButton.Disabled = true;

		AddSlidingNotification("🌀 Mass Revelation activated! All contestants drawn near for 10 seconds…");
		
		// Play the actual character sprite replacement animation
		_localPlayer.PlayMassRevelationAnimation();
		GetNode<SfxManager>("/root/SfxManager")?.FadeMassRevIn();
		GetNode<MusicManager>("/root/MusicManager")?.FadeOutForMassRev(MASS_REV_FADE);
		GD.Print("[MassRev] Prophet activated Mass Revelation");

		// Build aura visual (pulsing blue circle) parented to world-space
		BuildAuraVisual();

		// Notify the server (or act directly if hosting)
		if (Multiplayer.IsServer())
			StartMassRevelationServer(Multiplayer.GetUniqueId());
		else
			RpcId(1, MethodName.RequestProphetMassRevelation);
	}

	private void BuildAuraVisual()
	{
		_massRevAuraVisual = new Node2D();
		_massRevAuraVisual.Name   = "MassRevAura";
		_massRevAuraVisual.ZIndex = -1; // behind players, above floor
		AddChild(_massRevAuraVisual);

		var visual = new Node2D();
		visual.Name = "AuraCircle";
		_massRevAuraVisual.AddChild(visual);

		visual.Draw += () =>
		{
			// Divine outer ring background (soft gold)
			visual.DrawCircle(Vector2.Zero, MASS_REV_RADIUS,
				new Color(1.0f, 0.85f, 0.2f, 0.15f));
			
			// Inner intense core layer
			visual.DrawCircle(Vector2.Zero, MASS_REV_RADIUS * 0.8f,
				new Color(1.0f, 0.95f, 0.6f, 0.2f));

			// Powerful outer ring edge
			visual.DrawArc(Vector2.Zero, MASS_REV_RADIUS, 0, Mathf.Tau, 64,
				new Color(1.0f, 0.9f, 0.1f, 0.7f), 8f);
			
			// Concentric inner line
			visual.DrawArc(Vector2.Zero, MASS_REV_RADIUS * 0.9f, 0, Mathf.Tau, 64,
				new Color(1.0f, 1.0f, 0.8f, 0.5f), 3f);
		};
		visual.QueueRedraw();

		// Fast, pulsing scale tween (0.25s up, 0.25s down = 0.5s cycle)
		var tweenScale = visual.CreateTween();
		tweenScale.SetLoops();
		tweenScale.TweenProperty(visual, "scale", new Vector2(1.06f, 1.06f), 0.25f)
			.SetTrans(Tween.TransitionType.Sine);
		tweenScale.TweenProperty(visual, "scale", Vector2.One, 0.25f)
			.SetTrans(Tween.TransitionType.Sine);

		// Subtly pulse the brightness/opacity
		var tweenColor = visual.CreateTween();
		tweenColor.SetLoops();
		tweenColor.TweenProperty(visual, "modulate", new Color(1.2f, 1.2f, 1.0f, 1.0f), 0.25f)
			.SetTrans(Tween.TransitionType.Sine);
		tweenColor.TweenProperty(visual, "modulate", new Color(1.0f, 1.0f, 1.0f, 0.7f), 0.25f)
			.SetTrans(Tween.TransitionType.Sine);

		// ── Noise-driven shader glow ──────────────────────────────────────────
		// Shader is defined inline so no file import is ever needed.
		// It draws a glowing ring shape (transparent centre + exterior) animated
		// by two opposing noise layers, using additive blending so it purely adds
		// light on top of the existing draw calls with zero background.

		var glowShader = new Shader();
		glowShader.Code = @"
shader_type canvas_item;
render_mode blend_add;

uniform sampler2D noise_tex : repeat_enable, filter_linear_mipmap;
uniform float intensity  = 2.8;
uniform float speed      = 1.2;
uniform float ring_inner = 0.70;
uniform float ring_outer = 1.00;
uniform float ring_soft  = 0.14;

void fragment() {
    vec2  uv_c = UV * 2.0 - 1.0;
    float dist = length(uv_c);

    // Ring mask: zero inside core, zero outside circle
    float inner = smoothstep(ring_inner - ring_soft, ring_inner + ring_soft, dist);
    float outer = 1.0 - smoothstep(ring_outer - ring_soft, ring_outer, dist);
    float ring  = inner * outer;

    // Two noise layers scrolling in opposite directions → swirling effect
    vec2 s1 = vec2( TIME / (1.0 + speed),  TIME / (1.5 + speed));
    vec2 s2 = vec2(-TIME / (2.0 + speed), -TIME / (1.0 + speed));
    float n  = (texture(noise_tex, UV + s1).r + texture(noise_tex, UV + s2).r) * 0.5;

    float a = clamp(n * intensity * ring, 0.0, 1.0);
    a = pow(a, 2.0);   // sharpen the glow (from reference shader)

    // Warm divine gold colour; blend_add means alpha acts as additive weight
    COLOR = vec4(1.0, 0.88, 0.25, a);
}
";

		// Procedural seamless simplex noise — no external file needed
		var fnl = new FastNoiseLite();
		fnl.NoiseType      = FastNoiseLite.NoiseTypeEnum.Simplex;
		fnl.Frequency      = 0.012f;
		fnl.FractalOctaves = 4;

		var noiseTex = new NoiseTexture2D();
		noiseTex.Width    = 256;
		noiseTex.Height   = 256;
		noiseTex.Seamless = true;
		noiseTex.Noise    = fnl;

		var glowMat = new ShaderMaterial();
		glowMat.Shader = glowShader;
		glowMat.SetShaderParameter("noise_tex",   noiseTex);
		glowMat.SetShaderParameter("intensity",   2.8f);
		glowMat.SetShaderParameter("speed",       1.2f);
		glowMat.SetShaderParameter("ring_inner",  0.70f);
		glowMat.SetShaderParameter("ring_outer",  1.00f);
		glowMat.SetShaderParameter("ring_soft",   0.14f);

		// White 1×1 texture — TEXTURE is only needed to drive UV; the shader
		// does all masking itself.  Sprite2D centres at (0,0) automatically.
		var onePixel = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
		onePixel.Fill(Colors.White);
		var whiteTex = ImageTexture.CreateFromImage(onePixel);

		// Scale the sprite so it covers exactly MASS_REV_RADIUS in every direction
		float glowScale = MASS_REV_RADIUS * 2f; // Sprite2D at scale (s,s) → s world units wide

		var glowSprite = new Sprite2D();
		glowSprite.Name     = "AuraGlowShader";
		glowSprite.Texture  = whiteTex;
		glowSprite.Scale    = new Vector2(glowScale, glowScale);
		glowSprite.Material = glowMat;
		glowSprite.ZIndex   = 1; // above the DrawCircle / DrawArc layer
		_massRevAuraVisual.AddChild(glowSprite);
	}


	// ── Per-frame prophet handler ─────────────────────────────────────────
	private void HandleProphetUltimateInput(double delta)
	{
		if (_myRole?.ToLower() != "prophet") return;
		if (IsLocalPlayerDead()) return;

		// Resolve local player reference
		if (_localPlayer == null)
		{
			var myId = Multiplayer.GetUniqueId();
			_playerControllers.TryGetValue(myId, out _localPlayer);
		}

		// Manage button/label visibility
		bool showUI = !_massRevUsed || _massRevActive;
		if (_massRevButton != null) _massRevButton.Visible = showUI;
		if (_massRevLabel  != null) _massRevLabel.Visible  = showUI;

		// One-shot Q key activation
		bool qPressed = Input.IsKeyPressed(Key.Q);
		if (qPressed && !_wasMassRevPressed && !_massRevActive && !_massRevUsed)
			ActivateMassRevelation();
		_wasMassRevPressed = qPressed;

		// Active: countdown, aura follows prophet, update label
		if (!_massRevActive) return;

		_massRevTimer -= delta;

		// Keep aura visual centred on local prophet
		if (_massRevAuraVisual != null && _localPlayer != null)
			_massRevAuraVisual.GlobalPosition = _localPlayer.GlobalPosition;

		// (non-prophet aura tracking is handled in HandleNonProphetMassRevAura)


		if (_massRevLabel != null)
			_massRevLabel.Text = $"🌀 Chanting… {_massRevTimer:F0}s";

		if (_massRevTimer <= 0)
		{
			// Aura expired — clean up locally and notify the server so it stops NPC ticks
			_massRevActive = false;
			_massRevTimer  = 0;
			EndMassRevelationLocally(false);

			// Tell the server to stop the server-side aura (NPC ticks / points)
			if (Multiplayer.IsServer())
				Rpc(MethodName.RpcEndMassRevelationAura, false, Multiplayer.GetUniqueId());   // broadcast to all peers
			else
				RpcId(1, MethodName.RequestEndMassRevelation); // client asks server
		}
	}

	// ── Per-frame: non-prophet clients keep the aura on the prophet puppet ──
	private void HandleNonProphetMassRevAura()
	{
		if (!_massRevClientAuraActive) return;
		if (_massRevAuraVisual == null || !IsInstanceValid(_massRevAuraVisual)) return;

		if (_playerControllers.TryGetValue(_massRevProphetPeerId, out var prophetPuppet))
			_massRevAuraVisual.GlobalPosition = prophetPuppet.GlobalPosition;
	}

	private void EndMassRevelationLocally(bool shattered)
	{
		// Stop the looping sprite-sheet overlay
		_localPlayer?.StopMassRevelationAnimation();

		// Restore prophet speed
		if (_localPlayer != null)
			_localPlayer.Speed = 425.0f; // default Speed

		// Remove aura visual
		if (_massRevAuraVisual != null && IsInstanceValid(_massRevAuraVisual))
		{
			_massRevAuraVisual.QueueFree();
			_massRevAuraVisual = null;
		}

		// Update label / hide UI permanently
		if (_massRevButton != null) _massRevButton.Visible = false;
		if (_massRevLabel  != null) _massRevLabel.Visible  = false;

		string msg = shattered
			? "💥 Mass Revelation shattered by a punch!"
			: "🌀 Mass Revelation ended. Contestants keep their new positions!";
		AddSlidingNotification(msg);
		GetNode<SfxManager>("/root/SfxManager")?.FadeMassRevOut(MASS_REV_FADE);
		GetNode<MusicManager>("/root/MusicManager")?.FadeInAfterMassRev(MASS_REV_FADE);
		GD.Print($"[MassRev] Ended locally (shattered={shattered})");
	}

	// ── Server-side: start ────────────────────────────────────────────────
	private void StartMassRevelationServer(long prophetPeerId)
	{
		if (!Multiplayer.IsServer()) return;
		_massRevServerActive  = true;
		_massRevProphetPeerId = prophetPeerId;
		_massRevNpcTimers.Clear();
		_massRevNpcPoints.Clear();
		_gameEngine?.GameState.AddNotification("🌀 The Prophet begins Mass Revelation! Contestants are drawn near!");
		GD.Print($"[MassRev] Server started aura for peer {prophetPeerId}");

		// Notify all clients so they can show the aura
		Rpc(MethodName.RpcNotifyMassRevelationStart, prophetPeerId);
	}

	// ── Server-side: break all dialogs for an NPC ────────────────────────
	private void BreakNpcDialog(string npcId)
	{
		if (!Multiplayer.IsServer() || _gameEngine == null) return;

		// Snapshot first to avoid modifying the dictionary while iterating it
		// (EndActiveInteraction removes entries from these collections).
		// We intentionally do NOT break the Prophet's own ActiveConversions —
		// Mass Revelation should never cancel the Prophet's own interactions.

		// Break Admirer interviews for this NPC
		var admirerMatches = _gameEngine.GameState.ActiveInterviews
			.Where(kvp => kvp.Value.NpcId == npcId)
			.Select(kvp => kvp.Key)
			.ToList();
		foreach (var role in admirerMatches)
			_gameEngine.EndActiveInteraction(role);

		// Break Producer interviews for this NPC
		var producerMatches = _gameEngine.GameState.ActiveProducerInterviews
			.Where(kvp => kvp.Value.NpcId == npcId)
			.Select(kvp => kvp.Key)
			.ToList();
		foreach (var role in producerMatches)
			_gameEngine.EndActiveInteraction(role);

		// NOTE: We deliberately skip ActiveConversions (Prophet) here.
		// The Prophet's own conversion with this NPC must NOT be interrupted
		// by Mass Revelation's NPC-pull loop.
	}

	// ── Server-side: shatter ───────────────────────────────────────────────
	private void ShatterMassRevelationServer()
	{
		if (!Multiplayer.IsServer()) return;
		_massRevServerActive  = false;
		_massRevProphetPeerId = 0;
		// Clear all NPC march targets
		foreach (var npcEnt in _npcEntities.Values)
			npcEnt.ClearMarchTarget();
		_gameEngine?.GameState.AddNotification("💥 Mass Revelation shattered!");
		GD.Print("[MassRev] Server: aura shattered by punch.");
		Rpc(MethodName.RpcEndMassRevelationAura, true, _massRevProphetPeerId);
	}

	// ── RPC: client requests server to start ─────────────────────────────
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestProphetMassRevelation()
	{
		if (!Multiplayer.IsServer()) return;
		long senderId = Multiplayer.GetRemoteSenderId();
		if (!_networkManager.Players.TryGetValue(senderId, out var info)) return;
		if (!string.Equals(info.Role, "Prophet", StringComparison.OrdinalIgnoreCase)) return;

		var state = _gameEngine?.GameState.GetPlayerState(Role.Prophet);
		if (state != null && !state.Alive) return;

		StartMassRevelationServer(senderId);
	}

	// ── RPC: Prophet client notifies server that aura expired naturally ───
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestEndMassRevelation()
	{
		if (!Multiplayer.IsServer()) return;
		long senderId = Multiplayer.GetRemoteSenderId();
		if (!_networkManager.Players.TryGetValue(senderId, out var info)) return;
		if (!string.Equals(info.Role, "Prophet", StringComparison.OrdinalIgnoreCase)) return;
		if (!_massRevServerActive) return; // already ended

		GD.Print("[MassRev] Server: Prophet client reported natural expiry — ending server aura.");
		_massRevServerActive  = false;
		long endingProphetId  = _massRevProphetPeerId; // capture before zeroing
		_massRevProphetPeerId = 0;
		_massRevNpcTimers.Clear();
		_massRevNpcPoints.Clear();
		foreach (var npcEnt in _npcEntities.Values)
			npcEnt.ClearMarchTarget();
		// Broadcast the end to all other clients, passing prophetPeerId so they know whose animation to stop
		Rpc(MethodName.RpcEndMassRevelationAura, false, endingProphetId);
	}

	// ── RPC: all clients receive notification that aura started ──────────
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcNotifyMassRevelationStart(long prophetPeerId)
	{
		// Skip on the prophet's own machine — ActivateMassRevelation already handled it.
		if (_myRole?.ToLower() == "prophet") return;
		// All non-prophet clients now show the full animation + aura on the prophet puppet.
		AddSlidingNotification("🌀 The Prophet is channeling Mass Revelation!");

		// Store prophet peer so the aura tracker in _Process can follow the puppet.
		_massRevProphetPeerId    = prophetPeerId;
		_massRevClientAuraActive = true;

		// Play the Mass Revelation sounds on non-prophet screens too.
		GetNode<SfxManager>("/root/SfxManager")?.FadeMassRevIn();
		GetNode<MusicManager>("/root/MusicManager")?.FadeOutForMassRev(MASS_REV_FADE);

		// Find the prophet's puppet PlayerController and play the animation on it.
		if (_playerControllers.TryGetValue(prophetPeerId, out var prophetPuppet))
		{
			prophetPuppet.PlayMassRevelationAnimation();
		}

		// Build the aura visual centred on the prophet puppet (initial position).
		if (_massRevAuraVisual == null)
		{
			BuildAuraVisual();
			if (_playerControllers.TryGetValue(prophetPeerId, out var pc))
				_massRevAuraVisual.GlobalPosition = pc.GlobalPosition;
		}
	}

	// ── RPC: server broadcasts aura end to all peers ──────────────────────
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true,
		TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcEndMassRevelationAura(bool shattered, long prophetPeerId)
	{
		if (_myRole?.ToLower() == "prophet")
		{
			// Prophet client: stop active state and clean up
			_massRevActive = false;
			EndMassRevelationLocally(shattered);
		}
		else
		{
			// Non-prophet clients: stop the overlay on the prophet puppet.
			// Use the prophetPeerId passed in the RPC — don't rely on _massRevProphetPeerId
			// which may already be zeroed on the server before the RPC fires.
			long peerId = prophetPeerId != 0 ? prophetPeerId : _massRevProphetPeerId;
			if (peerId != 0 && _playerControllers.TryGetValue(peerId, out var prophetPuppet2))
				prophetPuppet2.StopMassRevelationAnimation();

			// Fade out the sounds that were started in RpcNotifyMassRevelationStart.
			GetNode<SfxManager>("/root/SfxManager")?.FadeMassRevOut(MASS_REV_FADE);
			GetNode<MusicManager>("/root/MusicManager")?.FadeInAfterMassRev(MASS_REV_FADE);

			// Remove our copy of the aura visual and show notification
			_massRevClientAuraActive = false;
			_massRevProphetPeerId    = 0;
			if (_massRevAuraVisual != null && IsInstanceValid(_massRevAuraVisual))
			{
				_massRevAuraVisual.QueueFree();
				_massRevAuraVisual = null;
			}
			string msg = shattered
				? "💥 Mass Revelation was shattered!"
				: "🌀 Mass Revelation ended.";
			AddSlidingNotification(msg);
		}

		// Server also clears its state here (CallLocal = true means server runs this too)
		if (Multiplayer.IsServer())
		{
			_massRevServerActive  = false;
			_massRevProphetPeerId = 0;
			_massRevNpcTimers.Clear();
			_massRevNpcPoints.Clear();
			foreach (var npcEnt in _npcEntities.Values)
				npcEnt.ClearMarchTarget();
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	private void RpcShowPlayerScoreFeedback(long playerId, string roleStr, int score)
	{
		if (_isGameOver || HasNode("GameOverOverlay")) return;

		string roleLower = roleStr.ToLower();
		
		if (roleLower == "witness")
		{
			// Special case: just a floating red "-" sign for witnessing stabs
			if (_playerControllers.TryGetValue(playerId, out var witnessPlayer))
			{
				var label = new Label();
				label.Text = "-";
				label.AddThemeFontOverride("font", _customFont);
				label.AddThemeFontSizeOverride("font_size", 48);
				label.AddThemeColorOverride("font_color", Colors.Red);
				label.AddThemeConstantOverride("outline_size", 4);
				label.AddThemeColorOverride("font_outline_color", Colors.Black);
				label.ZIndex = 100;

				// Position floating above Player puppet
				label.Position = witnessPlayer.Position - new Vector2(10, 80);
				AddChild(label);

				var tween = CreateTween();
				tween.SetParallel(true);
				Vector2 targetPos = label.Position - new Vector2(0, 100);
				tween.TweenProperty(label, "position", targetPos, 1.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
				tween.TweenProperty(label, "modulate", new Color(1, 1, 1, 0), 1.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
				tween.Chain().TweenCallback(OfCallable(() => label.QueueFree()));
			}
			return;
		}

		string assetName = score >= 1 ? $"ai_{roleLower}_plusone.png" : $"ai_{roleLower}_minusone.png";
		string path = $"res://assets/{assetName}";
		var texture = ResourceLoader.Load<Texture2D>(path);

		if (texture != null && _playerControllers.TryGetValue(playerId, out var player))
		{
			var floatingRect = new TextureRect();
			floatingRect.Texture = texture;
			floatingRect.ExpandMode = TextureRect.ExpandModeEnum.KeepSize;
			floatingRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			floatingRect.ZIndex = 100;
			floatingRect.Scale = new Vector2(0.05f, 0.05f); // smaller

			Vector2 texSize = texture.GetSize() * floatingRect.Scale;
			// Position floating above Player
			floatingRect.Position = player.Position - new Vector2(texSize.X / 2, 80);
			
			AddChild(floatingRect);

			var tween = CreateTween();
			tween.SetParallel(true);
			Vector2 targetPos = floatingRect.Position - new Vector2(0, 100);
			tween.TweenProperty(floatingRect, "position", targetPos, 1.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
			tween.TweenProperty(floatingRect, "modulate", new Color(1, 1, 1, 0), 1.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
			tween.Chain().TweenCallback(Callable.From(() => floatingRect.QueueFree()));
		}
	}
}
