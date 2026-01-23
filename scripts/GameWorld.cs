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
	private double _timeRemaining = 180.0;

	// Local State Cache
	private JObject _localGameState;
	private string _myRole;

	// Spawned entities
	private Dictionary<string, NPCEntity> _npcEntities = new();
	private Dictionary<long, PlayerController> _playerControllers = new();
	private PlayerController _localPlayer;
	
	// UI Components
	private InteractionPanel _interactionPanel;
	private CanvasLayer _uiLayer;
	private Label _timerLabel;
	private Label _roleLabel;
	private VBoxContainer _metersContainer;
	private RichTextLabel _notificationText;

	// World bounds
	private Vector2 _worldSize = new Vector2(1200, 800);

	public override void _Ready()
	{
		_networkManager = GetNode<NetworkManager>("/root/NetworkManager");
		// Listen for network player events to keep controllers in sync
		_networkManager.PlayerConnected += OnNetworkPlayerConnected;
		_networkManager.PlayerDisconnected += OnNetworkPlayerDisconnected;
		GD.Print($"[GameWorld] _Ready called on peer {Multiplayer.GetUniqueId()}, IsServer={Multiplayer.IsServer()}");
		SetupUI();
		SetupBackground();

		if (Multiplayer.IsServer())
		{
			GD.Print($"[GameWorld] Initializing as SERVER");
			InitializeServer();
		}
		else
		{
			GD.Print($"[GameWorld] Initializing as CLIENT");
			InitializeClient();
		}
	}

	private void SetupUI()
	{
		_uiLayer = new CanvasLayer();
		AddChild(_uiLayer);

		// HUD Container
		var hudContainer = new VBoxContainer();
		hudContainer.Position = new Vector2(20, 20);
		_uiLayer.AddChild(hudContainer);

		_timerLabel = new Label();
		_timerLabel.Text = "Time: 05:00";
		_timerLabel.AddThemeFontSizeOverride("font_size", 20);
		hudContainer.AddChild(_timerLabel);

		_roleLabel = new Label();
		_roleLabel.Text = "Role: Waiting...";
		_roleLabel.AddThemeFontSizeOverride("font_size", 18);
		hudContainer.AddChild(_roleLabel);

		_metersContainer = new VBoxContainer();
		hudContainer.AddChild(_metersContainer);

		// Notification Panel (bottom right)
		var notifPanel = new PanelContainer();
		notifPanel.Position = new Vector2(800, 500);
		notifPanel.CustomMinimumSize = new Vector2(380, 280);
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
		notifMargin.AddChild(_notificationText);

		// Interaction Panel
		_interactionPanel = new InteractionPanel();
		_interactionPanel.ActionSelected += OnActionSelected;
		_uiLayer.AddChild(_interactionPanel);

		// Prophet Trap Button
		var trapButton = new Button();
		trapButton.Text = "Set Trap (+1 Chaos)";
		trapButton.Position = new Vector2(20, 600);
		trapButton.Pressed += () => OnActionSelected("global", "set_trap");
		_uiLayer.AddChild(trapButton);
		// Only visible if Prophet (handled in UpdateUI or default hidden?)
		// Ideally we verify role in UpdateUI.
		trapButton.Name = "TrapButton";
		trapButton.Visible = false;

		// Producer UI Elements
		SetupProducerUI();
	}

	private void SetupProducerUI()
	{
		// Marriage Button
		var marryBtn = new Button();
		marryBtn.Name = "MarryButton";
		marryBtn.Text = "Marry NPCs (+1 Ratings)";
		marryBtn.Position = new Vector2(20, 640);
		marryBtn.Visible = false;
		marryBtn.Pressed += () => TogglePanel("MarriagePanel");
		_uiLayer.AddChild(marryBtn);

		// Editorial Focus Button
		var focusBtn = new Button();
		focusBtn.Name = "FocusButton";
		focusBtn.Text = "Set Editorial Focus";
		focusBtn.Position = new Vector2(20, 680);
		focusBtn.Visible = false;
		focusBtn.Pressed += () => TogglePanel("FocusPanel");
		_uiLayer.AddChild(focusBtn);

		// Marriage Panel (Hidden)
		var mPanel = new PanelContainer();
		mPanel.Name = "MarriagePanel";
		mPanel.Position = new Vector2(200, 200);
		mPanel.Visible = false;
		var mVBox = new VBoxContainer();
		mVBox.Name = "Container";
		mPanel.AddChild(mVBox);
		var mLabel = new Label();
		mLabel.Text = "Select 2 NPCs to Marry:";
		mVBox.AddChild(mLabel);
		// NPCs populated dynamically
		var mConfirm = new Button();
		mConfirm.Text = "CONFIRM MARRIAGE";
		mConfirm.Pressed += OnMarryConfirm;
		mVBox.AddChild(mConfirm);
		_uiLayer.AddChild(mPanel);

		// Focus Panel (Hidden)
		var fPanel = new PanelContainer();
		fPanel.Name = "FocusPanel";
		fPanel.Position = new Vector2(200, 200);
		fPanel.Visible = false;
		var fVBox = new VBoxContainer();
		fPanel.AddChild(fVBox);
		var fLabel = new Label();
		fLabel.Text = "Select Editorial Focus Quadrant:";
		fVBox.AddChild(fLabel);
		var fGrid = new GridContainer();
		fGrid.Columns = 2; // 2x2
		fVBox.AddChild(fGrid);
		
		for (int i = 0; i < 4; i++)
		{
			var qBtn = new Button();
			qBtn.Text = $"Quadrant {i}"; // Could map to TL/TR...
			qBtn.CustomMinimumSize = new Vector2(100, 100);
			int qIdx = i; // Capture closure
			qBtn.Pressed += () => {
				OnActionSelected("producer_global", $"set_focus_{qIdx}");
				fPanel.Visible = false;
			};
			fGrid.AddChild(qBtn);
		}
		_uiLayer.AddChild(fPanel);
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

	private void SetupBackground()
	{
		// Load the mock map as background
		var mapTexture = GD.Load<Texture2D>("res://assets/tilemap-basic.png");
		
		if (mapTexture != null)
		{
			var mapSprite = new Sprite2D();
			mapSprite.Texture = mapTexture;
			mapSprite.Centered = false; // Position from top-left
			mapSprite.ZIndex = -10;
			
			// Scale to fill the world
			float scaleX = _worldSize.X / mapTexture.GetWidth();
			float scaleY = _worldSize.Y / mapTexture.GetHeight();
			mapSprite.Scale = new Vector2(scaleX, scaleY);
			
			AddChild(mapSprite);
			GD.Print($"Background map loaded: {mapTexture.GetWidth()}x{mapTexture.GetHeight()}, scaled to {_worldSize}");
		}
		else
		{
			// Fallback to simple colored background
			var bg = new ColorRect();
			bg.Color = new Color(0.15f, 0.15f, 0.2f, 1f);
			bg.Size = _worldSize;
			bg.ZIndex = -10;
			AddChild(bg);
			GD.PrintErr("Failed to load tilemap-basic.png, using fallback background");
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
		GD.Print($"[GameWorld] Client requesting game state from server");
		// Request state from server - players will be spawned when state arrives
		RpcId(1, MethodName.RequestGameState);
	}

	private void SpawnNPCs()
	{
		if (_gameEngine == null) return;

		var npcPositions = new Dictionary<string, Vector2>
		{
			{ "katy", new Vector2(200, 200) },
			{ "john", new Vector2(600, 200) },
			{ "rebecca", new Vector2(1000, 200) },
			{ "marcus", new Vector2(400, 500) },
			{ "sofia", new Vector2(800, 500) }
		};

		var npcColors = new Dictionary<string, Color>
		{
			{ "katy", Colors.DeepPink },
			{ "john", Colors.DodgerBlue },
			{ "rebecca", Colors.Orange },
			{ "marcus", Colors.LimeGreen },
			{ "sofia", Colors.Orchid }
		};

		foreach (var npc in _gameEngine.GameState.NPCs.Values)
		{
			var entity = new NPCEntity();
			entity.NpcId = npc.Id;
			entity.NpcName = npc.Name;
			entity.NpcColor = npcColors.GetValueOrDefault(npc.Id, Colors.Blue);
			entity.Position = npcPositions.GetValueOrDefault(npc.Id, new Vector2(600, 400));
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
				_localPlayer = player;
			}
			
			// Connect to position change signal for local player only
			// (Remote players get updated via RPC, not signal)
			if (isLocal)
			{
				player.PositionChanged += OnPlayerPositionChanged;
				GD.Print($"[GameWorld] Connected PositionChanged signal for local player {kvp.Key}");
			}
			
			GD.Print($"[GameWorld] Spawning player {kvp.Key} as {kvp.Value.Role}, sprite={player.PlayerIndex}, isLocal={isLocal}, pos={player.Position}");
			
			AddChild(player);
			_playerControllers[kvp.Key] = player;
			idx++;
		}
		
		// Also set our role from network manager for UI
		long myId = Multiplayer.GetUniqueId();
		if (_networkManager.Players.ContainsKey(myId))
		{
			_myRole = _networkManager.Players[myId].Role;
			_roleLabel.Text = $"Role: {_myRole?.ToUpper()}";
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
		_interactionPanel.ShowForNPC(npcId, npcName, desc, actionsList);
	}

	private void OnActionSelected(string npcId, string actionId)
	{
		// Send to server
		RpcId(1, MethodName.SubmitAction, npcId, actionId);
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
			npcStates[npc.Id] = new JObject
			{
				{ "alive", npc.Alive },
				{ "converted", npc.Converted },
				{ "married", npc.Married }
			};
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

		return status.ToString();
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	private void UpdateGameState(string json)
	{
		GD.Print($"[GameWorld] UpdateGameState called on peer {Multiplayer.GetUniqueId()}, players before: {_playerControllers.Count}");
		_localGameState = JObject.Parse(json);
		
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
	}

	private void SpawnNPCsFromState()
	{
		var npcPositions = new Dictionary<string, Vector2>
		{
			{ "katy", new Vector2(200, 200) },
			{ "john", new Vector2(600, 200) },
			{ "rebecca", new Vector2(1000, 200) },
			{ "marcus", new Vector2(400, 500) },
			{ "sofia", new Vector2(800, 500) }
		};

		var npcColors = new Dictionary<string, Color>
		{
			{ "katy", Colors.DeepPink },
			{ "john", Colors.DodgerBlue },
			{ "rebecca", Colors.Orange },
			{ "marcus", Colors.LimeGreen },
			{ "sofia", Colors.Orchid }
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
			entity.Position = npcPositions.GetValueOrDefault(npcId, new Vector2(600, 400));
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
		_roleLabel.Text = $"Role: {_myRole?.ToUpper()}";

		// Update Trap Button Visibility
		// Update Trap Button Visibility
		if (_uiLayer.GetNodeOrNull<Button>("TrapButton") is Button trapBtn)
		{
			bool isProphet = (_myRole?.ToLower() == "prophet");
			trapBtn.Visible = isProphet;
		}

		// PRODUCER ACTIONS VISIBILITY
		var marryBtn = _uiLayer.GetNodeOrNull<Button>("MarryButton");
		var focusBtn = _uiLayer.GetNodeOrNull<Button>("FocusButton");
		bool isProducer = (_myRole?.ToLower() == "producer");
		if (marryBtn != null) marryBtn.Visible = isProducer;
		if (focusBtn != null) focusBtn.Visible = isProducer;

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

		// Clear explicit children if state invalid, but smart update better
		foreach (Node n in rpsContainer.GetChildren()) n.QueueFree();
		
		if (conversions != null && conversions.ContainsKey(myRoleStr))
		{
			var ctx = conversions[myRoleStr];
			string npcId = ctx["npcId"]?.Value<string>();
			string baseActionId = ctx["baseActionId"]?.Value<string>();
			var visibleOpts = ctx["visibleOptions"]?.ToObject<List<string>>();
			
			if (!string.IsNullOrEmpty(npcId) && !string.IsNullOrEmpty(baseActionId) && visibleOpts != null)
			{
				rpsContainer.Visible = true;
				
				// Show Header
				var label = new Label();
				label.Text = $"CONVERT {Capitalize(npcId)}:";
				rpsContainer.AddChild(label);
				
				foreach (var move in visibleOpts)
				{
					var btn = new Button();
					btn.Text = Capitalize(move); // Display "Rock"
					// Action ID format: baseActionId + "_" + move.ToLower() e.g. "convert_katy_rock"
					btn.Pressed += () => OnActionSelected(npcId, $"{baseActionId}_{move.ToLower()}");
					rpsContainer.AddChild(btn);
				}
			}
		}
		else
		{
			rpsContainer.Visible = false;
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
				// Full screen blocking rect
				var overlay = new ColorRect();
				overlay.Name = "GameOverOverlay";
				overlay.Size = _worldSize; // Cover entire world
				overlay.Color = new Color(0, 0, 0, 0.7f); // Semi-transparent black
				overlay.MouseFilter = Control.MouseFilterEnum.Stop; // BLOCK ALL CLICKS
				overlay.ZIndex = 99; // Above everything else
				_uiLayer.AddChild(overlay);

				// Centered Label
				var label = new Label();
				label.Name = "GameOverLabel";
				label.Text = winText;
				label.AddThemeFontSizeOverride("font_size", 64);
				label.HorizontalAlignment = HorizontalAlignment.Center;
				label.VerticalAlignment = VerticalAlignment.Center;
				label.AnchorsPreset = (int)Control.LayoutPreset.Center;
				// Center in overlay
				label.Position = _worldSize / 2 - new Vector2(200, 50);
				label.ZIndex = 100;
				_uiLayer.AddChild(label);
			}
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
}
