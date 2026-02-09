using Godot;
using System;
using System.Collections.Generic;

[GlobalClass]
public partial class NetworkManager : Node
{
	[Signal]
	public delegate void PlayerConnectedEventHandler(long id, string name);
	
	[Signal]
	public delegate void PlayerDisconnectedEventHandler(long id);
	
	[Signal]
	public delegate void ConnectionFailedEventHandler();
	
	[Signal]
	public delegate void ConnectionSucceededEventHandler();

	[Signal]
	public delegate void GameStartedEventHandler();

	[Signal]
	public delegate void RoleRejectedEventHandler(string role, string reason);

	[Signal]
	public delegate void PlayerInteractionEventHandler(long senderId, string npcId, string actionId);

	private const int DefaultPort = 7000;
	private const int MaxClients = 4;

	public string PlayerName { get; set; } = "Player";
	public Dictionary<long, PlayerInfo> Players { get; private set; } = new();

	public struct PlayerInfo
	{
		public string Name;
		public int Id;
		public string Role; // "Admirer", "Prophet", "Producer"
	}

	public override void _Ready()
	{
		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += OnPeerDisconnected;
		Multiplayer.ConnectedToServer += OnConnectedToServer;
		Multiplayer.ConnectionFailed += OnConnectionFailed;
		Multiplayer.ServerDisconnected += OnServerDisconnected;
	}

	public void HostGame(string playerName, int port = DefaultPort)
	{
		PlayerName = playerName;
		var peer = new ENetMultiplayerPeer();
		var error = peer.CreateServer(port, MaxClients);
		
		if (error != Error.Ok)
		{
			GD.PrintErr($"Failed to create server: {error}");
			return;
		}

		Multiplayer.MultiplayerPeer = peer;
		GD.Print("Server started. Waiting for players...");
		
		OnConnectedToServer(); // Host is always connected to self
	}

	public void JoinGame(string address, string playerName, int port = DefaultPort)
	{
		PlayerName = playerName;
		var peer = new ENetMultiplayerPeer();
		var error = peer.CreateClient(address, port);

		if (error != Error.Ok)
		{
			GD.PrintErr($"Failed to create client: {error}");
			EmitSignal(SignalName.ConnectionFailed);
			return;
		}

		Multiplayer.MultiplayerPeer = peer;
		GD.Print($"Connecting to {address}...");
	}

	private void OnPeerConnected(long id)
	{
		GD.Print($"Peer connected: {id}");
	}

	private void OnPeerDisconnected(long id)
	{
		GD.Print($"Peer disconnected: {id}");
		if (Multiplayer.IsServer())
		{
			// Update server state and broadcast removal to all clients
			if (Players.ContainsKey(id))
			{
				Players.Remove(id);
				Rpc(MethodName.SyncPlayerLeft, id);
			}
		}
		else
		{
			// Client just updates local view
			if (Players.ContainsKey(id))
			{
				Players.Remove(id);
				EmitSignal(SignalName.PlayerDisconnected, id);
			}
		}
	}

	private void OnConnectedToServer()
	{
		var id = Multiplayer.GetUniqueId();
		
		// Register ourselves
		Rpc(MethodName.RegisterPlayer, PlayerName);
		EmitSignal(SignalName.ConnectionSucceeded);
	}

	private void OnConnectionFailed()
	{
		EmitSignal(SignalName.ConnectionFailed);
	}

	private void OnServerDisconnected()
	{
		GD.Print("Server disconnected.");
		Players.Clear();
		Multiplayer.MultiplayerPeer = null;
		EmitSignal(SignalName.PlayerDisconnected, 1); // Server ID is always 1
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RegisterPlayer(string name)
	{
		var senderId = Multiplayer.GetRemoteSenderId();
		// For locally-called RPCs (like host registering itself), sender ID is 0
		// Use the actual unique ID in that case
		long id = senderId == 0 ? Multiplayer.GetUniqueId() : senderId;
		
		var info = new PlayerInfo { Name = name, Id = (int)id, Role = "Observer" };
		Players[id] = info;

		if (Multiplayer.IsServer())
		{
			// Broadcast the new player to all peers
			Rpc(MethodName.SyncPlayerJoined, id, name);
			// Send the full current player list to the newly joined peer
			foreach (var kvp in Players)
			{
				RpcId(id, MethodName.SyncPlayerJoined, kvp.Key, kvp.Value.Name);
				// Also sync role for each player
				RpcId(id, MethodName.SyncPlayerRole, kvp.Key, kvp.Value.Role);
			}
		}
		else
		{
			// Local execution for the origin peer (client or host)
			EmitSignal(SignalName.PlayerConnected, id, name);
		}
	}

	public void SendStartGame()
	{
		if (Multiplayer.IsServer())
		{
			GD.Print($"Server sending StartGame RPC to {Multiplayer.GetPeers().Length} peers.");
			Rpc(MethodName.StartGame);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void StartGame()
	{
		GD.Print($"StartGame RPC received from {Multiplayer.GetRemoteSenderId()} on peer {Multiplayer.GetUniqueId()}");
		EmitSignal(SignalName.GameStarted);
	}

	public void SendRoleRequest(string role)
	{
		if (Multiplayer.IsServer())
		{
			AssignRole(Multiplayer.GetUniqueId(), role);
		}
		else
		{
			RpcId(1, MethodName.RequestRole, role);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RequestRole(string role)
	{
		long senderId = Multiplayer.GetRemoteSenderId();
		AssignRole(senderId, role);
	}

	public void AssignRole(long playerId, string role)
	{
		if (!Multiplayer.IsServer()) return;
		
		GD.Print($"[NetworkManager] AssignRole called for player {playerId} with role '{role}'");
		
		// Check if player exists in dictionary
		if (!Players.ContainsKey(playerId))
		{
			GD.PrintErr($"[NetworkManager] Player {playerId} not found in Players dictionary!");
			return;
		}
		
		// Check if role is already taken by another player (ignore Observer)
		if (role != "Observer")
		{
			foreach (var kvp in Players)
			{
				if (kvp.Key != playerId && kvp.Value.Role == role && kvp.Value.Role != "Observer")
				{
					GD.Print($"[NetworkManager] Role '{role}' is already taken by player {kvp.Value.Name}");
					// Notify the requesting player that their role was rejected
					RpcId(playerId, MethodName.NotifyRoleRejected, role, "already taken");
					return;
				}
			}
		}
		
		GD.Print($"[NetworkManager] Role '{role}' approved for player {playerId}. Broadcasting...");
		Rpc(MethodName.SyncPlayerRole, playerId, role);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SyncPlayerRole(long playerId, string role)
	{
		GD.Print($"[NetworkManager] SyncPlayerRole called for player {playerId} with role '{role}' on peer {Multiplayer.GetUniqueId()}");
		
		if (Players.ContainsKey(playerId))
		{
			var info = Players[playerId];
			GD.Print($"[NetworkManager] Player {playerId} ({info.Name}) role updated from '{info.Role}' to '{role}'");
			info.Role = role;
			Players[playerId] = info;
			EmitSignal(SignalName.PlayerConnected, playerId, info.Name);
		}
		else
		{
			GD.PrintErr($"[NetworkManager] SyncPlayerRole: Player {playerId} not found in Players dictionary!");
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void NotifyRoleRejected(string role, string reason)
	{
		EmitSignal(SignalName.RoleRejected, role, reason);
	}

	// Broadcast: server informs clients a player joined
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SyncPlayerJoined(long playerId, string name)
	{
		var info = new PlayerInfo { Name = name, Id = (int)playerId, Role = Players.ContainsKey(playerId) ? Players[playerId].Role : "Observer" };
		Players[playerId] = info;
		EmitSignal(SignalName.PlayerConnected, playerId, name);
	}

	// Broadcast: server informs clients a player left
	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SyncPlayerLeft(long playerId)
	{
		if (Players.ContainsKey(playerId))
		{
			Players.Remove(playerId);
			EmitSignal(SignalName.PlayerDisconnected, playerId);
		}
	}

	public void SendInteract(string npcId, string actionId)
	{
		// Clients send to server (ID 1)
		RpcId(1, MethodName.Interact, npcId, actionId);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void Interact(string npcId, string actionId)
	{
		long senderId = Multiplayer.GetRemoteSenderId();
		// Emit signal for GameLogic to handle
		EmitSignal(SignalName.PlayerInteraction, senderId, npcId, actionId);
	}
}
