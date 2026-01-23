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
	private double _timeRemaining = 300.0;

	// Local State Cache
	private JObject _localGameState;
	private string _myRole;

	// Spawned entities
	private Dictionary<string, NPCEntity> _npcEntities = new();
	private Dictionary<long, PlayerController> _playerControllers = new();
	
	// UI Components
	private InteractionPanel _interactionPanel;
	private CanvasLayer _uiLayer;
	private Label _timerLabel;
	private Label _roleLabel;
	private VBoxContainer _metersContainer;
	private RichTextLabel _notificationText;

	// World bounds
	private Vector2 _worldSize = new Vector2(1200, 800);
	private Random _random = new Random();

	public override void _Ready()
	{
		_networkManager = GetNode<NetworkManager>("/root/NetworkManager");
		
		SetupUI();
		SetupBackground();
		
		// Ensure random is seeded differently if possible, but default is time-based which is fine for one instance.
		// For deterministic NPC spawning, we use local instances.
		
		if (Multiplayer.IsServer())
		{
			InitializeServer();
		}
		else
		{
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
	}

	private void SetupBackground()
	{
		// Load the demo world scene
		var scene = GD.Load<PackedScene>("res://scenes/demo_world.tscn");
		
		if (scene != null)
		{
			var mapNode = scene.Instantiate() as Node2D;
			if (mapNode != null)
			{
				mapNode.Name = "GameMap";
				mapNode.ZIndex = -10; // Ensure it's behind players/NPCs
				AddChild(mapNode);
				GD.Print("Loaded demo_world.tscn as map");
			}
		}
		else
		{
			// Fallback to simple colored background
			var bg = new ColorRect();
			bg.Color = new Color(0.15f, 0.15f, 0.2f, 1f);
			bg.Size = _worldSize;
			bg.ZIndex = -10;
			AddChild(bg);
			GD.PrintErr("Failed to load scenes/demo_world.tscn, using fallback background");
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

	private void SpawnNPCs()
	{
		if (_gameEngine == null) return;

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
			entity.Position = GetRandomSpawnPosition();
			entity.NPCClicked += OnNPCClicked;
			AddChild(entity);
			_npcEntities[npc.Id] = entity;
		}
	}

	private Vector2 GetRandomSpawnPosition()
	{
		// Margin of 100 to avoid edges
		float x = _random.Next(100, (int)_worldSize.X - 100);
		float y = _random.Next(100, (int)_worldSize.Y - 100);
		return new Vector2(x, y);
	}

	private void SpawnAllPlayers()
	{
		// Clear existing players
		foreach (var kvp in _playerControllers)
		{
			kvp.Value.QueueFree();
		}
		_playerControllers.Clear();

		int idx = 0;
		// Check if we have authoritative positions from server
		var playerStates = _localGameState?["player_states"] as JObject;

		foreach (var kvp in _networkManager.Players)
		{
			var player = new PlayerController();
			
			// Position Logic
			if (Multiplayer.IsServer())
			{
				// Server decides random position
				player.Position = GetRandomSpawnPosition();
			}
			else
			{
				// Client tries to use server position
				if (playerStates != null && playerStates.TryGetValue(kvp.Key.ToString(), out var stateToken))
				{
					float x = stateToken["x"]?.Value<float>() ?? 0;
					float y = stateToken["y"]?.Value<float>() ?? 0;
					player.Position = new Vector2(x, y);
					GD.Print($"Spawned player {kvp.Key} from server state at {player.Position}");
				}
				else
				{
					// Fallback (e.g. state not yet arrived or first frame)
					player.Position = GetRandomSpawnPosition();
					GD.Print($"Spawned player {kvp.Key} locally at {player.Position} (Fallback)");
				}
			}

			player.PlayerIndex = (idx % 3) + 1; // 1, 2, or 3 for sprite selection
			player.SetRole(kvp.Value.Role);
			player.SetPlayerId(kvp.Key); // Set the network player ID
			
			// Check if this is our local player
			bool isLocal = kvp.Key == Multiplayer.GetUniqueId();
			player.SetLocalPlayer(isLocal);
			
			// Connect to position change signal for local player only
			if (isLocal)
			{
				player.PositionChanged += OnPlayerPositionChanged;
			}
			
			// Critical for top-down movement: Disable gravity logic (Duplicate safe init)
			// player.MotionMode = CharacterBody2D.MotionModeEnum.Floating; // Handled in PlayerController._Ready

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
		if (string.IsNullOrEmpty(_myRole)) return;

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
			_timeRemaining -= delta;
			if (_timeRemaining <= 0)
			{
				_timeRemaining = 0;
				_gameActive = false;
				_gameEngine.GameState.AddNotification("GAME OVER - TIME UP!");
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

		// Player Positions (Sync for late joiners and initial spawn)
		var playerStates = new JObject();
		foreach (var kvp in _playerControllers)
		{
			playerStates[kvp.Key.ToString()] = new JObject
			{
				{ "x", kvp.Value.Position.X },
				{ "y", kvp.Value.Position.Y }
			};
		}
		status["player_states"] = playerStates;

		return status.ToString();
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	private void UpdateGameState(string json)
	{
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
			
			// Use deterministic position based on ID so all clients agree
			int seed = npcId.GetHashCode();
			var rnd = new Random(seed);
			float x = rnd.Next(100, (int)_worldSize.X - 100);
			float y = rnd.Next(100, (int)_worldSize.Y - 100);
			entity.Position = new Vector2(x, y);

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

	// ---- POSITION SYNCHRONIZATION ----

	private void OnPlayerPositionChanged(long playerId, Vector2 position)
	{
		GD.Print($"GameWorld: OnPlayerPositionChanged called - Player {playerId} at {position}, IsServer={Multiplayer.IsServer()}");
		// This is called when a local player moves
		// If we're the server, broadcast to all clients
		// If we're a client, send to server
		if (Multiplayer.IsServer())
		{
			// Server broadcasts to all clients
			GD.Print($"GameWorld: Server broadcasting position for player {playerId}");
			Rpc(MethodName.SyncPlayerPosition, playerId, position);
		}
		else
		{
			// Client sends to server only
			GD.Print($"GameWorld: Client sending position to server for player {playerId}");
			RpcId(1, MethodName.SyncPlayerPosition, playerId, position);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
	private void SyncPlayerPosition(long playerId, Vector2 position)
	{
		GD.Print($"GameWorld: SyncPlayerPosition RPC received - Player {playerId} at {position}, IsServer={Multiplayer.IsServer()}");
		// Update the player controller's position
		if (_playerControllers.TryGetValue(playerId, out var controller))
		{
			GD.Print($"GameWorld: Found controller for player {playerId}, updating position");
			controller.UpdateRemotePosition(position);
		}
		else
		{
			GD.Print($"GameWorld: WARNING - No controller found for player {playerId}");
		}
		
		// If we're the server and received this from a client, broadcast to all other clients
		if (Multiplayer.IsServer())
		{
			GD.Print($"GameWorld: Server re-broadcasting position for player {playerId}");
			Rpc(MethodName.SyncPlayerPosition, playerId, position);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	public void SubmitAction(string npcId, string actionId)
	{
		if (!Multiplayer.IsServer()) return;

		long senderId = Multiplayer.GetRemoteSenderId();
		string senderRole = _networkManager.Players[senderId].Role.ToLower();

		Role roleEnum = Enum.Parse<Role>(senderRole, true);
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
}
