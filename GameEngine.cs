using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json.Linq;

namespace FatalAttraction.Engine
{
    public enum Role
    {
        Admirer,
        Prophet,
        Producer
    }

    public class Meter
    {
        public string Name { get; set; }
        public double Value { get; set; }
        public double MinValue { get; set; } = 0;
        public double MaxValue { get; set; } = 10;

        public Meter(string name, double startValue = 0, double min = 0, double max = 10)
        {
            Name = name;
            Value = startValue;
            MinValue = min;
            MaxValue = max;
        }

        public double Add(double delta)
        {
            Value = Math.Max(MinValue, Math.Min(MaxValue, Value + delta));
            return Value;
        }

        public override string ToString()
        {
            return $"{Name}: {Value}/{MaxValue}";
        }
    }

    public class NPC
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool Converted { get; set; } = false;
        public bool Alive { get; set; } = true;
        public bool IsLoveInterest { get; set; } = false;

        public NPC(string id, string name, bool isLoveInterest = false)
        {
            Id = id;
            Name = name;
            IsLoveInterest = isLoveInterest;
        }
    }

    public class PlayerState
    {
        public Role Role { get; set; }
        public Dictionary<string, Meter> Meters { get; set; } = new();
        public List<string> ActionsTaken { get; set; } = new();
        public List<string> GoalsMet { get; set; } = new();

        public PlayerState(Role role)
        {
            Role = role;
        }

        public Meter GetMeter(string meterName)
        {
            return Meters.ContainsKey(meterName) ? Meters[meterName] : null;
        }
    }

    public class GameState
    {
        public JObject Config { get; private set; }
        public int CurrentTurn { get; private set; } = 1;
        public int MaxTurns { get; private set; }
        public Dictionary<Role, PlayerState> Players { get; private set; } = new();
        public Dictionary<string, NPC> NPCs { get; private set; } = new();
        public List<string> Notifications { get; private set; } = new();
        public string EditorialFocus { get; set; }
        public bool AdmirerReported { get; set; } = false;

        public GameState(string configPath)
        {
            var configText = File.ReadAllText(configPath);
            Config = JObject.Parse(configText);
            MaxTurns = Config["gameRules"]["turnsPerGame"].Value<int>();
            
            InitializeGame();
        }

        private void InitializeGame()
        {
            // Initialize players
            foreach (var roleName in new[] { "admirer", "prophet", "producer" })
            {
                var role = (Role)Enum.Parse(typeof(Role), roleName, ignoreCase: true);
                var player = new PlayerState(role);
                
                var roleConfig = Config["roles"][roleName];
                var metersConfig = roleConfig["meters"];
                
                if (metersConfig != null)
                {
                    foreach (var meterToken in metersConfig.Children().OfType<JProperty>())
                    {
                        var meterName = meterToken.Name;
                        var meterConfig = meterToken.Value;
                        
                        var meter = new Meter(
                            meterName,
                            meterConfig["start"]?.Value<double>() ?? 0,
                            meterConfig["min"]?.Value<double>() ?? 0,
                            meterConfig["max"]?.Value<double>() ?? 10
                        );
                        
                        player.Meters[meterName] = meter;
                    }
                }
                
                Players[role] = player;
            }

            // Initialize NPCs
            var npcsConfig = Config["npcs"];
            if (npcsConfig != null)
            {
                foreach (var npcToken in npcsConfig.Children().OfType<JProperty>())
                {
                    var npcId = npcToken.Name;
                    var npcConfig = npcToken.Value;
                    
                    var npc = new NPC(
                        npcId,
                        npcConfig["name"].Value<string>(),
                        npcConfig["role"]?.Value<string>() == "love_interest"
                    );
                    
                    NPCs[npcId] = npc;
                }
            }
        }

        public PlayerState GetPlayerState(Role role)
        {
            return Players.ContainsKey(role) ? Players[role] : null;
        }

        public NPC GetNPC(string npcId)
        {
            return NPCs.ContainsKey(npcId) ? NPCs[npcId] : null;
        }

        public void AddNotification(string message)
        {
            Notifications.Add(message);
        }

        public List<string> GetAndClearNotifications()
        {
            var notifs = new List<string>(Notifications);
            Notifications.Clear();
            return notifs;
        }

        public bool AdvanceTurn()
        {
            if (CurrentTurn < MaxTurns)
            {
                CurrentTurn++;
                return true;
            }
            return false;
        }

        public bool IsGameOver => CurrentTurn >= MaxTurns;
    }

    public class InteractionResolver
    {
        private readonly GameState _gameState;
        private readonly Random _random = new();

        public InteractionResolver(GameState gameState)
        {
            _gameState = gameState;
        }

        public List<JToken> GetNPCInteractions(string npcId, Role playerRole)
        {
            var npcConfig = _gameState.Config["npcs"]?[npcId];
            if (npcConfig == null)
                return new();

            var npc = _gameState.GetNPC(npcId);
            if (npc == null)
                return new();

            var options = npcConfig["interactionTree"]?["root"]?["options"] as JArray ?? new();
            var availableOptions = new List<JToken>();

            foreach (var option in options)
            {
                if (IsOptionAvailable(option, playerRole, npc))
                {
                    availableOptions.Add(option);
                }
            }

            return availableOptions;
        }

        private bool IsOptionAvailable(JToken option, Role playerRole, NPC npc)
        {
            var requires = option["requires"];
            if (requires != null)
            {
                var requiredRole = requires["role"]?.Value<string>();
                if (!string.IsNullOrEmpty(requiredRole) && requiredRole != playerRole.ToString().ToLower())
                    return false;

                var requiredStatus = requires["npcStatus"]?.Value<string>();
                if (!string.IsNullOrEmpty(requiredStatus))
                {
                    if (requiredStatus == "converted" && !npc.Converted)
                        return false;
                    if (requiredStatus == "nonConverted" && npc.Converted)
                        return false;
                }
            }

            return true;
        }

        public bool ResolveInteraction(string npcId, string optionId, Role playerRole)
        {
            var npcConfig = _gameState.Config["npcs"]?[npcId];
            if (npcConfig == null)
                return false;

            var options = npcConfig["interactionTree"]?["root"]?["options"] as JArray ?? new();
            var option = options.FirstOrDefault(o => o["id"]?.Value<string>() == optionId);

            if (option == null)
                return false;

            // Resolve chance-based or RPS actions
            var resolutionType = option["resolution"]?["type"]?.Value<string>();
            bool success = true;

            if (resolutionType == "chance")
            {
                var successChance = option["resolution"]["successChance"].Value<double>();
                success = _random.NextDouble() < successChance;
            }
            else if (resolutionType == "rps")
            {
                // Simplified RPS: 50% success for demo
                success = _random.NextDouble() < 0.5;
            }

            // Apply effects
            var effectsKey = success ? "effects_on_success" : "effects_on_failure";
            var effects = option[effectsKey] ?? option["effects"] ?? new JArray();

            foreach (var effect in effects.Children())
            {
                ApplyEffect(effect, playerRole, npcId, success);
            }

            return success;
        }

        private void ApplyEffect(JToken effect, Role playerRole, string npcId, bool success)
        {
            var meterName = effect["meter"]?.Value<string>();
            if (!string.IsNullOrEmpty(meterName))
            {
                var delta = effect["delta"].Value<double>();
                var player = _gameState.GetPlayerState(playerRole);
                var meter = player?.GetMeter(meterName);

                if (meter != null)
                {
                    meter.Add(delta);
                    var verb = delta > 0 ? "raised" : "lowered";
                    _gameState.AddNotification(
                        $"[{playerRole}] {verb} {meterName} to {meter.Value}/{meter.MaxValue}"
                    );
                }
            }

            var notification = effect["notification"]?.Value<string>();
            if (!string.IsNullOrEmpty(notification))
            {
                _gameState.AddNotification(notification);
            }

            var npcEffect = effect["npc"]?.Value<string>();
            if (!string.IsNullOrEmpty(npcEffect))
            {
                var npc = _gameState.GetNPC(npcId);
                if (npc != null)
                {
                    var field = effect["field"].Value<string>();
                    var value = effect["value"];

                    if (field == "convertedStatus")
                        npc.Converted = value.Value<bool>();
                    else if (field == "alive")
                        npc.Alive = value.Value<bool>();
                }
            }
        }
    }

    public class GameEngine
    {
        public GameState GameState { get; private set; }
        public InteractionResolver InteractionResolver { get; private set; }
        private readonly Random _random = new();

        public GameEngine(string configPath)
        {
            GameState = new GameState(configPath);
            InteractionResolver = new InteractionResolver(GameState);
        }

        public (string npcId, string prompt) GetCurrentNPCPrompt()
        {
            var npcPool = GameState.Config["demoClient"]["npcPool"].Values<string>().ToList();
            var npcId = npcPool[_random.Next(npcPool.Count)];
            var npc = GameState.GetNPC(npcId);

            if (npc == null || !npc.Alive)
                return GetCurrentNPCPrompt();

            var npcConfig = GameState.Config["npcs"][npcId];
            var prompt = npcConfig["interactionTree"]["root"]["text"].Value<string>();

            return (npcId, prompt);
        }

        public List<JToken> GetAvailableActions(string npcId, Role playerRole)
        {
            return InteractionResolver.GetNPCInteractions(npcId, playerRole);
        }

        public JObject HandleAction(string npcId, string actionId, Role playerRole)
        {
            var result = new JObject
            {
                ["success"] = false,
                ["notifications"] = new JArray()
            };

            // Check if Admirer is reported (Observer mode)
            if (playerRole == Role.Admirer && GameState.AdmirerReported)
            {
                result["notifications"] = new JArray("You have been reported! You are now an observer.");
                return result;
            }

            var npc = GameState.GetNPC(npcId);

            // Special Action Logic based on ID conventions
            if (actionId.StartsWith("kill"))
            {
                if (npc != null && !npc.Converted)
                {
                    npc.Alive = false;
                    result["success"] = true;
                    ((JArray)result["notifications"]).Add($"{playerRole} killed {npc.Name}!");
                }
            }
            else if (actionId.StartsWith("convert"))
            {
                // Simple probabilistic conversion for now (RPS placeholder)
                if (npc != null && !npc.Converted)
                {
                    // Success based on chaos level or pure chance
                    var chaos = GameState.GetPlayerState(Role.Prophet)?.GetMeter("chaos")?.Value ?? 0;
                    var chance = 0.3 + (chaos * 0.05); // higher chaos = easier convert
                    if (_random.NextDouble() < chance)
                    {
                        npc.Converted = true;
                        result["success"] = true;
                         // Add Chaos
                        GameState.GetPlayerState(Role.Prophet)?.GetMeter("chaos")?.Add(1);
                        ((JArray)result["notifications"]).Add($"Prophet converted {npc.Name}!");
                    }
                    else
                    {
                        ((JArray)result["notifications"]).Add($"{npc.Name} resisted conversion.");
                    }
                }
            }
            else if (actionId == "report_admirer")
            {
                GameState.AdmirerReported = true;
                result["success"] = true;
                ((JArray)result["notifications"]).Add("PRODUCER REPORTED THE ADMIRER! Admirer is now powerless.");
            }
            else if (actionId.StartsWith("unconvert"))
            {
                 if (npc != null && npc.Converted)
                 {
                    double chance = playerRole == Role.Producer ? 0.75 : 0.55;
                    if (_random.NextDouble() < chance)
                    {
                        npc.Converted = false;
                        result["success"] = true;
                        ((JArray)result["notifications"]).Add($"{playerRole} unconverted {npc.Name}!");
                    }
                    else
                    {
                        ((JArray)result["notifications"]).Add($"Failed to unconvert {npc.Name}.");
                    }
                 }
            }
            else if (actionId.StartsWith("marry"))
            {
                 // Marriage logic
                 if (npc != null)
                 {
                     result["success"] = true;
                     GameState.GetPlayerState(Role.Producer)?.GetMeter("ratings")?.Add(1);
                     if (playerRole == Role.Admirer && npc.IsLoveInterest)
                        GameState.GetPlayerState(Role.Admirer)?.GetMeter("love")?.Add(5); // Big boost

                     ((JArray)result["notifications"]).Add($"{playerRole} arranged a marriage for {npc.Name}!");
                 }
            }
            else
            {
                // Fallback to interaction resolver (generic prompts)
                // For this refactor we rely on the special cases above for the core mechanics interaction
                // But we can still support the text tree via Resolver if needed.
                bool resolved = InteractionResolver.ResolveInteraction(npcId, actionId, playerRole);
                result["success"] = resolved;
            }

            return result;
        }

        public JObject GetGameStatus()
        {
            var status = new JObject
            {
                { "turn", GameState.CurrentTurn },
                { "max_turns", GameState.MaxTurns },
                { "editorial_focus", GameState.EditorialFocus ?? "" },
                { "admirer_reported", GameState.AdmirerReported }
            };

            var playersObj = new JObject();
            foreach (var kvp in GameState.Players)
            {
                var playerObj = new JObject();
                var metersObj = new JObject();

                foreach (var meter in kvp.Value.Meters.Values)
                {
                    metersObj[meter.Name] = new JObject
                    {
                        { "value", meter.Value },
                        { "max", meter.MaxValue }
                    };
                }

                playerObj["meters"] = metersObj;
                playersObj[kvp.Key.ToString().ToLower()] = playerObj;
            }

            status["players"] = playersObj;
            return status;
        }
    }
}
