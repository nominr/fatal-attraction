using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FatalAttraction.Engine;

// Alias Godot's Collections to avoid ambiguity if needed, otherwise rely on System.Collections.Generic
// We will use standard C# collections where possible.

public partial class MainGame : Control
{
	// UI References
	private PanelContainer _goalPanel;
	private RichTextLabel _goalText;
	private Label _turnLabel;
	private Label _roleLabel;
	private VBoxContainer _metersContainer;
	private VBoxContainer _npcContainer;
	private PanelContainer _notificationPanel;
	private RichTextLabel _notificationText;
	private Button _startButton;
	private PanelContainer _editorialPanel;
	private VBoxContainer _editorialOptions;
	private Label _editorialLabel; // Added to HUD

	// Game State
	private GameEngine _gameEngine;
	private NetworkManager _networkManager;
	private Control _marriageModal;
	private string _pendingMarryNpcId;
	
	// Local State Cache (for Clients)
	private JObject _localGameState;
	private string _myRole;

	public override void _Ready()
	{
		_networkManager = GetNode<NetworkManager>("/root/NetworkManager");
		
		// Get Nodes
		_goalPanel = GetNode<PanelContainer>("GoalPanel");
		_goalText = GetNode<RichTextLabel>("GoalPanel/MarginContainer/GoalText");
		_turnLabel = GetNode<Label>("HUD/TurnLabel");
		_roleLabel = GetNode<Label>("HUD/RoleLabel");
		_metersContainer = GetNode<VBoxContainer>("HUD/MetersContainer");
		_npcContainer = GetNode<VBoxContainer>("NPCContainer");
		_notificationPanel = GetNode<PanelContainer>("NotificationPanel");
		_notificationText = GetNode<RichTextLabel>("NotificationPanel/MarginContainer/NotificationText");
		_startButton = GetNode<Button>("GoalPanel/StartButton");
		_editorialPanel = GetNode<PanelContainer>("EditorialPanel");
		_editorialOptions = GetNode<VBoxContainer>("EditorialPanel/MarginContainer/VBoxContainer/OptionsContainer");

		// Create Editorial Label in HUD
		_editorialLabel = new Label();
		GetNode("HUD").AddChild(_editorialLabel);
		GetNode("HUD").MoveChild(_editorialLabel, _roleLabel.GetIndex() + 1);

		// Connect Signals
		_startButton.Pressed += OnStartButtonPressed;

		// Initial UI State
		_npcContainer.Hide();
		_notificationPanel.Hide();
		_editorialPanel.Hide();

		// Check Role and Init
		if (Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer())
		{
			InitializeServer();
		}
		else
		{
			InitializeClient();
		}
	}

	private void InitializeServer()
	{
		var configPath = ProjectSettings.GlobalizePath("res://data/game_configuration.json");
		_gameEngine = new GameEngine(configPath);
		
		// Show goals locally for host (Producer usually)
		ShowAllGoals(); 
	}

	private void InitializeClient()
	{
		_goalText.Text = "[center]Waiting for Host to start...[/center]";
		_startButton.Disabled = true;
		
		// Race Condition Fix: Client proactively asks for state now that it's ready.
		RpcId(1, MethodName.RequestGameState);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestGameState()
	{
		if (Multiplayer.MultiplayerPeer == null || !Multiplayer.IsServer()) return;
		long senderId = Multiplayer.GetRemoteSenderId();
		GD.Print($"Client {senderId} requested game state.");
		
		// Reuse Broadcast logic but targeted
		// We need to construct the JSON. 
		// Ideally refactor BroadcastGameState to return JSON or take target ID.
		// For now, I'll copy the generation logic or verify if BroadcastGameState can send to specific target?
		// No, Rpc() sends to all. use RpcId.
		
		string json = GenerateGameStateJson();
		RpcId(senderId, MethodName.UpdateGameState, json);
	}


	private void ShowAllGoals()
	{
		// Simplified goal display for now
		_goalText.Text = "[center][b]FATAL ATTRACTION[/b][/center]\n\nWaiting to start...";
	}

	private void OnStartButtonPressed()
	{
		if (Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer())
		{
			StartNewTurn();
			_goalPanel.Hide();
			_npcContainer.Show();
			_notificationPanel.Show();
			
			// Broadcast start to clients
			Rpc(MethodName.ClientGameStarted);
			BroadcastGameState();
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	private void ClientGameStarted()
	{
		_goalPanel.Hide();
		_npcContainer.Show();
		_notificationPanel.Show();
	}

	// --- GAME LOOP (SERVER) ---

	// --- ASYNC GAME LOOP (SERVER) ---

	private double _timeRemaining = 60.0; // 1 minute
	private bool _gameActive = false;
	private List<string> _currentTurnActiveNpcs; // Keep for now to limit visible NPCs

	public override void _Process(double delta)
	{
		if (Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer() && _gameActive)
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

	private void StartNewTurn()
	{
		// Start Game Async
		_gameActive = true;
		_timeRemaining = 300.0;
		
		// Pick initial NPCs
		UpdateActiveNpcs();

		// Update Clients
		BroadcastGameState();
	}

	private void UpdateActiveNpcs()
	{
		var aliveNpcs = _gameEngine.GameState.NPCs.Values.Where(n => n.Alive).ToList();
		var rnd = new Random();
		_currentTurnActiveNpcs = aliveNpcs.OrderBy(x => rnd.Next()).Take(3).Select(n => n.Id).ToList();
	}

	private void BroadcastGameState()
	{
		if (Multiplayer.MultiplayerPeer == null || !Multiplayer.IsServer()) return;
		string json = GenerateGameStateJson();
		Rpc(MethodName.UpdateGameState, json);
	}
	
	private string GenerateGameStateJson()
	{
		var status = _gameEngine.GetGameStatus();
		status["time_remaining"] = _timeRemaining;
		status["game_active"] = _gameActive;
		status["notifications"] = JToken.FromObject(_gameEngine.GetNotifications());
		
		// Ensure NPCs are selected
		if (_currentTurnActiveNpcs == null) UpdateActiveNpcs();
		status["active_npcs"] = JToken.FromObject(_currentTurnActiveNpcs);

		// Calculate available actions for ALL roles
		// Map: Role -> { NpcId -> [Actions] }
		var allActions = new JObject();
		foreach (string roleName in new[] { "admirer", "prophet", "producer" })
		{
			Role roleEnum = Enum.Parse<Role>(roleName, true);
			var roleActions = new JObject();
			
			foreach(var npcId in _currentTurnActiveNpcs)
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
		_localGameState = JObject.Parse(json);
		UpdateUI();
	}

	private void UpdateUI()
	{
		if (_localGameState == null) return;

		// 1. Update HUD
		double time = _localGameState["time_remaining"]?.Value<double>() ?? 0;
		TimeSpan ts = TimeSpan.FromSeconds(time);
		_turnLabel.Text = $"Time: {ts.Minutes:D2}:{ts.Seconds:D2}"; // Replaces Turn Label

		// Identify MY role network-wise
		long myId = Multiplayer.GetUniqueId();
		if (_networkManager.Players.ContainsKey(myId))
		{
			_myRole = _networkManager.Players[myId].Role.ToLower();
		}
		_roleLabel.Text = $"My Role: {_myRole?.ToUpper()}";

		// Update Meters (Show my meters)
		foreach (Node child in _metersContainer.GetChildren()) child.QueueFree();

		if (!string.IsNullOrEmpty(_myRole))
		{
			var players = _localGameState["players"];
			var myRoleState = players[_myRole];
			if (myRoleState != null)
			{
				var meters = myRoleState["meters"];
				foreach (JProperty meter in meters)
				{
					CreateMeterDisplay(meter.Name, meter.Value["value"].Value<double>(), meter.Value["max"].Value<double>());
				}
			}
		}

		// 2. Notifications
		var notifs = _localGameState["notifications"];
		if (notifs != null)
		{
			foreach (string msg in notifs)
			{
				_notificationText.AddText(msg + "\n");
			}
		}

		// 3. NPCs
		foreach (Node child in _npcContainer.GetChildren()) child.QueueFree();
		
		var title = new Label();
		title.Text = "NPC Encounters";
		_npcContainer.AddChild(title);

		var activeNpcs = _localGameState["active_npcs"]; 
		if (activeNpcs != null)
		{
			foreach (string npcId in activeNpcs)
			{
				CreateNpcPanel(npcId);
			}
		}
		
		bool gameActive = _localGameState["game_active"]?.Value<bool>() ?? false;
		_npcContainer.Modulate = gameActive ?Colors.White : Colors.Gray;
	}

	private void CreateMeterDisplay(string name, double val, double max)
	{
		var hbox = new HBoxContainer();
		var label = new Label { Text = $"{name.ToUpper()}: ", CustomMinimumSize = new Vector2(100, 0) };
		var progress = new ProgressBar { 
			MinValue = 0, 
			MaxValue = max, 
			Value = val, 
			CustomMinimumSize = new Vector2(200, 20),
			ShowPercentage = false 
		};
		var valLabel = new Label { Text = $" {val:F1}/{max:F0}" };
		
		hbox.AddChild(label);
		hbox.AddChild(progress);
		hbox.AddChild(valLabel);
		_metersContainer.AddChild(hbox);
	}

	private void CreateNpcPanel(string npcId)
	{
		var panel = new PanelContainer { CustomMinimumSize = new Vector2(600, 150) };
		var vbox = new VBoxContainer();
		panel.AddChild(vbox);

		var label = new Label { Text = $"NPC: {npcId}" };
		vbox.AddChild(label);
		
		var actionsBox = new HBoxContainer();
		vbox.AddChild(actionsBox);
		
		// Render Actions for MY role
		if (!string.IsNullOrEmpty(_myRole))
		{
			var allActions = _localGameState["all_actions"] as JObject;
			var myActions = allActions?[_myRole] as JObject;
			var npcActions = myActions?[npcId];

			if (npcActions != null)
			{
				foreach (var action in npcActions)
				{
					string actionId = action["id"].Value<string>();
					string actionText = action["text"].Value<string>();
					
					var btn = new Button { Text = actionText };
					btn.Pressed += () => {
						RpcId(1, MethodName.SubmitAction, npcId, actionId);
						// Visual feedback
						btn.Disabled = true; 
					};
					actionsBox.AddChild(btn);
				}
			}
		}

		_npcContainer.AddChild(panel);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	public void SubmitAction(string npcId, string actionId)
	{
		if (Multiplayer.MultiplayerPeer == null || !Multiplayer.IsServer()) return;

		// Validate sender
		long senderId = Multiplayer.GetRemoteSenderId();
		string senderRole = _networkManager.Players[senderId].Role.ToLower();
		
		// No turn check anymore! Any player can play.
		
		Role roleEnum = Enum.Parse<Role>(senderRole, true);
		var (success, failReason) = _gameEngine.PerformAction(npcId, actionId, roleEnum);
		
		if (success)
		{
			_gameEngine.GameState.AddNotification($"{senderRole.ToUpper()} performed {actionId} on {npcId}");
		}
		else
		{
			// Provide feedback on why the action failed
			string reasonMsg = failReason ?? "unknown reason";
			_gameEngine.GameState.AddNotification($"{senderRole.ToUpper()}: {reasonMsg}");
		}
		
		// Broadcast update immediately
		BroadcastGameState();
	}
}
