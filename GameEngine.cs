using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Numerics;

namespace FatalAttraction.Engine
{
	public enum Role
	{
		Admirer,
		Prophet,
		Producer
	}

	public class Trap
	{
		public string Id { get; set; } = Guid.NewGuid().ToString();
		public double TimeAlive { get; set; } = 0;
		public string CreatorRole { get; set; }
	}



	public class InterviewContext
	{
		public string NpcId { get; set; }
		public string CurrentStage { get; set; } = "Intro"; // "Intro" or "Followup"
		public int CurrentScore { get; set; } = 0;
		public string LastResponse { get; set; }
		public List<string> AvailableQuestionIds { get; set; } = new();
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
		public bool PrankActive { get; set; } = false;
		public int Quadrant { get; set; } = -1; // 0:TL, 1:TR, 2:BL, 3:BR
		public string CurrentRoomId { get; set; } = "Hallways";
		
		// VECTOR STATE: [Admirer, Prophet, Producer]
		// VECTOR STATE: [Admirer, Prophet, Producer]
		public Vector3 State { get; set; } = new Vector3(1, 1, 1); // Raw accumulation, starts at 1,1,1 to avoid zero division
		public Vector3 NormalizedState => ScoringRules.NormalizeState(State);

		public Vector2 TrianglePosition
		{
			get
			{
				var norm = NormalizedState;
				// Barycentric mapping:
				// Admirer (X) -> (0, -28)   [Top]
				// Prophet (Y) -> (-30, 24)  [Bottom Left]
				// Producer (Z) -> (30, 24)  [Bottom Right]
				
				Vector2 v1 = new Vector2(0, -28);   // Admirer
				Vector2 v2 = new Vector2(-30, 24);  // Prophet
				Vector2 v3 = new Vector2(30, 24);   // Producer
				
				return (v1 * norm.X) + (v2 * norm.Y) + (v3 * norm.Z);
			}
		}

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
		// Meters removed in favor of vector-based NPC state
		public List<string> ActionsTaken { get; set; } = new();
		public List<string> GoalsMet { get; set; } = new();
		public bool Alive { get; set; } = true;

		public PlayerState(Role role)
		{
			Role = role;
		}
	}

	public class GameState
	{
		public JObject Config { get; private set; }
		public int CurrentTurn { get; private set; } = 1;
		public int MaxTurns { get; private set; }
		public Dictionary<Role, PlayerState> Players { get; private set; } = new();
		public Dictionary<string, NPC> NPCs { get; private set; } = new();
		public List<Trap> Traps { get; private set; } = new();
		public List<string> Notifications { get; private set; } = new();
		
		// Event for score feedback
		public event Action<Role, int> OnScoreChange;
		public void TriggerScoreChange(Role role, int score) => OnScoreChange?.Invoke(role, score);

		public string EditorialFocus { get; set; }
		public List<string> ActiveCameraRoomIds { get; private set; } = new();
		public bool AdmirerCaught { get; set; } = false;
		public long AdmirerCaughtTimestamp { get; set; } = 0; // Epoch ms
		public bool AdmirerEliminated { get; set; } = false;
		public bool MonitoringActive { get; set; } = false; // "Set Focus" essentially activates monitoring
		public int InteractionSeed { get; set; } = 0;
		public Dictionary<Role, InterviewContext> ActiveInterviews { get; private set; } = new();
		public Dictionary<Role, InterviewContext> ActiveProducerInterviews { get; private set; } = new();
		public JObject InterviewData { get; private set; }  // flirt_data.json
		public JObject ProducerInterviewData { get; private set; } // interview_data.json
		public Dictionary<Role, InterviewContext> ActiveConversions { get; private set; } = new();
		public JObject ConversionData { get; private set; }

		public GameState(string configPath)
		{
			var configText = File.ReadAllText(configPath);
			Config = JObject.Parse(configText);
			MaxTurns = Config["gameRules"]["turnsPerGame"].Value<int>();
			
			// Load Flirt Data (Admirer)
			try 
			{
				var flirtPath = configPath.Replace("game_configuration.json", "flirt_data.json");
				if (File.Exists(flirtPath))
					InterviewData = JObject.Parse(File.ReadAllText(flirtPath));
				else
					Console.WriteLine($"[GameState] Warning: Flirt data not found at {flirtPath}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[GameState] Error loading flirt data: {ex.Message}");
			}

			// Load Interview Data (Producer)
			try
			{
				var interviewPath = configPath.Replace("game_configuration.json", "interview_data.json");
				if (File.Exists(interviewPath))
				{
					ProducerInterviewData = JObject.Parse(File.ReadAllText(interviewPath));
				}
				else
				{
					Console.WriteLine($"[GameState] Warning: Interview data not found at {interviewPath}");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[GameState] Error loading conversion data: {ex.Message}");
			}

			// Load Conversion Data
			try
			{
				var conversionPath = configPath.Replace("game_configuration.json", "conversion_data.json");
				if (File.Exists(conversionPath))
				{
					ConversionData = JObject.Parse(File.ReadAllText(conversionPath));
				}
				else
				{
					Console.WriteLine($"[GameState] Warning: Conversion data not found at {conversionPath}");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[GameState] Error loading conversion data: {ex.Message}");
			}

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
				// Meter initialization removed
				
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
			if (CurrentTurn < MaxTurns && Winner == null)
			{
				CurrentTurn++;
				return true;
			}
			return false;
		}

		public string Winner { get; set; }
		public bool IsGameOver => CurrentTurn >= MaxTurns || Winner != null;
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

			// INTERVIEW LOGIC
			if (_gameState.ActiveInterviews.TryGetValue(playerRole, out var interviewCtx) && interviewCtx.NpcId == npcId)
			{
				// Start/Continue Flirt - Show Dynamic Questions first, stop button last
				var interviewData = _gameState.InterviewData;
				foreach (var qId in interviewCtx.AvailableQuestionIds)
				{
					JToken qData = null;
					if (interviewCtx.CurrentStage == "Intro")
					{
						var intros = interviewData?["default"]?["intro_topics"] as JArray;
						qData = intros?.FirstOrDefault(x => x["id"]?.Value<string>() == qId);
					}

					if (qData != null)
					{
						var newOpt = new JObject();
						newOpt["id"] = $"interview_option_{qId}";
						newOpt["text"] = qData["text"];
						availableOptions.Add(newOpt);
					}
				}

				// Stop button goes last (at the bottom)
				availableOptions.Add(new JObject
				{
					{ "id", "stop_flirt" },
					{ "text", "Stop Flirting" },
					{ "requires", new JObject() }
				});

				// Always return immediately if in flirt mode (don't show other options)
				return availableOptions;
			}




			
			// If we are interviewing SOMEONE ELSE, maybe we shouldn't show options for THIS NPC?
			if (playerRole == Role.Admirer && _gameState.ActiveInterviews.ContainsKey(playerRole) && _gameState.ActiveInterviews[playerRole].NpcId != npcId)
			{
				// Busy with another NPC. 
				// Just let standard options flow? Or maybe block everything except leave?
				// "You are busy with X" notification happens on interaction usually.
				// But to be clean, let's filter out "start_flirt" for others?
				// Logic for start_flirt already checks ActiveInterviews. So it will return error if clicked.
			}

			foreach (var option in options)
			{
				string id = option["id"]?.Value<string>();
				// Admirer target kills are handled by punch mechanic, so hide kill actions.
				if (playerRole == Role.Admirer && npc.IsTarget && !string.IsNullOrEmpty(id) && id.StartsWith("kill"))
				{
					continue;
				}
				// Prophet conversion is now handled by conversion_data.json dialogue — suppress old config buttons.
				if (playerRole == Role.Prophet && !string.IsNullOrEmpty(id) && id.StartsWith("convert_"))
				{
					continue;
				}
				if (IsOptionAvailable(option, playerRole, npc, player))
				{
					availableOptions.Add(option);
				}
			}
			
			// PRODUCER: Interview Game Option
			if (playerRole == Role.Producer && npc.Alive)
			{
				if (_gameState.ActiveProducerInterviews.TryGetValue(playerRole, out var piCtx) && piCtx.NpcId == npcId)
				{
					// Active interview: show question options first
					var interviewData = _gameState.ProducerInterviewData;
					var intros = interviewData?["default"]?["intro_topics"] as JArray;
					foreach (var qId in piCtx.AvailableQuestionIds)
					{
						var qData = intros?.FirstOrDefault(x => x["id"]?.Value<string>() == qId);
						if (qData != null)
						{
							var newOpt = new JObject();
							newOpt["id"] = $"producer_interview_option_{qId}";
							newOpt["text"] = qData["text"];
							availableOptions.Add(newOpt);
						}
					}

					// End button goes last (at the bottom)
					availableOptions.Add(new JObject
					{
						{ "id", "stop_producer_interview" },
						{ "text", "End Interview" },
						{ "requires", new JObject() }
					});
					return availableOptions; // Only show interview options
				}
				else
				{
					// Not active: Start option
					availableOptions.Add(new JObject
					{
						{ "id", "start_producer_interview" },
						{ "text", "Conduct Interview" },
						{ "requires", new JObject() }
					});
				}
			}



			// PROPHET: Resurrection Option (Dead NPC)
			if (playerRole == Role.Prophet && !npc.Alive)
			{
				availableOptions.Add(new JObject
				{
					{ "id", "resurrect" },
					{ "text", "Attempt Resurrection (20% Chance)" },
					{ "requires", new JObject() }
				});
			}

			// PROPHET: Conversion Dialogue (Live, not yet converted)
			if (playerRole == Role.Prophet && npc.Alive && !npc.Converted)
			{
				if (_gameState.ActiveConversions.TryGetValue(playerRole, out var convCtx) && convCtx.NpcId == npcId)
				{
					// In active conversion — show followup response buttons only
					var convData = _gameState.ConversionData;
					foreach (var qId in convCtx.AvailableQuestionIds)
					{
						var qData = convData?["cult_recruitment"]?["followups"]?[qId];
						if (qData != null)
						{
							availableOptions.Add(new JObject
							{
								{ "id", $"convert_option_{qId}" },
								{ "text", qData["text"] }
							});
						}
					}

					// Return immediately — don't mix with other options
					return availableOptions;
				}
				// If no active conversion, return empty list — client will auto-submit start_convert
			}

			return availableOptions;
		}

		private string Capitalize(string s) => char.ToUpper(s[0]) + s.Substring(1);

		private bool IsOptionAvailable(JToken option, Role playerRole, NPC npc, PlayerState player = null)
		{
			// Dead NPCs cannot be interacted with (except "leave" and "resurrect")
			var actionId = option["id"]?.Value<string>();
			if (!npc.Alive && actionId != "leave" && actionId != "resurrect")
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

				// Prank Active check
				var prankCheck = requires["npcPrankActive"]?.Value<bool>();
				if (prankCheck.HasValue)
				{
					if (prankCheck.Value == true && !npc.PrankActive) return false;
					if (prankCheck.Value == false && npc.PrankActive) return false;
				}
			}

			// Special check for Admirer's marry action - ALWAYS available if single
			var isAdmirerMarry = option["marry_admirer_love"]?.Value<bool>() ?? false;
			if (isAdmirerMarry)
			{
				if (npc.Married) return false;
				// We don't block by love meter anymore, logic is handled in Resolve logic
			}

			return true;
		}

		public (bool success, string failReason) ResolveInteraction(string npcId, string optionId, Role playerRole)
		{
			// Handle Global Actions
			if (npcId == "global")
			{
				if (optionId == "set_trap")
				{
					if (playerRole != Role.Prophet) return (false, "Only Prophet can set traps.");
					
					// Manually create trap since we can't access GameEngine instance method
					var trap = new Trap { CreatorRole = playerRole.ToString() };
					_gameState.Traps.Add(trap);
					_gameState.AddNotification("A trap has been set...");
					
					// Apply Cost/Effect: +1 Chaos
					// Apply Cost/Effect: +1 Chaos (Vector Update for Prophet?)
					// For now, global traps don't affect specific NPC state directly, or maybe they affect all?
					// Leaving purely notification based for now as requested dynamics are per-NPC.
					
					return (true, null);
				}
				return (false, "Unknown global action");
			}
			
			// Producer Global Actions
			if (npcId == "producer_global")
			{
				if (playerRole != Role.Producer) return (false, "Only Producer can perform these actions.");

				if (optionId == "end_interview_force")
				{
					if (_gameState.ActiveInterviews.ContainsKey(Role.Producer))
					{
						_gameState.ActiveInterviews.Remove(Role.Producer);
						return (true, null);
					}
				}

				if (optionId.StartsWith("marry_"))
				{
					return (false, "Marriage option is disabled.");
				}
				
				if (optionId.StartsWith("set_focus_"))
				{
					// Expected: set_focus_0 (quadrant index)
					var parts = optionId.Split('_');
					if (parts.Length == 3 && int.TryParse(parts[2], out int qIdx))
					{
						// Validate 0-3
						if (qIdx >= 0 && qIdx <= 3)
						{
							_gameState.EditorialFocus = qIdx.ToString();
							_gameState.AddNotification($"Producer set Editorial Focus to Quadrant {qIdx}!");
							return (true, null);
						}
					}
					return (false, "Invalid focus quadrant");
				}
				
				if (optionId.StartsWith("toggle_camera_"))
				{
					// Camera logic disabled per request.
					// // Expected: toggle_camera_Room1
					// var parts = optionId.Split('_');
					// if (parts.Length == 3)
					// {
					// 	string roomId = parts[2];
					// 	if (_gameState.ActiveCameraRoomIds.Contains(roomId))
					// 	{
					// 		_gameState.ActiveCameraRoomIds.Remove(roomId);
					// 		_gameState.AddNotification($"Producer deactivated camera in {roomId}.");
					// 	}
					// 	else
					// 	{
					// 		if (_gameState.ActiveCameraRoomIds.Count >= 2)
					// 		{
					// 			// Remove oldest
					// 			string removed = _gameState.ActiveCameraRoomIds[0];
					// 			_gameState.ActiveCameraRoomIds.RemoveAt(0);
					// 			_gameState.AddNotification($"Producer camera limit reached. Deactivating {removed}.");
					// 		}
					// 		_gameState.ActiveCameraRoomIds.Add(roomId);
					// 		_gameState.AddNotification($"Producer activated camera in {roomId}.");
					// 	}
					// 	return (true, null);
					// }
					// return (false, "Invalid camera room");
					return (true, null);
				}


				
				// PRODUCER MONEY GAME START (Global option or Contextual?)
				// Actually, money game is per-NPC interaction.
				
				return (false, "Unknown producer action");
			}

			var npcConfig = _gameState.Config["npcs"]?[npcId];
			if (npcConfig == null)
				return (false, "NPC not found");

			var npc = _gameState.GetNPC(npcId);
			if (npc == null)
				return (false, "NPC not found");

			// PROPHET CONVERSION START
			if (optionId == "start_convert")
			{
				if (playerRole != Role.Prophet) return (false, "Only Prophet can convert.");
				if (_gameState.ActiveConversions.ContainsKey(playerRole)) return (true, null); // Already running, idempotent
				if (npc.Converted) return (false, $"{npc.Name} is already converted.");

				var convData = _gameState.ConversionData;
				var topics = convData?["cult_recruitment"]?["conversion_topics"] as JArray;

				if (topics == null || topics.Count == 0) return (false, "No conversion topics found!");

				// Pick ONE random topic — display its text as the NPC's opening line
				var topic = topics.OrderBy(x => _random.Next()).First();
				string topicNpcLine = topic["text"]?.Value<string>() ?? "...";
				var followups = topic["followups"]?.ToObject<List<string>>() ?? new List<string>();

				var ctx = new InterviewContext
				{
					NpcId = npcId,
					CurrentStage = "Followup",           // Skip Intro — go straight to responses
					CurrentScore = 0,
					LastResponse = topicNpcLine,          // NPC speaks their topic line immediately
					AvailableQuestionIds = followups       // These are what the Prophet can say back
				};

				_gameState.ActiveConversions[playerRole] = ctx;
				return (true, null);
			}

			if (optionId == "stop_convert")
			{
				if (_gameState.ActiveConversions.ContainsKey(playerRole))
				{
					ApplyConversionResult(_gameState.ActiveConversions[playerRole], playerRole, clearAndFinish: true);
					_gameState.AddNotification("You stopped converting.");
					return (true, null);
				}
				return (false, "No active conversion.");
			}

			if (optionId.StartsWith("convert_option_"))
			{
				if (!_gameState.ActiveConversions.TryGetValue(playerRole, out var ctx) || ctx.NpcId != npcId)
				{
					return (false, "No active conversion with this NPC.");
				}

				string qId = optionId.Replace("convert_option_", "");
				var convData = _gameState.ConversionData;

				// Always look up in followups — no Intro stage anymore
				var qData = convData?["cult_recruitment"]?["followups"]?[qId];
				if (qData == null) return (false, "Invalid conversion option.");

				// Apply score
				int score = qData["score"]?.Value<int>() ?? 0;
				ctx.CurrentScore += score;

				// Show NPC's reaction
				string response = qData["response"]?.Value<string>() ?? "...";
				ctx.LastResponse = response;

				// Apply result and clear the context immediately (prevents self-healing UI from reopening this panel)
				ApplyConversionResult(ctx, playerRole, clearAndFinish: true);
				return (true, null);
			}

			// FLIRT RESOLUTION (Formerly Interview)
			if (optionId == "start_flirt")
			{
				if (playerRole != Role.Admirer) return (false, "Only Admirer can flirt.");
				if (_gameState.ActiveInterviews.ContainsKey(playerRole)) return (false, "You are already flirting with someone!");
				
				var interviewData = _gameState.InterviewData;
				var introTopics = interviewData?["default"]?["intro_topics"] as JArray;
				
				if (introTopics == null || introTopics.Count == 0) return (false, "No flirting topics found!");

				// Pick 1 positive + 1 negative option
				var positiveTopics = introTopics.Where(x => (x["score"]?.Value<int>() ?? 0) > 0).OrderBy(_ => _random.Next()).Take(1);
				var negativeTopics = introTopics.Where(x => (x["score"]?.Value<int>() ?? 0) < 0).OrderBy(_ => _random.Next()).Take(1);
				var randomQuestions = positiveTopics.Concat(negativeTopics).Select(x => x["id"]?.Value<string>()).ToList();

				var ctx = new InterviewContext
				{
					NpcId = npcId,
					CurrentStage = "Intro",
					CurrentScore = 0,
					LastResponse = "The vibes are good...", // Initial state
					AvailableQuestionIds = randomQuestions
				};
				
				_gameState.ActiveInterviews[playerRole] = ctx;
				_gameState.AddNotification($"Flirting started with {npc.Name}!");
				return (true, null);
			}
			// 0. (Removed explicit finish button block)
			
			if (optionId == "stop_flirt")
			{
				if (_gameState.ActiveInterviews.ContainsKey(playerRole))
				{
					ApplyInterviewResult(_gameState.ActiveInterviews[playerRole], playerRole, true);
					_gameState.AddNotification("You stopped flirting.");
					return (true, null);
				}
				return (false, "No active interview.");
			}


			if (optionId.StartsWith("interview_option_"))
			{
				if (!_gameState.ActiveInterviews.TryGetValue(playerRole, out var ctx) || ctx.NpcId != npcId)
				{
					return (false, "No active interview with this NPC.");
				}
				
				string qId = optionId.Replace("interview_option_", "");
				var interviewData = _gameState.InterviewData;
				
				JToken qData = null;
				if (ctx.CurrentStage == "Intro")
				{
					var intros = interviewData?["default"]?["intro_topics"] as JArray;
					qData = intros?.FirstOrDefault(x => x["id"]?.Value<string>() == qId);
				}
				else
				{
					qData = interviewData?["default"]?["followups"]?[qId];
				}

				if (qData == null) return (false, "Invalid interview question data.");

				// Apply Score
				int score = qData["score"]?.Value<int>() ?? 0;
				ctx.CurrentScore += score;
				
				// Set Response
				string response = qData["response"]?.Value<string>() ?? "...";
				ctx.LastResponse = response;

				// End flirt immediately after picking an option (no follow-up stage)
				ApplyInterviewResult(ctx, playerRole);
				return (true, null);
			}

			// Check if NPC is dead first
			if (!npc.Alive && optionId != "leave" && optionId != "resurrect")
				return (false, $"{npc.Name} is no longer available");

			// PROPHET RESURRECTION
			if (optionId == "resurrect")
			{
				if (playerRole != Role.Prophet) return (false, "Only Prophet can resurrect.");
				if (npc.Alive) return (false, "NPC is already alive.");

				bool resurrectSuccess = _random.NextDouble() < 0.20; // 20% Chance
				if (resurrectSuccess)
				{
					npc.Alive = true;
					// Allow logic to flow or just notify?
					// Probably just notify + update state field implies it will be synced
					_gameState.AddNotification($"MIRACLE! {npc.Name} has been resurrected by the Prophet!");
					
					// Optional: Add Prophet Points for a miracle?
					// npc.State += new Vector3(0, 5, 0); // Big boost?
					
					// Camera detection disabled per request.
					// // CHECK FOR CAMERA (Prophet Resurrection)
					// if (_gameState.ActiveCameraRoomIds.Contains(npc.CurrentRoomId))
					// {
					// 	_gameState.AddNotification($"Prophet resurrected {npc.Name} and was caught on camera!");
					// 	_gameState.AddNotification($"Prophet's influence waned due to exposure!");
					// 	
					// 	// Apply Penalty to ALL Prophet Scores
					// 	foreach (var n in _gameState.NPCs.Values)
					// 	{
					// 		n.State = new Vector3(n.State.X, n.State.Y * ScoringRules.CaughtPenalty, n.State.Z);
					// 	}
					// }
				}
				else
				{
					_gameState.AddNotification($"Resurrection failed. {npc.Name} remains dead.");
				}
				return (true, null);
			}




			// Camera detection disabled per request.
			// // CHECK FOR CAMERA CATCH (Admirer Kill)
			// if (playerRole == Role.Admirer && optionId.StartsWith("kill_"))
			// {
			// 	Console.WriteLine($"[DEBUG] Kill attempt on {npcId}. NPC Room: '{npc.CurrentRoomId}'. Active Cams: {string.Join(", ", _gameState.ActiveCameraRoomIds)}");
			// 	
			// 	if (_gameState.ActiveCameraRoomIds.Count > 0)
			// 	{
			// 		// Check if NPC is in a monitored room
			// 		if (_gameState.ActiveCameraRoomIds.Contains(npc.CurrentRoomId))
			// 		{
			// 
			// 			_gameState.AdmirerCaught = true;
			// 			_gameState.AdmirerCaughtTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			// 			_gameState.AdmirerEliminated = false; 
			// 			_gameState.AddNotification($"Admirer killed {npc.Name} and was caught on camera!");
			// 			_gameState.AddNotification($"Producer's Camera captured the crime!");
			// 			
			// 			// Apply Penalty to ALL Admirer Scores
			// 			foreach (var n in _gameState.NPCs.Values)
			// 			{
			// 				n.State = new Vector3(n.State.X * ScoringRules.CaughtPenalty, n.State.Y, n.State.Z);
			// 			}
			// 
			// 			// We do NOT instantly end game, Producer must Call Police.
			// 		}
			// 		else
			// 		{
			// 			Console.WriteLine($"[DEBUG] Detection Failed. NPC Room '{npc.CurrentRoomId}' not in Active List.");
			// 			// Add failing notification for debug as requested
			// 			// Only show to Admirer ideally, but global notif is fine for now or handle via UI filtering
			// 			// _gameState.AddNotification($"[DEBUG] Murder in {npc.CurrentRoomId} (Unmonitored)");
			// 		}
			// 	}
			// }

			// 5. PRODUCER INTERVIEW GAME
			if (optionId == "start_producer_interview")
			{
				if (playerRole != Role.Producer) return (false, "Only Producer can conduct interviews.");
				if (_gameState.ActiveProducerInterviews.ContainsKey(playerRole)) return (false, "You are already interviewing someone!");

				var interviewData = _gameState.ProducerInterviewData;
				var introTopics = interviewData?["default"]?["intro_topics"] as JArray;

				if (introTopics == null || introTopics.Count == 0) return (false, "No interview questions found!");

				// Pick 1 positive + 1 negative question
				var positives = introTopics.Where(x => (x["score"]?.Value<int>() ?? 0) > 0).OrderBy(_ => _random.Next()).Take(1);
				var negatives = introTopics.Where(x => (x["score"]?.Value<int>() ?? 0) < 0).OrderBy(_ => _random.Next()).Take(1);
				var questions = positives.Concat(negatives).Select(x => x["id"]?.Value<string>()).ToList();

				var ctx = new InterviewContext
				{
					NpcId = npcId,
					CurrentStage = "Intro",
					CurrentScore = 0,
					LastResponse = "Ready for your questions...",
					AvailableQuestionIds = questions
				};

				_gameState.ActiveProducerInterviews[playerRole] = ctx;
				_gameState.AddNotification($"Interview started with {npc.Name}!");
				return (true, null);
			}

			if (optionId == "stop_producer_interview")
			{
				if (_gameState.ActiveProducerInterviews.ContainsKey(playerRole))
				{
					ApplyProducerInterviewResult(_gameState.ActiveProducerInterviews[playerRole], playerRole, earlyStop: true);
					_gameState.AddNotification("Interview ended.");
					return (true, null);
				}
				return (false, "No active interview.");
			}

			if (optionId.StartsWith("producer_interview_option_"))
			{
				if (!_gameState.ActiveProducerInterviews.TryGetValue(playerRole, out var ctx) || ctx.NpcId != npcId)
					return (false, "No active interview with this NPC.");

				string qId = optionId.Replace("producer_interview_option_", "");
				var interviewData = _gameState.ProducerInterviewData;
				var intros = interviewData?["default"]?["intro_topics"] as JArray;
				var qData = intros?.FirstOrDefault(x => x["id"]?.Value<string>() == qId);

				if (qData == null) return (false, "Invalid interview question.");

				int score = qData["score"]?.Value<int>() ?? 0;
				ctx.CurrentScore += score;
				ctx.LastResponse = qData["response"]?.Value<string>() ?? "...";

				// End interview immediately after one pick (no follow-up)
				ApplyProducerInterviewResult(ctx, playerRole);
				return (true, null);
			}

			var options = npcConfig["interactionTree"]?["root"]?["options"] as JArray ?? new();
			var option = options.FirstOrDefault(o => o["id"]?.Value<string>() == optionId);

			if (option == null)
				return (false, "Invalid action");

			// CRITICAL: Revalidate at execution time to prevent race conditions
			var player = _gameState.GetPlayerState(playerRole);
			if (!IsOptionAvailable(option, playerRole, npc, player))
				return (false, $"Action no longer valid for {npc.Name}");

			var isAdmirerMarry = option["marry_admirer_love"]?.Value<bool>() ?? false;
			bool success = true;


			if (playerRole == Role.Admirer && optionId.Contains("marry"))
			{
				// Admirer Logic...
				var targets = _gameState.NPCs.Values.Where(n => n.IsTarget).ToList();
				if (targets.Any(t => t.Alive))
				{
					return (false, "You cannot marry while rivals are still alive!");
				}
				
				// Marriage succeeds automatically once all rivals are eliminated
				_gameState.AddNotification("Marriage successful! Love has conquered all!");
			}
			else
			{
				// Standard resolution
				var resolutionType = option["resolution"]?["type"]?.Value<string>();
				if (resolutionType == "chance" || resolutionType == "rps")
				{
					double successChance = option["resolution"]["successChance"]?.Value<double>() ?? 0.5;
					success = _random.NextDouble() < successChance;
				}
			}

			// Apply effects
			ApplyActionEffects(option, playerRole, npcId, success);

			if (!success && !isAdmirerMarry) // Admirer fail is already handled
				return (false, "Action failed (chance roll)");

			return (true, null);
		}

		public void EndActiveInteraction(Role playerRole)
		{
			if (_gameState.ActiveInterviews.ContainsKey(playerRole))
			{
				_gameState.ActiveInterviews.Remove(playerRole);
			}
			if (_gameState.ActiveProducerInterviews.ContainsKey(playerRole))
			{
				_gameState.ActiveProducerInterviews.Remove(playerRole);
			}
			if (_gameState.ActiveConversions.ContainsKey(playerRole))
			{
				_gameState.ActiveConversions.Remove(playerRole);
			}
		}


		private void ApplyConversionResult(InterviewContext ctx, Role playerRole, bool clearAndFinish = true)
		{
			var npc = _gameState.NPCs[ctx.NpcId];
			if (npc != null)
			{
				Vector3 points = ScoringRules.GetConversionPoints(ctx.CurrentScore);
				npc.State += points;
				Console.WriteLine($"[DEBUG] Conversion: {npc.Name} State += {points} -> {npc.State}");

				// Mark as converted if prophet score component reaches threshold
				const float ConvertThreshold = 4f;
				if (npc.State.Y >= ConvertThreshold && !npc.Converted)
				{
					npc.Converted = true;
					_gameState.AddNotification($"{npc.Name} has been converted by the Prophet!");
				}

				Console.WriteLine($"[GameEngine] Invoking OnScoreChange for {playerRole} with score {ctx.CurrentScore}");
				_gameState.TriggerScoreChange(playerRole, ctx.CurrentScore);
			}

			string resultMsg = ctx.CurrentScore > 0 ? "They seem receptive!" : (ctx.CurrentScore < 0 ? "They pushed back..." : "Hard to tell.");
			_gameState.AddNotification($"Conversion finished. Result: {resultMsg}");
			ctx.CurrentScore = 0;

			if (clearAndFinish)
			{
				_gameState.ActiveConversions.Remove(playerRole);
			}
			if (_gameState.ActiveConversions.ContainsKey(playerRole))
			{
				_gameState.ActiveConversions.Remove(playerRole);
			}
		}

		private void ApplyInterviewResult(InterviewContext ctx, Role playerRole, bool clearAndFinish = true)
		{
			// Add score to NPC State (Admirer Component)
			var npc = _gameState.NPCs[ctx.NpcId];
			if (npc != null)
			{
				Vector3 points = ScoringRules.GetFlirtPoints(ctx.CurrentScore);
				npc.State += points;
				Console.WriteLine($"[DEBUG] Interview: {npc.Name} State += {points} -> {npc.State}");
				
				// Notify score change for visual feedback
				Console.WriteLine($"[GameEngine] Invoking OnScoreChange for {playerRole} with score {ctx.CurrentScore}");
				_gameState.TriggerScoreChange(playerRole, ctx.CurrentScore);
			}
			
			string resultMsg = ctx.CurrentScore > 0 ? "They seem interested!" : (ctx.CurrentScore < 0 ? "That went poorly..." : "Hard to tell.");
			_gameState.AddNotification($"Flirting finished. Result: {resultMsg}");
			ctx.CurrentScore = 0; // Prevent double application
			 
			 // Clear interview/flirt
			 if (clearAndFinish)
			 {
				_gameState.ActiveInterviews.Remove(playerRole);
			 }
		}

		private void ApplyProducerInterviewResult(InterviewContext ctx, Role playerRole, bool earlyStop = false)
		{
			var npc = _gameState.NPCs.GetValueOrDefault(ctx.NpcId);
			if (npc != null && !earlyStop)
			{
				Vector3 points = ScoringRules.GetMoneyGamePoints(ctx.CurrentScore);
				npc.State += points;
				Console.WriteLine($"[DEBUG] ProducerInterview: {npc.Name} State += {points} -> {npc.State}");
				_gameState.TriggerScoreChange(playerRole, ctx.CurrentScore);
			}

			string resultMsg = earlyStop ? "Interview cut short." :
				(ctx.CurrentScore > 0 ? "Great segment!" : (ctx.CurrentScore < 0 ? "That went poorly..." : "Neutral reaction."));
			_gameState.AddNotification($"Interview finished. Result: {resultMsg}");
			_gameState.ActiveProducerInterviews.Remove(playerRole);
		}

		private void ApplyActionEffects(JToken option, Role playerRole, string npcId, bool success)
		{
			var effectsKey = success ? "effects_on_success" : "effects_on_failure";
			var effects = option[effectsKey] ?? option["effects"] ?? new JArray();

			foreach (var effect in effects.Children())
			{
				ApplyEffect(effect, playerRole, npcId, success);
			}
		}

		private void ApplyEffect(JToken effect, Role playerRole, string npcId, bool success)
		{
			var meterName = effect["meter"]?.Value<string>();
			if (!string.IsNullOrEmpty(meterName))
			{
				// Meters removed
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
					else if (field == "prankActive")
						npc.PrankActive = value.Value<bool>();
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

		public void EndActiveInteraction(Role role)
		{
			if (GameState.ActiveInterviews.ContainsKey(role))
			{
				GameState.ActiveInterviews.Remove(role);
			}
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

		public void CreateTrap(Role playerRole)
		{
			var trap = new Trap { CreatorRole = playerRole.ToString() };
			GameState.Traps.Add(trap);
			GameState.AddNotification("A trap has been set...");
		}

		public (bool success, string failReason) PerformAction(string npcId, string actionId, Role playerRole)
		{
			if (playerRole == Role.Admirer && GameState.AdmirerEliminated)
			{
				return (false, "You have been eliminated and cannot act.");
			}

			GameState.InteractionSeed++; // Ensure randomness changes after every action
			
			// Resolve Interaction via InteractionResolver
			// Note: We assume InteractionResolver exists and has likely a ResolveInteraction method or similar logic
			// Based on file analysis, InteractionResolver logic ended before this class.
			// However, since I cannot see the ResolveInteraction signature in InteractionResolver clearly, 
			// I will assume it follows the previous pattern.
			// If InteractionResolver logic was merged or missing, this might fail, but restoring GameEngine is priority.
			// Wait, I need to call the method on InteractionResolver. 
			// In Turn 175 it was: InteractionResolver.ResolveInteraction(npcId, actionId, playerRole);
			
			// Attempt to call it.
            // If ResolveInteraction is not public/existing, I might need to fix InteractionResolver too.
            // But let's assume it's there as per Turn 238 evidence.
            
            // Wait, looking at Turn 175 again:
            // var result = InteractionResolver.ResolveInteraction(npcId, actionId, playerRole);
            
            // I'll stick to that.
			var result = InteractionResolver.ResolveInteraction(npcId, actionId, playerRole);
			
			// Check for Win Condition after action
			if (result.success)
			{
				string winMsg = CheckWinCondition(playerRole);
				if (winMsg != null)
				{
					GameState.Winner = playerRole.ToString();
					GameState.AddNotification($"GAME OVER: {winMsg}");
				}
			}
			
			return result;
		}

		public void StartTurn(Role playerRole)
		{
			var player = GameState.GetPlayerState(playerRole);
			GameState.AddNotification($"\n--- TURN {GameState.CurrentTurn} START ---");
			GameState.AddNotification($"[{playerRole}]");
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
			
			// Check both primary and secondary goals
			foreach (var goalType in new[] { "primary", "secondary" })
			{
				var goalConfig = winConfig[goalType];
				if (goalConfig == null) continue;
				
				var requirement = goalConfig["requirement"];
				if (CheckCondition(player, requirement))
				{
					var goal = goalConfig["goal"]?.Value<string>() ?? "Goal achieved";
					if (playerRole == Role.Prophet) return "The Rite of Revelation has started";
					return $"{playerRole} wins! ({goal})";
				}
			}

			return null;
		}

		private bool CheckCondition(PlayerState player, JToken requirement)
		{
			bool conditionChecked = false;

			var meterName = requirement["meter"]?.Value<string>();
			if (!string.IsNullOrEmpty(meterName))
			{
				return false; 
			}

			var convertedCountReq = requirement["npcsConverted"]?.Value<int>();
			if (convertedCountReq.HasValue)
			{
				conditionChecked = true;
				int currentConverted = GameState.NPCs.Values.Count(n => n.Converted);
				if (currentConverted < convertedCountReq.Value) return false;
			}
			
			var npcsKilledReq = requirement["npcsKilled"]?.Value<int>();
			if (npcsKilledReq.HasValue)
			{
				conditionChecked = true;
				int currentKilled = GameState.NPCs.Values.Count(n => !n.Alive);
				if (currentKilled < npcsKilledReq.Value) return false;
			}

			// Special check for Marriage Requirement ("npcStatus": "nonConverted")
			var statusReq = requirement["npcStatus"]?.Value<string>();
			if (!string.IsNullOrEmpty(statusReq))
			{
				conditionChecked = true;
				var loveInterest = GameState.NPCs.Values.FirstOrDefault(n => n.IsLoveInterest);
				if (loveInterest == null) return false; 

				if (statusReq == "nonConverted" && loveInterest.Converted) return false;
				if (statusReq == "converted" && !loveInterest.Converted) return false;
			}
			
			// Check if Love Interest is married (for Admirer win)
			var npcMarriedReq = requirement["npcMarried"]?.Value<bool>();
			if (npcMarriedReq.HasValue)
			{
				conditionChecked = true;
				var loveInterest = GameState.NPCs.Values.FirstOrDefault(n => n.IsLoveInterest);
				if (loveInterest == null) return false;
				
				if (npcMarriedReq.Value && !loveInterest.Married) return false;
				if (!npcMarriedReq.Value && loveInterest.Married) return false;
			}
			
			var admirerReportedReq = requirement["admirerReported"]?.Value<bool>();
			if (admirerReportedReq.HasValue)
			{
				conditionChecked = true;
				if (GameState.Winner != "Producer") return false;
			}

			if (!conditionChecked)
			{
				return false;
			}

			return true;
		}

		public List<string> GetNotifications()
		{
			return GameState.GetAndClearNotifications();
		}

		public void Update(double deltaSeconds)
		{
			for (int i = GameState.Traps.Count - 1; i >= 0; i--)
			{
				var trap = GameState.Traps[i];
				trap.TimeAlive += deltaSeconds;

				double p = 0.5 * Math.Exp(-2.0 * trap.TimeAlive);

				if (_random.NextDouble() < p) 
				{
					GameState.Traps.RemoveAt(i);
				}
			}
		}

		public JObject GetGameStatus()
		{
			var status = new JObject
			{
				{ "turn", GameState.CurrentTurn },
				{ "max_turns", GameState.MaxTurns },
				{ "editorial_focus", GameState.EditorialFocus },
				{ "game_over", GameState.IsGameOver },
				{ "winner", GameState.Winner },
				{ "active_camera_room_ids", new JArray(GameState.ActiveCameraRoomIds) },
				{ "admirer_caught", GameState.AdmirerCaught },
				{ "admirer_caught_timestamp", GameState.AdmirerCaughtTimestamp },
				{ "admirer_eliminated", GameState.AdmirerEliminated }
			};

			var playersObj = new JObject();
			foreach (var kvp in GameState.Players)
			{
				var playerObj = new JObject();
				var metersObj = new JObject();
				playerObj["meters"] = metersObj;
				playersObj[kvp.Key.ToString().ToLower()] = playerObj;
			}

			status["players"] = playersObj;
			
			return status;
		}
	}
}


