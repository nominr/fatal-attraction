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
	
	// Interaction tracking
	private string _currentInteractingNpcId = null;
	private bool _goalsShownAtStart = false;
	
	private Label _timerLabel;
	private Label _roleLabel;
	private Label _convertedLabel;
	private VBoxContainer _metersContainer;
	private RichTextLabel _notificationText;


	private Font _customFont;
	
	// Hover color for buttons
	private Color _normalColor = new Color(1, 1, 1, 1); // White
	private Color _hoverColor = new Color(1, 0.9f, 0.2f, 1); // Yellowish

	// World bounds
	private Vector2 _worldSize = new Vector2(1200, 800);
	private const int PROPHET_CONVERT_GOAL = 5;

	public override void _Ready()
	{
		// RUN DEBUG TESTS
		FatalAttraction.Tests.MurderTest.RunTests();

		_networkManager = GetNode<NetworkManager>("/root/NetworkManager");
		// Listen for network player events to keep controllers in sync
		_networkManager.PlayerConnected += OnNetworkPlayerConnected;
		_networkManager.PlayerDisconnected += OnNetworkPlayerDisconnected;
		_networkManager.PlayerConnected += OnNetworkPlayerConnected;
		_networkManager.PlayerDisconnected += OnNetworkPlayerDisconnected;
		
		// Connect Room Signals
		ConnectRoomSignals();
		
		SetupUI();
		// TileMap is now defined in GameWorld.tscn scene file
		
		// Debug: Check if TileMapLayer loaded from scene and scale it
		var tileMapLayer = GetNodeOrNull("TileMapLayer");
		if (tileMapLayer != null)
		{
			GD.Print($"TileMapLayer found in scene! Type: {tileMapLayer.GetType().Name}");
			
			// Scale the tilemap to fill the viewport
			// The tilemap uses 32x32 tiles, and we want it to fill 1200x800 world
			// Assuming the tile layout is roughly 37.5 tiles wide x 25 tiles tall
			// We can scale it up by a factor to make it visible
			if (tileMapLayer is Node2D tileMapNode)
			{
				// Scale tilemap to match character sprite scale (4x)
				tileMapNode.Scale = new Vector2(4.0f, 4.0f);
				// Center the tilemap - offset it to align with viewport center
				// The tilemap data uses negative coordinates, so we need to offset it
				tileMapNode.Position = new Vector2(500, 200); // Center of 1200x800 world
				tileMapNode.ZIndex = -10; // Ensure it's behind everything
				GD.Print($"TileMapLayer scaled to {tileMapNode.Scale} and positioned at {tileMapNode.Position}");
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
				bool shapesUpdated = false;

				// FIX: Hallways Area2D in scene doesn't match NPC coordinates.
				if (rName == "Hallways")
				{
					// Clear existing incorrect shapes
					foreach (Node child in area.GetChildren())
					{
						if (child is CollisionShape2D) child.QueueFree();
					}

					// Define Corridors (MinX, MaxX, MinY, MaxY)
					var corridors = new[]
					{
						new Vector4(1564, 1612, 547, 900),   // C0
						new Vector4(156, 204, 547, 900),     // C1
						new Vector4(924, 972, 1056, 1370),   // C2
						new Vector4(2716, 2764, 1056, 1370)  // C3
					};

					foreach (var c in corridors)
					{
						float width = c.Y - c.X;
						float height = c.W - c.Z;
						float centerX = c.X + width / 2;
						float centerY = c.Z + height / 2;

						var shape = new CollisionShape2D();
						var rect = new RectangleShape2D();
						rect.Size = new Vector2(width, height);
						shape.Shape = rect;
						// Convert global coordinate to local coordinate relative to the Area2D
						shape.Position = new Vector2(centerX, centerY) - area.Position;
						area.AddChild(shape);
					}
					shapesUpdated = true;
				}
				else if (rName.StartsWith("Room"))
				{
					// FIX: Rooms also need to match NPCEntity coordinates EXACTLY
					if (int.TryParse(rName.Substring(4), out int roomNum))
					{
						int roomIndex = roomNum - 1;
						// Definitions from NPCEntity.cs (Must match!)
						var roomDefs = new[]
						{
							new Vector4(50, 1000, 175, 400),       // Room 1 (Idx 0)
							new Vector4(-300, 2700, 930, 950),     // Room 2 (Idx 1)
							new Vector4(2500, 2850, 1450, 1750),   // Room 3 (Idx 2) - Fixed height
							new Vector4(580, 2030, 1450, 1740),    // Room 4 (Idx 3)
							new Vector4(1600, 2300, 160, 440)      // Room 5 (Idx 4)
						};

						if (roomIndex >= 0 && roomIndex < roomDefs.Length)
						{
							// Clear existing
							foreach (Node child in area.GetChildren())
							{
								if (child is CollisionShape2D) child.QueueFree();
							}

							var def = roomDefs[roomIndex];
							float width = def.Y - def.X;
							float height = def.W - def.Z;
							float centerX = def.X + width / 2;
							float centerY = def.Z + height / 2;

							var shape = new CollisionShape2D();
							var rect = new RectangleShape2D();
							rect.Size = new Vector2(width, height);
							shape.Shape = rect;
							// Convert global coordinate to local coordinate relative to the Area2D
							shape.Position = new Vector2(centerX, centerY) - area.Position;
							area.AddChild(shape);
							// GD.Print($"[GameWorld] Fixed Shape for {rName}: {rect.Size} at {shape.Position}");
							shapesUpdated = true;
						}
					}
				}

				if (shapesUpdated)
				{
					GD.Print($"[GameWorld] Updated collision shapes for {rName}");
				}

				// We need to capture the room name variable for the lambda
				string capturedRoomName = rName;
				
				// Ensure Area monitors the NPC layer (Layer 3/Value 4 based on NPCEntity.cs)
				// NPCEntity uses CollisionLayer = 4. 
				// We'll set Mask to include 4 (plus 1 for players etc just in case).
				area.CollisionMask = 0xFF; // Monitor first 8 layers
				area.Monitorable = false; // Room areas don't need to be detected by others
				area.Monitoring = true;
				
				area.BodyEntered += (body) => OnBodyEnteredRoom(body, capturedRoomName);
			}
			else
			{
				GD.PrintErr($"Room Area not found: {rName}");
			}
		}
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
		_uiLayer.AddChild(hudContainer);

		_timerLabel = new Label();
		_timerLabel.Text = "Time: 05:00";
		_timerLabel.AddThemeFontOverride("font", _customFont);
		_timerLabel.AddThemeFontSizeOverride("font_size", 20);
		hudContainer.AddChild(_timerLabel);
		
		_roleLabel = new Label();
		_roleLabel.Text = "";
		_roleLabel.Visible = false;
		_roleLabel.AddThemeFontOverride("font", _customFont);
		_roleLabel.AddThemeFontSizeOverride("font_size", 18);
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
		_convertedLabel.Visible = false; // Only relevant for Prophet
		hudContainer.AddChild(_convertedLabel);

		_metersContainer = new VBoxContainer();
		hudContainer.AddChild(_metersContainer);

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

		// Interaction Panel
		_interactionPanel = new InteractionPanel();
		_interactionPanel.ActionSelected += OnActionSelected;
		_interactionPanel.PanelClosed += OnInteractionPanelClosed;
		_uiLayer.AddChild(_interactionPanel);

		// Prophet Trap Button
		var trapButton = new Button();
		trapButton.Text = "Set Trap";
		trapButton.AddThemeFontOverride("font", _customFont);
		trapButton.Position = new Vector2(20, 600);
		trapButton.Pressed += () => OnActionSelected("global", "set_trap");
		_uiLayer.AddChild(trapButton);
		// Only visible if Prophet (handled in UpdateUI or default hidden?)
		// Ideally we verify role in UpdateUI.
		trapButton.Name = "TrapButton";
		trapButton.Visible = false;

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
		mPanel.Position = new Vector2(20, 150);
		mPanel.CustomMinimumSize = new Vector2(250, 200);
		mPanel.Visible = false;
		var mVBox = new VBoxContainer();
		mVBox.Name = "Container";
		mVBox.AddThemeConstantOverride("separation", 5);
		mPanel.AddChild(mVBox);
		var mLabel = new Label();
		mLabel.Text = "Select 2 NPCs to Marry:";
		mLabel.AddThemeFontOverride("font", _customFont);
		mLabel.AddThemeFontSizeOverride("font_size", 14);
		mVBox.AddChild(mLabel);
		// NPCs populated dynamically
		var mConfirm = new Button();
		mConfirm.Text = "CONFIRM MARRIAGE";
		mConfirm.AddThemeFontOverride("font", _customFont);
		mConfirm.Pressed += OnMarryConfirm;
		mVBox.AddChild(mConfirm);
		_uiLayer.AddChild(mPanel);

		// Marriage Toggle Button
		var marryBtn = new Button();
		marryBtn.Name = "MarryButton";
		marryBtn.Text = "Marry NPCs (+1 Ratings)";
		marryBtn.AddThemeFontOverride("font", _customFont);
		marryBtn.Position = new Vector2(20, 480);
		marryBtn.Visible = false;
		marryBtn.Pressed += () => TogglePanel("MarriagePanel");
		_uiLayer.AddChild(marryBtn);
		// Security Cameras Section (Visible)
		var cPanel = new PanelContainer();
		cPanel.Name = "CameraPanel";
		cPanel.Position = new Vector2(950, 120);
		cPanel.CustomMinimumSize = new Vector2(220, 200);
		cPanel.Visible = false;
		var cVBox = new VBoxContainer();
		cVBox.Name = "CameraContainer";
		cVBox.AddThemeConstantOverride("separation", 5);
		cPanel.AddChild(cVBox);
		var cLabel = new Label();
		cLabel.Text = "Security Cameras";
		cLabel.AddThemeFontSizeOverride("font_size", 14);
		cLabel.AddThemeFontOverride("font", _customFont);
		cVBox.AddChild(cLabel);
		
		var cInfo = new Label();
		cInfo.Name = "CameraInfo";
		cInfo.Text = "Active: None";
		cInfo.AddThemeFontOverride("font", _customFont);
		cInfo.AutowrapMode = TextServer.AutowrapMode.Word;
		cVBox.AddChild(cInfo);
		
		var cBtn = new Button();
		cBtn.Text = "Manage Cameras";
		cBtn.AddThemeFontOverride("font", _customFont);
		cBtn.Pressed += () => TogglePanel("CameraSelectPanel");
		cVBox.AddChild(cBtn);

		// Call Police Button (Initially Hidden)
		var policeBtn = new Button();
		policeBtn.Name = "CallPoliceButton";
		policeBtn.Text = "CALL POLICE!";
		policeBtn.Modulate = Colors.Red;
		policeBtn.Visible = false;
		policeBtn.Pressed += () => OnActionSelected("producer_global", "call_police");
		policeBtn.AddThemeFontOverride("font", _customFont);
		cVBox.AddChild(policeBtn);

		_uiLayer.AddChild(cPanel);


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
		// Move panel to Top Center but lower down
		csPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
		csPanel.Position = new Vector2(csPanel.Position.X, 150); // Increased margin from 80 to 150
		csPanel.GrowHorizontal = Control.GrowDirection.Both;
		csPanel.GrowVertical = Control.GrowDirection.Both;
		csPanel.Visible = false;
		
		var csVBox = new VBoxContainer();
		csVBox.AddThemeConstantOverride("separation", 10);
		csPanel.AddChild(csVBox);
		var csLabel = new Label();
		csLabel.Text = "Toggle Cameras (Max 2):";
		csLabel.AddThemeFontSizeOverride("font_size", 14);
		csLabel.AddThemeFontOverride("font", _customFont);
		csVBox.AddChild(csLabel);
		
		// Editorial Room Asset Integration
		var svContainer = new SubViewportContainer();
		// Reduced size significantly
		svContainer.CustomMinimumSize = new Vector2(300, 200);
		svContainer.Stretch = true;
		csVBox.AddChild(svContainer);

		var subViewport = new SubViewport();
		subViewport.Size = new Vector2I(300, 200); 
		subViewport.Disable3D = true;
		subViewport.TransparentBg = true;
		subViewport.PhysicsObjectPicking = true;
		svContainer.AddChild(subViewport);

		// Adjusted Camera:
		// Position: 640, 360 (Asset Center) because 620 was too low (shifting asset up).
		// Zoom: 0.45 (Larger than 0.3)
		var camera = new Camera2D();
		camera.Position = new Vector2(640, 360); 
		camera.Zoom = new Vector2(0.5f, 0.5f); 
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
		closeBtn.Text = "Close";
		closeBtn.AddThemeFontOverride("font", _customFont);
		closeBtn.Pressed += () => csPanel.Visible = false;
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
		var panel = _uiLayer.GetNodeOrNull("MarriagePanel/Container");
		if (panel == null) return;

		List<string> selected = new();
		foreach (var node in panel.GetChildren())
		{
			if (node is CheckButton cb && cb.ButtonPressed)
			{
				selected.Add(cb.Name); // Name holds NPC ID
			}
		}

		if (selected.Count == 2)
		{
			OnActionSelected("producer_global", $"marry_{selected[0]}_{selected[1]}");
			_uiLayer.GetNode<Control>("MarriagePanel").Visible = false;
			// Reset checks?
			foreach (var node in panel.GetChildren()) if (node is CheckButton cb) cb.ButtonPressed = false;
		}
		else
		{
			// Show error? For now print
			GD.Print("Must select exactly 2 NPCs");
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

		var actionsList = npcActions?.ToObject<List<JToken>>() ?? new List<JToken>();
		
		// Store current interacting NPC ID for proximity tracking
		_currentInteractingNpcId = npcId;
		_interactionPanel.ShowForNPC(npcId, npcName, desc, actionsList);
	}

	private void OnActionSelected(string npcId, string actionId)
	{
		// Send to server
		RpcId(1, MethodName.SubmitAction, npcId, actionId);
	}
	
	private void OnInteractionPanelClosed()
	{
		_currentInteractingNpcId = null;
		GD.Print("Interaction panel closed, cleared current NPC");
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
			foreach (var kvp in _npcEntities)
			{
				var npcEntity = kvp.Value;
				var npcData = _gameEngine.GameState.GetNPC(kvp.Key);
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
			var npc = _npcEntities.GetValueOrDefault(_currentInteractingNpcId);
			if (npc != null && _localPlayer != null)
			{
				float distance = _localPlayer.Position.DistanceTo(npc.Position);
				// Close menu if player is too far (150 = interaction range + buffer)
				if (distance > 150)
				{
					GD.Print($"Player moved too far from NPC {_currentInteractingNpcId} (distance: {distance}), closing menu");
					_interactionPanel.Hide();
					_currentInteractingNpcId = null;
				}
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
		foreach (var npc in _gameEngine.GameState.NPCs.Values)
		{
			var stateObj = new JObject
			{
				{ "alive", npc.Alive },
				{ "converted", npc.Converted },
				{ "married", npc.Married }
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
		var cameraPanel = _uiLayer.GetNodeOrNull<Control>("CameraPanel");
		bool isProducer = (_myRole?.ToLower() == "producer");
		if (marryBtn != null) marryBtn.Visible = isProducer;
		if (cameraPanel != null) cameraPanel.Visible = isProducer;
		
		if (isProducer && cameraPanel != null)
		{
			// Update Active Cameras Text
			var activeCameras = _localGameState?["active_camera_room_ids"]?.ToObject<List<string>>() ?? new List<string>();
			var infoLabel = cameraPanel.GetNodeOrNull<Label>("CameraContainer/CameraInfo");
			if (infoLabel != null)
			{
				infoLabel.Text = activeCameras.Count > 0 
					? $"Active: {string.Join(", ", activeCameras)}"
					: "Active: None";
			}
			
			// Update Call Police Button
			var policeBtn = cameraPanel.GetNodeOrNull<Button>("CameraContainer/CallPoliceButton");
			bool admirerCaught = _localGameState?["admirer_caught"]?.Value<bool>() ?? false;
			if (policeBtn != null)
			{
				policeBtn.Visible = admirerCaught;
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
			}
		}

		if (isProducer)
		{
			// Update Marriage Panel List if needed
			var mPanelBox = _uiLayer.GetNodeOrNull<VBoxContainer>("MarriagePanel/Container");
			if (mPanelBox != null)
			{
				var activeNpcs = _localGameState["active_npcs"]?.ToObject<List<string>>() ?? new();
				// Simple check: if checkbox count != npc count, rebuild
				int checkBoxCount = mPanelBox.GetChildren().OfType<CheckButton>().Count();
				if (checkBoxCount != activeNpcs.Count)
				{
					// Remove old checks
					foreach (var child in mPanelBox.GetChildren().OfType<CheckButton>().ToList()) child.QueueFree();
					
					// Add new checks (Insert before Confirm button)
					int idx = 1; // After Label
					foreach (var npcId in activeNpcs)
					{
						var cb = new CheckButton();
						cb.Name = npcId; // Store ID in Name
						cb.Text = Capitalize(npcId);
						cb.AddThemeFontOverride("font", _customFont);
						mPanelBox.AddChild(cb);
						mPanelBox.MoveChild(cb, idx++);
					}
				}
			}
		}


		// Persistent RPS Conversion UI
		// myId already defined above
		string myRoleStr = _myRole?.ToLower() ?? "";
		
		var conversions = _localGameState?["active_conversions"] as JObject;
		var rpsContainer = _uiLayer.GetNodeOrNull<HBoxContainer>("RPSContainer");
		
		if (rpsContainer == null)
		{
			rpsContainer = new HBoxContainer();
			rpsContainer.Name = "RPSContainer";
			rpsContainer.Position = new Vector2(400, 600); // Center-ish
			_uiLayer.AddChild(rpsContainer);
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

				// Only rebuild if the context has changed
				if (_lastRPSKey != currentRPSKey)
				{
					// Clear existing buttons
					foreach (Node child in rpsContainer.GetChildren())
					{
						child.QueueFree();
					}

					rpsContainer.Visible = true;
					
					// Show Header
					var label = new Label();
					label.Text = $"CONVERT {Capitalize(npcId)}:";
					label.AddThemeFontOverride("font", _customFont); // Apply custom font
					rpsContainer.AddChild(label);
					
					foreach (var move in visibleOpts)
					{
						var btn = new Button();
						btn.Text = Capitalize(move); // Display "Rock"
						btn.AddThemeFontOverride("font", _customFont);
						// Action ID format: baseActionId + "_" + move.ToLower() e.g. "convert_katy_rock"
						btn.Pressed += () => OnActionSelected(npcId, $"{baseActionId}_{move.ToLower()}");
						rpsContainer.AddChild(btn);
					}
					_lastRPSKey = currentRPSKey;
				}
			}
			else
			{
				// If context is invalid, hide and clear
				if (rpsContainer.Visible)
				{
					foreach (Node child in rpsContainer.GetChildren()) child.QueueFree();
					rpsContainer.Visible = false;
					_lastRPSKey = "";
				}
			}
		}
		else
		{
			// No active conversion for this role, hide and clear
			if (rpsContainer.Visible)
			{
				foreach (Node child in rpsContainer.GetChildren()) child.QueueFree();
				rpsContainer.Visible = false;
				_lastRPSKey = "";
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
					var label = new Label();
					double val = meter.Value["value"].Value<double>();
					double max = meter.Value["max"].Value<double>();
					label.Text = $"{meter.Name.ToUpper()}: {val:F1}/{max:F0}";
					label.AddThemeFontOverride("font", _customFont);
					_metersContainer.AddChild(label);
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
		string senderRole = _networkManager.Players[senderId].Role.ToLower();
		Role roleEnum = Enum.Parse<Role>(senderRole, true);

		// Special Handling: Global Actions (e.g. Set Trap)
		if (npcId == "global")
		{
			if (actionId == "set_trap" && roleEnum == Role.Prophet)
			{
				// TODO: Check cooldown or limits if needed
				_gameEngine.CreateTrap(roleEnum);
				BroadcastGameState();
			}
			return;
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
}
