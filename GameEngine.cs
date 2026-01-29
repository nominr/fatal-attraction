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

	public class Trap
	{
		public string Id { get; set; } = Guid.NewGuid().ToString();
		public double TimeAlive { get; set; } = 0;
		public string CreatorRole { get; set; }
	}

	public class ConversionContext
	{
		public string NpcId { get; set; }
		public string WinningMove { get; set; } // "rock", "paper", "scissors"
		public string BaseActionId { get; set; } // e.g. "convert_katy"
		public List<string> VisibleOptions { get; set; } = new();
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
		public string TemporaryDialogOverride { get; set; }

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
		public List<Trap> Traps { get; private set; } = new();
		public List<string> Notifications { get; private set; } = new();

		public string EditorialFocus { get; set; }
		public List<string> ActiveCameraRoomIds { get; private set; } = new();
		public bool AdmirerCaught { get; set; } = false;
		public bool AdmirerEliminated { get; set; } = false;
		public bool MonitoringActive { get; set; } = false; // "Set Focus" essentially activates monitoring
		public int InteractionSeed { get; set; } = 0;
		public Dictionary<Role, ConversionContext> ActiveConversions { get; private set; } = new();
		public Dictionary<Role, InterviewContext> ActiveInterviews { get; private set; } = new();
		public JObject InterviewData { get; private set; }

		public GameState(string configPath)
		{
			var configText = File.ReadAllText(configPath);
			Config = JObject.Parse(configText);
			MaxTurns = Config["gameRules"]["turnsPerGame"].Value<int>();
			
			// Load Interview Data
			try 
			{
				var interviewPath = configPath.Replace("game_configuration.json", "interview_data.json");
				if (File.Exists(interviewPath))
				{
					InterviewData = JObject.Parse(File.ReadAllText(interviewPath));
				}
				else
				{
					Console.WriteLine($"[GameState] Warning: Interview data not found at {interviewPath}");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[GameState] Error loading interview data: {ex.Message}");
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
				// Start/Continue Interview - Show Dynamic Questions
				var interviewData = _gameState.InterviewData;
				foreach (var qId in interviewCtx.AvailableQuestionIds)
				{
					JToken qData = null;
					if (interviewCtx.CurrentStage == "Intro")
					{
						var intros = interviewData?["default"]?["intro_topics"] as JArray;
						qData = intros?.FirstOrDefault(x => x["id"]?.Value<string>() == qId);
					}
					else // Followup
					{
						qData = interviewData?["default"]?["followups"]?[qId];
					}

					if (qData != null)
					{
						var newOpt = new JObject();
						newOpt["id"] = $"interview_option_{qId}"; // Prefix to identify it in Resolve
						newOpt["text"] = qData["text"];
						availableOptions.Add(newOpt);
					}
				}
				
				// Always return immediately if in interview mode (don't show other options)
				return availableOptions;
			}

			foreach (var option in options)
			{
				// Check for Prophet "convert_npc" power/action
				// The JSON config has "convert_<npcname>" or generic "convert" IDs.
				// We need to detect if this is a "RPS" resolution type or specific ID logic.
				// User wants: Hard (0-3 chaos) -> 3 options. Normal (3-7) -> 2 options. Easy (7-10) -> 1 option.
				
				// Identify convert action by ID or properties
				string id = option["id"]?.Value<string>();
				bool isConvert = id != null && (id.StartsWith("convert") || id.Contains("convert_"));
				
				if (isConvert && playerRole == Role.Prophet)
				{
					if (!IsOptionAvailable(option, playerRole, npc, player)) continue;

					// Check active conversion
					if (_gameState.ActiveConversions.TryGetValue(playerRole, out var ctx) && ctx.NpcId == npcId)
					{
						// STEP 2: Show RPS Options (User has already started conversion)
						
						// Determine "Level"
						var chaosMeter = player.GetMeter("chaos");
						double chaos = chaosMeter?.Value ?? 0;
						string difficulty = "hard";
						if (chaos >= 7) difficulty = "easy";
						else if (chaos >= 3) difficulty = "normal";

						var rpsOptions = new List<JToken>();
						string[] moves = { "rock", "paper", "scissors" };
						string winningMove = ctx.WinningMove;
						var rng = Random.Shared;

						if (difficulty == "hard") // All 3 (1/3 chance)
						{
							rpsOptions.Add(CreateRPSOption(id, "Rock", "rock"));
							rpsOptions.Add(CreateRPSOption(id, "Paper", "paper"));
							rpsOptions.Add(CreateRPSOption(id, "Scissors", "scissors"));
						}
						else if (difficulty == "normal") // 2 Options (1 Winner, 1 Loser -> 1/2 chance)
						{
							// Show winner
							rpsOptions.Add(CreateRPSOption(id, $"Use {Capitalize(winningMove)}", winningMove));
							
							// Show 1 random loser
							string loser = moves.Where(m => m != winningMove).OrderBy(_ => rng.Next()).First();
							rpsOptions.Add(CreateRPSOption(id, $"Use {Capitalize(loser)}", loser));
						}
						else // Easy: 1 Option (Winner -> 1/1 chance)
						{
							rpsOptions.Add(CreateRPSOption(id, $"Use {Capitalize(winningMove)}", winningMove));
						}
						
						availableOptions.AddRange(rpsOptions);
					}
					else
					{
						// STEP 1: Show "Start Conversion" button
						var startOption = new JObject
						{
							{ "id", $"start_convert_{npcId}" },
							{ "text", "Start Conversion Ritual" },
							{ "requires", new JObject() }
						};
						availableOptions.Add(startOption);
					}
				}
				else
				{
					if (IsOptionAvailable(option, playerRole, npc, player))
					{
						availableOptions.Add(option);
					}
				}
			}

			return availableOptions;
		}

		private string Capitalize(string s) => char.ToUpper(s[0]) + s.Substring(1);

		private JObject CreateRPSOption(string baseId, string label, string moveSuffix)
		{
			return new JObject
			{
				{ "id", $"{baseId}_{moveSuffix}" }, // e.g. convert_katy_rock
				{ "text", label },
				{ "requires", new JObject() } // Already validated
			};
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
					var playerS = _gameState.GetPlayerState(playerRole);
					var chaos = playerS.GetMeter("chaos");
					if (chaos != null)
					{
						_gameState.AddNotification($"Prophet gained Chaos! ({playerS.GetMeter("chaos").Value}/{playerS.GetMeter("chaos").MaxValue})");
					}
					
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
					// Expected format: marry_npc1_npc2
					var parts = optionId.Split('_');
					if (parts.Length == 3)
					{
						string n1 = parts[1];
						string n2 = parts[2];
						var npc1 = _gameState.GetNPC(n1);
						var npc2 = _gameState.GetNPC(n2);
						
						if (npc1 != null && npc2 != null)
						{
							// Validation
							if (npc1.Married || npc2.Married) return (false, "One or both are already married");
							if (npc1 == npc2) return (false, "Cannot marry self");
							if (npc1.IsLoveInterest || npc2.IsLoveInterest) return (false, "Cannot marry the Admirer's Love Interest!");

							npc1.Married = true;
							npc2.Married = true;
							
							// Ratings +1
							var pState = _gameState.GetPlayerState(Role.Producer);
							pState.GetMeter("ratings")?.Add(1);
							
							_gameState.AddNotification($"Producer married {Capitalize(n1)} and {Capitalize(n2)}! (+Rating)");
							return (true, null);
						}
					}
					return (false, "Invalid marriage target(s)");
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
					// Expected: toggle_camera_Room1
					var parts = optionId.Split('_');
					if (parts.Length == 3)
					{
						string roomId = parts[2];
						if (_gameState.ActiveCameraRoomIds.Contains(roomId))
						{
							_gameState.ActiveCameraRoomIds.Remove(roomId);
							_gameState.AddNotification($"Producer deactivated camera in {roomId}.");
						}
						else
						{
							if (_gameState.ActiveCameraRoomIds.Count >= 2)
							{
								// Remove oldest
								string removed = _gameState.ActiveCameraRoomIds[0];
								_gameState.ActiveCameraRoomIds.RemoveAt(0);
								_gameState.AddNotification($"Producer camera limit reached. Deactivating {removed}.");
							}
							_gameState.ActiveCameraRoomIds.Add(roomId);
							_gameState.AddNotification($"Producer activated camera in {roomId}.");
						}
						return (true, null);
					}
					return (false, "Invalid camera room");
				}

				if (optionId == "call_police")
				{
					if (_gameState.AdmirerCaught)
					{
				if (optionId == "call_police")
				{
					if (_gameState.AdmirerCaught)
					{
						// _gameState.Winner = Role.Producer.ToString(); // OLD: Ended game
						_gameState.AdmirerEliminated = true; // NEW: Just eliminate admirer
						
						_gameState.AddNotification("POLICE CALLED! The Admirer has been arrested based on video evidence!");
						// _gameState.AddNotification("The Producer has saved the show! ADMIRER ELIMINATED.");
						// Don't clutter notification log too much, UI will handle specific messages
						return (true, null);
					}
					return (false, "You have no evidence to call the police!");
				}
					}
					return (false, "You have no evidence to call the police!");
				}
				
				return (false, "Unknown producer action");
			}

			var npcConfig = _gameState.Config["npcs"]?[npcId];
			if (npcConfig == null)
				return (false, "NPC not found");

			var npc = _gameState.GetNPC(npcId);
			if (npc == null)
				return (false, "NPC not found");

			// INTERVIEW RESOLUTION
			if (optionId == "start_interview")
			{
				if (playerRole != Role.Producer) return (false, "Only Producer can interview.");
				if (_gameState.ActiveInterviews.ContainsKey(playerRole)) return (false, "You are already interviewing someone!");
				
				var interviewData = _gameState.InterviewData;
				var introTopics = interviewData?["default"]?["intro_topics"] as JArray;
				
				if (introTopics == null || introTopics.Count == 0) return (false, "No interview topics found!");

				// Pick 3 random intro questions
				var randomQuestions = introTopics.OrderBy(x => _random.Next()).Take(3)
					.Select(x => x["id"]?.Value<string>()).ToList();

				var ctx = new InterviewContext
				{
					NpcId = npcId,
					CurrentStage = "Intro",
					CurrentScore = 0,
					LastResponse = "The camera is rolling...", // Initial state
					AvailableQuestionIds = randomQuestions
				};
				
				_gameState.ActiveInterviews[playerRole] = ctx;
				_gameState.AddNotification($"Interview started with {npc.Name}!");
				return (true, null);
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

				// Flow Logic
				if (ctx.CurrentStage == "Intro")
				{
					// Move to Followup
					var followups = qData["followups"]?.ToObject<List<string>>() ?? new List<string>();
					if (followups.Count > 0)
					{
						ctx.CurrentStage = "Followup";
						// Take up to 3
						ctx.AvailableQuestionIds = followups.OrderBy(x => _random.Next()).Take(3).ToList();
						return (true, null);
					}
					else
					{
						// No followups? End interview early
						ApplyInterviewResult(ctx, playerRole, response);
						return (true, null);
					}
				}
				else // Followup Done
				{
					ApplyInterviewResult(ctx, playerRole, response);
					return (true, null);
				}
			}

			// Check if NPC is dead first
			if (!npc.Alive && optionId != "leave")
				return (false, $"{npc.Name} is no longer available");


			// CHECK FOR CAMERA CATCH (Admirer Kill)
			if (playerRole == Role.Admirer && optionId.StartsWith("kill_"))
			{
				Console.WriteLine($"[DEBUG] Kill attempt on {npcId}. NPC Room: '{npc.CurrentRoomId}'. Active Cams: {string.Join(", ", _gameState.ActiveCameraRoomIds)}");
				
				if (_gameState.ActiveCameraRoomIds.Count > 0)
				{
					// Check if NPC is in a monitored room
					if (_gameState.ActiveCameraRoomIds.Contains(npc.CurrentRoomId))
					{
						// ADMIRER CAUGHT!
						_gameState.AdmirerCaught = true;
						_gameState.AdmirerEliminated = false; // Just to be sure, though it's set on police call
						_gameState.AddNotification($"[CAMERA ALERT] Suspicious activity detected in {npc.CurrentRoomId}!");
						_gameState.AddNotification($"Producer's Camera captured the crime!");
						// We do NOT instantly end game, Producer must Call Police.
					}
					else
					{
						Console.WriteLine($"[DEBUG] Detection Failed. NPC Room '{npc.CurrentRoomId}' not in Active List.");
						// Add failing notification for debug as requested
						// Only show to Admirer ideally, but global notif is fine for now or handle via UI filtering
						// _gameState.AddNotification($"[DEBUG] Murder in {npc.CurrentRoomId} (Unmonitored)");
					}
				}
			}

			// 3. Prophet RPS Resolution (Step 1 Trigger - Virtual Action)
			if (optionId.StartsWith("start_convert_"))
			{
				if (playerRole != Role.Prophet) return (false, "Only Prophets can convert.");
				
				// Find valid RPS action for this NPC to store as context
				var npcActions = npcConfig["interactionTree"]?["root"]?["options"] as JArray;
				string baseActionId = null;
				if (npcActions != null)
				{
					foreach (var act in npcActions)
					{
						string aId = act["id"]?.Value<string>();
						if (aId != null && (aId.StartsWith("convert") || aId.Contains("convert_")))
						{
							// Ideally confirm it is the RPS one
							baseActionId = aId;
							break; 
						}
					}
				}
				
				if (baseActionId == null) return (false, "This NPC cannot be converted (No RPS action found).");

				// Chaos / Difficulty Logic
				var playerState = _gameState.GetPlayerState(playerRole);
				var chaosMeter = playerState.GetMeter("chaos");
				double chaos = chaosMeter?.Value ?? 0;
				string difficulty = "hard";
				if (chaos >= 7) difficulty = "easy";
				else if (chaos >= 3) difficulty = "normal";

				// Easy Mode: Auto-Win immediately
				if (difficulty == "easy")
				{
					// Apply effects of base ID immediately
					var baseOption = npcActions.FirstOrDefault(o => o["id"]?.Value<string>() == baseActionId);
					if (baseOption != null)
					{
						ApplyActionEffects(baseOption, playerRole, npcId, true);
						_gameState.AddNotification($"[EASY] Your Prophet Powers overwhelmed {npcId} instantly!");
						return (true, null);
					}
				}

				// Generate Winning Move
				string[] moves = { "rock", "paper", "scissors" };
				string winningMove = moves[Random.Shared.Next(moves.Length)];
				var visibleOptions = new List<string>();

				if (difficulty == "normal")
				{
					// Normal: Winner + 1 Loser
					var losers = moves.Where(m => m != winningMove).OrderBy(_ => Random.Shared.Next()).Take(1);
					visibleOptions.Add(winningMove);
					visibleOptions.AddRange(losers);
					// Shuffle them for display so winner isn't always first
					visibleOptions = visibleOptions.OrderBy(_ => Random.Shared.Next()).ToList();
				}
				else
				{
					// Hard: All 3
					visibleOptions = moves.ToList();
				}
				
				_gameState.ActiveConversions[playerRole] = new ConversionContext 
				{
					NpcId = npcId,
					WinningMove = winningMove,
					BaseActionId = baseActionId,
					VisibleOptions = visibleOptions
				};
				
				_gameState.AddNotification($"Ritual started ({difficulty.ToUpper()})! Check your UI choices...");
				return (true, null); // Step 1 Success
			}


			// 3b. Prophet RPS Resolution (Step 2 - The Choice)
			if (playerRole == Role.Prophet && (optionId.EndsWith("_rock") || optionId.EndsWith("_paper") || optionId.EndsWith("_scissors")))
			{
				int lastUnderscore = optionId.LastIndexOf('_');
				string baseId = optionId.Substring(0, lastUnderscore);
				string playerMove = optionId.Substring(lastUnderscore + 1);

				// Retrieve Conversion Context
				if (!_gameState.ActiveConversions.TryGetValue(playerRole, out var ctx) || ctx.NpcId != npcId)
				{
					return (false, "Conversion session expired or mismatch");
				}

				// Find the actual config option for the base action (e.g. "convert_john")
				var npcActions = npcConfig["interactionTree"]?["root"]?["options"] as JArray;
				var baseOption = npcActions?.FirstOrDefault(o => o["id"]?.Value<string>() == baseId);
				
				if (baseOption == null) return (false, "Invalid RPS base action configuration");

				// Use STORED Winning Move
				string winningMove = ctx.WinningMove;
				
				// Clear Context (One shot)
				_gameState.ActiveConversions.Remove(playerRole);

				bool rpsSuccess = false;
				
				// Game Rules:
				// Easy: Guaranteed win (winningMove == playerMove should hopefully match if logic is right, but easy mode skips to win anyway).
				// Normal: We showed Winner + Loser.
				// Hard: Standard RPS.
				
				if (playerMove == winningMove)
				{
					rpsSuccess = true;
				}
				

				// Derive NPC's move from the winning move (Rule: winningMove beats npcMove)
				string npcMove = "";
				if (winningMove == "rock") npcMove = "scissors";
				else if (winningMove == "paper") npcMove = "rock";
				else if (winningMove == "scissors") npcMove = "paper";
				
				string resultStr = rpsSuccess ? "WON" : "LOST";
				_gameState.AddNotification($"{playerRole} played {Capitalize(playerMove)} vs {Capitalize(npcMove)}... and {resultStr}!");

				// Apply effects using the BASE option config
				ApplyActionEffects(baseOption, playerRole, npcId, rpsSuccess);
				
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

				// 2. Probability Math: (8 * score + 5) / 100
				var loveMeter = player.GetMeter("love");
				double loveScore = loveMeter?.Value ?? 0;
				double chance = (8.0 * loveScore + 5.0) / 100.0;
				
				// Clamp chance to reasonable bounds if needed (max 1.0)
				chance = Math.Min(chance, 1.0);

				success = _random.NextDouble() < chance;

				if (!success)
				{
					_gameState.AddNotification($"Marriage rejected! (Chance was {chance:P0})");
					return (false, "Marriage proposal rejected. Try increasing Love.");
				}
				
				// Success - let the effects be applied and win condition will be checked
				_gameState.AddNotification("Marriage successful! Love has conquered all!");
			}
			else
			{
				// Standard resolution
				var resolutionType = option["resolution"]?["type"]?.Value<string>();
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
			}

			// Apply effects
			ApplyActionEffects(option, playerRole, npcId, success);

			if (!success && !isAdmirerMarry) // Admirer fail is already handled
				return (false, "Action failed (chance roll)");

			return (true, null);
		}

		private void ApplyInterviewResult(InterviewContext ctx, Role playerRole, string finalAnswerText = "")
		{
			// Add score to Ratings (if > 0)
			// User request: "-1 ratings points" for bad, so we apply delta directly.
			// However, ratings can't go below 0 usually, handled by Meter.
			
			var playerS = _gameState.GetPlayerState(playerRole);
			var ratings = playerS?.GetMeter("ratings");
			
			if (ratings != null)
			{
				ratings.Add(ctx.CurrentScore);
			}

			string resultMsg = ctx.CurrentScore > 0 ? "Great interview!" : (ctx.CurrentScore < 0 ? "Disastrous interview..." : "Average interview.");
			 _gameState.AddNotification($"Interview finished. Score: {ctx.CurrentScore}. {resultMsg}");
			 
			 // Update NPC with temporary text override so it persists after interview closes
			 var npc = _gameState.GetNPC(ctx.NpcId);
			 if (npc != null)
			 {
			 	string fullText = "";
				if (!string.IsNullOrEmpty(finalAnswerText)) fullText += $"{finalAnswerText}\n\n";
				fullText += $"[Interview Complete]\nFinal Score: {ctx.CurrentScore}\n{resultMsg}";
				
			 	npc.TemporaryDialogOverride = fullText;
			 }
			 
			 // Clear interview context to end the mode
			 _gameState.ActiveInterviews.Remove(playerRole);
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

			// Clear temporary dialog override on any new action
			var npc = GameState.GetNPC(npcId);
			if (npc != null) npc.TemporaryDialogOverride = null;

			GameState.InteractionSeed++; // Ensure randomness changes after every action
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
			bool conditionChecked = false; // Ensure we checked AT LEAST one thing

			var meterName = requirement["meter"]?.Value<string>();
			if (!string.IsNullOrEmpty(meterName))
			{
				conditionChecked = true;
				var minValue = requirement["minValue"]?.Value<double>();
				var meter = player.GetMeter(meterName);
				
				if (meter == null) 
				{
					// Console.WriteLine($"[CheckWin] Meter '{meterName}' not found for {player.Role}. FAILING.");
					return false;
				}

				if (minValue.HasValue)
				{
					// Console.WriteLine($"[CheckWin] {meterName}: {meter.Value} < {minValue.Value}?");
					if (meter.Value < minValue.Value) return false;
				}
			}

			var convertedCountReq = requirement["npcsConverted"]?.Value<int>();
			if (convertedCountReq.HasValue)
			{
				conditionChecked = true;
				int currentConverted = GameState.NPCs.Values.Count(n => n.Converted);
				// Console.WriteLine($"[CheckWin] Converted: {currentConverted} < {convertedCountReq.Value}?");
				if (currentConverted < convertedCountReq.Value) return false;
			}
			
			var npcsKilledReq = requirement["npcsKilled"]?.Value<int>();
			if (npcsKilledReq.HasValue)
			{
				conditionChecked = true;
				int currentKilled = GameState.NPCs.Values.Count(n => !n.Alive);
				// Console.WriteLine($"[CheckWin] Killed: {currentKilled} < {npcsKilledReq.Value}?");
				if (currentKilled < npcsKilledReq.Value) return false;
			}

			// Special check for Marriage Requirement ("npcStatus": "nonConverted")
			// This usually implies checking the "target" of the goal (Love Interest)
			var statusReq = requirement["npcStatus"]?.Value<string>();
			if (!string.IsNullOrEmpty(statusReq))
			{
				conditionChecked = true;
				// Find Love Interest (assuming this requirement is for Admirer's Marriage)
				var loveInterest = GameState.NPCs.Values.FirstOrDefault(n => n.IsLoveInterest);
				if (loveInterest == null) return false; // Should not happen

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
				// Only pass if we explicitly caught them (Winner set to Producer via catch mechanism)
				if (GameState.Winner != "Producer") return false;
			}

			// Fail-Closed: If we didn't check anything, assume the config key is typo'd or logic is missing.
			// Do NOT return true by default.
			if (!conditionChecked)
			{
				// Console.WriteLine("[CheckWin] No known conditions found in requirement block. FAILING.");
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

			// P(x) = 0.5 * e^(-2x)
			// User specified: "at any time x, f(x) is the probability of the trap triggering."
			// Since we check discrete steps, we treat this as instantaneous probability for this frame.
			double p = 0.5 * Math.Exp(-2.0 * trap.TimeAlive);

			if (_random.NextDouble() < p) // Check directly against probability (assuming it's per-check or normalized)
			{
				// Remove trap
				GameState.Traps.RemoveAt(i);
			}
		}
	}

	public void StartConversion(Role role, string npcId)
		{
			// Generate Winning Move Randomly (Uniform Distribution)
			// Using Random.Shared to avoid seed bias
			string[] moves = { "rock", "paper", "scissors" };
			string winningMove = moves[Random.Shared.Next(moves.Length)];
			
			GameState.ActiveConversions[role] = new ConversionContext 
			{
				NpcId = npcId,
				WinningMove = winningMove
			};
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
				{ "admirer_eliminated", GameState.AdmirerEliminated }
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
			
			// Export Active Interviews for Client UI
			var interviewsObj = new JObject();
			foreach (var kvp in GameState.ActiveInterviews)
			{
				interviewsObj[kvp.Key.ToString().ToLower()] = new JObject
				{
					{ "npcId", kvp.Value.NpcId },
					{ "currentStage", kvp.Value.CurrentStage },
					{ "lastResponse", kvp.Value.LastResponse }
				};
			}
			status["active_interviews"] = interviewsObj;

			// Export Active Conversions for Client UI
			var conversionsObj = new JObject();
			foreach (var kvp in GameState.ActiveConversions)
			{
				conversionsObj[kvp.Key.ToString().ToLower()] = new JObject
				{
					{ "npcId", kvp.Value.NpcId },
					{ "baseActionId", kvp.Value.BaseActionId },
					{ "visibleOptions", new JArray(kvp.Value.VisibleOptions) }
				};
			}
			status["active_conversions"] = conversionsObj;

			// Export NPC States (sync positions/status)
			var npcStatesObj = new JObject();
			foreach (var kvp in GameState.NPCs)
			{
				var npc = kvp.Value;
				npcStatesObj[kvp.Key] = new JObject
				{
					{ "alive", npc.Alive },
					{ "converted", npc.Converted },
					{ "married", npc.Married },
					{ "is_target", npc.IsTarget },
					{ "prank_active", npc.PrankActive },
					{ "temporary_dialog_override", npc.TemporaryDialogOverride }
					// Pos sync is handled differently or can be added here if needed, 
					// but sticking to logic required for UI.
				};
			}
			status["npc_states"] = npcStatesObj;

			return status;
		}
	}
}
