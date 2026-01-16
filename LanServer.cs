using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FatalAttraction.Engine;

namespace FatalAttraction.Networking
{
    // Simple newline-delimited JSON TCP server for LAN play
    public class LanServer
    {
        private readonly int _port;
        private TcpListener _listener;
        private readonly GameEngine _engine;
        private readonly object _lock = new();

        private readonly Dictionary<Role, ClientConnection> _roleToClient = new();
        private readonly List<ClientConnection> _clients = new();
        private readonly Random _random = new();

        public LanServer(string configPath, int port)
        {
            _port = port;
            _engine = new GameEngine(configPath);
        }

        public void Start()
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            Console.WriteLine($"[LAN] Server listening on 0.0.0.0:{_port}");

            new Thread(AcceptLoop) { IsBackground = true }.Start();
            Console.WriteLine("[LAN] Waiting for clients (Admirer, Prophet, Producer)...");

            // Server main loop thread (turn broadcasting happens per action)
        }

        private void AcceptLoop()
        {
            while (true)
            {
                var clientTcp = _listener.AcceptTcpClient();
                var conn = new ClientConnection(clientTcp);
                lock (_lock)
                {
                    _clients.Add(conn);
                }
                new Thread(() => ClientLoop(conn)) { IsBackground = true }.Start();
            }
        }

        private void ClientLoop(ClientConnection conn)
        {
            try
            {
                Console.WriteLine("[LAN] Client connected");
                while (conn.IsConnected)
                {
                    var line = conn.ReadLine();
                    if (line == null) break;
                    JObject msg;
                    try
                    {
                        msg = JObject.Parse(line);
                    }
                    catch
                    {
                        continue;
                    }

                    var type = msg["type"]?.Value<string>();
                    if (type == "join")
                    {
                        var name = msg["player_name"]?.Value<string>() ?? "Player";
                        Role assigned = AssignRole(conn);
                        conn.PlayerName = name;
                        conn.AssignedRole = assigned;
                        Send(conn, new JObject
                        {
                            ["type"] = "assigned_role",
                            ["role"] = assigned.ToString().ToLower()
                        });

                        // Send snapshot
                        SendSnapshot(conn);

                        // If all roles connected, maybe notify?
                        // For real-time, we just let them play.
                        if (AllRolesConnected())
                        {
                            Console.WriteLine("[LAN] All roles connected. Game is live.");
                        }
                    }
                    else if (type == "perform_action")
                    {
                        var npcId = msg["npc_id"]?.Value<string>();
                        var actionId = msg["action_id"]?.Value<string>();
                        var roleStr = msg["role"]?.Value<string>();
                        var role = ParseRole(roleStr);

                        lock (_lock)
                        {
                            // Real-time handling
                            var result = _engine.HandleAction(npcId, actionId, role);
                            bool success = result["success"].Value<bool>();
                            var notifications = result["notifications"] as JArray;

                            Broadcast(new JObject
                            {
                                ["type"] = "action_result",
                                ["success"] = success,
                                ["notifications"] = notifications
                            });

                            BroadcastState();

                            var winMsg = _engine.CheckWinCondition(role);
                            if (winMsg != null)
                            {
                                Broadcast(new JObject
                                {
                                    ["type"] = "game_over",
                                    ["message"] = winMsg
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LAN] Client error: {ex.Message}");
            }
            finally
            {
                Disconnect(conn);
            }
        }

        private void SendSnapshot(ClientConnection conn)
        {
            var status = _engine.GetGameStatus(); 
            // Add NPCs
            var npcs = new JArray();
            foreach (var n in _engine.GameState.NPCs.Values)
            {
                npcs.Add(new JObject {
                     ["id"] = n.Id,
                     ["name"] = n.Name,
                     ["alive"] = n.Alive,
                     ["converted"] = n.Converted
                });
            }
            status["npcs"] = npcs;

            Send(conn, new JObject
            {
                ["type"] = "snapshot",
                ["state"] = status
            });
        }

        private void BroadcastState()
        {
             var status = _engine.GetGameStatus();
            var npcs = new JArray();
            foreach (var n in _engine.GameState.NPCs.Values)
            {
                npcs.Add(new JObject {
                     ["id"] = n.Id,
                     ["name"] = n.Name,
                     ["alive"] = n.Alive,
                     ["converted"] = n.Converted,
                     ["married"] = false
                });
            }
            status["npcs"] = npcs;

            Broadcast(new JObject
            {
                ["type"] = "state_update",
                ["state"] = status
            });
        }

        private void BroadcastState()
        {
             var status = _engine.GetGameStatus();
             // Add detailed NPC state for clients
            var npcs = new JArray();
            foreach (var n in _engine.GameState.NPCs.Values)
            {
                npcs.Add(new JObject {
                     ["id"] = n.Id,
                     ["name"] = n.Name,
                     ["alive"] = n.Alive,
                     ["converted"] = n.Converted,
                     ["married"] = false // Godot specific, but server tracks logic
                });
            }
            status["npcs"] = npcs;

            Broadcast(new JObject
            {
                ["type"] = "state_update",
                ["state"] = status
            });
        }


        private Role AssignRole(ClientConnection conn)
        {
            // Strict order: 1->Admirer, 2->Prophet, 3->Producer
            if (!_roleToClient.ContainsKey(Role.Admirer))
            {
                _roleToClient[Role.Admirer] = conn;
                return Role.Admirer;
            }
            if (!_roleToClient.ContainsKey(Role.Prophet))
            {
                _roleToClient[Role.Prophet] = conn;
                return Role.Prophet;
            }
            if (!_roleToClient.ContainsKey(Role.Producer))
            {
                _roleToClient[Role.Producer] = conn;
                return Role.Producer;
            }
            return Role.Admirer; // Fallback
        }

        private bool AllRolesConnected()
        {
            return new[] { Role.Admirer, Role.Prophet, Role.Producer }.All(r => _roleToClient.ContainsKey(r));
        }

        private void Broadcast(JObject payload)
        {
            lock (_lock)
            {
                foreach (var c in _clients.ToList())
                {
                    if (c.IsConnected) Send(c, payload);
                }
            }
        }

        private void Send(ClientConnection conn, JObject payload)
        {
            try
            {
                var json = payload.ToString(Formatting.None) + "\n";
                var bytes = Encoding.UTF8.GetBytes(json);
                conn.Stream.Write(bytes, 0, bytes.Length);
                conn.Stream.Flush();
            }
            catch
            {
                Disconnect(conn);
            }
        }

        private void Disconnect(ClientConnection conn)
        {
            lock (_lock)
            {
                try { conn.Dispose(); } catch { }
                _clients.Remove(conn);
                foreach (var kv in _roleToClient.Where(kv => kv.Value == conn).ToList())
                {
                    _roleToClient.Remove(kv.Key);
                }
                Console.WriteLine("[LAN] Client disconnected");
            }
        }

        private static Role ParseRole(string? role)
        {
            if (string.IsNullOrEmpty(role)) return Role.Admirer;
            return (Role)Enum.Parse(typeof(Role), role, true);
        }

        private class ClientConnection : IDisposable
        {
            private readonly TcpClient _tcp;
            private readonly StreamReader _reader;
            public readonly Stream Stream;
            public string PlayerName { get; set; } = "Player";
            public Role AssignedRole { get; set; } = Role.Admirer;

            public ClientConnection(TcpClient tcp)
            {
                _tcp = tcp;
                Stream = _tcp.GetStream();
                _reader = new StreamReader(Stream, Encoding.UTF8);
            }

            public bool IsConnected => _tcp.Connected;
            public string? ReadLine()
            {
                try { return _reader.ReadLine(); } catch { return null; }
            }

            public void Dispose()
            {
                try { Stream.Close(); } catch { }
                try { _tcp.Close(); } catch { }
            }
        }

        private class JTokenIdComparer : IEqualityComparer<JToken>
        {
            public bool Equals(JToken? x, JToken? y)
            {
                if (x == null || y == null) return false;
                return x["id"]?.Value<string>() == y["id"]?.Value<string>();
            }

            public int GetHashCode(JToken obj)
            {
                return obj["id"]?.Value<string>()?.GetHashCode() ?? 0;
            }
        }
    }
}
