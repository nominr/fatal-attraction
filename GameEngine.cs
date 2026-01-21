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
		public bool Married { get; set; } = false;
		public bool IsLoveInterest { get; set; } = false;
		public bool IsTarget { get; set; } = false;

		public NPC(string id, string name, bool isLoveInterest = false, bool isTarget = false)
		{
			Id = id;
			Name = name;
			IsLoveInterest = isLoveInterest;
			IsTarget = isTarget;
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
						npcConfig["role"]?.Value<string>() == "love_interest",
						npcConfig["isTarget"]?.Value<bool>() ?? false
					);
					npc.Married = npcConfig["married"]?.Value<bool>() ?? false;
					npc.Converted = npcConfig["convertedStatus"]?.Value<bool>() ?? false;
					
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

			var player = _gameState.GetPlayerState(playerRole);
			var options = npcConfig["interactionTree"]?["root"]?["options"] as JArray ?? new();
			var availableOptions = new List<JToken>();

			foreach (var option in options)
			{
				if (IsOptionAvailable(option, playerRole, npc, player))
				{
					availableOptions.Add(option);
				}
			}

			return availableOptions;
		}

		private bool IsOptionAvailable(JToken option, Role playerRole, NPC npc, PlayerState player = null)
		{
			// Dead NPCs cannot be interacted with (except "leave")
			var actionId = option["id"]?.Value<string>();
			if (!npc.Alive && actionId != "leave")
				return false;

			var requires = option["requires"];
			if (requires != null)
			{
				// Role check
				var requiredRole = requires["role"]?.Value<string>();
				if (!string.IsNullOrEmpty(requiredRole) && requiredRole != playerRole.ToString().ToLower())
					return false;

				// Converted status check
				var requiredStatus = requires["npcStatus"]?.Value<string>();
				if (!string.IsNullOrEmpty(requiredStatus))
				{
					if (requiredStatus == "converted" && !npc.Converted)
						return false;
					if (requiredStatus == "nonConverted" && npc.Converted)
						return false;
				}

				// Marriage eligibility check (NPC must not be married)
				var marriageCheck = requires["npcMarriageable"]?.Value<bool>();
				if (marriageCheck == true && npc.Married)
					return false;
			}

			// Special check for Admirer's marry action - requires love meter at max
			var isAdmirerMarry = option["marry_admirer_love"]?.Value<bool>() ?? false;
			if (isAdmirerMarry && player != null)
			{
				var loveMeter = player.GetMeter("love");
				if (loveMeter == null || loveMeter.Value < loveMeter.MaxValue)
					return false;
				// Also can't marry if already married
				if (npc.Married)
					return false;
			}

			return true;
		}

		public (bool success, string failReason) ResolveInteraction(string npcId, string optionId, Role playerRole)
		{
			var npcConfig = _gameState.Config["npcs"]?[npcId];
			if (npcConfig == null)
				return (false, "NPC not found");

			var npc = _gameState.GetNPC(npcId);
			if (npc == null)
				return (false, "NPC not found");

			// Check if NPC is dead first
			if (!npc.Alive && optionId != "leave")
				return (false, $"{npc.Name} is no longer available");

			var options = npcConfig["interactionTree"]?["root"]?["options"] as JArray ?? new();
			var option = options.FirstOrDefault(o => o["id"]?.Value<string>() == optionId);

			if (option == null)
				return (false, "Invalid action");

			// CRITICAL: Revalidate at execution time to prevent race conditions
			var player = _gameState.GetPlayerState(playerRole);
			if (!IsOptionAvailable(option, playerRole, npc, player))
				return (false, $"Action no longer valid for {npc.Name}");

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

			if (!success)
				return (false, "Action failed (chance roll)");

			return (true, null);
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
					else if (field == "married")
						npc.Married = value.Value<bool>();
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

			public (bool success, string failReason) PerformAction(string npcId, string actionId, Role playerRole)
		{
			return InteractionResolver.ResolveInteraction(npcId, actionId, playerRole);
		}

		public void StartTurn(Role playerRole)
		{
			var player = GameState.GetPlayerState(playerRole);
			GameState.AddNotification($"\n--- TURN {GameState.CurrentTurn} START ---");
			GameState.AddNotification($"[{playerRole}]");

			foreach (var meter in player.Meters.Values)
			{
				GameState.AddNotification($"  {meter.Name.ToUpper()}: {meter.Value}/{meter.MaxValue}");
			}
		}

		public void EndTurn()
		{
			GameState.AddNotification($"--- TURN {GameState.CurrentTurn} END ---\n");
			GameState.AdvanceTurn();
		}

		public string CheckWinCondition(Role playerRole)
		{
			var player = GameState.GetPlayerState(playerRole);
			var winConfig = GameState.Config["gameRules"]["winConditions"][playerRole.ToString().ToLower()];

			var primary = winConfig["primary"];
			var requirement = primary["requirement"];

			if (CheckCondition(player, requirement))
			{
				var goal = primary["goal"].Value<string>();
				return $"{playerRole} wins! ({goal})";
			}

			return null;
		}

		private bool CheckCondition(PlayerState player, JToken requirement)
		{
			var meterName = requirement["meter"]?.Value<string>();
			if (!string.IsNullOrEmpty(meterName))
			{
				var minValue = requirement["minValue"]?.Value<double>();
				var meter = player.GetMeter(meterName);
				if (meter != null && minValue.HasValue && meter.Value >= minValue.Value)
					return true;
			}

			return false;
		}

		public List<string> GetNotifications()
		{
			return GameState.GetAndClearNotifications();
		}

		public JObject GetGameStatus()
		{
			var status = new JObject
			{
				{ "turn", GameState.CurrentTurn },
				{ "max_turns", GameState.MaxTurns },
				{ "editorial_focus", GameState.EditorialFocus }
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
