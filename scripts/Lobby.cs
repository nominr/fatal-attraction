using Godot;
using System;

public partial class Lobby : Control
{
	[Export]
	public PackedScene MainGameScene { get; set; }

	private NetworkManager _networkManager;

	private LineEdit _nameInput;
	private LineEdit _ipInput;
	private Button _hostButton;
	private Button _joinButton;
	private Button _cancelButton;
	private Button _startButton;
	private Label _statusLabel;
	private ItemList _playerList;
	private Button _admireButton;
	private Button _prophetButton;
	private Button _producerButton;
	private bool _isHosting = false;
	private bool _isConnected = false;
	private string _autoJoinRole = null;

	public override void _Ready()
	{
		// Assuming NetworkManager is an autoload named "NetworkManager"
		_networkManager = GetNode<NetworkManager>("/root/NetworkManager");
		
		// Map UI nodes
		_nameInput = GetNode<LineEdit>("Panel/VBoxContainer/NameInput");
		_ipInput = GetNode<LineEdit>("Panel/VBoxContainer/IPInput");
		_hostButton = GetNode<Button>("Panel/VBoxContainer/HBoxContainer/HostButton");
		_joinButton = GetNode<Button>("Panel/VBoxContainer/HBoxContainer/JoinButton");
		_cancelButton = GetNode<Button>("Panel/VBoxContainer/HBoxContainer/CancelButton");
		_startButton = GetNode<Button>("Panel/VBoxContainer/StartButton");
		_statusLabel = GetNode<Label>("Panel/VBoxContainer/StatusLabel");
		_playerList = GetNode<ItemList>("Panel/VBoxContainer/PlayerList");
		_admireButton = GetNode<Button>("Panel/VBoxContainer/RoleButtonsContainer/AdmirerButton");
		_prophetButton = GetNode<Button>("Panel/VBoxContainer/RoleButtonsContainer/ProphetButton");
		_producerButton = GetNode<Button>("Panel/VBoxContainer/RoleButtonsContainer/ProducerButton");

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

		_startButton.Disabled = true;
		
		// Hide role selection until player joins or hosts
		var roleButtonsContainer = GetNode<Control>("Panel/VBoxContainer/RoleButtonsContainer");
		roleButtonsContainer.Visible = false;
		var roleLabel = GetNode<Label>("Panel/VBoxContainer/RoleLabel");
		roleLabel.Visible = false;

		// Display Local IP(s) to help with LAN hosting
		var ips = Godot.IP.GetLocalAddresses();
		string ipText = "Your IP(s): ";
		foreach(string ip in ips)
		{
			// Filter for likely LAN IPs (IPv4, not localhost)
			if (ip.Contains(".") && !ip.StartsWith("127.") && !ip.StartsWith("169."))
			{
				ipText += ip + "\n";
			}
		}
		var ipLabel = new Label { Text = ipText };
		GetNode("Panel/VBoxContainer").AddChild(ipLabel);
		GetNode("Panel/VBoxContainer").MoveChild(ipLabel, 0);

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
		
		// Show role options for host
		var roleButtonsContainer = GetNode<Control>("Panel/VBoxContainer/RoleButtonsContainer");
		roleButtonsContainer.Visible = true;
		var roleLabel = GetNode<Label>("Panel/VBoxContainer/RoleLabel");
		roleLabel.Visible = true;
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
		
		// Hide role selection
		var roleButtonsContainer = GetNode<Control>("Panel/VBoxContainer/RoleButtonsContainer");
		roleButtonsContainer.Visible = false;
		var roleLabel = GetNode<Label>("Panel/VBoxContainer/RoleLabel");
		roleLabel.Visible = false;
		
		UpdatePlayerList();
		UpdateAvailableRoles();
	}

	private void OnPlayerConnected(long id, string name)
	{
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
			
			// Hide role selection
			var roleButtonsContainer = GetNode<Control>("Panel/VBoxContainer/RoleButtonsContainer");
			roleButtonsContainer.Visible = false;
			var roleLabel = GetNode<Label>("Panel/VBoxContainer/RoleLabel");
			roleLabel.Visible = false;
			
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
		
		// Hide role selection
		var roleButtonsContainer = GetNode<Control>("Panel/VBoxContainer/RoleButtonsContainer");
		roleButtonsContainer.Visible = false;
		var roleLabel = GetNode<Label>("Panel/VBoxContainer/RoleLabel");
		roleLabel.Visible = false;
		
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
		
		// NOW show role options
		var roleButtonsContainer = GetNode<Control>("Panel/VBoxContainer/RoleButtonsContainer");
		roleButtonsContainer.Visible = true;
		var roleLabel = GetNode<Label>("Panel/VBoxContainer/RoleLabel");
		roleLabel.Visible = true;
		
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
}
