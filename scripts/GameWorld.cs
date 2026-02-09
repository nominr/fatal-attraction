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
	private string _lastRPSKey = ""; // Track RPS state to avoid constant rebuilds

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
	private GoalsMenu _goalsMenu;
	private TextureButton _goalsButton;
	// Editorial asset content node (holds room Area2D children)
	private Node2D _editorialContentNode;
	
	// Interaction tracking
	private string _currentInteractingNpcId = null;
	private string _currentConversionNpcId = null;
	private int _lastGameStateHash = 0; // Track when game state changes to prevent unnecessary refreshes
	private bool _goalsShownAtStart = false;
	
	private Label _timerLabel;
	private Label _roleLabel;
	private Label _convertedLabel;
	private VBoxContainer _metersContainer;
	private RichTextLabel _notificationText;
	private RPSResultOverlay _rpsResultOverlay;
	private PanelContainer _notificationPanel;
	private Button _collapseNotificationButton;
	private bool _notificationCollapsed = false;
	private MarginContainer _notificationMargin;
	private Vector2 _notificationPanelExpandedPosition;
	private int _lastNotificationCount = 0; // Track which notifications have been displayed

	// Bomb Target Selection UI
	private Control _bombTargetOverlay;
	private string _selectedBombTarget = null;
	private Node2D _targetRedDot = null;
	private HashSet<string> _bombFrozenNpcs = new HashSet<string>();
	private Label _bombCounterLabel;
	private int _bombsRemaining = 3;
	private Control _bombSliderOverlay;
	private bool _bombOperationActive = false;
	private float _sliderPosition = 0f;
	private float _sliderDirection = 1f;
	private bool _sliderMoving = true;
	private const float SLIDER_SPEED = 500f; // pixels per second (increased from 200)
	private List<Vector2> _targetZones = new List<Vector2>(); // x position and width
	private BombSlider _sliderControl;

	// Admirer punch logic
	private const int PUNCHES_TO_KILL = 50;
	private const int PLAYER_PUNCHES_TO_KILL = 200;
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

	private Button _callPoliceButton;

	private Label _callPoliceTimerLabel;
	private long _localCaughtTimeFallback = 0;
	private int _lastProcessedNotificationCount = 0;
	
	// Track where panels were opened to auto-close on distance
	private Vector2 _cameraSelectPanelOpenPos;
	
	// +1 Rating Visual Feedback
	private double _previousProducerRating = 0;
	private CenterContainer _plusOneOverlay;
	private double _plusOneTimer = 0;
	private Texture2D _plusOneTexture;

	// Player Health Display
	private Label _playerHealthLabel;

	// Room 5 Health Regeneration
	private double _room5RegenAccumulator = 0;

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
		FatalAttraction.Tests.MurderTest.RunTests();

		_networkManager = GetNode<NetworkManager>("/root/NetworkManager");
		// Listen for network player events to keep controllers in sync
		_networkManager.PlayerConnected += OnNetworkPlayerConnected;
		_networkManager.PlayerDisconnected += OnNetworkPlayerDisconnected;
		
		// Connect Room Signals
		ConnectRoomSignals();
		
		SetupUI();
		// TileMap is now defined in GameWorld.tscn scene file
		
		// Debug: Check if TileMapLayer loaded from scene and scale it
		var tileMapLayer = GetNodeOrNull("TileMapLayer");
		if (tileMapLayer != null && tileMapLayer is Node2D tileMapNode)
		{
			GD.Print($"TileMapLayer found! Original Pos: {tileMapNode.Position}");
			
			// Store original position for relative calculations
			Vector2 originalTileMapPos = tileMapNode.Position;
			Vector2 targetPos = new Vector2(500, 200);
			Vector2 targetScale = new Vector2(4.0f, 4.0f);
			
			// Scale and Move TileMap
			tileMapNode.Scale = targetScale;
			tileMapNode.Position = targetPos;
			tileMapNode.ZIndex = -10;
			
			// CRITICAL FIX: Set Z-index on all child TileMapLayers to prevent camera tiles from appearing over NPCs
			foreach (var child in tileMapNode.GetChildren())
			{
				if (child is Node2D childLayer)
				{
					childLayer.ZIndex = -10;
					GD.Print($"Set ZIndex=-10 on child layer: {childLayer.Name}");
				}
			} 
			
			GD.Print($"TileMapLayer scaled to {tileMapNode.Scale} and positioned at {tileMapNode.Position}");
			
			// Match Layer1 to the same transform (since it was reparented to root)
			var layer1 = GetNodeOrNull("Layer1");
			if (layer1 != null && layer1 is Node2D layer1Node)
			{
				layer1Node.Scale = targetScale;
				layer1Node.Position = targetPos;
				layer1Node.ZIndex = -5; // Above floor (-10) but BELOW players (0)
				GD.Print($"Layer1 scaled to {targetScale} and positioned at {targetPos}");
			}
			
			// ALIGN ROOM AREAS TO MATCH SCALED WORLD
			// The Areas in the scene are 1x scale and relative to the original TileMap layout.
			// We must transform them to match the new world coordinates.
			string[] roomNames = { "Room1", "Room2", "Room3", "Room4", "Room5", "Hallways" };
			foreach (var rName in roomNames)
			{
				var area = GetNodeOrNull<Area2D>(rName);
				if (area != null)
				{
					// Calculate relative position.
				// NOTE: We assume Area2Ds were placed relative to world origin (0,0) which was the intended
				// top-left of the map, even if the TileMapLayer itself ended up at (11, -46) in the scene.
				// Subtracting originalTileMapPos (11, -46) introduces a shift that gets scaled x4, causing misalignment.
				// So we use area.Position directly as the relative offset from "Map Top-Left".
				Vector2 relPos = area.Position; 
				
				// Apply Scale
				// New Relative Pos = Old Rel Pos * Scale
				Vector2 newRelPos = relPos * targetScale;
				
				// Apply Global Offset (TargetPos)
				area.Position = targetPos + newRelPos;
				area.Scale = targetScale;
				
				GD.Print($"[GameWorld] Aligned {rName} to World: Pos {area.Position} (was {area.Position}), Scale {area.Scale}");
				}
			}
		}
		else
		{
			GD.PrintErr("TileMapLayer NOT found in scene!");
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
		
		var hudBgStyle = new StyleBoxFlat();
		hudBgStyle.BgColor = new Color(0.2f, 0.2f, 0.2f, 0.6f); // Transparent grey
		hudBgStyle.SetCornerRadiusAll(4);
		hudBgStyle.SetContentMarginAll(8);
		hudPanel.AddThemeStyleboxOverride("panel", hudBgStyle);
		hudPanel.AddChild(hudContainer);
		_uiLayer.AddChild(hudPanel);

		_timerLabel = new Label();
		_timerLabel.Text = "Time: 05:00";
		_timerLabel.AddThemeFontOverride("font", _customFont);
		_timerLabel.AddThemeFontSizeOverride("font_size", 26);
		_timerLabel.AddThemeColorOverride("font_color", Colors.White);
		hudContainer.AddChild(_timerLabel);
		
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

		_convertedLabel = new Label();
		_convertedLabel.Text = "Converted: 0";
		_convertedLabel.AddThemeFontOverride("font", _customFont);
		_convertedLabel.AddThemeFontSizeOverride("font_size", 26);
		_convertedLabel.AddThemeColorOverride("font_color", Colors.White);
		_convertedLabel.Visible = false; // Only relevant for Prophet
		hudContainer.AddChild(_convertedLabel);

		// Knife Counter (Admirer only)
		_bombCounterLabel = new Label();
		_bombCounterLabel.Text = $"KNIVES LEFT: {_bombsRemaining}";
		_bombCounterLabel.AddThemeFontOverride("font", _customFont);
		_bombCounterLabel.AddThemeFontSizeOverride("font_size", 26);
		_bombCounterLabel.AddThemeColorOverride("font_color", Colors.White);
		_bombCounterLabel.Visible = false; // Only relevant for Admirer
		hudContainer.AddChild(_bombCounterLabel);

		// Player Health Label (Prophet/Producer only)
		_playerHealthLabel = new Label();
		_playerHealthLabel.Text = "Health: 200/200";
		_playerHealthLabel.AddThemeFontOverride("font", _customFont);
		_playerHealthLabel.AddThemeFontSizeOverride("font_size", 26);
		_playerHealthLabel.AddThemeColorOverride("font_color", new Color(1, 0, 0, 1)); // Red text
		_playerHealthLabel.Visible = false; // Only for Prophet/Producer
		hudContainer.AddChild(_playerHealthLabel);

		_metersContainer = new VBoxContainer();
		_metersContainer = new VBoxContainer();
		hudContainer.AddChild(_metersContainer);

		// Producer Stats Container (Active Cameras / Police)
		_producerStatsContainer = new VBoxContainer();
		hudContainer.AddChild(_producerStatsContainer);

		_activeCamerasLabel = new Label();
		_activeCamerasLabel.Text = "Active Security Cameras:\nNone";
		_activeCamerasLabel.AddThemeFontOverride("font", _customFont);
		_activeCamerasLabel.AddThemeFontSizeOverride("font_size", 26);
		_activeCamerasLabel.Visible = false;
		_producerStatsContainer.AddChild(_activeCamerasLabel);

		// Call Police Section (HBox for Button + Timer)
		var policeHBox = new HBoxContainer();
		policeHBox.AddThemeConstantOverride("separation", 10);
		_producerStatsContainer.AddChild(policeHBox);

		_callPoliceButton = new Button();
		_callPoliceButton.Text = "CALL POLICE!";
		_callPoliceButton.Modulate = Colors.Red;
		_callPoliceButton.Visible = false;
		_callPoliceButton.AddThemeFontOverride("font", _customFont);
		_callPoliceButton.AddThemeFontSizeOverride("font_size", 25);
		_callPoliceButton.Pressed += () => OnActionSelected("producer_global", "call_police");
		policeHBox.AddChild(_callPoliceButton);

		_callPoliceTimerLabel = new Label();
		_callPoliceTimerLabel.Text = "";
		_callPoliceTimerLabel.Visible = false;
		_callPoliceTimerLabel.AddThemeFontOverride("font", _customFont);
		_callPoliceTimerLabel.AddThemeFontSizeOverride("font_size", 24); // Larger text to match button
		_callPoliceTimerLabel.AddThemeColorOverride("font_color", Colors.Yellow);
		_callPoliceTimerLabel.VerticalAlignment = VerticalAlignment.Center;
		policeHBox.AddChild(_callPoliceTimerLabel);

		// Notification Panel (bottom right)
		var viewportSize = GetViewportRect().Size; // Use actual viewport to avoid clipping on smaller windows
		_notificationPanel = new PanelContainer();
		_notificationPanel.CustomMinimumSize = new Vector2(380, 280);
		_notificationPanelExpandedPosition = new Vector2(
			Mathf.Max(20, viewportSize.X - _notificationPanel.CustomMinimumSize.X - 30),
			Mathf.Max(20, viewportSize.Y - _notificationPanel.CustomMinimumSize.Y - 30));
		_notificationPanel.Position = _notificationPanelExpandedPosition;
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
		
		trapButton.Pressed += OnTrapButtonPressed;
		_uiLayer.AddChild(trapButton);
		// Only visible if Prophet (handled in UpdateUI or default hidden?)
		// Ideally we verify role in UpdateUI.
		trapButton.Name = "TrapButton";
		trapButton.Visible = false;

		// Admirer Knife Button
		var bombButton = new Button();
		bombButton.Text = "Throw Knife";
		bombButton.AddThemeFontOverride("font", _customFont);
		bombButton.AddThemeFontSizeOverride("font_size", 26);
		bombButton.Position = new Vector2(20, 600);
		bombButton.CustomMinimumSize = new Vector2(180, 60);
		
		// Add Knife Icon
		var bombTexture = ResourceLoader.Load<Texture2D>("res://assets/knife.png");
		bombButton.Icon = bombTexture;
		bombButton.ExpandIcon = true;
		bombButton.IconAlignment = HorizontalAlignment.Left;
		bombButton.AddThemeConstantOverride("h_separation", 10);
		bombButton.AddThemeConstantOverride("icon_max_width", 60);

		// Style settings (same as trap button)
		var bombNormalStyle = CreateTrapStyle(Colors.White, Colors.Black);
		var bombHoverStyle = CreateTrapStyle(new Color(0.85f, 0.85f, 0.85f, 1), Colors.Black);
		var bombPressedStyle = CreateTrapStyle(new Color(0.85f, 0.85f, 0.85f, 1), Colors.Black);
		var bombDisabledStyle = CreateTrapStyle(new Color(0.6f, 0.6f, 0.6f, 1), Colors.Black);
		bombDisabledStyle.SetBorderWidthAll(0);

		bombButton.AddThemeStyleboxOverride("normal", bombNormalStyle);
		bombButton.AddThemeStyleboxOverride("hover", bombHoverStyle);
		bombButton.AddThemeStyleboxOverride("pressed", bombPressedStyle);
		bombButton.AddThemeStyleboxOverride("disabled", bombDisabledStyle);
		
		// Black text that stays black
		bombButton.AddThemeColorOverride("font_color", new Color(0, 0, 0, 1));
		bombButton.AddThemeColorOverride("font_hover_color", new Color(0, 0, 0, 1));
		bombButton.AddThemeColorOverride("font_pressed_color", new Color(0, 0, 0, 1));
		bombButton.AddThemeColorOverride("font_focus_color", new Color(0, 0, 0, 1));
		bombButton.AddThemeColorOverride("font_disabled_color", new Color(0, 0, 0, 1));
		
		bombButton.Pressed += OnBombButtonPressed;
		_uiLayer.AddChild(bombButton);
		bombButton.Name = "BombButton";
		bombButton.Visible = false;

		// RPS Result Overlay
		_rpsResultOverlay = new RPSResultOverlay();
		AddChild(_rpsResultOverlay);

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
		
		// Goals Menu System
		SetupGoalsMenu();
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
		manageCamsBtn.Toggled += (pressed) => 
		{
			var panel = _uiLayer.GetNodeOrNull<Control>("CameraSelectPanel");
			if (panel != null) panel.Visible = pressed;
			
			if (pressed)
			{
				// Capture open position for distance check
				if (_localPlayer != null) _cameraSelectPanelOpenPos = _localPlayer.Position;
			}
		};
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
							GD.Print($"[GameWorld] Producer selected camera: {cleanName}");
							OnActionSelected("producer_global", $"toggle_camera_{cleanName}");
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

	private void SetupGoalsMenu()
	{
		// Goals Menu Panel
		_goalsMenu = new GoalsMenu();
		_goalsMenu.MenuClosed += OnGoalsMenuClosed;
		_uiLayer.AddChild(_goalsMenu);

		// Goals Button (upper right corner) - Phone Icon
		var viewportSize = GetViewportRect().Size;
		var phoneTexture = ResourceLoader.Load<Texture2D>("res://assets/phone-menu.png");

		_goalsButton = new TextureButton();
		_goalsButton.TextureNormal = phoneTexture;
		_goalsButton.IgnoreTextureSize = false;
		_goalsButton.StretchMode = TextureButton.StretchModeEnum.Scale;
		// Scale the phone icon (2x scale for reasonable size)
		_goalsButton.Scale = new Vector2(2.0f, 2.0f);
		
		// Position in upper right corner
		float buttonWidth = phoneTexture != null ? phoneTexture.GetWidth() * 2 : 40;
		_goalsButton.Position = new Vector2(
			Mathf.Max(20, viewportSize.X - buttonWidth - 30),
			20);
		_goalsButton.Pressed += OnGoalsButtonPressed;
		_uiLayer.AddChild(_goalsButton);
	}

	private void OnGoalsButtonPressed()
	{
		if (_goalsMenu != null && !string.IsNullOrEmpty(_myRole))
		{
			// Toggle: if visible, hide it; if hidden, show it
			if (_goalsMenu.Visible)
			{
				_goalsMenu.Hide();
			}
			 else
			{
				// Update with latest info
				string loveInterest = "";
				string targets = "";
				
				// Get love interest and targets from GameEngine state
				if (_gameEngine != null)
				{
					var loveInterestList = new List<string>();
					var targetsList = new List<string>();
					
					foreach (var npc in _gameEngine.GameState.NPCs.Values)
					{
						if (npc.IsLoveInterest)
							loveInterestList.Add(npc.Name);
						if (npc.IsTarget)
							targetsList.Add(npc.Name);
					}
					
					loveInterest = string.Join(", ", loveInterestList);
					targets = string.Join(", ", targetsList);
				}
				else if (_localGameState != null)
				{
					// Client-side: try to get from cached state
					var loveInterestList = new List<string>();
					var targetsList = new List<string>();
					
					var npcs = _localGameState["npcs"] as JObject;
					if (npcs != null)
					{
						foreach (var prop in npcs.Properties())
						{
							bool isLoveInterest = prop.Value["role"]?.Value<string>() == "love_interest";
							bool isTarget = prop.Value["isTarget"]?.Value<bool>() ?? false;
							
							if (isLoveInterest)
								loveInterestList.Add(Capitalize(prop.Value["name"]?.Value<string>() ?? prop.Name));
							if (isTarget)
								targetsList.Add(Capitalize(prop.Value["name"]?.Value<string>() ?? prop.Name));
						}
					}
					
					loveInterest = string.Join(", ", loveInterestList);
					targets = string.Join(", ", targetsList);
				}
				
				_goalsMenu.SetRole(_myRole, loveInterest, targets);
				_goalsMenu.ShowMenu();
			}
		}
	}

	private void OnGoalsMenuClosed()
	{
		// Optional: Handle any logic when goals menu is closed
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
		
		_gameActive = true;
		_timeRemaining = 300.0;

		SpawnNPCs();
		SpawnAllPlayers();
		BroadcastGameState();
	}

	private void InitializeClient()
	{
		// Request state from server - players will be spawned when state arrives
		RpcId(1, MethodName.RequestGameState);
	}

	private Vector2 GetRandomNPCSpawnPosition()
	{
		// Define spawn ranges (rectangular zones)
		var spawnRanges = new List<(float minX, float maxX, float minY, float maxY)>
		{
			(50, 1000, 175, 400),      // Range 1
			(-300, 2700, 930, 950),    // Range 2
			(2500, 2850, 1450, 1450),  // Range 3 (single Y value)
			(580, 2030, 1450, 1740),   // Range 4
			(1600, 2300, 160, 440)     // Range 5
		};

		var random = new Random();
		// Pick a random spawn range
		var range = spawnRanges[random.Next(spawnRanges.Count)];

		// Generate random position within the selected range
		float x = (float)(random.NextDouble() * (range.maxX - range.minX) + range.minX);
		float y = (float)(random.NextDouble() * (range.maxY - range.minY) + range.minY);

		return new Vector2(x, y);
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
			entity.Position = GetRandomNPCSpawnPosition();
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
		// Don't allow dead players to interact with NPCs
		if (IsLocalPlayerDead())
		{
			GD.Print($"[GameWorld] Dead player tried to interact with NPC {npcId}, ignoring.");
			return;
		}
		
		// Don't allow interactions when in bomb mode - show "too close" message
		if (_bombOperationActive)
		{
			GD.Print($"[BOMB] NPC {npcId} clicked. Selecting as target.");
			// Check if this is one of the target NPCs
			var targetNPCs = new[] { "john", "rebecca", "marcus" };
			// Convert to lower case for comparison just in case
			if (targetNPCs.Contains(npcId.ToLower()))
			{
				_selectedBombTarget = npcId;
				
				// Hide selection overlay
				if (_bombTargetOverlay != null)
				{
					_bombTargetOverlay.Visible = false;
				}
				
				// Proceed to slider minigame
				ShowBombSlider();
			}
			return;
		}
		
		GD.Print($"GameWorld received OnNPCClicked for {npcId}. Current Role: '{_myRole}'");
		if (string.IsNullOrEmpty(_myRole)) 
		{
			GD.Print("Role is empty, ignoring click.");
			return;
		}

		// Get available actions from cached state
		var allActions = _localGameState?["all_actions"] as JObject;
		var myActions = allActions?[_myRole.ToLower()] as JObject;
		var npcActions = myActions?[npcId];

		var npcEntity = _npcEntities.GetValueOrDefault(npcId);
		string npcName = npcEntity?.NpcName ?? npcId;

		var npcConfig = _localGameState?["npcs"]?[npcId];
		string desc = npcConfig?["interactionTree"]?["root"]?["text"]?.Value<string>() 
			?? "An NPC awaits your action.";

		// INTERVIEW UI OVERRIDE
		// Check if we are interviewing this NPC
		var activeInterviews = _localGameState?["active_interviews"] as JObject;
		// _myRole is e.g. "producer", key in dictionary is "Producer" (Enum strings usually PascalCase?)
		// Let's check both or normalize. Using Enum.Parse logic earlier means keys are likely Role.ToString().
		// GameEngine generates keys as Role.ToString() -> "Producer".
		// _myRole from network might be "producer" (lowercase).
		
		string roleKey = Capitalize(_myRole); // Ensure "Producer"
		if (activeInterviews != null && activeInterviews.ContainsKey(roleKey))
		{
			var interviewInfo = activeInterviews[roleKey];
			if (interviewInfo["npcId"]?.Value<string>() == npcId)
			{
				desc = interviewInfo["lastResponse"]?.Value<string>() ?? desc;
			}
		}

		var actionsList = npcActions?.ToObject<List<JToken>>() ?? new List<JToken>();
		

		// Unfreeze previous NPC if any (safety check if panel was somehow bypassed)
		if (!string.IsNullOrEmpty(_currentInteractingNpcId) && _currentInteractingNpcId != npcId)
		{
			if (_npcEntities.TryGetValue(_currentInteractingNpcId, out var prevNpc))
			{
				prevNpc.SetFrozen(false);
			}
		}

		// Freeze the NPC
		if (_npcEntities.TryGetValue(npcId, out var npc))
		{
			npc.SetFrozen(true);
		}

		_currentInteractingNpcId = npcId;
		_npcDialogueUI.ShowForNPC(npcId, npcName, desc, actionsList);
	}

	private void OnActionSelected(string npcId, string actionId)
	{
		// Send to server - overlay will be shown when result comes back in notifications
		RpcId(1, MethodName.SubmitAction, npcId, actionId);
	}

	private void OnTrapButtonPressed()
	{
		// Local cooldown: disable for 10 seconds and send action
		var trapBtn = _uiLayer.GetNodeOrNull<Button>("TrapButton");
		if (trapBtn != null)
		{
			if (!trapBtn.Disabled)
			{
				trapBtn.Disabled = true;
				var timer = new Timer();
				timer.Name = $"TrapCooldownTimer_{Time.GetTicksMsec()}";
				timer.OneShot = true;
				timer.WaitTime = 10.0;
				timer.Timeout += () =>
				{
					if (IsInstanceValid(trapBtn)) trapBtn.Disabled = false;
					timer.QueueFree();
				};
				AddChild(timer);
				timer.Start();
			}
		}
		OnActionSelected("global", "set_trap");
	}

	private void OnBombButtonPressed()
	{
		ShowBombTargetSelection();
	}
	
	
	private void ShowBombTargetSelection()
	{
		// Create overlay if it doesn't exist
		if (_bombTargetOverlay == null)
		{
			_bombTargetOverlay = new Control();
			_bombTargetOverlay.Name = "BombTargetOverlay";
			_bombTargetOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			_bombTargetOverlay.MouseFilter = Control.MouseFilterEnum.Ignore; // Allow clicks through to NPCs
			
			// Top instruction label (smaller, inside overlay with transparent background)
			var instructionPanel = new PanelContainer();
			var bgStyle = new StyleBoxFlat();
			bgStyle.BgColor = new Color(0.2f, 0.2f, 0.2f, 0.7f); // Transparent grey
			bgStyle.ContentMarginLeft = 10;
			bgStyle.ContentMarginRight = 10;
			bgStyle.ContentMarginTop = 5;
			bgStyle.ContentMarginBottom = 5;
			instructionPanel.AddThemeStyleboxOverride("panel", bgStyle);
			instructionPanel.Position = new Vector2(GetViewportRect().Size.X / 2 - 120, 50);
			
			var instructionLabel = new Label();
			instructionLabel.Name = "InstructionLabel";
			instructionLabel.Text = "Choose your target.";
			instructionLabel.AddThemeFontSizeOverride("font_size", 24);
			instructionLabel.AddThemeColorOverride("font_color", Colors.White);
			instructionLabel.HorizontalAlignment = HorizontalAlignment.Center;
			instructionPanel.AddChild(instructionLabel);
			_bombTargetOverlay.AddChild(instructionPanel);
			
			// Close button (top right)
			var closeButton = new Button();
			closeButton.Name = "CloseButton";
			closeButton.Text = "Close";
			closeButton.Position = new Vector2(GetViewportRect().Size.X - 120, 20);
			closeButton.CustomMinimumSize = new Vector2(100, 50);
			closeButton.AddThemeFontSizeOverride("font_size", 20);
			closeButton.Pressed += OnBombTargetClose;
			_bombTargetOverlay.AddChild(closeButton);
			
			_uiLayer.AddChild(_bombTargetOverlay);
		}
		
		// Show the overlay
		_bombTargetOverlay.Visible = true;
		_selectedBombTarget = null;
		_bombOperationActive = true;
		GD.Print($"[BOMB] _bombOperationActive set to true in ShowBombTargetSelection");
		
		// Pause all target NPCs
		PauseTargetNPCs(true);
		
		// Enable click detection on target NPCs
		EnableTargetNPCClicks(true);
	}
	
	private void OnBombTargetClose()
	{
		// Stop the entire bomb operation
		_bombOperationActive = false;
		
		if (_bombTargetOverlay != null)
		{
			_bombTargetOverlay.Visible = false;
		}
		
		if (_bombSliderOverlay != null)
		{
			_bombSliderOverlay.Visible = false;
		}
		
		// Unpause target NPCs
		PauseTargetNPCs(false);
		
		// Disable click detection
		EnableTargetNPCClicks(false);
		
		// Remove red dot if exists
		if (_targetRedDot != null && IsInstanceValid(_targetRedDot))
		{
			_targetRedDot.QueueFree();
			_targetRedDot = null;
		}
		
		_selectedBombTarget = null;
	}
	
	private void ShowBombSlider()
	{
		GD.Print("[GameWorld] ShowBombSlider called");
		
		// Reset slider state
		_sliderPosition = 0f;
		_sliderDirection = 1f;
		_sliderMoving = true;
		
		// Generate random target zones (3-5 zones)
		_targetZones.Clear();
		var random = new Random();
		int numZones = random.Next(3, 6); // 3 to 5 zones
		const float barWidth = 400f;
		
		for (int i = 0; i < numZones; i++)
		{
			float zoneX = (float)(random.NextDouble() * (barWidth - 24)); // Leave room for zone width
			float zoneWidth = (float)(random.Next(14, 24)); // Zone width 14-24 pixels (slightly bigger)
			_targetZones.Add(new Vector2(zoneX, zoneWidth));
		}
		
		// Create slider overlay if it doesn't exist
		if (_bombSliderOverlay == null)
		{
			_bombSliderOverlay = new Control();
			_bombSliderOverlay.Name = "BombSliderOverlay";
			_bombSliderOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			_bombSliderOverlay.MouseFilter = Control.MouseFilterEnum.Stop;
			
			// Panel at bottom left of screen (away from notifications)
			var panel = new PanelContainer();
			panel.Name = "SliderPanel";
			var viewportSize = GetViewportRect().Size;
			panel.Position = new Vector2(230, viewportSize.Y - 200);
			panel.CustomMinimumSize = new Vector2(600, 120);
			
			// Panel styling - transparent grey background like HUD
			var panelStyle = new StyleBoxFlat();
			panelStyle.BgColor = new Color(0.2f, 0.2f, 0.2f, 0.6f);
			panelStyle.SetCornerRadiusAll(4);
			panelStyle.SetContentMarginAll(15);
			panel.AddThemeStyleboxOverride("panel", panelStyle);
			
			var vbox = new VBoxContainer();
			vbox.AddThemeConstantOverride("separation", 10);
			panel.AddChild(vbox);
			
			// Title label
			var titleLabel = new Label();
			titleLabel.Text = "Choose your target.";
			titleLabel.AddThemeFontOverride("font", _customFont);
			titleLabel.AddThemeFontSizeOverride("font_size", 24);
			titleLabel.AddThemeColorOverride("font_color", Colors.White);
			titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
			vbox.AddChild(titleLabel);
			
			// Instruction label
			var instructionLabel = new Label();
			instructionLabel.Text = "Stop the slider in the target zone!";
			instructionLabel.AddThemeFontOverride("font", _customFont);
			instructionLabel.AddThemeFontSizeOverride("font_size", 26);
			instructionLabel.AddThemeColorOverride("font_color", Colors.White);
			instructionLabel.HorizontalAlignment = HorizontalAlignment.Center;
			vbox.AddChild(instructionLabel);
			
			// Custom slider container (centered)
			var sliderHBox = new HBoxContainer();
			sliderHBox.Alignment = BoxContainer.AlignmentMode.Center;
			_sliderControl = new BombSlider();
			_sliderControl.Name = "SliderContainer";
			_sliderControl.CustomMinimumSize = new Vector2(400, 40);
			sliderHBox.AddChild(_sliderControl);
			vbox.AddChild(sliderHBox);
			
			// Buttons container
			var buttonBox = new HBoxContainer();
			buttonBox.Alignment = BoxContainer.AlignmentMode.Center;
			buttonBox.AddThemeConstantOverride("separation", 20);
			vbox.AddChild(buttonBox);
			
			// Stop button (renamed from Throw)
			var stopButton = new Button();
			stopButton.Name = "StopButton";
			stopButton.Text = "Stop";
			stopButton.AddThemeFontOverride("font", _customFont);
			stopButton.AddThemeFontSizeOverride("font_size", 24);
			stopButton.CustomMinimumSize = new Vector2(120, 40);
			
			// Apply trap-style button styling
			stopButton.AddThemeStyleboxOverride("normal", CreateTrapStyle(Colors.White, Colors.Black));
			stopButton.AddThemeStyleboxOverride("hover", CreateTrapStyle(new Color(0.85f, 0.85f, 0.85f, 1), Colors.Black));
			stopButton.AddThemeStyleboxOverride("pressed", CreateTrapStyle(new Color(0.85f, 0.85f, 0.85f, 1), Colors.Black));
			stopButton.AddThemeColorOverride("font_color", Colors.Black);
			stopButton.AddThemeColorOverride("font_hover_color", Colors.Black);
			stopButton.AddThemeColorOverride("font_pressed_color", Colors.Black);
			
			stopButton.Pressed += OnBombStop;
			buttonBox.AddChild(stopButton);
			
			// Close button
			var closeButton = new Button();
			closeButton.Name = "CloseButton";
			closeButton.Text = "Close";
			closeButton.AddThemeFontOverride("font", _customFont);
			closeButton.AddThemeFontSizeOverride("font_size", 24);
			closeButton.CustomMinimumSize = new Vector2(120, 40);
			
			// Apply trap-style button styling
			closeButton.AddThemeStyleboxOverride("normal", CreateTrapStyle(Colors.White, Colors.Black));
			closeButton.AddThemeStyleboxOverride("hover", CreateTrapStyle(new Color(0.85f, 0.85f, 0.85f, 1), Colors.Black));
			closeButton.AddThemeStyleboxOverride("pressed", CreateTrapStyle(new Color(0.85f, 0.85f, 0.85f, 1), Colors.Black));
			closeButton.AddThemeColorOverride("font_color", Colors.Black);
			closeButton.AddThemeColorOverride("font_hover_color", Colors.Black);
			closeButton.AddThemeColorOverride("font_pressed_color", Colors.Black);
			
			closeButton.Pressed += OnBombTargetClose;
			buttonBox.AddChild(closeButton);
			
			_bombSliderOverlay.AddChild(panel);
			_uiLayer.AddChild(_bombSliderOverlay);
		}
		
		// Ensure slider control state is synced to current target zones and position
		if (_sliderControl != null)
		{
			_sliderControl.TargetZones = _targetZones;
			_sliderControl.SliderPosition = _sliderPosition;
		}

		// Show the slider
		_bombSliderOverlay.Visible = true;
		_bombOperationActive = true;
	}

	private bool IsSliderInTargetZone(float sliderPosition)
	{
		const float HITBOX_EXTENSION = 10f;
		foreach (var zone in _targetZones)
		{
			float zoneStart = zone.X - HITBOX_EXTENSION;
			float zoneEnd = zone.X + zone.Y + HITBOX_EXTENSION;
			if (sliderPosition >= zoneStart && sliderPosition <= zoneEnd)
			{
				return true;
			}
		}
		return false;
	}
	
	private void OnBombStop()
	{
		if (!_bombOperationActive || string.IsNullOrEmpty(_selectedBombTarget))
		{
			GD.Print("[GameWorld] Cannot throw bomb - operation not active or no target");
			return;
		}
		
		if (_bombsRemaining <= 0)
		{
			GD.Print("[GameWorld] No bombs remaining");
			return;
		}
		
		// Stop the slider movement
		_sliderMoving = false;

		// Use the visual slider position to avoid mismatch between UI and hit detection
		float barWidth = 400f;
		if (_sliderControl != null && _sliderControl.Size.X > 0)
		{
			barWidth = _sliderControl.Size.X;
		}
		float sliderPosition = _sliderControl?.SliderPosition ?? _sliderPosition;
		sliderPosition = Mathf.Clamp(sliderPosition, 0f, barWidth);
		_sliderPosition = sliderPosition;

		bool inTargetZone = IsSliderInTargetZone(sliderPosition);
		
		GD.Print($"[GameWorld] Slider stopped at {_sliderPosition}, in target zone: {inTargetZone}");
		
		// Decrement knife counter
		_bombsRemaining--;
		if (_bombCounterLabel != null)
		{
			_bombCounterLabel.Text = $"KNIVES LEFT: {_bombsRemaining}";
		}
		
		// Hide knife button if no knives left
		if (_bombsRemaining <= 0)
		{
			var bombBtn = _uiLayer.GetNodeOrNull<Button>("BombButton");
			if (bombBtn != null)
			{
				bombBtn.Visible = false;
			}
		}
		
		if (inTargetZone)
		{
			// Successful hit - kill the NPC (same as punch effect)
			GD.Print($"[GameWorld] Bomb hit! Killing NPC {_selectedBombTarget}");
			RpcId(1, MethodName.BombKillNPC, _selectedBombTarget);
		}
		else
		{
			// Missed - show unsuccessful message
			GD.Print("[GameWorld] Knife missed!");
			if (_gameEngine != null)
			{
				_gameEngine.GameState.AddNotification("Knife unsuccessful.");
				BroadcastGameState();
			}
		}
		
		// Clean up
		OnBombTargetClose();
	}
	
	private void OnDrawSliderBar()
	{
		// This method is no longer needed - drawing is handled by BombSlider class
	}
	
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void BombKillNPC(string targetNpcId)
	{
		if (!Multiplayer.IsServer()) return;
		
		GD.Print($"[GameWorld] BombKillNPC called for {targetNpcId}");
		
		// Get the NPC and mark it as eliminated
		var npc = _gameEngine.GameState.GetNPC(targetNpcId);
		if (npc != null)
		{
			npc.Alive = false;
			npc.PunchesTaken = PUNCHES_TO_KILL;
			GD.Print($"[GameWorld] NPC {targetNpcId} marked as eliminated by bomb");
			
			// Apply immobilization effect visually
			if (_npcEntities.TryGetValue(targetNpcId, out var npcEntity))
			{
				npcEntity.SetFrozen(true);
				GD.Print($"[GameWorld] NPC {targetNpcId} frozen (immobilized)");
			}

			// CAMERA DETECTION LOGIC
			if (_gameEngine.GameState.ActiveCameraRoomIds.Contains(npc.CurrentRoomId))
			{
				_gameEngine.GameState.AdmirerCaught = true;
				_gameEngine.GameState.AdmirerEliminated = false;
				_gameEngine.GameState.AddNotification($"[CAMERA ALERT] Suspicious activity detected in {npc.CurrentRoomId}!");
				_gameEngine.GameState.AddNotification($"Producer's Camera captured the crime!");
			}
			
			_gameEngine.GameState.AddNotification($"Knife successful! {npc.Name} has been eliminated!");
			BroadcastGameState();
		}
		else
		{
			GD.PrintErr($"[GameWorld] Could not find NPC {targetNpcId} for bomb kill");
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

		npc.PunchesTaken = Math.Min(npc.PunchesTaken + 1, PUNCHES_TO_KILL);
		Rpc(MethodName.RpcFlashNpcDamage, targetNpcId);

		if (npc.PunchesTaken >= PUNCHES_TO_KILL)
		{
			npc.Alive = false;
			npc.PunchesTaken = PUNCHES_TO_KILL;

			if (_npcEntities.TryGetValue(targetNpcId, out var targetEntity))
			{
				targetEntity.SetFrozen(true);
			}

			// CAMERA DETECTION LOGIC
			if (_gameEngine.GameState.ActiveCameraRoomIds.Contains(npc.CurrentRoomId))
			{
				_gameEngine.GameState.AdmirerCaught = true;
				_gameEngine.GameState.AdmirerEliminated = false;
				_gameEngine.GameState.AddNotification($"[CAMERA ALERT] Suspicious activity detected in {npc.CurrentRoomId}!");
				_gameEngine.GameState.AddNotification($"Producer's Camera captured the crime!");
			}

			_gameEngine.GameState.AddNotification($"{npc.Name} has been eliminated. Target down!");
			BroadcastGameState();
		}
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

		// Increment punches taken
		targetPlayerState.PunchesTaken = Math.Min(targetPlayerState.PunchesTaken + 1, PLAYER_PUNCHES_TO_KILL);
		Rpc(MethodName.RpcFlashPlayerDamage, targetPlayerId);

		if (targetPlayerState.PunchesTaken >= PLAYER_PUNCHES_TO_KILL)
		{
			targetPlayerState.Alive = false;
			targetPlayerState.PunchesTaken = PLAYER_PUNCHES_TO_KILL;

			// Set ghost mode for eliminated player
			if (_playerControllers.TryGetValue(targetPlayerId, out var eliminatedPlayer))
			{
				eliminatedPlayer.SetGhostMode(true);
			}

			_gameEngine.GameState.AddNotification($"{targetRole} has been eliminated by the {senderRole}!");
			BroadcastGameState();
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	private void RpcFlashPlayerDamage(long playerId)
	{
		if (_playerControllers.TryGetValue(playerId, out var playerCtrl))
		{
			playerCtrl.FlashDamage(0.5);
		}
	}
	
	
	private void PauseTargetNPCs(bool pause)
	{
		GD.Print($"[GameWorld] PauseTargetNPCs called with pause={pause}");
		
		// Get target NPCs from game state
		if (_gameEngine?.GameState?.NPCs == null)
		{
			GD.Print("[GameWorld] ERROR: GameState or NPCs is null");
			return;
		}
		
		// Freeze john, rebecca, and marcus specifically
		string[] targetIds = { "john", "rebecca", "marcus" };
		
		foreach (var targetId in targetIds)
		{
			if (pause)
			{
				_bombFrozenNpcs.Add(targetId);
			}
			else
			{
				_bombFrozenNpcs.Remove(targetId);
			}
			
			if (_npcEntities.TryGetValue(targetId, out var npcEntity))
			{
				GD.Print($"[GameWorld] Setting {targetId} frozen to {pause}");
				npcEntity.SetFrozen(pause);
				
				// Broadcast to all clients
				if (Multiplayer.IsServer())
				{
					Rpc(MethodName.RpcFreezeNPC, targetId, pause);
				}
			}
			else
			{
				GD.Print($"[GameWorld] WARNING: No NPC entity found for {targetId}");
			}
		}
	}
	
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
	public void RpcFreezeNPC(string npcId, bool frozen)
	{
		if (_npcEntities.TryGetValue(npcId, out var npcEntity))
		{
			GD.Print($"[GameWorld] RPC: Setting {npcId} frozen to {frozen}");
			npcEntity.SetFrozen(frozen);
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
	
	private void EnableTargetNPCClicks(bool enable)
	{
		// Get target NPCs from game state
		if (_gameEngine?.GameState?.NPCs == null) return;
		
		var targetNpcIds = new HashSet<string>();
		foreach (var npc in _gameEngine.GameState.NPCs.Values)
		{
			if (npc.IsTarget)
			{
				targetNpcIds.Add(npc.Id);
			}
		}
		
		foreach (var npcEntity in _npcEntities.Values)
		{
			if (targetNpcIds.Contains(npcEntity.NpcId))
			{
				// This is a target NPC - enable clicking
				if (enable)
				{
					npcEntity.InputPickable = true;
					npcEntity.InputEvent += (viewport, inputEvent, shapeIdx) => OnTargetNPCClicked(npcEntity, inputEvent);
				}
				else
				{
					npcEntity.InputPickable = false;
				}
			}
		}
	}
	
	private void OnTargetNPCClicked(NPCEntity npcEntity, InputEvent inputEvent)
	{
		if (inputEvent is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
		{
			if (_bombTargetOverlay == null || !_bombTargetOverlay.Visible) return;
			
			GD.Print($"[BOMB] Target NPC clicked: {npcEntity.NpcId}");
			
			// Check if player is too close to the clicked NPC
			GD.Print($"[BOMB] Target NPC clicked: {npcEntity.NpcId}");
			
			// Distance check removed per user request (Bomb Fix)
			// Allow selection at any distance
			
			_selectedBombTarget = npcEntity.NpcId;
			
			// Remove old red dot if exists
			if (_targetRedDot != null && IsInstanceValid(_targetRedDot))
			{
				_targetRedDot.QueueFree();
			}
			
			// Create red dot on the NPC
			_targetRedDot = new Node2D();
			_targetRedDot.Name = "RedDot";
			_targetRedDot.ZIndex = 100; // Above everything
			
			var circle = new Sprite2D();
			var circleTexture = new GradientTexture2D();
			var gradient = new Gradient();
			gradient.SetColor(0, new Color(1, 0, 0, 1)); // Red center
			gradient.SetColor(1, new Color(1, 0, 0, 0.5f)); // Transparent edge
			circleTexture.Gradient = gradient;
			circleTexture.Fill = GradientTexture2D.FillEnum.Radial;
			circleTexture.Width = 64;
			circleTexture.Height = 64;
			circle.Texture = circleTexture;
			circle.Scale = new Vector2(0.5f, 0.5f);
			_targetRedDot.AddChild(circle);
			
			// Position on NPC
			_targetRedDot.Position = npcEntity.Position + new Vector2(0, -40); // Above NPC head
			AddChild(_targetRedDot);
			
			// Hide target selection overlay and show slider
			_bombTargetOverlay.Visible = false;
			ShowBombSlider();
		}
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
			foreach (var kvp in _playerControllers)
			{
				kvp.Value.SetHealthHintVisible(false);
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
			foreach (var kvp in _playerControllers)
			{
				kvp.Value.SetHealthHintVisible(false);
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

		// Update Player health hints based on role
		var playerStates = _localGameState?["player_states"] as JObject;
		foreach (var kvp in _playerControllers)
		{
			long playerId = kvp.Key;
			var playerCtrl = kvp.Value;

			// Skip self
			if (playerId == Multiplayer.GetUniqueId())
			{
				playerCtrl.SetHealthHintVisible(false);
				continue;
			}

			// Get target player's role
			if (!_networkManager.Players.TryGetValue(playerId, out var playerInfo))
			{
				playerCtrl.SetHealthHintVisible(false);
				continue;
			}

			string targetRole = playerInfo.Role;
			
			// All players can target any other player
			bool isValidTarget = !string.Equals(targetRole, myRoleLower, StringComparison.OrdinalIgnoreCase);

			if (!isValidTarget)
			{
				playerCtrl.SetHealthHintVisible(false);
				continue;
			}

			// Check distance
			float distance = _localPlayer.Position.DistanceTo(playerCtrl.Position);
			bool inRange = distance <= PUNCH_RANGE;

			if (inRange && playerStates != null)
			{
				// Get player health
				string roleKey = targetRole; // "Prophet", "Producer", or "Admirer"
				var playerState = playerStates[roleKey];
				if (playerState != null)
				{
					int punchesTaken = playerState["punches_taken"]?.Value<int>() ?? 0;
					int healthRemaining = PLAYER_PUNCHES_TO_KILL - punchesTaken;
					playerCtrl.SetHealthHintVisible(true, healthRemaining, PLAYER_PUNCHES_TO_KILL);
				}
				else
				{
					playerCtrl.SetHealthHintVisible(false);
				}
			}
			else
			{
				playerCtrl.SetHealthHintVisible(false);
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

	public override void _Process(double delta)
	{
		// Handle +1 Rating Visual Feedback Timer
		if (_plusOneTimer > 0)
		{
			_plusOneTimer -= delta;
			if (_plusOneTimer <= 0 && _plusOneOverlay != null)
			{
				_plusOneOverlay.Visible = false;
				GD.Print("[GameWorld] Hiding +1 rating visual feedback");
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

		// Update bomb slider animation
		if (_bombSliderOverlay != null && _bombSliderOverlay.Visible && _sliderMoving)
		{
			const float barWidth = 400f;
			_sliderPosition += _sliderDirection * SLIDER_SPEED * (float)delta;
			
			// Bounce at edges
			if (_sliderPosition >= barWidth)
			{
				_sliderPosition = barWidth;
				_sliderDirection = -1f;
			}
			else if (_sliderPosition <= 0)
			{
				_sliderPosition = 0;
				_sliderDirection = 1f;
			}
			
			// Update the slider control
			if (_sliderControl != null)
			{
				_sliderControl.SliderPosition = _sliderPosition;
				_sliderControl.TargetZones = _targetZones;
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
			
			// Add bomb-frozen NPCs to the frozen set
			foreach (var npcId in _bombFrozenNpcs)
			{
				frozenNpcIds.Add(npcId);
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

			// Player Health Regeneration in Room 5
			foreach (var kvp in _playerControllers)
			{
				long playerId = kvp.Key;
				var playerCtrl = kvp.Value;

				// Get player role
				if (!_networkManager.Players.TryGetValue(playerId, out var playerInfo)) continue;
				string role = playerInfo.Role;

				// Only Prophet and Producer can regenerate
				if (!string.Equals(role, "Prophet", StringComparison.OrdinalIgnoreCase) && 
					!string.Equals(role, "Producer", StringComparison.OrdinalIgnoreCase)) continue;

				// Get player state
				if (!Enum.TryParse<Role>(role, ignoreCase: true, out var roleEnum)) continue;
				var playerState = _gameEngine.GameState.GetPlayerState(roleEnum);
				if (playerState == null || !playerState.Alive) continue;

				// Check if player is in Room 5
				string roomId = GetRoomIdAtPosition(playerCtrl.Position);
				if (roomId == "Room5" && playerState.PunchesTaken > 0)
				{
					// Regenerate health slowly (1 HP every 0.5 seconds = 2 HP per second)
					// Using delta time to smooth the regeneration
					_room5RegenAccumulator += delta;
					if (_room5RegenAccumulator >= 0.5)
					{
						_room5RegenAccumulator = 0;
						playerState.PunchesTaken = Math.Max(0, playerState.PunchesTaken - 1);
					}
				}
			}

			_gameEngine.Update(delta);
			
			_timeRemaining -= delta;
			if (_timeRemaining <= 0)
			{
				_timeRemaining = 0;
				_gameActive = false;
				
				// Producer Wins on Time Out
				_gameEngine.GameState.Winner = "Producer";
				_gameEngine.GameState.AddNotification("GAME OVER - TIME UP! Producer Wins (Schedule Kept)!");
				
				BroadcastGameState();
			}
			else
			{
				// Periodically broadcast game state to keep timer updated on clients
				_broadcastTimer += delta;
				if (_broadcastTimer >= BROADCAST_INTERVAL)
				{
					_broadcastTimer = 0.0;
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
		
		// If RPS conversion overlay is visible, check if player is still in range of NPC
		if (_currentConversionNpcId != null)
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
			
			var rpsOverlay = _uiLayer?.GetNodeOrNull<CenterContainer>("RPSOverlay");
			
			if (_localPlayer != null && rpsOverlay != null && rpsOverlay.Visible)
			{
				var npc = _npcEntities.GetValueOrDefault(_currentConversionNpcId);
				
				if (npc != null)
				{
					float distance = _localPlayer.Position.DistanceTo(npc.Position);
					
					// Close RPS overlay if player gets too far (200 = larger buffer for conversion game)
					if (distance > 200)
					{
						GD.Print($"Player moved too far from NPC {_currentConversionNpcId} during conversion (distance: {distance}), cancelling RPS overlay");
						
						// Tell server to cancel the conversion
						if (!string.IsNullOrEmpty(_myRole))
						{
							if (Multiplayer.IsServer())
							{
								// We are the server, directly cancel
								if (Enum.TryParse<Role>(_myRole, ignoreCase: true, out var role))
								{
									if (_gameEngine.GameState.ActiveConversions.ContainsKey(role))
									{
										_gameEngine.GameState.ActiveConversions.Remove(role);
										_gameEngine.GameState.AddNotification($"Conversion cancelled - moved too far away!");
										BroadcastGameState();
									}
								}
							}
							else
							{
								// Send RPC to server
								RpcId(1, MethodName.CancelConversionDueToDistance, _myRole);
							}
						}
						
						// Hide the overlay locally and clear state
						var rpsRoot = rpsOverlay.GetNodeOrNull<PanelContainer>("RPSRootContainer");
						if (rpsRoot != null)
						{
							foreach (Node child in rpsRoot.GetChildren()) child.QueueFree();
						}
						rpsOverlay.Visible = false;
						_lastRPSKey = "";
						_currentConversionNpcId = null;
					}
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
				{ "is_target", npc.IsTarget } // Expose target status for Admirer
			};
			
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

		// Player Health States
		var playerStates = new JObject();
		foreach (var kvp in _gameEngine.GameState.Players)
		{
			var playerState = kvp.Value;
			playerStates[kvp.Key.ToString()] = new JObject
			{
				{ "punches_taken", playerState.PunchesTaken },
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
		UpdateNPCVisuals();
		UpdateGhostMode();

		// Show goals menu at game start (first time game state is received)
		if (!_goalsShownAtStart && !string.IsNullOrEmpty(_myRole))
		{
			_goalsShownAtStart = true;
			// Delay slightly to ensure UI is ready
			CallDeferred(MethodName.ShowGoalsMenuAtStart);
		}
	}

	private void ShowGoalsMenuAtStart()
	{
		if (_goalsMenu != null && !string.IsNullOrEmpty(_myRole))
		{
			// Get love interest and targets info
			string loveInterest = "";
			string targets = "";
			
			// Server: read directly from GameEngine
			if (_gameEngine != null)
			{
				var loveInterestList = new List<string>();
				var targetsList = new List<string>();
				
				foreach (var npc in _gameEngine.GameState.NPCs.Values)
				{
					if (npc.IsLoveInterest)
						loveInterestList.Add(npc.Name);
					if (npc.IsTarget)
						targetsList.Add(npc.Name);
				}
				
				loveInterest = string.Join(", ", loveInterestList);
				targets = string.Join(", ", targetsList);
			}
			// Client: read from local cached state
			else if (_localGameState != null)
			{
				var npcs = _localGameState["npcs"] as JObject;
				if (npcs != null)
				{
					var loveInterestList = new List<string>();
					var targetsList = new List<string>();
					
					foreach (var prop in npcs.Properties())
					{
						// Check role field for love_interest
						bool isLoveInterest = prop.Value["role"]?.Value<string>() == "love_interest";
						bool isTarget = prop.Value["isTarget"]?.Value<bool>() ?? false;
						
						if (isLoveInterest)
							loveInterestList.Add(Capitalize(prop.Value["name"]?.Value<string>() ?? prop.Name));
						if (isTarget)
							targetsList.Add(Capitalize(prop.Value["name"]?.Value<string>() ?? prop.Name));
					}
					
					loveInterest = string.Join(", ", loveInterestList);
					targets = string.Join(", ", targetsList);
				}
			}
			
			_goalsMenu.SetRole(_myRole, loveInterest, targets);
			_goalsMenu.ShowMenu();
		}

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

		var actionsList = npcActions?.ToObject<List<JToken>>() ?? new List<JToken>();
		_npcDialogueUI.ShowForNPC(npcId, npcName, desc, actionsList);
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
			Vector2 initPos = GetRandomNPCSpawnPosition();
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
		_timerLabel.Text = $"Time: {ts.Minutes:D2}:{ts.Seconds:D2}";

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
					_localCaughtTimeFallback = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

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
			trapBtn.Visible = isProphet;
		}

		// Update Bomb Button Visibility (Admirer only)
		if (_uiLayer.GetNodeOrNull<Button>("BombButton") is Button bombBtn)
		{
			bombBtn.Visible = isAdmirer && _bombsRemaining > 0;
		}
		
		// Update Bomb Counter Visibility (Admirer only)
		if (_bombCounterLabel != null)
		{
			_bombCounterLabel.Visible = isAdmirer;
		}

		// Player Health Label (All Roles)
		if (_playerHealthLabel != null)
		{
			if (!string.IsNullOrEmpty(_myRole))
			{
				// Get player health from game state
				var playerStates = _localGameState?["player_states"] as JObject;
				if (playerStates != null)
				{
					string myRoleKey = _myRole?.ToLower() switch
					{
						"prophet" => "Prophet",
						"producer" => "Producer",
						"admirer" => "Admirer",
						_ => null
					};
					if (myRoleKey != null)
					{
						var myPlayerState = playerStates[myRoleKey];
						if (myPlayerState != null)
						{
							int punchesTaken = myPlayerState["punches_taken"]?.Value<int>() ?? 0;
							int healthRemaining = PLAYER_PUNCHES_TO_KILL - punchesTaken;
							_playerHealthLabel.Text = $"Health: {healthRemaining}/{PLAYER_PUNCHES_TO_KILL}";
							_playerHealthLabel.Visible = true;
						}
						else
						{
							_playerHealthLabel.Visible = false;
						}
					}
					else
					{
						_playerHealthLabel.Visible = false;
					}
				}
				else
				{
					_playerHealthLabel.Visible = false;
				}
			}
			else
			{
				_playerHealthLabel.Visible = false;
			}
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
		if (manageCamsBtn != null) manageCamsBtn.Visible = isProducer;
		if (isProducer)
		{
			// Update Active Cameras Text in HUD
			var activeCameras = _localGameState?["active_camera_room_ids"]?.ToObject<List<string>>() ?? new List<string>();
			if (_activeCamerasLabel != null)
			{
				_activeCamerasLabel.Visible = true;
				_activeCamerasLabel.Text = activeCameras.Count > 0 
					? $"Active Security Cameras:\n{string.Join(", ", activeCameras)}"
					: "Active Security Cameras:\nNone";
			}
			
			// Update Call Police Button in HUD
			bool admirerCaught = _localGameState?["admirer_caught"]?.Value<bool>() ?? false;
			bool isEliminated = _localGameState?["admirer_eliminated"]?.Value<bool>() ?? false;
			
			// Get caught time - try snake_case first (standard), then PascalCase fallback
			long caughtTime = _localGameState?["admirer_caught_timestamp"]?.Value<long>() 
							?? _localGameState?["AdmirerCaughtTimestamp"]?.Value<long>() ?? 0;

			if (_callPoliceButton != null)
			{
				bool isWithinWindow = false;
				
				if (admirerCaught && !isEliminated)
				{

					// Check 40 second window
					long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
					
					// LOGIC FIX: Always use the most recent timestamp.
					// If a new notification arrived, _localCaughtTimeFallback is NOW.
					// If server sends old time T1, and we have T2 (now), use T2.
					// If server sends 0, and we have T2, use T2.
					if (_localCaughtTimeFallback > caughtTime)
					{
						caughtTime = _localCaughtTimeFallback;
					}
					// Only clear fallback if server time is actually newer (meaning server caught up)
					else if (caughtTime > 0)
					{
						_localCaughtTimeFallback = 0; 
					}

					long elapsed = now - caughtTime;
					long remaining = 40000 - elapsed;
					
					if (remaining > 0)
					{
						isWithinWindow = true;
						_callPoliceTimerLabel.Text = $"{Math.Ceiling(remaining / 1000.0)}s";
						_callPoliceTimerLabel.Visible = true;
					}
					else
					{
						_callPoliceTimerLabel.Visible = false;
					}
				}
				else
				{
					_callPoliceTimerLabel.Visible = false;
				}
				
				// Only show if caught AND not yet eliminated AND within 40s window
				_callPoliceButton.Visible = isWithinWindow;
			}
			
			// Update Camera Select Panel Buttons (if open)
			var csPanel = _uiLayer.GetNodeOrNull<Control>("CameraSelectPanel");
			if (csPanel != null && csPanel.Visible)
			{
				foreach (var camRoom in new[] { "Room1", "Room2", "Room3", "Room4", "Room5", "Hallways" })
				{
					var btn = csPanel.FindChild($"Btn_{camRoom}", true, false) as CheckButton;
					if (btn != null)
					{
						bool isActive = activeCameras.Contains(camRoom);
						btn.ButtonPressed = isActive; // Set check state
					}
				}

				// Also update the visual overlay on the editorial asset (rooms greyed)
				if (_editorialContentNode != null)
				{
					// Iterate children that are Area2D rooms and set their is_active property
					foreach (var child in _editorialContentNode.GetChildren())
					{
						if (child is Node roomNode)
						{
							// Expect the room node to have an exported `room_name` property
							var nameProp = roomNode.Get("room_name");
							string roomName = nameProp.Obj != null ? nameProp.ToString().Replace(" ", "") : roomNode.Name.ToString().Replace(" ", "");
							bool shouldBeActive = activeCameras.Contains(roomName);
							// Only set if property exists to avoid errors
							if (roomNode.HasMethod("set_active"))
							{
								roomNode.Call("set_active", shouldBeActive);
							}
							else 
							{
								// Fallback (shouldn't be needed after fix)
								try
								{
									roomNode.Set("is_active", shouldBeActive);
									if (roomNode.HasMethod("update_visual")) roomNode.Call("update_visual");
								}
								catch (Exception) { /* catch */ }
							}
						}
					}
				}
			}
		}


		// Refresh Interaction Panel if open (for dynamic content like Interview)
		if (_npcDialogueUI.Visible && !string.IsNullOrEmpty(_currentInteractingNpcId))
		{
			RefreshInteractionPanel();
		}


		// Persistent RPS Conversion UI
		string myRoleStr = _myRole?.ToLower() ?? "";
		var conversions = _localGameState?["active_conversions"] as JObject;
		
		var rpsOverlay = _uiLayer.GetNodeOrNull<CenterContainer>("RPSOverlay");
		if (rpsOverlay == null)
		{
			rpsOverlay = new CenterContainer();
			rpsOverlay.Name = "RPSOverlay";
			rpsOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			rpsOverlay.MouseFilter = Control.MouseFilterEnum.Ignore;
			_uiLayer.AddChild(rpsOverlay);
		}

		var rpsRoot = rpsOverlay.GetNodeOrNull<PanelContainer>("RPSRootContainer");
		if (rpsRoot == null)
		{
			rpsRoot = new PanelContainer();
			rpsRoot.Name = "RPSRootContainer";
			
			// Styling: Dark semi-transparent background
			var panelStyle = new StyleBoxFlat();
			panelStyle.BgColor = new Color(0, 0, 0, 0.85f);
			panelStyle.SetContentMarginAll(40); // More padding
			panelStyle.SetCornerRadiusAll(15);
			panelStyle.BorderWidthBottom = 4;
			panelStyle.BorderColor = new Color(1, 1, 1, 0.2f);
			rpsRoot.AddThemeStyleboxOverride("panel", panelStyle);

			rpsOverlay.AddChild(rpsRoot);
		}

		string currentRPSKey = "";
		if (conversions != null && conversions.ContainsKey(myRoleStr))
		{
			var ctx = conversions[myRoleStr];
			string npcId = ctx["npcId"]?.Value<string>();
			string baseActionId = ctx["baseActionId"]?.Value<string>();
			var visibleOpts = ctx["visibleOptions"]?.ToObject<List<string>>();
			
			if (!string.IsNullOrEmpty(npcId) && !string.IsNullOrEmpty(baseActionId) && visibleOpts != null)
			{
				currentRPSKey = $"{myRoleStr}_{npcId}_{baseActionId}";
				
				// Track which NPC is in conversion for distance checking
				_currentConversionNpcId = npcId;
				
				// ALWAYS ensure it's visible if we have a context
				rpsOverlay.Visible = true;
				rpsRoot.Visible = true;

				if (_lastRPSKey != currentRPSKey)
				{
					// Clear existing
					foreach (Node child in rpsRoot.GetChildren()) child.QueueFree();

					rpsRoot.Visible = true;
					
					var mainVBox = new VBoxContainer();
					mainVBox.AddThemeConstantOverride("separation", 30);
					rpsRoot.AddChild(mainVBox);

					// 1. Header (Centered)
					var headerLabel = new Label();
					headerLabel.Text = $"Convert {Capitalize(npcId)} through a game of rock, paper, scissors.";
					headerLabel.AddThemeFontOverride("font", _customFont);
					headerLabel.AddThemeFontSizeOverride("font_size", 26);
					headerLabel.HorizontalAlignment = HorizontalAlignment.Center;
					mainVBox.AddChild(headerLabel);

					// 2. Buttons Container
					var rpsContainer = new HBoxContainer();
					rpsContainer.Name = "RPSContainer";
					rpsContainer.Alignment = BoxContainer.AlignmentMode.Center;
					rpsContainer.AddThemeConstantOverride("separation", 50);
					mainVBox.AddChild(rpsContainer);
					
					foreach (var move in visibleOpts)
					{
						var btn = CreateRPSButton(move, npcId, baseActionId);
						rpsContainer.AddChild(btn);
					}
					_lastRPSKey = currentRPSKey;
				}
			}
			else
			{
				if (rpsOverlay.Visible)
				{
					foreach (Node child in rpsRoot.GetChildren()) child.QueueFree();
					rpsOverlay.Visible = false;
					_lastRPSKey = "";
					_currentConversionNpcId = null;
				}
			}
		}
		else
		{
			if (rpsOverlay != null && rpsOverlay.Visible)
			{
				foreach (Node child in rpsRoot.GetChildren()) child.QueueFree();
				rpsOverlay.Visible = false;
				_lastRPSKey = "";
				_currentConversionNpcId = null;
			}
		}

		// Meters
		foreach (Node child in _metersContainer.GetChildren())
			child.QueueFree();

		if (!string.IsNullOrEmpty(_myRole))
		{
			var players = _localGameState["players"];
			var myRoleState = players?[_myRole.ToLower()];
			if (myRoleState != null)
			{
				var meters = myRoleState["meters"];
				foreach (JProperty meter in meters)
				{
					double val = meter.Value["value"].Value<double>();
					double max = meter.Value["max"].Value<double>();
					var meterControl = CreateMeterControl(meter.Name, val, max, isProducer);
					if (meterControl != null) _metersContainer.AddChild(meterControl);
				}
			}
		}

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
				
				// Check for RPS result in notifications to show the overlay
				if (_rpsResultOverlay != null && msg.Contains("played") && (msg.Contains("WON") || msg.Contains("LOST")))
				{
					// Parse the notification to extract the player's move and result
					// Format: "Prophet played Rock vs Scissors... and WON!"
					// Only show the animation if the LOCAL player is the Prophet
					bool isLocalPlayerProphet = (_myRole?.ToLower() == "prophet");
					
					if (isLocalPlayerProphet)
					{
						bool playerWon = msg.Contains("WON");
						string playerMove = "";
						
						// Extract the player's move (comes after "played " and before " vs")
						int playedIndex = msg.IndexOf("played ");
						int vsIndex = msg.IndexOf(" vs");
						
						if (playedIndex >= 0 && vsIndex > playedIndex)
						{
							string moveText = msg.Substring(playedIndex + 7, vsIndex - (playedIndex + 7)).Trim();
							playerMove = moveText.ToLower();
						}
						
						if (!string.IsNullOrEmpty(playerMove))
						{
							_rpsResultOverlay.Show(playerMove, playerWins: playerWon);
						}
					}
				}
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
						
						// Check if rating increased by exactly +1 and an interview just finished
						if (currentRating == _previousProducerRating + 1)
						{
							GD.Print("[+1 Debug] Rating increased by +1! Checking for interview notification...");
							
							// Check if any of the new notifications mention "Interview finished"
							bool interviewFinished = false;
							// Check only the NEW notifications that were just added (from oldNotificationCount to current)
							for (int i = oldNotificationCount; i < notifList.Count; i++)
							{
								GD.Print($"[+1 Debug] Checking notification {i}: {notifList[i]}");
								if (notifList[i].Contains("Interview finished"))
								{
									interviewFinished = true;
									GD.Print("[+1 Debug] Found 'Interview finished' notification!");
									break;
								}
							}
							
							GD.Print($"[+1 Debug] Interview finished: {interviewFinished}, Timer: {_plusOneTimer}, Overlay null: {_plusOneOverlay == null}");
							
							if (interviewFinished && _plusOneTimer <= 0)
							{
								// Show +1 visual
								if (_plusOneOverlay != null)
								{
									_plusOneOverlay.Visible = true;
									_plusOneTimer = 1.0; // Display for 1 second
									GD.Print("[GameWorld] ✅ Showing +1 rating visual feedback!");
								}
								else
								{
									GD.PrintErr("[+1 Debug] ERROR: _plusOneOverlay is null!");
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
			if (actionId == "set_trap" && roleEnum == Role.Prophet)
			{
				// TODO: Check cooldown or limits if needed
				_gameEngine.CreateTrap(roleEnum);
				_gameEngine.GameState.AddNotification("A trap has been placed by the Prophet.");
				// Spawn a banana at the Prophet's current location (server authoritative), replicate to all clients
				if (_playerControllers.TryGetValue(senderId, out var prophetController))
				{
					var pos = prophetController.Position;
					string trapId = $"Trap_{Time.GetTicksMsec()}_{senderId}";
					GD.Print($"[SubmitAction] Prophet {senderId} placed trap {trapId} at {pos}. Broadcasting to all.");
					Rpc(MethodName.SpawnBananaVisual, pos, trapId, senderId);
				}
				else
				{
					GD.PrintErr($"[SubmitAction] Could not find controller for Prophet {senderId}");
				}
				BroadcastGameState();
			}
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

	private Control CreateRPSButton(string move, string npcId, string baseActionId)
	{
		var btn = new Button();
		btn.CustomMinimumSize = new Vector2(100, 120);
		btn.Flat = true; // No default background
		
		// Transparent styleboxes
		var emptyStyle = new StyleBoxEmpty();
		btn.AddThemeStyleboxOverride("normal", emptyStyle);
		btn.AddThemeStyleboxOverride("hover", emptyStyle);
		btn.AddThemeStyleboxOverride("pressed", emptyStyle);
		btn.AddThemeStyleboxOverride("focus", emptyStyle);

		var vbox = new VBoxContainer();
		vbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		vbox.MouseFilter = Control.MouseFilterEnum.Ignore; // CRITICAL: Container must ignore mouse to let button handle it
		vbox.AddThemeConstantOverride("separation", 5);
		btn.AddChild(vbox);

		// Image
		var tex = new TextureRect();
		string texturePath = $"res://assets/{move.ToLower()}-btn.png";
		if (ResourceLoader.Exists(texturePath))
		{
			tex.Texture = ResourceLoader.Load<Texture2D>(texturePath);
		}
		tex.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		tex.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		tex.MouseFilter = Control.MouseFilterEnum.Ignore; // CRITICAL: Don't block button click
		vbox.AddChild(tex);

		// Name at bottom
		var lbl = new Label();
		lbl.Text = Capitalize(move);
		lbl.AddThemeFontOverride("font", _customFont);
		lbl.AddThemeFontSizeOverride("font_size", 18);
		lbl.AddThemeColorOverride("font_color", Colors.White);
		lbl.HorizontalAlignment = HorizontalAlignment.Center;
		lbl.MouseFilter = Control.MouseFilterEnum.Ignore; // CRITICAL: Don't block button click
		vbox.AddChild(lbl);

		// Hover effect: Darken asset
		btn.MouseEntered += () => tex.Modulate = new Color(0.7f, 0.7f, 0.7f, 1.0f);
		btn.MouseExited += () => tex.Modulate = new Color(1.0f, 1.0f, 1.0f, 1.0f);

		btn.Pressed += () => OnActionSelected(npcId, $"{baseActionId}_{move.ToLower()}");
		
		return btn;
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

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
	private void CancelConversionDueToDistance(string playerRole)
	{
		if (!Multiplayer.IsServer()) return;
		
		GD.Print($"[GameWorld] Cancelling conversion for {playerRole} due to distance");
		
		// Parse the role string to Role enum
		if (Enum.TryParse<Role>(playerRole, ignoreCase: true, out var role))
		{
			// Remove the conversion from the game engine
			if (_gameEngine.GameState.ActiveConversions.ContainsKey(role))
			{
				_gameEngine.GameState.ActiveConversions.Remove(role);
				_gameEngine.GameState.AddNotification($"Conversion cancelled - {playerRole} moved too far away!");
				
				// Broadcast the updated state
				BroadcastGameState();
			}
		}
	}
}
