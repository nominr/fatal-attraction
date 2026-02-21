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
	private Label _roleLabel;
	private Label _convertedLabel;
	private VBoxContainer _metersContainer;
	private RichTextLabel _notificationText;
	private MoneyGameOverlay _moneyGameOverlay;
	private PanelContainer _notificationPanel;
	private Button _collapseNotificationButton;
	private bool _notificationCollapsed = false;
	private MarginContainer _notificationMargin;
	private Vector2 _notificationPanelExpandedPosition;
	private int _lastNotificationCount = 0; // Track which notifications have been displayed

	// Punch logic
	private const float PUNCH_RANGE = 120f;
	private bool _wasPunchPressed = false;


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
	
	// +1 Rating Visual Feedback
	private double _previousProducerRating = 0;
	private CenterContainer _plusOneOverlay;
	private double _plusOneTimer = 0;
	private Texture2D _plusOneTexture;

	// Global Influence Counter
	private PanelContainer _globalInfluencePanel;
	private RichTextLabel _globalInfluenceLabel;

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
			isoMapNode.ZIndex = -10;
			GD.Print($"[Isometric] IsometricWorldMap scaled to {isoMapNode.Scale} and positioned at {isoMapNode.Position}");
			
			// Set z-index on each TileMapLayer child so they render below players/NPCs (z=0)
			int layerZIndex = -10;
			foreach (var child in isoMapNode.GetChildren())
			{
				if (child is Node2D childLayer)
				{
					childLayer.ZIndex = layerZIndex;
					layerZIndex++;
					GD.Print($"[Isometric] Set ZIndex={childLayer.ZIndex} on {childLayer.Name}");
				}
			}
			
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

		// HUD Container
		var hudContainer = new VBoxContainer();
		hudContainer.Position = new Vector2(20, 20);
		hudContainer.AddThemeConstantOverride("separation", 5);
		// Add transparent grey background to HUD
		var hudPanel = new PanelContainer();
		hudPanel.Position = new Vector2(20, 20);
		// Ensure HUD doesn't block clicks in empty areas, but let buttons inside work
		hudPanel.MouseFilter = Control.MouseFilterEnum.Pass;
		
		var hudBgStyle = new StyleBoxEmpty();
		hudPanel.AddThemeStyleboxOverride("panel", hudBgStyle);
		hudPanel.AddChild(hudContainer);
		_uiLayer.AddChild(hudPanel);

		_timerLabel = new Label();
		_timerLabel.Text = "05:00";
		_timerLabel.AddThemeFontOverride("font", _customFont);
		_timerLabel.AddThemeFontSizeOverride("font_size", 48);
		_timerLabel.AddThemeColorOverride("font_color", Colors.White);
		// Black outline around the text
		_timerLabel.AddThemeConstantOverride("outline_size", 6);
		_timerLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
		_timerLabel.HorizontalAlignment = HorizontalAlignment.Center;
		// Center the timer at the top of the screen
		_timerLabel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
		_timerLabel.GrowHorizontal = Control.GrowDirection.Both;
		_timerLabel.OffsetLeft = -150;
		_timerLabel.OffsetRight = 150;
		_timerLabel.OffsetTop = 15;
		_uiLayer.AddChild(_timerLabel);
		
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

		_convertedLabel = new Label();
		_convertedLabel.Text = "Converted: 0";
		_convertedLabel.AddThemeFontOverride("font", _customFont);
		_convertedLabel.AddThemeFontSizeOverride("font_size", 26);
		_convertedLabel.AddThemeColorOverride("font_color", Colors.White);
		_convertedLabel.Visible = false; // Only relevant for Prophet
		_bottomLeftStatusContainer.AddChild(_convertedLabel);

		// _metersContainer removed

		// Global Influence Counter
		_globalInfluencePanel = new PanelContainer();
		// Ensure panel shrinks to fit content exactly with no extra width
		_globalInfluencePanel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; 
		_globalInfluencePanel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		
		var globalCounterStyle = new StyleBoxFlat();
		globalCounterStyle.BgColor = new Color(1.0f, 1.0f, 1.0f, 0.9f); // White background
		globalCounterStyle.SetCornerRadiusAll(4);
		// Minimal padding to fit tightly
		globalCounterStyle.SetContentMarginAll(4);
		_globalInfluencePanel.AddThemeStyleboxOverride("panel", globalCounterStyle);
		
		hudContainer.AddChild(_globalInfluencePanel);

		_globalInfluenceLabel = new RichTextLabel();
		_globalInfluenceLabel.BbcodeEnabled = true;
		_globalInfluenceLabel.FitContent = true;
		_globalInfluenceLabel.ScrollActive = false;
		_globalInfluenceLabel.AutowrapMode = TextServer.AutowrapMode.Off; // Prevent wrapping adding width
		// Remove custom min size to allow shrinking
		_globalInfluenceLabel.CustomMinimumSize = Vector2.Zero;
		_globalInfluenceLabel.AddThemeFontOverride("normal_font", _customFont);
		_globalInfluenceLabel.AddThemeFontSizeOverride("normal_font_size", 24); 
		
		// Default color
		_globalInfluenceLabel.AddThemeColorOverride("default_color", Colors.Black);
		
		_globalInfluencePanel.AddChild(_globalInfluenceLabel);

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

		_moneyGameOverlay = new MoneyGameOverlay();
		_moneyGameOverlay.ActionSelected += OnMoneyGameAction;
		_uiLayer.AddChild(_moneyGameOverlay);

		// +1 Rating Visual Feedback Overlay
		_plusOneTexture = ResourceLoader.Load<Texture2D>("res://assets/ai_plusone-nobg.png");
		if (_plusOneTexture == null)
		{
			GD.PrintErr("[GameWorld] Failed to load ai_plusone-nobg.png texture");
		}
		
		_plusOneOverlay = new CenterContainer();
		_plusOneOverlay.Name = "PlusOneOverlay";
		_plusOneOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_plusOneOverlay.MouseFilter = Control.MouseFilterEnum.Stop; // Block clicks during display
		_plusOneOverlay.Visible = false;
		_plusOneOverlay.ZIndex = 100; // Above everything
		
		var plusOneTexRect = new TextureRect();
		plusOneTexRect.Name = "PlusOneImage";
		plusOneTexRect.Texture = _plusOneTexture;
		plusOneTexRect.ExpandMode = TextureRect.ExpandModeEnum.KeepSize;
		plusOneTexRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		_plusOneOverlay.AddChild(plusOneTexRect);
		
		_uiLayer.AddChild(_plusOneOverlay);

		// Producer UI Elements
		SetupProducerUI();
		
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

	private void SetupProducerUI()
	{
		
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
		_timeRemaining = 50.0;

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

		foreach (var npc in _gameEngine.GameState.NPCs.Values)
		{
			var entity = new NPCEntity();
			entity.NpcId = npc.Id;
			entity.NpcName = npc.Name;
			entity.NpcColor = npcColors.GetValueOrDefault(npc.Id, Colors.Blue);
			entity.Position = FindFreeNPCSpawnPosition();
			entity.NPCClicked += OnNPCClicked;
			AddChild(entity);
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

		int idx = 0;
		foreach (var kvp in _networkManager.Players)
		{
			var player = new PlayerController();
			player.Name = $"Player_{kvp.Key}"; // Set unique name for debugging
			player.Position = startPositions[idx % startPositions.Length];
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
			
			GD.Print($"[GameWorld] Spawning player {kvp.Key} as {kvp.Value.Role}, sprite={player.PlayerIndex}, isLocal={isLocal}, pos={player.Position}");
			
			// Add child first so _Ready() gets called and camera is created
			AddChild(player);
			
			// Then set local player status (camera must exist first)
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
		}
	}

	private void OnNPCClicked(string npcId)
	{
		if (IsLocalPlayerDead())
		{
			GD.Print($"[GameWorld] Dead player tried to interact with NPC {npcId}, ignoring.");
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
	}

	private void OnActionSelected(string npcId, string actionId)
	{
		RpcId(1, MethodName.SubmitAction, npcId, actionId);
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

	private void OnMoneyGameAction(string actionId)
	{
		if (_localGameState == null) return;
		var moneyGames = _localGameState["active_money_games"] as JObject;
		if (moneyGames != null && moneyGames.ContainsKey(_myRole))
		{
			string npcId = moneyGames[_myRole]["npcId"]?.Value<string>();
			if (!string.IsNullOrEmpty(npcId))
			{
				_networkManager.SendInteract(npcId, actionId);
			}
		}
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
	private void PunchTargetNPC(string targetNpcId)
	{
		if (!Multiplayer.IsServer()) return;
		if (_gameEngine == null) return;

		var npc = _gameEngine.GameState.GetNPC(targetNpcId);
		if (npc == null || !npc.IsTarget || !npc.Alive) return;

		long senderId = Multiplayer.GetRemoteSenderId();
		if (senderId == 0) senderId = Multiplayer.GetUniqueId();

		if (!_networkManager.Players.TryGetValue(senderId, out var playerInfo)) return;
		if (!string.Equals(playerInfo.Role, "Admirer", StringComparison.OrdinalIgnoreCase)) return;

		// Check if Admirer is dead (server-side verification)
		var admirerState = _gameEngine.GameState.GetPlayerState(Role.Admirer);
		if (admirerState != null && !admirerState.Alive) return;

		if (!_playerControllers.TryGetValue(senderId, out var playerCtrl)) return;
		if (!_npcEntities.TryGetValue(targetNpcId, out var npcEntity)) return;

		float distance = playerCtrl.Position.DistanceTo(npcEntity.Position);
		if (distance > PUNCH_RANGE) return;

		Rpc(MethodName.RpcFlashNpcDamage, targetNpcId);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	private void RpcFlashNpcDamage(string npcId)
	{
		if (_npcEntities.TryGetValue(npcId, out var npcEntity))
		{
			npcEntity.FlashDamage(0.5);
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

		string myRoleLower = _myRole.ToLower();

		// ADMIRER: Update NPC punch hints
		if (myRoleLower == "admirer")
		{
			foreach (var kvp in _npcEntities)
			{
				if (!TryGetNpcTargetState(kvp.Key, out bool isTarget, out bool alive))
				{
					kvp.Value.SetPunchHintVisible(false);
					continue;
				}

				if (!isTarget || !alive)
				{
					kvp.Value.SetPunchHintVisible(false);
					continue;
				}

				float distance = _localPlayer.Position.DistanceTo(kvp.Value.Position);
				bool inRange = distance <= PUNCH_RANGE;
				kvp.Value.SetPunchHintVisible(inRange);
			}
		}
		else
		{
			// Non-Adimirer: hide all NPC punch hints
			foreach (var kvp in _npcEntities)
			{
				kvp.Value.SetPunchHintVisible(false);
			}
		}

	}

	private void HandlePunchInput()
	{
		bool pressed = Input.IsKeyPressed(Key.P);
		
		// All roles can punch now
		if (string.IsNullOrEmpty(_myRole))
		{
			_wasPunchPressed = pressed;
			return;
		}

		// Don't allow dead players to punch
		if (IsLocalPlayerDead())
		{
			_wasPunchPressed = pressed;
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
			_wasPunchPressed = pressed;
			return;
		}

		if (pressed && !_wasPunchPressed)
		{
			string closestNpcId = null;
			long closestPlayerId = -1;
			float closestDistance = float.MaxValue;

			// ADMIRER: Can punch target NPCs and Prophet/Producer
			if (_myRole.ToLower() == "admirer")
			{
				// Check NPCs
				foreach (var kvp in _npcEntities)
				{
					if (!TryGetNpcTargetState(kvp.Key, out bool isTarget, out bool alive)) continue;
					if (!isTarget || !alive) continue;

					float distance = _localPlayer.Position.DistanceTo(kvp.Value.Position);
					if (distance <= PUNCH_RANGE && distance < closestDistance)
					{
						closestDistance = distance;
						closestNpcId = kvp.Key;
						closestPlayerId = -1; // Reset player target
					}
				}

				// Check Players (Prophet and Producer only)
				foreach (var kvp in _playerControllers)
				{
					long playerId = kvp.Key;
					var playerCtrl = kvp.Value;

					// Skip self
					if (playerId == Multiplayer.GetUniqueId()) continue;

					// Check if target is Prophet or Producer
					if (!_networkManager.Players.TryGetValue(playerId, out var playerInfo)) continue;
					string role = playerInfo.Role;
					if (!string.Equals(role, "Prophet", StringComparison.OrdinalIgnoreCase) && 
						!string.Equals(role, "Producer", StringComparison.OrdinalIgnoreCase)) continue;

					// Skip dead players (check cached player states)
					var playerStates = _localGameState?["player_states"] as JObject;
					if (playerStates != null)
					{
						var targetState = playerStates[role];
						bool targetAlive = targetState?["alive"]?.Value<bool>() ?? true;
						if (!targetAlive) continue;
					}

					float distance = _localPlayer.Position.DistanceTo(playerCtrl.Position);
					if (distance <= PUNCH_RANGE && distance < closestDistance)
					{
						closestDistance = distance;
						closestPlayerId = playerId;
						closestNpcId = null; // Reset NPC target
					}
				}
			}
			// PROPHET/PRODUCER: Can punch other players
			else if (_myRole.ToLower() == "prophet" || _myRole.ToLower() == "producer")
			{
				// Check Players (any other role)
				foreach (var kvp in _playerControllers)
				{
					long playerId = kvp.Key;
					var playerCtrl = kvp.Value;

					// Skip self
					if (playerId == Multiplayer.GetUniqueId()) continue;

					// Get target role
					if (!_networkManager.Players.TryGetValue(playerId, out var playerInfo)) continue;
					string role = playerInfo.Role;

					// Skip dead players (check cached player states)
					var playerStates = _localGameState?["player_states"] as JObject;
					if (playerStates != null)
					{
						var targetState = playerStates[role];
						bool targetAlive = targetState?["alive"]?.Value<bool>() ?? true;
						if (!targetAlive) continue;
					}

					float distance = _localPlayer.Position.DistanceTo(playerCtrl.Position);
					if (distance <= PUNCH_RANGE && distance < closestDistance)
					{
						closestDistance = distance;
						closestPlayerId = playerId;
					}
				}
			}

			// Punch the closest target (player or NPC)
			if (closestPlayerId != -1)
			{
				RpcId(1, MethodName.PunchPlayer, closestPlayerId);
			}
			else if (!string.IsNullOrEmpty(closestNpcId))
			{
				RpcId(1, MethodName.PunchTargetNPC, closestNpcId);
			}
		}

		_wasPunchPressed = pressed;
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
		// We need to find the player ID associated with this role
		long targetPlayerId = -1;
		
		foreach (var kvp in _networkManager.Players)
		{
			GD.Print($"[GameWorldDebug] Checking player {kvp.Key} with role {kvp.Value.Role} against {role}");
			if (string.Equals(kvp.Value.Role, role.ToString(), StringComparison.OrdinalIgnoreCase))
			{
				targetPlayerId = kvp.Key;
				break;
			}
		}
		
		if (targetPlayerId != -1)
		{
			// Send RPC to the specific client
			GD.Print($"[GameWorld] Sending ClientShowScoreFeedback RPC to {targetPlayerId}");
			RpcId(targetPlayerId, MethodName.ClientShowScoreFeedback, score);
		}
		else
		{
			GD.Print($"[GameWorld] ERROR: Could not find player for role {role}");
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	private void ClientShowScoreFeedback(int score)
	{
		// Called on Client
		GD.Print($"[GameWorld] Received Score Feedback RPC: {score} on peer {Multiplayer.GetUniqueId()}");
		
		string assetName = null;
		
		if (score >= 2) assetName = "ai_plustwo.png";
		else if (score == 1) assetName = "ai_plusone-nobg.png";
		else if (score == 0) assetName = "ai_pluszero.png"; // Assuming this exists or needed
		else if (score == -1) assetName = "ai_minusone.png";
		else if (score <= -2) assetName = "ai_minustwo.png";
		
		if (assetName != null)
		{
			ShowScoreFeedback(assetName);
		}
		else
		{
			GD.Print($"[GameWorld] No asset defined for score {score}");
		}
	}
	
	private void ShowScoreFeedback(string assetName)
	{
		if (_plusOneOverlay == null)
		{
			GD.PrintErr("[GameWorld] _plusOneOverlay is null!");
			return;
		}
		
		string path = $"res://assets/{assetName}";
		GD.Print($"[GameWorld] Loading score asset: {path}");
		var texture = ResourceLoader.Load<Texture2D>(path);
		
		if (texture != null)
		{
			var texRect = _plusOneOverlay.GetNodeOrNull<TextureRect>("PlusOneImage");
			if (texRect != null)
			{
				texRect.Texture = texture;
				GD.Print($"[GameWorld] Set texture on PlusOneImage. Making visible.");
			}
			else
			{
				GD.PrintErr("[GameWorld] PlusOneImage TextureRect not found in overlay!");
			}

			_plusOneOverlay.Visible = true;
			_plusOneTimer = 2.0f; // Show for 2 seconds
			
			// Optional: Add a tween or animation for pop effect
			var tween = CreateTween();
			_plusOneOverlay.Scale = Vector2.Zero;
			tween.TweenProperty(_plusOneOverlay, "scale", Vector2.One, 0.3f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		}
		else
		{
			GD.PrintErr($"[GameWorld] Failed to load score asset: {path}");
		}
	}

	public override void _Process(double delta)
	{
		// Handle +1 Rating Visual Feedback Timer
		if (_plusOneTimer > 0)
		{
			_plusOneTimer -= delta;
			if (_plusOneTimer <= 0 && _plusOneOverlay != null)
			{
				_plusOneOverlay.Visible = false;
				// GD.Print("[GameWorld] Hiding +1 rating visual feedback");
			}
		}
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
				
				BroadcastGameState();
				return;
			}
			
			// Update Game Engine (Traps, etc.)
			_gameEngine.Update(delta);
			
			_timeRemaining -= delta;
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
					_npcDialogueUI.Close();
					OnInteractionPanelClosed(); // Ensure we notify server to clear state
				}
			}
		}
		

		// Check Producer Panels Auto-Close on Move
		CheckProducerPanelsOnMove();

		// Admirer punch hints and input handling
		UpdatePunchHints();
		HandlePunchInput();
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
			// Check Money Game
			var moneyGames = _localGameState["active_money_games"] as JObject;
			if (moneyGames != null && moneyGames.ContainsKey(_myRole))
			{
				var ctx = moneyGames[_myRole];
				int target = ctx["target"]?.Value<int>() ?? 0;
				int current = ctx["current"]?.Value<int>() ?? 0;
				
				_moneyGameOverlay.UpdateState(target, current);
				_moneyGameOverlay.ShowGame();
			}
			else
			{
				_moneyGameOverlay.HideGame();
			}
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

		// Active Money Games (for UI)
		var moneyGames = new JObject();
		foreach (var kvp in _gameEngine.GameState.ActiveMoneyGames)
		{
			var ctx = kvp.Value;
			moneyGames[kvp.Key.ToString()] = new JObject
			{
				{ "npcId", ctx.NpcId },
				{ "target", ctx.TargetSum },
				{ "current", ctx.CurrentSum }
			};
		}
		status["active_money_games"] = moneyGames;

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
				
				// Update Global Influence Counter
				if (_globalInfluenceLabel != null)
				{
					int admirerCount = _leaderboardTriangle.AdmirerZoneCount;
					int prophetCount = _leaderboardTriangle.ProphetZoneCount;
					int producerCount = _leaderboardTriangle.ProducerZoneCount;
					
					// Counter: Black, Names: Colored
					// "Counter:" in Black
					string text = $"[color=black]Counter:[/color] [color=#cc0000]{admirerCount}[/color], [color=#0044cc]{prophetCount}[/color], [color=#00bb00]{producerCount}[/color]";
					_globalInfluenceLabel.Text = text;
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
		bool isInInterview = false;
		string interviewNpcId = null;
		
		if (!string.IsNullOrEmpty(_myRole) && activeInterviews != null)
		{
			string roleKey = Capitalize(_myRole); 
			if (activeInterviews.ContainsKey(roleKey))
			{
				var interviewInfo = activeInterviews[roleKey];
				string nId = interviewInfo["npcId"]?.Value<string>();
				if (!string.IsNullOrEmpty(nId))
				{
					isInInterview = true;
					interviewNpcId = nId;
				}
			}
		}

		if (isInInterview && interviewNpcId != null)
		{
			// If panel is closed or showing wrong NPC, force it open/correct
			if (!_npcDialogueUI.Visible || _currentInteractingNpcId != interviewNpcId)
			{
				_currentInteractingNpcId = interviewNpcId;
				RefreshInteractionPanel(); 
			}
			// Don't refresh every frame - InteractionPanel now caches and checks if rebuild is needed
		}
		else if (_npcDialogueUI.Visible && !string.IsNullOrEmpty(_currentInteractingNpcId))
		{
			// Only refresh if game state actually changed
			int currentHash = _localGameState?.GetHashCode() ?? 0;
			if (currentHash != _lastGameStateHash)
			{
				_lastGameStateHash = currentHash;
				RefreshInteractionPanel();
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

		// MONEY GAME UI OVERRIDE
		var moneyGames = _localGameState?["active_money_games"] as JObject;
		if (moneyGames != null && !string.IsNullOrEmpty(_myRole) && moneyGames.ContainsKey(_myRole))
		{
			var ctx = moneyGames[_myRole];
			string mNpcId = ctx["npcId"]?.Value<string>();
			if (mNpcId == npcId)
			{
				int target = ctx["target"]?.Value<int>() ?? 0;
				desc = $"{npcName} requests [b]{target} coins[/b].";
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
			entity.Position = initPos;
			// Initial sync for interpolation
			entity.SyncPosition(initPos);

			entity.NPCClicked += OnNPCClicked;
			AddChild(entity);
			_npcEntities[npcId] = entity;
		}
	}

	private void UpdateUI()
	{
		if (_localGameState == null) return;

		// Timer
		double time = _localGameState["time_remaining"]?.Value<double>() ?? 0;
		TimeSpan ts = TimeSpan.FromSeconds(time);
		_timerLabel.Text = $"{ts.Minutes:D2}:{ts.Seconds:D2}";

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

		// Update Trap Button Visibility (Prophet only)
		if (_uiLayer.GetNodeOrNull<Button>("TrapButton") is Button trapBtn)
		{
			// trapBtn.Visible = isProphet; // Set Trap disabled per request
			trapBtn.Visible = false;
		}

		// Update bottom-left status panel visibility
		if (_bottomLeftStatusPanel != null)
		{
			// Show panel if any of the role labels inside are visible
			_bottomLeftStatusPanel.Visible = _convertedLabel.Visible;
		}

		// Prophet conversion progress
		if (_convertedLabel != null)
		{
			var npcStates = _localGameState?["npc_states"] as JObject;
			int convertedCount = npcStates?.Properties()
				.Where(p => p.Value["converted"]?.Value<bool>() == true)
				.Count() ?? 0;

			_convertedLabel.Text = $"Converted: {convertedCount}";
			_convertedLabel.Visible = isProphet;
		}


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
		if (_npcDialogueUI.Visible && !string.IsNullOrEmpty(_currentInteractingNpcId))
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
							if (_plusOneTimer <= 0)
							{
								if (_plusOneOverlay != null)
								{
									_plusOneOverlay.Visible = true;
									_plusOneTimer = 1.0; // Display for 1 second
									
									// If distinct +2 asset existed, we would select it here. 
									// For now, reuse +1 or just show the feedback.
									GD.Print("[GameWorld] ✅ Showing rating increase visual feedback!");
								}
							}
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

			string winner = _localGameState["winner"]?.Value<string>();
			string winText = !string.IsNullOrEmpty(winner) ? $"{winner.ToUpper()} WINS!" : "GAME OVER";
			
			// 2. BLOCK UI CLICKS & SHOW OVERLAY
			if (!HasNode("GameOverOverlay"))
			{
				// Full screen blocking rect with title background
				var overlay = new TextureRect();
				overlay.Name = "GameOverOverlay";
				overlay.Texture = ResourceLoader.Load<Texture2D>("res://assets/Title_BG_1.png");
				overlay.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
				overlay.StretchMode = TextureRect.StretchModeEnum.Scale;
				overlay.Size = _worldSize * 1.2f; // Cover entire world (20% bigger)
				overlay.MouseFilter = Control.MouseFilterEnum.Stop; // BLOCK ALL CLICKS
				overlay.ZIndex = 99; // Above everything else
				_uiLayer.AddChild(overlay);

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
				label.ZIndex = 100;
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
				lobbyButton.ZIndex = 100;
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
		player.Position = startPositions[idx];
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

		AddChild(player);
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
		// This is called when the LOCAL player moves on this machine
		GD.Print($"[GameWorld] OnPlayerPositionChanged: Player {playerId} at {position}, IsServer={Multiplayer.IsServer()}");
		
		// Send the position update to all other peers
		if (Multiplayer.IsServer())
		{
			// Server: Update own position locally and broadcast to all clients
			if (_playerControllers.TryGetValue(playerId, out var controller))
			{
				controller.Position = position; // Update server's own position
				GD.Print($"[GameWorld] Server updated own position for player {playerId}");
			}
			// Broadcast to all clients
			GD.Print($"[GameWorld] Server broadcasting position to all clients");
			Rpc(MethodName.SyncPlayerPosition, playerId, position);
		}
		else
		{
			// Client: Send position to server, server will broadcast to everyone
			GD.Print($"[GameWorld] Client sending position to server");
			RpcId(1, MethodName.SendPlayerPosition, playerId, position);
		}
	}

	// Called by clients to send their position to the server
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	private void SendPlayerPosition(long playerId, Vector2 position)
	{
		if (!Multiplayer.IsServer()) return;
		
		GD.Print($"[GameWorld] Server received position from client: Player {playerId} at {position}");
		
		// Server received position from a client
		// Update the position on the server
		if (_playerControllers.TryGetValue(playerId, out var controller))
		{
			controller.Position = position; // Directly set position (not UpdateRemotePosition)
			GD.Print($"[GameWorld] Server updated remote player {playerId} position");
		}
		else
		{
			GD.PrintErr($"[GameWorld] Server couldn't find controller for player {playerId}");
		}
		
		// Broadcast to ALL clients
		GD.Print($"[GameWorld] Server broadcasting to all clients");
		Rpc(MethodName.SyncPlayerPosition, playerId, position);
	}

	// Called by the server to sync player position to all clients
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	private void SyncPlayerPosition(long playerId, Vector2 position)
	{
		// This is received by all clients (not the server)
		GD.Print($"[GameWorld] SyncPlayerPosition RPC received on peer {Multiplayer.GetUniqueId()}: Player {playerId} at {position}");
		GD.Print($"[GameWorld] Current player controllers: {_playerControllers.Count}, Keys: [{string.Join(", ", _playerControllers.Keys)}]");
		
		// Update the player controller's position for remote players
		if (_playerControllers.TryGetValue(playerId, out var controller))
		{
			GD.Print($"[GameWorld] Client updating remote player {playerId}, isLocal={controller.Name}");
			controller.UpdateRemotePosition(position);
		}
		else
		{
			GD.PrintErr($"[GameWorld] Client couldn't find controller for player {playerId}");
			GD.Print($"[GameWorld] Available players: {string.Join(", ", _playerControllers.Keys)}");
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
			// Server tells all clients to return to lobby, then returns itself
			Rpc(MethodName.ReturnToLobby);
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
		// Server broadcasts to all clients (including itself via CallLocal in ReturnToLobby)
		Rpc(MethodName.ReturnToLobby);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	private void ReturnToLobby()
	{
		GD.Print("[GameWorld] Returning to lobby...");
		
		// Disconnect all peers and reset network state
		if (Multiplayer.HasMultiplayerPeer())
		{
			Multiplayer.MultiplayerPeer.Close();
			Multiplayer.MultiplayerPeer = null;
		}
		
		// Clear network manager state
		_networkManager.Players.Clear();
		
		// Change scene to lobby
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
					if (zone == myRoleLower)
						AddSlidingNotification($"YOU are influencing {npcName} now.");
					else if (prevZone == myRoleLower)
						AddSlidingNotification($"YOU have lost influence over {npcName}!");
					
					// Other player notifications
					else if (zone != "neutral" && zone != myRoleLower)
						AddSlidingNotification($"{Capitalize(zone)} has gained influence over {npcName}!");
					else if (prevZone != "neutral" && prevZone != myRoleLower)
						AddSlidingNotification($"{Capitalize(prevZone)} has lost influence over {npcName}!");
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
