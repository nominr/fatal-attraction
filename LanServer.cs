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

                        // If all roles connected, start first turn broadcast
                        if (AllRolesConnected())
                        {
                            BroadcastTurnStart();
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
                            // Validate active role
                            // Active role rotation is tracked in clients; for simplicity assume role matches current player index order
                            // Trust server-side validation via GetAvailableActions
                            bool success = _engine.PerformAction(npcId, actionId, role);

                            // Build deltas
                            var notifications = new JArray(_engine.GetNotifications());

                            var meterUpdates = new JArray();
                            foreach (var kvp in _engine.GameState.Players)
                            {
                                var pRole = kvp.Key;
                                var player = kvp.Value;
                                foreach (var m in player.Meters.Values)
                                {
                                    meterUpdates.Add(new JObject
                                    {
                                        ["role"] = pRole.ToString().ToLower(),
                                        ["meter"] = m.Name,
                                        ["value"] = m.Value,
                                        ["max"] = m.MaxValue
                                    });
                                }
                            }

                            var npcUpdates = new JArray();
                            foreach (var n in _engine.GameState.NPCs.Values)
                            {
                                npcUpdates.Add(new JObject
                                {
                                    ["npc_id"] = n.Id,
                                    ["converted"] = n.Converted,
                                    ["alive"] = n.Alive,
                                    ["married"] = false // field exists in Godot only; keep false here
                                });
                            }

                            Broadcast(new JObject
                            {
                                ["type"] = "action_result",
                                ["success"] = success,
                                ["notifications"] = notifications,
                                ["meter_updates"] = meterUpdates,
                                ["npc_updates"] = npcUpdates
                            });

                            // Win check
                            var winMsg = _engine.CheckWinCondition(role);
                            if (winMsg != null)
                            {
                                Broadcast(new JObject
                                {
                                    ["type"] = "game_over",
                                    ["message"] = winMsg
                                });
                            }

                            _engine.EndTurn();
                            BroadcastTurnEnd();
                            BroadcastTurnStart();
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
            var players = new JObject();
            foreach (var kvp in _engine.GameState.Players)
            {
                var role = kvp.Key.ToString().ToLower();
                var meters = new JObject();
                foreach (var m in kvp.Value.Meters.Values)
                {
                    meters[m.Name] = new JObject
                    {
                        ["value"] = m.Value,
                        ["max"] = m.MaxValue
                    };
                }
                players[role] = new JObject { ["meters"] = meters };
            }

            var npcs = new JArray();
            foreach (var n in _engine.GameState.NPCs.Values)
            {
                npcs.Add(new JObject
                {
                    ["id"] = n.Id,
                    ["name"] = n.Name,
                    ["alive"] = n.Alive,
                    ["converted"] = n.Converted
                });
            }

            Send(conn, new JObject
            {
                ["type"] = "snapshot",
                ["state"] = new JObject
                {
                    ["current_turn"] = _engine.GameState.CurrentTurn,
                    ["max_turns"] = _engine.GameState.MaxTurns,
                    ["players"] = players,
                    ["npcs"] = npcs,
                    ["editorial_focus"] = _engine.GameState.EditorialFocus ?? ""
                }
            });
        }

        private void BroadcastTurnStart()
        {
            // Pick NPCs according to config (default 1 if missing)
            int npcCount = _engine.GameState.Config["gameRules"]?["npcsPerTurn"]?.Value<int>() ?? 1;
            var alive = _engine.GameState.NPCs.Values.Where(n => n.Alive).ToList();
            alive = alive.OrderBy(_ => _random.Next()).ToList();
            var picked = alive.Take(Math.Max(1, npcCount)).ToList();

            var npcsPayload = new JArray();
            foreach (var npc in picked)
            {
                var prompt = _engine.GameState.Config["npcs"][npc.Id]["interactionTree"]["root"]["text"].Value<string>();
                // Provide actions for each role for convenience; client filters per active role
                var actions = _engine.GetAvailableActions(npc.Id, Role.Admirer)
                    .Union(_engine.GetAvailableActions(npc.Id, Role.Prophet))
                    .Union(_engine.GetAvailableActions(npc.Id, Role.Producer))
                    .Distinct(new JTokenIdComparer())
                    .Select(a => new JObject { ["id"] = a["id"].Value<string>(), ["text"] = a["text"].Value<string>() });
                npcsPayload.Add(new JObject
                {
                    ["id"] = npc.Id,
                    ["prompt"] = prompt,
                    ["actions"] = new JArray(actions)
                });
            }

            Broadcast(new JObject
            {
                ["type"] = "turn_started",
                ["turn"] = _engine.GameState.CurrentTurn,
                ["active_role"] = GetActiveRoleString(),
                ["npcs"] = npcsPayload,
                ["editorial_focus"] = _engine.GameState.EditorialFocus ?? ""
            });
        }

        private void BroadcastTurnEnd()
        {
            Broadcast(new JObject
            {
                ["type"] = "turn_ended",
                ["turn"] = _engine.GameState.CurrentTurn
            });
        }

        private string GetActiveRoleString()
        {
            // Rotate roles based on current turn and index, mirroring ChatClient order
            var order = new[] { Role.Admirer, Role.Prophet, Role.Producer };
            int idx = (_engine.GameState.CurrentTurn - 1) % order.Length;
            return order[idx].ToString().ToLower();
        }

        private Role AssignRole(ClientConnection conn)
        {
            foreach (var role in new[] { Role.Admirer, Role.Prophet, Role.Producer })
            {
                if (!_roleToClient.ContainsKey(role))
                {
                    _roleToClient[role] = conn;
                    return role;
                }
            }
            // If all taken, default to Admirer (spectator mode can be added later)
            return Role.Admirer;
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
