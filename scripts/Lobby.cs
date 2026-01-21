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
	private Button _startButton;
	private Label _statusLabel;
	private ItemList _playerList;
	private OptionButton _roleOption;

	public override void _Ready()
	{
		// Assuming NetworkManager is an autoload named "NetworkManager"
		_networkManager = GetNode<NetworkManager>("/root/NetworkManager");
		
		// Map UI nodes
		_nameInput = GetNode<LineEdit>("Panel/VBoxContainer/NameInput");
		_ipInput = GetNode<LineEdit>("Panel/VBoxContainer/IPInput");
		_hostButton = GetNode<Button>("Panel/VBoxContainer/HBoxContainer/HostButton");
		_joinButton = GetNode<Button>("Panel/VBoxContainer/HBoxContainer/JoinButton");
		_startButton = GetNode<Button>("Panel/VBoxContainer/StartButton");
		_statusLabel = GetNode<Label>("Panel/VBoxContainer/StatusLabel");
		_playerList = GetNode<ItemList>("Panel/VBoxContainer/PlayerList");
		_roleOption = GetNode<OptionButton>("Panel/VBoxContainer/RoleOption");

		// Connect signals
		_hostButton.Pressed += OnHostPressed;
		_joinButton.Pressed += OnJoinPressed;
		_startButton.Pressed += OnStartPressed;
		_roleOption.ItemSelected += OnRoleSelected;

		_networkManager.PlayerConnected += OnPlayerConnected;
		_networkManager.PlayerDisconnected += OnPlayerDisconnected;
		_networkManager.ConnectionFailed += OnConnectionFailed;
		_networkManager.ConnectionSucceeded += OnConnectionSucceeded;
		_networkManager.GameStarted += OnGameStarted;

		_startButton.Disabled = true;

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
	}

	private void OnHostPressed()
	{
		if (string.IsNullOrEmpty(_nameInput.Text))
		{
			_statusLabel.Text = "Please enter a name.";
			return;
		}

		_networkManager.HostGame(_nameInput.Text);
		_statusLabel.Text = "Hosting... Waiting for players.";
		_hostButton.Disabled = true;
		_joinButton.Disabled = true;
		_startButton.Disabled = false; // Host can start
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
	}

	private void OnStartPressed()
	{
		_networkManager.SendStartGame();
	}

	private void OnPlayerConnected(long id, string name)
	{
		UpdatePlayerList();
	}

	private void OnPlayerDisconnected(long id)
	{
		UpdatePlayerList();
	}

	private void OnConnectionFailed()
	{
		_statusLabel.Text = "Connection Failed.";
		_hostButton.Disabled = false;
		_joinButton.Disabled = false;
	}

	private void OnRoleSelected(long index)
	{
		// If connected, request update
		if (_networkManager.Players.ContainsKey(Multiplayer.GetUniqueId()))
		{
			string role = _roleOption.GetItemText((int)index);
			_networkManager.SendRoleRequest(role);
		}
	}

	private void OnConnectionSucceeded()
	{
		_statusLabel.Text = "Connected!";
		// Send selected role
		string role = _roleOption.GetItemText(_roleOption.Selected);
		_networkManager.SendRoleRequest(role);
	}

	private void OnGameStarted()
	{
		GD.Print("Transitioning to main game...");
		// Transition code - assuming usage of SceneTree.ChangeSceneToPacked
		// But since we are passing args, we might need a different approach or set globals
		// For now, simpler transition
		GetTree().ChangeSceneToPacked(MainGameScene);
	}

	private void UpdatePlayerList()
	{
		_playerList.Clear();
		foreach (var player in _networkManager.Players.Values)
		{
			_playerList.AddItem($"{player.Name} ({player.Role})");
		}
	}
}
