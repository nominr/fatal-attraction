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
	private InteractionPanel _interactionPanel;
	private CanvasLayer _uiLayer;
	private GoalsMenu _goalsMenu;
	private TextureButton _goalsButton;
	// Editorial asset content node (holds room Area2D children)
	private Node2D _editorialContentNode;
	
	// Interaction tracking
	private string _currentInteractingNpcId = null;
	private string _currentConversionNpcId = null;
	private bool _goalsShownAtStart = false;
	
	private Label _timerLabel;
	private Label _roleLabel;
	private Label _convertedLabel;
	private VBoxContainer _metersContainer;
	private RichTextLabel _notificationText;
	private RPSResultOverlay _rpsResultOverlay;


	private Font _customFont;
	private Texture2D _ratingMeterTexture;
	
	// Hover color for buttons
	private Color _normalColor = new Color(1, 1, 1, 1); // White
	private Color _hoverColor = new Color(1, 0.9f, 0.2f, 1); // Yellowish

	// World bounds
	private Vector2 _worldSize = new Vector2(1200, 800);
	private const int PROPHET_CONVERT_GOAL = 5;

	// Marriage UI State
	private string _marrySelectionA = "";
	private string _marrySelectionB = "";

	// Producer HUD Elements
	private VBoxContainer _producerStatsContainer;
	private Label _activeCamerasLabel;
	private Button _callPoliceButton;

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

	public override void _Ready()
	{
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
			
			GD.Print($"TileMapLayer scaled to {tileMapNode.Scale} and positioned at {tileMapNode.Position}");
			
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
		// GD.Print($"[GameWorld] Body {body.Name} entered {roomId}");
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

		// Eliminated Status Label (Top Center - for non-Admirers)
		var elimLabel = new Label();
		elimLabel.Name = "EliminatedLabel";
		elimLabel.Text = "ADMIRER ELIMINATED"; 
		elimLabel.AddThemeFontSizeOverride("font_size", 24);
		elimLabel.AddThemeColorOverride("font_color", Colors.White);
		// elimLabel.AddThemeColorOverride("font_outline_color", Colors.Black); // Outline for contrast
		// elimLabel.AddThemeConstantOverride("outline_size", 4);
		elimLabel.HorizontalAlignment = HorizontalAlignment.Center;
		elimLabel.Visible = false;
		// Position Manually at top center
		elimLabel.AnchorsPreset = (int)Control.LayoutPreset.TopWide;
		_uiLayer.AddChild(elimLabel);

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
		_convertedLabel.Text = $"Converted: 0/{PROPHET_CONVERT_GOAL}";
		_convertedLabel.AddThemeFontOverride("font", _customFont);
		_convertedLabel.AddThemeFontSizeOverride("font_size", 26);
		_convertedLabel.AddThemeColorOverride("font_color", Colors.White);
		_convertedLabel.Visible = false; // Only relevant for Prophet
		hudContainer.AddChild(_convertedLabel);

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

		_callPoliceButton = new Button();
		_callPoliceButton.Text = "CALL POLICE!";
		_callPoliceButton.Modulate = Colors.Red;
		_callPoliceButton.Visible = false;
		_callPoliceButton.AddThemeFontOverride("font", _customFont);
		_callPoliceButton.Pressed += () => OnActionSelected("producer_global", "call_police");
		_producerStatsContainer.AddChild(_callPoliceButton);

		// Notification Panel (bottom right)
		var viewportSize = GetViewportRect().Size; // Use actual viewport to avoid clipping on smaller windows
		var notifPanel = new PanelContainer();
		notifPanel.CustomMinimumSize = new Vector2(380, 280);
		notifPanel.Position = new Vector2(
			Mathf.Max(20, viewportSize.X - notifPanel.CustomMinimumSize.X - 30),
			Mathf.Max(20, viewportSize.Y - notifPanel.CustomMinimumSize.Y - 30));
		_uiLayer.AddChild(notifPanel);

		var notifMargin = new MarginContainer();
		notifMargin.AddThemeConstantOverride("margin_left", 10);
		notifMargin.AddThemeConstantOverride("margin_top", 10);
		notifMargin.AddThemeConstantOverride("margin_right", 10);
		notifMargin.AddThemeConstantOverride("margin_bottom", 10);
		notifPanel.AddChild(notifMargin);

		_notificationText = new RichTextLabel();
		_notificationText.BbcodeEnabled = true;
		_notificationText.ScrollFollowing = true;
		_notificationText.AddThemeFontOverride("normal_font", _customFont);
		notifMargin.AddChild(_notificationText);
		_notificationText.AddThemeFontSizeOverride("normal_font_size", 22);

		// Interaction Panel
		_interactionPanel = new InteractionPanel();
		_interactionPanel.ActionSelected += OnActionSelected;
		_interactionPanel.PanelClosed += OnInteractionPanelClosed;
		_uiLayer.AddChild(_interactionPanel);

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

		// RPS Result Overlay
		_rpsResultOverlay = new RPSResultOverlay();
		AddChild(_rpsResultOverlay);

		// Producer UI Elements
		SetupProducerUI();
		
		// Goals Menu System
		SetupGoalsMenu();
	}

	private void SetupProducerUI()
	{
		// Marriage Section (Visible)
		var mPanel = new PanelContainer();
		mPanel.Name = "MarriagePanel";
		mPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center); // Center screen
		mPanel.GrowHorizontal = Control.GrowDirection.Both; // Required for true center
		mPanel.GrowVertical = Control.GrowDirection.Both;
		mPanel.CustomMinimumSize = new Vector2(400, 400); // Wider for 2 columns
		mPanel.Visible = false;
		mPanel.ZIndex = 20; // Ensure it's above other UI elements
		
		var mVBoxMain = new VBoxContainer();
		mVBoxMain.Name = "Container"; // Keeping name for reference finding
		mVBoxMain.AddThemeConstantOverride("separation", 10);
		mVBoxMain.MouseFilter = Control.MouseFilterEnum.Pass;
		mPanel.AddChild(mVBoxMain);

		// Header Row (Title + Close Button)
		var headerHBox = new HBoxContainer();
		headerHBox.Name = "HeaderHBox";
		mVBoxMain.AddChild(headerHBox);

		var mTitle = new Label();
		mTitle.Text = "Select 2 NPCs to Marry:";
		mTitle.HorizontalAlignment = HorizontalAlignment.Center;
		mTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; // Center title
		mTitle.AddThemeFontOverride("font", _customFont);
		mTitle.AddThemeFontSizeOverride("font_size", 23);
		headerHBox.AddChild(mTitle);

		var mCloseInfoBtn = new Button();
		mCloseInfoBtn.Text = "X";
		mCloseInfoBtn.AddThemeFontOverride("font", _customFont);
		mCloseInfoBtn.Flat = true;
		mCloseInfoBtn.CustomMinimumSize = new Vector2(30, 30);
		mCloseInfoBtn.Pressed += () => 
		{
			// Close Panel Logic
			mPanel.Visible = false;
			var btn = _uiLayer.GetNodeOrNull<Button>("MarryButton");
			if (btn != null) btn.ButtonPressed = false; 
		};
		headerHBox.AddChild(mCloseInfoBtn);

		// Columns Container
		var columnsHBox = new HBoxContainer();
		columnsHBox.Name = "ColumnsContainer";
		columnsHBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		columnsHBox.AddThemeConstantOverride("separation", 20);
		columnsHBox.MouseFilter = Control.MouseFilterEnum.Pass;
		mVBoxMain.AddChild(columnsHBox);

		// Column A
		var colA = new VBoxContainer();
		colA.Name = "ColumnA";
		colA.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		colA.MouseFilter = Control.MouseFilterEnum.Pass;
		columnsHBox.AddChild(colA);
		var lblA = new Label();
		lblA.Text = "Partner 1";
		lblA.HorizontalAlignment = HorizontalAlignment.Center;
		lblA.AddThemeFontOverride("font", _customFont);
		lblA.AddThemeFontSizeOverride("font_size", 21);
		colA.AddChild(lblA);
		// ScrollContainer for list A
		var scrollA = new ScrollContainer();
		scrollA.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		scrollA.MouseFilter = Control.MouseFilterEnum.Pass; // Allow clicks to pass through if hitting empty space
		colA.AddChild(scrollA);
		var listA = new VBoxContainer();
		listA.Name = "ListA";
		listA.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		listA.MouseFilter = Control.MouseFilterEnum.Pass;
		scrollA.AddChild(listA);

		// Column B
		var colB = new VBoxContainer();
		colB.Name = "ColumnB";
		colB.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		colB.MouseFilter = Control.MouseFilterEnum.Pass;
		columnsHBox.AddChild(colB);
		var lblB = new Label();
		lblB.Text = "Partner 2";
		lblB.HorizontalAlignment = HorizontalAlignment.Center;
		lblB.AddThemeFontOverride("font", _customFont);
		lblB.AddThemeFontSizeOverride("font_size", 21);
		colB.AddChild(lblB);
		// ScrollContainer for list B
		var scrollB = new ScrollContainer();
		scrollB.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		scrollB.MouseFilter = Control.MouseFilterEnum.Pass;
		colB.AddChild(scrollB);
		var listB = new VBoxContainer();
		listB.Name = "ListB";
		listB.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		listB.MouseFilter = Control.MouseFilterEnum.Pass;
		scrollB.AddChild(listB);



		// Confirm Button at bottom
		var mConfirm = new Button();
		mConfirm.Text = "CONFIRM MARRIAGE";
		mConfirm.CustomMinimumSize = new Vector2(0, 50);
		mConfirm.AddThemeFontOverride("font", _customFont);
		mConfirm.AddThemeFontSizeOverride("font_size", 21);
		
		mConfirm.AddThemeStyleboxOverride("normal", CreateTrapStyle(Colors.White, Colors.Black));
		mConfirm.AddThemeStyleboxOverride("hover", CreateTrapStyle(new Color(0.85f, 0.85f, 0.85f, 1), Colors.Black));
		mConfirm.AddThemeStyleboxOverride("pressed", CreateTrapStyle(new Color(0.85f, 0.85f, 0.85f, 1), Colors.Black));
		mConfirm.AddThemeColorOverride("font_color", Colors.Black);
		mConfirm.AddThemeColorOverride("font_hover_color", Colors.Black);
		mConfirm.AddThemeColorOverride("font_pressed_color", Colors.Black);
		mConfirm.MouseFilter = Control.MouseFilterEnum.Stop;
		
		mConfirm.Pressed += OnMarryConfirm;
		mVBoxMain.AddChild(mConfirm);

		_uiLayer.AddChild(mPanel);

		// Marriage Toggle Button
		var marryBtn = new Button();
		marryBtn.Name = "MarryButton";
		marryBtn.Text = "Marry NPCs";
		marryBtn.ToggleMode = true; // Stay pressed when active
		marryBtn.AddThemeFontOverride("font", _customFont);
		marryBtn.AddThemeFontSizeOverride("font_size", 26);
		// Position bottom left (Swapped with Manage Cameras)
		marryBtn.Position = new Vector2(20, 530);
		marryBtn.CustomMinimumSize = new Vector2(180, 60);
		marryBtn.Visible = false;
		
		// Trap Styling for Marry Button
		// Normal/Hover match Trap Normal/Hover
		marryBtn.AddThemeStyleboxOverride("normal", CreateTrapStyle(Colors.White, Colors.Black));
		marryBtn.AddThemeStyleboxOverride("hover", CreateTrapStyle(new Color(0.85f, 0.85f, 0.85f, 1), Colors.Black));
		
		// Pressed matches Trap DISABLED (Dark Grey, No Border)
		var mPressed = CreateTrapStyle(new Color(0.6f, 0.6f, 0.6f, 1), Colors.Black);
		mPressed.SetBorderWidthAll(0);
		marryBtn.AddThemeStyleboxOverride("pressed", mPressed);
		
		var mDisabled = CreateTrapStyle(new Color(0.6f, 0.6f, 0.6f, 1), Colors.Black);
		mDisabled.SetBorderWidthAll(0);
		marryBtn.AddThemeStyleboxOverride("disabled", mDisabled);
		
		marryBtn.AddThemeColorOverride("font_color", Colors.Black);
		marryBtn.AddThemeColorOverride("font_hover_color", Colors.Black);
		marryBtn.AddThemeColorOverride("font_pressed_color", Colors.Black);
		marryBtn.AddThemeColorOverride("font_focus_color", Colors.Black);
		marryBtn.MouseFilter = Control.MouseFilterEnum.Stop; 

		// Use Toggled to bind visibility directly to button state
		marryBtn.Toggled += (pressed) => 
		{
			var panel = _uiLayer.GetNodeOrNull<Control>("MarriagePanel");
			if (panel != null) panel.Visible = pressed;
			
			// Mutual Exclusivity: Close Camera Panel if opening Marriage
			if (pressed)
			{
				var camBtn = _uiLayer.GetNodeOrNull<Button>("ManageCamerasButton");
				if (camBtn != null && camBtn.ButtonPressed) camBtn.ButtonPressed = false;
			}
		};
		_uiLayer.AddChild(marryBtn);
		
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
			
			// Mutual Exclusivity: Close Marriage Panel if opening Cameras
			if (pressed)
			{
				var mBtn = _uiLayer.GetNodeOrNull<Button>("MarryButton");
				if (mBtn != null && mBtn.ButtonPressed) mBtn.ButtonPressed = false;
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
		csLabel.AddThemeFontSizeOverride("font_size", 24); // Larger text
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
								loveInterestList.Add(Capitalize(prop.Name));
							if (isTarget)
								targetsList.Add(Capitalize(prop.Name));
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

	private void OnMarryConfirm()
	{
		if (!string.IsNullOrEmpty(_marrySelectionA) && !string.IsNullOrEmpty(_marrySelectionB))
		{
			OnActionSelected("producer_global", $"marry_{_marrySelectionA}_{_marrySelectionB}");
			_uiLayer.GetNode<Control>("MarriagePanel").Visible = false;
			
			// Unpress the toggle button
			var btn = _uiLayer.GetNodeOrNull<Button>("MarryButton");
			if (btn != null) btn.ButtonPressed = false;
			
			// Reset selections
			_marrySelectionA = "";
			_marrySelectionB = "";
			UpdateMarriageLists(); // Refresh UI to clear checks
		}
		else
		{
			// Show error? For now print
			GD.Print("Must select 2 NPCs");
		}
	}

	private void OnMarrySelectA(string npcId)
	{
		if (_marrySelectionA == npcId) _marrySelectionA = ""; // Toggle off
		else _marrySelectionA = npcId;
		
		UpdateMarriageLists();
	}

	private void OnMarrySelectB(string npcId)
	{
		if (_marrySelectionB == npcId) _marrySelectionB = ""; // Toggle off
		else _marrySelectionB = npcId;
		
		UpdateMarriageLists();
	}

	private void UpdateMarriageLists()
	{
		if (_localGameState == null) return;
		var activeNpcs = _localGameState["active_npcs"]?.ToObject<List<string>>() ?? new();
		
		var panel = _uiLayer?.GetNodeOrNull("MarriagePanel");
		if (panel == null) return;
		
		var listA = panel.FindChild("ListA", true, false) as VBoxContainer;
		var listB = panel.FindChild("ListB", true, false) as VBoxContainer;
		
		if (listA == null || listB == null) return;

		// Rebuild List A
		PopulateMarriageList(listA, activeNpcs, _marrySelectionA, _marrySelectionB, true);
		
		// Rebuild List B
		PopulateMarriageList(listB, activeNpcs, _marrySelectionB, _marrySelectionA, false);
	}

	private void PopulateMarriageList(VBoxContainer listContainer, List<string> npcs, string mySelection, string otherSelection, bool isListA)
	{
		// Ideally we reuse buttons instead of destroy/create every frame, but for low NPC count (10) it's fine
		foreach (Node child in listContainer.GetChildren()) child.QueueFree();

		var npcStates = _localGameState?["npc_states"] as JObject;

		foreach (var npcId in npcs)
		{
			// Check if Love Interest - SKIP
			if (npcStates != null && npcStates[npcId]?["is_love_interest"]?.Value<bool>() == true)
			{
				continue;
			}

			var btn = new Button();
			btn.ToggleMode = true;
			btn.Text = Capitalize(npcId);
			btn.AddThemeFontOverride("font", _customFont);
			btn.AddThemeFontSizeOverride("font_size", 21);
			
			// Trap Styling for List Items
			btn.AddThemeStyleboxOverride("normal", CreateTrapStyle(Colors.White, Colors.Black));
			btn.AddThemeStyleboxOverride("hover", CreateTrapStyle(new Color(0.85f, 0.85f, 0.85f, 1), Colors.Black));
			// Selected (Pressed) -> Black with White Border
			btn.AddThemeStyleboxOverride("pressed", CreateTrapStyle(Colors.Black, Colors.White)); 
			btn.AddThemeStyleboxOverride("disabled", CreateTrapStyle(Colors.Gray, Colors.Black));

			btn.AddThemeColorOverride("font_color", Colors.Black);
			btn.AddThemeColorOverride("font_hover_color", Colors.Black);
			btn.AddThemeColorOverride("font_pressed_color", Colors.White); // White text when selected
			btn.AddThemeColorOverride("font_focus_color", Colors.Black);
			btn.MouseFilter = Control.MouseFilterEnum.Stop;
			
			// Check state
			btn.ButtonPressed = (npcId == mySelection);
			
			// Disable if selected in other list
			if (npcId == otherSelection)
			{
				btn.Disabled = true;
			}
			
			// Connect signal
			if (isListA) btn.Pressed += () => OnMarrySelectA(npcId);
			else btn.Pressed += () => OnMarrySelectB(npcId);
			
			listContainer.AddChild(btn);
		}
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
		
		// Store current interacting NPC ID for proximity tracking
		_currentInteractingNpcId = npcId;
		_interactionPanel.ShowForNPC(npcId, npcName, desc, actionsList);
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
	
	private void OnInteractionPanelClosed()
	{
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

	public override void _Process(double delta)
	{
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
	}
	
	public override void _PhysicsProcess(double delta)
	{
		// If interaction panel is visible, check if player is still in range of NPC
		if (_interactionPanel != null && _interactionPanel.Visible && _currentInteractingNpcId != null)
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
					_interactionPanel.Hide();
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
	}

	private void CheckProducerPanelsOnMove()
	{
		if (!GodotObject.IsInstanceValid(_localPlayer) || _localPlayer.Velocity.LengthSquared() < 100) return; // Not moving significantly

		// Check Marriage Panel
		var mPanel = _uiLayer.GetNodeOrNull<Control>("MarriagePanel");
		if (mPanel != null && mPanel.Visible)
		{
			mPanel.Visible = false;
			var btn = _uiLayer.GetNodeOrNull<Button>("MarryButton");
			if (btn != null) btn.SetPressedNoSignal(false);
		}

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
		foreach (var npc in _gameEngine.GameState.NPCs.Values)
		{
			var stateObj = new JObject
			{
				{ "alive", npc.Alive },
				{ "converted", npc.Converted },
				{ "married", npc.Married },
				{ "is_love_interest", npc.IsLoveInterest } // Expose for UI filtering
			};
			
			// Include Position (SERVER AUTHORITY)
			if (_npcEntities.TryGetValue(npc.Id, out var entity))
			{
				stateObj["pos_x"] = entity.Position.X;
				stateObj["pos_y"] = entity.Position.Y;
			}
			
			npcStates[npc.Id] = stateObj;
		}
		status["npc_states"] = npcStates;

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
							loveInterestList.Add(Capitalize(prop.Name));
						if (isTarget)
							targetsList.Add(Capitalize(prop.Name));
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
			if (!_interactionPanel.Visible || _currentInteractingNpcId != interviewNpcId)
			{
				_currentInteractingNpcId = interviewNpcId;
				RefreshInteractionPanel(); 
			}
			else
			{
				// Just refresh content
				RefreshInteractionPanel();
			}
		}
		else if (_interactionPanel.Visible && !string.IsNullOrEmpty(_currentInteractingNpcId))
		{
			RefreshInteractionPanel();
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
		_interactionPanel.ShowForNPC(npcId, npcName, desc, actionsList);
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

		// Role
		long myId = Multiplayer.GetUniqueId();
		if (_networkManager.Players.ContainsKey(myId))
		{
			_myRole = _networkManager.Players[myId].Role;
		}
		// Role label hidden per user request
		// _roleLabel.Text = $"Role: {_myRole?.ToUpper()}";

		bool isProphet = (_myRole?.ToLower() == "prophet");

		// Update Trap Button Visibility
		// Update Trap Button Visibility
		if (_uiLayer.GetNodeOrNull<Button>("TrapButton") is Button trapBtn)
		{
			trapBtn.Visible = isProphet;
		}

		// Prophet conversion progress
		if (_convertedLabel != null)
		{
			var npcStates = _localGameState?["npc_states"] as JObject;
			int convertedCount = npcStates?.Properties()
				.Where(p => p.Value["converted"]?.Value<bool>() == true)
				.Count() ?? 0;

			int goal = PROPHET_CONVERT_GOAL;
			convertedCount = Math.Min(convertedCount, goal);
			_convertedLabel.Text = $"Converted: {convertedCount}/{goal}";
			_convertedLabel.Visible = isProphet;
		}


		// PRODUCER ACTIONS VISIBILITY
		var marryBtn = _uiLayer.GetNodeOrNull<Button>("MarryButton");
		var manageCamsBtn = _uiLayer.GetNodeOrNull<Button>("ManageCamerasButton");
		var cameraPanel = _uiLayer.GetNodeOrNull<Control>("CameraPanel");
		bool isProducer = (_myRole?.ToLower() == "producer");
		if (marryBtn != null) marryBtn.Visible = isProducer;
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
			
			if (_callPoliceButton != null)
			{
				// Only show if caught AND not yet eliminated
				_callPoliceButton.Visible = admirerCaught && !isEliminated;
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

		if (isProducer)
		{
			// Update Marriage Panel List if needed
			var mPanel = _uiLayer.GetNodeOrNull<Control>("MarriagePanel");
			if (mPanel != null && mPanel.Visible)
			{
				var activeNpcs = _localGameState["active_npcs"]?.ToObject<List<string>>() ?? new();
				var listA = mPanel.FindChild("ListA", true, false) as VBoxContainer;
				
				// Rebuild if empty or count mismatch (e.g. new NPC)
				// FIX: Must filter activeNpcs same way PopulateMarriageList does to avoid infinite update loop
				var npcStates = _localGameState?["npc_states"] as JObject;
				int visibleNpcs = 0;
				foreach (var npc in activeNpcs)
				{
					if (npcStates != null && npcStates[npc]?["is_love_interest"]?.Value<bool>() == true) continue;
					visibleNpcs++;
				}
				
				if (listA != null && listA.GetChildCount() != visibleNpcs)
				{
					UpdateMarriageLists();
				}
			}
		}

		// Refresh Interaction Panel if open (for dynamic content like Interview)
		if (_interactionPanel.Visible && !string.IsNullOrEmpty(_currentInteractingNpcId))
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
			foreach (string msg in notifs)
			{
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


		// Admirer Elimination Check
		bool admirerEliminated = _localGameState["admirer_eliminated"]?.Value<bool>() ?? false;
		var elimLabel = _uiLayer.GetNodeOrNull<Label>("EliminatedLabel");
		var gameOverOverlay = _uiLayer.GetNodeOrNull<Control>("GameOverOverlay");

		if (admirerEliminated)
		{
			bool isAdmirer = (_myRole?.ToLower() == "admirer");
			
			if (isAdmirer)
			{
				// I AM ELIMINATED
				if (gameOverOverlay != null) 
				{
					gameOverOverlay.Visible = true;
					// Ensure it blocks mouse if possible? PanelContainer usually monitors mouse.
				}
				if (_localPlayer != null)
				{
					_localPlayer.InputEnabled = false;
					_localPlayer.Velocity = Vector2.Zero;
				}
				if (elimLabel != null) elimLabel.Visible = false; // Don't show top label
			}
			else
			{
				// Someone else eliminated
				if (elimLabel != null) 
				{
					elimLabel.Visible = true;
					elimLabel.Text = "ADMIRER HAS BEEN ELIMINATED"; // Requirements: "white text on top... producer lost (sic: admirer lost)"
				}
				if (gameOverOverlay != null) gameOverOverlay.Visible = false;
			}
		}
		else if (!isGameOver) // Only hide if game isn't over otherwise
		{
			// Reset if new game
			if (elimLabel != null) elimLabel.Visible = false;
			if (gameOverOverlay != null) gameOverOverlay.Visible = false;
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

				kvp.Value.UpdateState(alive, converted, married);
			}
		}
	}

	private void UpdateGhostMode()
	{
		// Check for Admirer Elimination
		bool admirerEliminated = _localGameState?["admirer_eliminated"]?.Value<bool>() ?? false;
		
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
			
			bool isAdmirer = role.ToLower() == "admirer";
			
			// Enable Ghost Mode if it's the Admirer and they are eliminated
			// Note: We might want to expand this to any eliminated role in future
			if (isAdmirer && admirerEliminated)
			{
				// Only set if not already set (optimize?) - SetGhostMode handles internal checks or lightweight assignment
				// Check collision layer to see if update needed? 
				// Just call it, it's cheap.
				controller.SetGhostMode(true);
			}
			else
			{
				controller.SetGhostMode(false);
			}
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
					Rpc(MethodName.SpawnBananaVisual, pos, trapId);
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
	public void SpawnBananaVisual(Vector2 position, string trapId)
	{
		GD.Print($"[SpawnBananaVisual] Spawning banana {trapId} at {position} on peer {Multiplayer.GetUniqueId()}");
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

		var sprite = new Sprite2D();
		sprite.Texture = bananaTexture;
		sprite.Scale = new Vector2(1.5f, 1.5f); // Reverted to original size
		bananaRoot.AddChild(sprite);

		// Collision detector
		var area = new Area2D();
		area.Monitoring = true;
		area.Monitorable = true;
		area.CollisionMask = 4; // Detect bodies on NPC layer (NPCEntity)
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
				if (body is NPCEntity npc)
				{
					// Notify and apply slip globally
					_gameEngine.GameState.AddNotification($"A trap has been triggered! {npc.NpcName} was caught in the banana trap!");
					
					// Sync the slip and removal to ALL clients
					Rpc(MethodName.SyncTrapTriggered, trapId, npc.NpcId);
					
					// Increase Prophet's chaos by 1
					var prophetState = _gameEngine.GameState.GetPlayerState(Role.Prophet);
					if (prophetState != null)
					{
						var chaosMeter = prophetState.GetMeter("chaos");
						if (chaosMeter != null)
						{
							chaosMeter.Add(1);
							_gameEngine.GameState.AddNotification($"Prophet gained Chaos! ({chaosMeter.Value}/{chaosMeter.MaxValue})");
						}
					}
					
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
