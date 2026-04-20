using Godot;
using System;

public partial class Lobby : Control
{
	[Export]
	public PackedScene MainGameScene { get; set; }

	private const int MAX_PLAYERS = 3;

	private NetworkManager _networkManager;

	private LineEdit _nameInput;
	private LineEdit _ipInput;
	private Button _hostButton;
	private Button _joinButton;
	private Button _cancelButton;
	private Button _startButton;
	private Label _backLabel;
	private Label _statusLabel;
	private ItemList _playerList;
	private Button _admireButton;
	private Button _prophetButton;
	private Button _producerButton;
	private bool _isHosting = false;
	private bool _isConnected = false;
	private string _autoJoinRole = null;

	// Background animation (same as InfoScene)
	private const int TotalFrames = 191;
	private Sprite2D _background;
	private Timer _backgroundTimer;
	private Texture2D[] _bgFrames;
	private int _currentFrame = 0;

	// Hover color for buttons
	private Color _normalColor = new Color(1, 1, 1, 1); // White
	private Color _hoverColor = new Color(1, 0.9f, 0.2f, 1); // Yellowish

	public override void _Ready()
	{
		// Assuming NetworkManager is an autoload named "NetworkManager"
		_networkManager = GetNode<NetworkManager>("/root/NetworkManager");

		// Map UI nodes
		_nameInput = GetNode<LineEdit>("NameInputContainer/NameInput");
		_ipInput = GetNode<LineEdit>("IPInputContainer/IPInput");
		_hostButton = GetNode<Button>("ButtonsContainer/HostButton");
		_joinButton = GetNode<Button>("ButtonsContainer/JoinButton");
		_cancelButton = GetNode<Button>("ButtonsContainer/CancelButton");
		_startButton = GetNode<Button>("StartButton");
		_backLabel = GetNode<Label>("BackLabel");
		_statusLabel = GetNode<Label>("StatusLabel");
		_playerList = GetNode<ItemList>("PlayerListContainer/PlayerList");
		_admireButton = GetNode<Button>("RoleButtonsContainer/AdmirerButton");
		_prophetButton = GetNode<Button>("RoleButtonsContainer/ProphetButton");
		_producerButton = GetNode<Button>("RoleButtonsContainer/ProducerButton");

		// Map background nodes
		_background = GetNode<Sprite2D>("Background");
		_backgroundTimer = GetNode<Timer>("BackgroundTimer");

		// Load only the first frame immediately to show something
		_bgFrames = new Texture2D[TotalFrames];
		string firstFramePath = $"res://assets/Title_BG_Frames/bg_frame_1.png";
		_bgFrames[0] = GD.Load<Texture2D>(firstFramePath);
		
		if (_bgFrames[0] != null)
		{
			_background.Texture = _bgFrames[0];
			_currentFrame = 0;
			
			// Connect timer and start
			_backgroundTimer.Timeout += OnBackgroundTimerTimeout;
			_backgroundTimer.WaitTime = 1.0f / 24.0f; // ~24 FPS
			_backgroundTimer.Start();
		}
		else
		{
			GD.PushError("[Lobby] Failed to load the first background frame. Check paths.");
		}

		// Request all other frames asynchronously
		for (int i = 1; i < TotalFrames; i++)
		{
			string path = $"res://assets/Title_BG_Frames/bg_frame_{i + 1}.png";
			ResourceLoader.LoadThreadedRequest(path);
		}

		// Make back label clickable and set up hover signals
		_backLabel.MouseFilter = MouseFilterEnum.Stop;
		_backLabel.MouseEntered += OnBackLabelMouseEntered;
		_backLabel.MouseExited += OnBackLabelMouseExited;

		// Connect signals
		_hostButton.Pressed += OnHostPressed;
		_joinButton.Pressed += OnJoinPressed;
		_cancelButton.Pressed += OnCancelPressed;
		_startButton.Pressed += OnStartPressed;
		_admireButton.Pressed += () => OnRoleButtonPressed("Admirer");
		_prophetButton.Pressed += () => OnRoleButtonPressed("Prophet");
		_producerButton.Pressed += () => OnRoleButtonPressed("Producer");

		// Initially hide the cancel button
		_cancelButton.Visible = false;

		_networkManager.PlayerConnected += OnPlayerConnected;
		_networkManager.PlayerDisconnected += OnPlayerDisconnected;
		_networkManager.ConnectionFailed += OnConnectionFailed;
		_networkManager.ConnectionSucceeded += OnConnectionSucceeded;
		_networkManager.GameStarted += OnGameStarted;
		_networkManager.RoleRejected += OnRoleRejected;

		_startButton.Disabled = true;

		// ── Detect if we're returning from a game with a live connection ──
		bool alreadyConnected = Multiplayer.HasMultiplayerPeer();
		if (alreadyConnected)
		{
			_isHosting   = Multiplayer.IsServer();
			_isConnected = true;

			// Hide setup buttons; show cancel
			_hostButton.Disabled   = true;
			_joinButton.Disabled   = true;
			_cancelButton.Visible  = true;

			// Role buttons always available when returning
			_admireButton.Disabled  = false;
			_prophetButton.Disabled = false;
			_producerButton.Disabled = false;

			if (_isHosting)
			{
				_startButton.Disabled = false;
				_statusLabel.Text = "Returned to lobby. Start a new game or wait for players.";
			}
			else
			{
				_statusLabel.Text = "Returned to lobby. Select a role to play again.";
			}

			// Pull a fresh snapshot of all connected players from the server
			UpdatePlayerList();
			_networkManager.RequestLobbySync();
		}

		// Display Local IP(s) to help with LAN hosting
		var ips = Godot.IP.GetLocalAddresses();
		string ipText = "Your IP: ";
		bool firstIP = true;
		bool foundIP = false;

		GD.Print("[Lobby] Checking local IP addresses...");
		foreach (string ip in ips)
		{
			GD.Print($"[Lobby] Found IP: {ip}");
			// Filter for likely LAN IPs (IPv4, not localhost)
			if (ip.Contains(".") && !ip.StartsWith("127.") && !ip.StartsWith("169."))
			{
				if (!firstIP) ipText += ", ";
				ipText += ip;
				firstIP = false;
				foundIP = true;
			}
		}

		if (!foundIP)
		{
			ipText += "No network found";
		}

		// Update the IP address label
		var ipAddressLabel = GetNode<Label>("IPAddressLabel");
		if (ipAddressLabel != null)
		{
			ipAddressLabel.Text = ipText;
			GD.Print($"[Lobby] IP Label updated: {ipText}");
		}
		else
		{
			GD.PrintErr("[Lobby] IPAddressLabel node not found!");
		}

		// Process Command Line Arguments for Auto-Start
		CallDeferred(MethodName.ProcessCommandLineArgs);
		
		// Pre-cache NPC and Player assets in the background
		PrecacheGameAssets();
	}

	private void PrecacheGameAssets()
	{
		string basePath = "res://assets/new-character-assets/";
		string[] characterCores = { "admirer2", "prophet", "producer2", "npc1", "npc2", "npc3", "npc4", "npc5", "npc6", "npc7", "npc8", "npc9", "npc10" };
		string[] suffixes = { "-front-idle.png", "-front-walk.png", "-back-idle.png", "-back-walk.png" };

		GD.Print("[Lobby] Starting background pre-cache of character assets...");
		foreach (var core in characterCores)
		{
			foreach (var suffix in suffixes)
			{
				string path = $"{basePath}{core}{suffix}";
				ResourceLoader.LoadThreadedRequest(path);
			}
		}
	}

	public override void _ExitTree()
	{
		// Disconnect signal handlers to prevent ObjectDisposedException
		if (_networkManager != null)
		{
			_networkManager.PlayerConnected -= OnPlayerConnected;
			_networkManager.PlayerDisconnected -= OnPlayerDisconnected;
			_networkManager.ConnectionFailed -= OnConnectionFailed;
			_networkManager.ConnectionSucceeded -= OnConnectionSucceeded;
			_networkManager.GameStarted -= OnGameStarted;
			_networkManager.RoleRejected -= OnRoleRejected;
		}

		// Optional cleanup: stop timer to avoid callbacks after node is gone
		if (_backgroundTimer != null)
		{
			_backgroundTimer.Stop();
			_backgroundTimer.Timeout -= OnBackgroundTimerTimeout;
		}
	}

	private void ProcessCommandLineArgs()
	{
		var args = OS.GetCmdlineArgs();
		for (int i = 0; i < args.Length; i++)
		{
			if (args[i] == "--host" && i + 1 < args.Length)
			{
				string role = args[i + 1];
				_nameInput.Text = "Host";
				OnHostPressed();
				OnRoleButtonPressed(role);
				GD.Print($"[Auto] Hosting as {role}");
			}
			else if (args[i] == "--join" && i + 1 < args.Length)
			{
				string role = args[i + 1];
				_nameInput.Text = $"Player_{role}";
				_ipInput.Text = "127.0.0.1";
				_autoJoinRole = role;
				OnJoinPressed();
				GD.Print($"[Auto] Joining as {role}");
			}
		}
	}

	private void OnHostPressed()
	{
		if (string.IsNullOrEmpty(_nameInput.Text))
		{
			_statusLabel.Text = "Please enter a name.";
			return;
		}

		_networkManager.HostGame(_nameInput.Text);
		_statusLabel.Text = "Select your role to begin hosting.";
		_hostButton.Disabled = true;
		_joinButton.Disabled = true;
		_cancelButton.Visible = true;
		_startButton.Disabled = true;
		_isHosting = true;
		_isConnected = false;

		_admireButton.Disabled = false;
		_prophetButton.Disabled = false;
		_producerButton.Disabled = false;
		UpdateAvailableRoles();
	}

	private void OnJoinPressed()
	{
		if (string.IsNullOrEmpty(_nameInput.Text) || string.IsNullOrEmpty(_ipInput.Text))
		{
			_statusLabel.Text = "Please enter a name and IP.";
			return;
		}

		if (_networkManager.Players.Count >= MAX_PLAYERS)
		{
			_statusLabel.Text = "Lobby is full (max 3 players).";
			return;
		}

		_networkManager.JoinGame(_ipInput.Text, _nameInput.Text);
		_statusLabel.Text = "Connecting...";
		_hostButton.Disabled = true;
		_joinButton.Disabled = true;
		_cancelButton.Visible = true;
		_isHosting = false;
	}

	private void OnStartPressed()
	{
		_networkManager.SendStartGame();
	}

	private void OnCancelPressed()
	{
		if (Multiplayer.MultiplayerPeer != null)
		{
			Multiplayer.MultiplayerPeer.Close();
			Multiplayer.MultiplayerPeer = null;
		}

		_networkManager.Players.Clear();

		_statusLabel.Text = _isHosting ? "Hosting cancelled." : "Left the game.";
		_hostButton.Disabled = false;
		_joinButton.Disabled = false;
		_cancelButton.Visible = false;
		_startButton.Disabled = true;
		_isHosting = false;
		_isConnected = false;

		_admireButton.Disabled = true;
		_prophetButton.Disabled = true;
		_producerButton.Disabled = true;

		UpdatePlayerList();
		UpdateAvailableRoles();
	}

	private void OnPlayerConnected(long id, string name)
	{
		if (Multiplayer.IsServer() && _networkManager.Players.Count > MAX_PLAYERS)
		{
			GD.Print($"[Lobby] Player limit exceeded. Disconnecting player {id}");
			Multiplayer.MultiplayerPeer.DisconnectPeer((int)id);
			return;
		}

		UpdatePlayerList();
	}

	private void OnPlayerDisconnected(long id)
	{
		if (id == 1 && !_isHosting)
		{
			if (Multiplayer.MultiplayerPeer != null)
			{
				Multiplayer.MultiplayerPeer.Close();
				Multiplayer.MultiplayerPeer = null;
			}

			_networkManager.Players.Clear();

			_statusLabel.Text = "Host disconnected. Returned to lobby.";
			_hostButton.Disabled = false;
			_joinButton.Disabled = false;
			_cancelButton.Visible = false;
			_startButton.Disabled = true;
			_isConnected = false;

			_admireButton.Disabled = true;
			_prophetButton.Disabled = true;
			_producerButton.Disabled = true;

			UpdatePlayerList();
			return;
		}

		UpdatePlayerList();
	}

	private void OnConnectionFailed()
	{
		_statusLabel.Text = "Connection Failed. Unable to reach host.";
		_hostButton.Disabled = false;
		_joinButton.Disabled = false;
		_cancelButton.Visible = false;
		_isConnected = false;
		_isHosting = false;

		_admireButton.Disabled = true;
		_prophetButton.Disabled = true;
		_producerButton.Disabled = true;

		_networkManager.Players.Clear();
		UpdatePlayerList();
	}

	private void OnRoleButtonPressed(string role)
	{
		if (_isHosting && !_isConnected)
		{
			_isConnected = true;
			_statusLabel.Text = "Hosting... Waiting for players.";
			_startButton.Disabled = false;
		}

		_networkManager.SendRoleRequest(role);
	}

	private void OnConnectionSucceeded()
	{
		_statusLabel.Text = "Connected! Please select a role.";
		_isConnected = true;

		_admireButton.Disabled = false;
		_prophetButton.Disabled = false;
		_producerButton.Disabled = false;

		UpdateAvailableRoles();

		// Ask the server for a full snapshot of the current lobby so we see
		// all already-connected players immediately (handles reconnect/restart cases).
		_networkManager.RequestLobbySync();

		if (!string.IsNullOrEmpty(_autoJoinRole))
		{
			OnRoleButtonPressed(_autoJoinRole);
			_autoJoinRole = null;
		}
	}

	private void OnGameStarted()
	{
		GD.Print("Lobby: GameStarted signal received. Transitioning scene...");
		if (MainGameScene == null)
		{
			GD.PrintErr("Lobby: MainGameScene is null!");
			return;
		}
		GetTree().ChangeSceneToPacked(MainGameScene);
	}

	private void OnRoleRejected(string role, string reason)
	{
		_statusLabel.Text = $"Role '{role}' is {reason}. Please choose another role.";
		GD.Print($"[Lobby] Role '{role}' was rejected: {reason}");
		UpdateAvailableRoles();
	}

	private void UpdatePlayerList()
	{
		_playerList.Clear();
		foreach (var player in _networkManager.Players.Values)
		{
			_playerList.AddItem($"{player.Name} ({player.Role})");
		}
		UpdateAvailableRoles();
	}

	private void UpdateAvailableRoles()
	{
		bool isServer = Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer();

		if (!_isConnected || isServer)
		{
			_admireButton.Disabled = false;
			_prophetButton.Disabled = false;
			_producerButton.Disabled = false;
			return;
		}

		var takenRoles = new System.Collections.Generic.HashSet<string>();
		long myId = Multiplayer.GetUniqueId();

		GD.Print($"[Lobby] UpdateAvailableRoles - My ID: {myId}, Total Players: {_networkManager.Players.Count}");

		foreach (var kvp in _networkManager.Players)
		{
			if (kvp.Key != myId && kvp.Value.Role != "Observer")
			{
				GD.Print($"[Lobby] Player {kvp.Value.Name} (ID: {kvp.Key}) has role: {kvp.Value.Role}");
				takenRoles.Add(kvp.Value.Role);
			}
		}

		_admireButton.Disabled = takenRoles.Contains("Admirer");
		_prophetButton.Disabled = takenRoles.Contains("Prophet");
		_producerButton.Disabled = takenRoles.Contains("Producer");

		if (_admireButton.Disabled) GD.Print("[Lobby] Admirer role is taken");
		if (_prophetButton.Disabled) GD.Print("[Lobby] Prophet role is taken");
		if (_producerButton.Disabled) GD.Print("[Lobby] Producer role is taken");
	}

	// Same animation logic as InfoScene: advance frames, skip nulls so it never turns black
	private void OnBackgroundTimerTimeout()
	{
		_currentFrame = (_currentFrame + 1) % TotalFrames;
		
		// If frame 1 already exists, use it
		if (_bgFrames[_currentFrame] != null)
		{
			_background.Texture = _bgFrames[_currentFrame];
			return;
		}

		// Otherwise check if it's finished loading
		string path = $"res://assets/Title_BG_Frames/bg_frame_{_currentFrame + 1}.png";
		if (ResourceLoader.LoadThreadedGetStatus(path) == ResourceLoader.ThreadLoadStatus.Loaded)
		{
			var tex = (Texture2D)ResourceLoader.LoadThreadedGet(path);
			_bgFrames[_currentFrame] = tex;
			_background.Texture = tex;
		}
		// If not loaded yet, the previous frame stays visible (animation "stalls" briefly but UI stays responsive)
	}

	private void OnBackLabelMouseEntered()
	{
		_backLabel.AddThemeColorOverride("font_color", _hoverColor);
	}

	private void OnBackLabelMouseExited()
	{
		_backLabel.AddThemeColorOverride("font_color", _normalColor);
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
		{
			if (_backLabel != null)
			{
				var labelRect = _backLabel.GetGlobalRect();
				if (labelRect.HasPoint(mouseEvent.Position))
				{
					OnBackClicked();
				}
			}
		}
	}

	private void OnBackClicked()
	{
		if (_isConnected || _isHosting)
		{
			OnCancelPressed();
		}

		GD.Print("Going back to Info scene");
		GetTree().ChangeSceneToFile("res://scenes/InfoScene.tscn");
	}
}
