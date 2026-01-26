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
	
	// Background animation
	private Sprite2D _background;
	private Timer _backgroundTimer;
	private Texture2D _bg1;
	private Texture2D _bg2;
	private bool _showingBg1 = true;

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

		// Load background textures
		_bg1 = GD.Load<Texture2D>("res://assets/Title_BG_1.png");
		_bg2 = GD.Load<Texture2D>("res://assets/Title_BG_2.png");
		
		// Make back label clickable
		_backLabel.MouseFilter = MouseFilterEnum.Stop;
		
		// Connect signals
		_hostButton.Pressed += OnHostPressed;
		_joinButton.Pressed += OnJoinPressed;
		_cancelButton.Pressed += OnCancelPressed;
		_startButton.Pressed += OnStartPressed;
		_admireButton.Pressed += () => OnRoleButtonPressed("Admirer");
		_prophetButton.Pressed += () => OnRoleButtonPressed("Prophet");
		_producerButton.Pressed += () => OnRoleButtonPressed("Producer");
		_backgroundTimer.Timeout += OnBackgroundTimerTimeout;

		// Initially hide the cancel button
		_cancelButton.Visible = false;

		_networkManager.PlayerConnected += OnPlayerConnected;
		_networkManager.PlayerDisconnected += OnPlayerDisconnected;
		_networkManager.ConnectionFailed += OnConnectionFailed;
		_networkManager.ConnectionSucceeded += OnConnectionSucceeded;
		_networkManager.GameStarted += OnGameStarted;

		_startButton.Disabled = true;
		
		// Display Local IP(s) to help with LAN hosting
		var ips = Godot.IP.GetLocalAddresses();
		string ipText = "Your IP: ";
		bool firstIP = true;
		bool foundIP = false;
		
		GD.Print("[Lobby] Checking local IP addresses...");
		foreach(string ip in ips)
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
		_startButton.Disabled = true; // Disabled until role is selected
		_isHosting = true;
		_isConnected = false; // Not considered connected until role is selected
		
		// Enable role buttons for host
		_admireButton.Disabled = false;
		_prophetButton.Disabled = false;
		_producerButton.Disabled = false;
		UpdateAvailableRoles();
		
		// Don't send role yet - wait for host to select one
	}

	private void OnJoinPressed()
	{
		if (string.IsNullOrEmpty(_nameInput.Text) || string.IsNullOrEmpty(_ipInput.Text))
		{
			_statusLabel.Text = "Please enter a name and IP.";
			return;
		}

		// Check player count before allowing join (this will be validated server-side too)
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
		// Close connection
		if (Multiplayer.MultiplayerPeer != null)
		{
			Multiplayer.MultiplayerPeer.Close();
			Multiplayer.MultiplayerPeer = null;
		}

		// Reset network manager state
		_networkManager.Players.Clear();

		// Reset UI
		_statusLabel.Text = _isHosting ? "Hosting cancelled." : "Left the game.";
		_hostButton.Disabled = false;
		_joinButton.Disabled = false;
		_cancelButton.Visible = false;
		_startButton.Disabled = true;
		_isHosting = false;
		_isConnected = false;
		
		// Disable role selection buttons instead of hiding
		_admireButton.Disabled = true;
		_prophetButton.Disabled = true;
		_producerButton.Disabled = true;
		
		UpdatePlayerList();
		UpdateAvailableRoles();
	}

	private void OnPlayerConnected(long id, string name)
	{
		// Enforce player limit on server
		if (Multiplayer.IsServer() && _networkManager.Players.Count > MAX_PLAYERS)
		{
			GD.Print($"[Lobby] Player limit exceeded. Disconnecting player {id}");
			// Disconnect the player who exceeded the limit
			Multiplayer.MultiplayerPeer.DisconnectPeer((int)id);
			return;
		}
		
		UpdatePlayerList();
	}

	private void OnPlayerDisconnected(long id)
	{
		// Check if the host/server disconnected (ID 1 is always the server)
		if (id == 1 && !_isHosting)
		{
			// Host disconnected - reset to default screen for clients
			if (Multiplayer.MultiplayerPeer != null)
			{
				Multiplayer.MultiplayerPeer.Close();
				Multiplayer.MultiplayerPeer = null;
			}
			
			_networkManager.Players.Clear();
			
			// Reset UI to default state
			_statusLabel.Text = "Host disconnected. Returned to lobby.";
			_hostButton.Disabled = false;
			_joinButton.Disabled = false;
			_cancelButton.Visible = false;
			_startButton.Disabled = true;
			_isConnected = false;
			
			// Disable role selection buttons instead of hiding
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
		
		// Disable role selection buttons instead of hiding
		_admireButton.Disabled = true;
		_prophetButton.Disabled = true;
		_producerButton.Disabled = true;
		
		// Clear player list
		_networkManager.Players.Clear();
		UpdatePlayerList();
	}

	private void OnRoleButtonPressed(string role)
	{
		// If host hasn't confirmed yet, this is the confirmation
		if (_isHosting && !_isConnected)
		{
			_isConnected = true;
			_statusLabel.Text = "Hosting... Waiting for players.";
			_startButton.Disabled = false; // Now host can start
		}
		
		// Send role request
		_networkManager.SendRoleRequest(role);
	}


	private void OnConnectionSucceeded()
	{
		_statusLabel.Text = "Connected! Please select a role.";
		_isConnected = true;
		
		// Enable role buttons for client
		_admireButton.Disabled = false;
		_prophetButton.Disabled = false;
		_producerButton.Disabled = false;
		
		UpdateAvailableRoles();

		// Auto-select role if requested via command line
		if (!string.IsNullOrEmpty(_autoJoinRole))
		{
			OnRoleButtonPressed(_autoJoinRole);
			_autoJoinRole = null; // Clear it
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
		// Only filter roles for clients who are connected
		// Host (server) can see all roles
		bool isServer = Multiplayer.MultiplayerPeer != null && Multiplayer.IsServer();
		
		if (!_isConnected || isServer)
		{
			// Host or not connected - enable all buttons
			_admireButton.Disabled = false;
			_prophetButton.Disabled = false;
			_producerButton.Disabled = false;
			return;
		}

		// Get roles already taken by other players
		var takenRoles = new System.Collections.Generic.HashSet<string>();
		long myId = Multiplayer.GetUniqueId();
		
		GD.Print($"[Lobby] UpdateAvailableRoles - My ID: {myId}, Total Players: {_networkManager.Players.Count}");
		
		foreach (var kvp in _networkManager.Players)
		{
			// Skip our own role selection
			if (kvp.Key != myId && kvp.Value.Role != "Observer")
			{
				GD.Print($"[Lobby] Player {kvp.Value.Name} (ID: {kvp.Key}) has role: {kvp.Value.Role}");
				takenRoles.Add(kvp.Value.Role);
			}
		}

		// Enable/disable buttons based on taken roles
		_admireButton.Disabled = takenRoles.Contains("Admirer");
		_prophetButton.Disabled = takenRoles.Contains("Prophet");
		_producerButton.Disabled = takenRoles.Contains("Producer");
		
		if (_admireButton.Disabled) GD.Print("[Lobby] Admirer role is taken");
		if (_prophetButton.Disabled) GD.Print("[Lobby] Prophet role is taken");
		if (_producerButton.Disabled) GD.Print("[Lobby] Producer role is taken");
	}
	
	private void OnBackgroundTimerTimeout()
	{
		// Alternate between the two background images
		_showingBg1 = !_showingBg1;
		_background.Texture = _showingBg1 ? _bg1 : _bg2;
	}
	
	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
		{
			// Check if click is within the Back label bounds
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
		// Disconnect if connected
		if (_isConnected || _isHosting)
		{
			OnCancelPressed();
		}
		
		GD.Print("Going back to Info scene");
		GetTree().ChangeSceneToFile("res://scenes/InfoScene.tscn");
	}
}
