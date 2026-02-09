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

	public class ConversionContext
	{
		public string NpcId { get; set; }
		public int Round { get; set; } = 1; // 1 or 2
		public int Score { get; set; } = 0; // Win=+1, Loss=-1, Tie=0
		public string BaseActionId { get; set; } 
		public List<string> VisibleOptions { get; set; } = new();
	}

	public class MoneyGameContext
	{
		public string NpcId { get; set; }
		public int TargetSum { get; set; }
		public int CurrentSum { get; set; } = 0;
		public List<int> SelectedCoins { get; set; } = new();
		public int Attempts { get; set; } = 0;
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
		public int PunchesTaken { get; set; } = 0;
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
				// Admirer (X) -> (-30, 24)
				// Prophet (Y) -> (30, 24)
				// Producer (Z) -> (0, -28)
				
				Vector2 v1 = new Vector2(-30, 24);
				Vector2 v2 = new Vector2(30, 24);
				Vector2 v3 = new Vector2(0, -28);
				
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
		public int PunchesTaken { get; set; } = 0;
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

		public string EditorialFocus { get; set; }
		public List<string> ActiveCameraRoomIds { get; private set; } = new();
		public bool AdmirerCaught { get; set; } = false;
		public long AdmirerCaughtTimestamp { get; set; } = 0; // Epoch ms
		public bool AdmirerEliminated { get; set; } = false;
		public bool MonitoringActive { get; set; } = false; // "Set Focus" essentially activates monitoring
		public int InteractionSeed { get; set; } = 0;
		public Dictionary<Role, ConversionContext> ActiveConversions { get; private set; } = new();
		public Dictionary<Role, MoneyGameContext> ActiveMoneyGames { get; private set; } = new();
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
				// Start/Continue Interview - Show Dynamic Questions
				// Add "Stop Flirting" option
				availableOptions.Add(new JObject
				{
					{ "id", "stop_flirt" },
					{ "text", "Stop Flirting" },
					{ "requires", new JObject() }
				});

				var interviewData = _gameState.InterviewData;
				foreach (var qId in interviewCtx.AvailableQuestionIds)
				{
					/// REMOVED: finish_interview button logic
					
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
				// Check for Prophet "convert_npc" power/action
				// The JSON config has "convert_<npcname>" or generic "convert" IDs.
				// We need to detect if this is a "RPS" resolution type or specific ID logic.
				// User wants: Hard (0-3 chaos) -> 3 options. Normal (3-7) -> 2 options. Easy (7-10) -> 1 option.
				
				// Identify convert action by ID or properties
				string id = option["id"]?.Value<string>();
				// Admirer target kills are handled by punch mechanic, so hide kill actions.
				if (playerRole == Role.Admirer && npc.IsTarget && !string.IsNullOrEmpty(id) && id.StartsWith("kill"))
				{
					continue;
				}
				bool isConvert = id != null && (id.StartsWith("convert") || id.Contains("convert_"));
				
				if (isConvert && playerRole == Role.Prophet)
				{
					if (!IsOptionAvailable(option, playerRole, npc, player)) continue;

					// Check active conversion
					if (_gameState.ActiveConversions.TryGetValue(playerRole, out var ctx) && ctx.NpcId == npcId)
					{
						// STEP 2: Show RPS Options (User has already started conversion)
						// Show all visible options from context
						foreach (var move in ctx.VisibleOptions)
						{
							availableOptions.Add(CreateRPSOption(id, $"Use {Capitalize(move)}", move));
						}
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
			
			// PRODUCER: Money Game Option
			if (playerRole == Role.Producer && npc.Alive)
			{
				if (_gameState.ActiveMoneyGames.TryGetValue(playerRole, out var ctx) && ctx.NpcId == npcId)
				{
					// Game Active: Show Coin Options and Submit
					int[] coins = { 1, 5, 10 };
					foreach (var c in coins)
					{
						availableOptions.Add(new JObject
						{
							{ "id", $"money_add_{c}" },
							{ "text", $"Add {c} Coin" },
							{ "requires", new JObject() }
						});
					}
					
					availableOptions.Add(new JObject
					{
						{ "id", "money_submit" },
						{ "text", "Submit Offer" },
						{ "requires", new JObject() }
					});
				}
				else
				{
					// Not Active: Start Option
					availableOptions.Add(new JObject
					{
						{ "id", "start_money_game" },
						{ "text", "Play Money Game" },
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
						long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
						long elapsed = now - _gameState.AdmirerCaughtTimestamp;

						// FAIL-SAFE: If timestamp is 0 (missing) but caught is true, allow it.
						if (_gameState.AdmirerCaughtTimestamp == 0)
						{
							elapsed = 0; 
						}
						
						if (elapsed > 40000) // 40 seconds
						{
							return (false, "The 40 second window to call police has expired!");
						}
						
						// _gameState.Winner = Role.Producer.ToString(); // OLD: Ended game
						_gameState.AdmirerEliminated = true; // NEW: Just eliminate admirer
						
						_gameState.AddNotification("POLICE CALLED! The Admirer has been arrested based on video evidence!");
						return (true, null);
					}
					return (false, "You have no evidence to call the police!");
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

			// FLIRT RESOLUTION (Formerly Interview)
			if (optionId == "start_flirt")
			{
				if (playerRole != Role.Admirer) return (false, "Only Admirer can flirt.");
				if (_gameState.ActiveInterviews.ContainsKey(playerRole)) return (false, "You are already flirting with someone!");
				
				var interviewData = _gameState.InterviewData;
				var introTopics = interviewData?["default"]?["intro_topics"] as JArray;
				
				if (introTopics == null || introTopics.Count == 0) return (false, "No flirting topics found!");

				// Pick 3 random intro questions
				var randomQuestions = introTopics.OrderBy(x => _random.Next()).Take(3)
					.Select(x => x["id"]?.Value<string>()).ToList();

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
						ApplyInterviewResult(ctx, playerRole);
						return (true, null);
					}
				}
				else // Followup Done
				{
					// Apply Score IMMEDIATELY so it counts even if user walks away
					ApplyInterviewResult(ctx, playerRole, clearAndFinish: false); // Don't clear active status yet
					
					// Set to Finished state so text persists but no buttons shown
					ctx.CurrentStage = "Finished";
					ctx.AvailableQuestionIds = new List<string>(); // Empty list = no buttons
					return (true, null);
				}
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
					npc.PunchesTaken = 0; // Reset health to full (requires 50 punches again)
					// Allow logic to flow or just notify?
					// Probably just notify + update state field implies it will be synced
					_gameState.AddNotification($"MIRACLE! {npc.Name} has been resurrected by the Prophet!");
					
					// Optional: Add Prophet Points for a miracle?
					// npc.State += new Vector3(0, 5, 0); // Big boost?
				}
				else
				{
					_gameState.AddNotification($"Resurrection failed. {npc.Name} remains dead.");
				}
				return (true, null);
			}




			// CHECK FOR CAMERA CATCH (Admirer Kill)
			if (playerRole == Role.Admirer && optionId.StartsWith("kill_"))
			{
				Console.WriteLine($"[DEBUG] Kill attempt on {npcId}. NPC Room: '{npc.CurrentRoomId}'. Active Cams: {string.Join(", ", _gameState.ActiveCameraRoomIds)}");
				
				if (_gameState.ActiveCameraRoomIds.Count > 0)
				{
					// Check if NPC is in a monitored room
					if (_gameState.ActiveCameraRoomIds.Contains(npc.CurrentRoomId))
					{

						_gameState.AdmirerCaughtTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
						_gameState.AdmirerEliminated = false; 
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

				// Prophet Logic: 2-Round RPS
				// Start conversion initializes the context
				
				var ctx = new ConversionContext
				{
					NpcId = npcId,
					BaseActionId = baseActionId,
					Round = 1,
					Score = 0
				};
				
				// Generate options for Round 1
				string[] moves = { "rock", "paper", "scissors" };
				ctx.VisibleOptions = moves.ToList(); // Show all options
				
				_gameState.ActiveConversions[playerRole] = ctx;
				_gameState.AddNotification($"Prophet started conversion ritual with {npcId}. Round 1!");
				return (true, null);
			}

			// 5. PRODUCER MONEY GAME
			if (optionId == "start_money_game")
			{
				if (playerRole != Role.Producer) return (false, "Only Producer can play Money Game.");
				
				// Initialize Context
				// Target sum: Random 15-30?
				int target = Random.Shared.Next(15, 31);
				
				var ctx = new MoneyGameContext
				{
					NpcId = npcId,
					TargetSum = target,
					CurrentSum = 0,
					SelectedCoins = new List<int>()
				};
				
				_gameState.ActiveMoneyGames[playerRole] = ctx;
				_gameState.AddNotification($"Producer started Money Game with {npc.Name}. Target: {target}");
				return (true, null);
			}
			
			if (optionId.StartsWith("money_add_"))
			{
				if (!_gameState.ActiveMoneyGames.TryGetValue(playerRole, out var ctx) || ctx.NpcId != npcId)
				{
					return (false, "No active money game.");
				}
				
				int coinVal = int.Parse(optionId.Split('_')[2]);
				ctx.SelectedCoins.Add(coinVal);
				ctx.CurrentSum += coinVal;
				
				// _gameState.AddNotification($"Added {coinVal}. Current: {ctx.CurrentSum}/{ctx.TargetSum}");
				return (true, null);
			}
			
			if (optionId == "money_submit")
			{
				if (!_gameState.ActiveMoneyGames.TryGetValue(playerRole, out var ctx) || ctx.NpcId != npcId)
				{
					return (false, "No active money game.");
				}
				
				int score = (ctx.CurrentSum == ctx.TargetSum) ? 1 : -1;
				
				var targetNpc = _gameState.GetNPC(ctx.NpcId); // Use local var to avoid closure issues if any
				if (targetNpc != null)
				{
					Vector3 points = ScoringRules.GetMoneyGamePoints(score);
					targetNpc.State += points;
					Console.WriteLine($"[DEBUG] MoneyGame: {targetNpc.Name} State += {points} -> {targetNpc.State}");
				}
				
				_gameState.AddNotification($"Money Game Result: {(score > 0 ? "SUCCESS" : "FAILURE")} (Sum: {ctx.CurrentSum}, Target: {ctx.TargetSum})");
				_gameState.ActiveMoneyGames.Remove(playerRole);
				return (true, null);
			}

			// 4. Prophet RPS Resolution (Step 2 - Choice Made)
			if (optionId.StartsWith("convert_") && _gameState.ActiveConversions.TryGetValue(playerRole, out var convCtx))
			{
				// Format: convert_katy_rock
				string selectedMove = optionId.Split('_').Last(); // rock/paper/scissors
				
				// Server chooses move
				string[] moves = { "rock", "paper", "scissors" };
				string npcMove = moves[Random.Shared.Next(moves.Length)];
				
				// Determine result
				int roundScore = 0;
				if (selectedMove == npcMove) roundScore = 0; // Tie
				else if ((selectedMove == "rock" && npcMove == "scissors") || 
						 (selectedMove == "paper" && npcMove == "rock") ||
						 (selectedMove == "scissors" && npcMove == "paper"))
				{
					roundScore = 1; // Win
				}
				else
				{
					roundScore = -1; // Loss
				}
				
				convCtx.Score += roundScore;
				_gameState.AddNotification($"Round {convCtx.Round}: Prophet played {selectedMove} vs {npcMove}. Result: {(roundScore > 0 ? "WIN" : (roundScore < 0 ? "LOSS" : "TIE"))}");

				if (convCtx.Round < 2)
				{
					// Prepare Round 2
					convCtx.Round++;
					_gameState.AddNotification($"Starting Round 2...");
					return (true, null);
				}
				else
				{
					// Finished
					// Finished
					var targetNpc = _gameState.GetNPC(convCtx.NpcId); // Use unique variable name
					if (targetNpc != null)
					{
						Vector3 points = ScoringRules.GetConversionPoints(convCtx.Score);
						targetNpc.State += points;
						Console.WriteLine($"[DEBUG] Convert: {targetNpc.Name} State += {points} -> {targetNpc.State}");
						
						// Check for "Conversion" status update based on State?
						// "Prophet successful conversion... generates points"
						// User didn't specify threshold for "Converted" status, just points accumulation.
						// We'll keep the boolean "Converted" flag logic if the Prophet dominates the state?
						// For now, just accumulation.
					}
					
					_gameState.AddNotification($"Conversion ritual finished. Total Score: {convCtx.Score}");
					_gameState.ActiveConversions.Remove(playerRole);
					return (true, null);
				}
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

		public void EndActiveInteraction(Role playerRole)
		{
			if (_gameState.ActiveInterviews.ContainsKey(playerRole))
			{
				_gameState.ActiveInterviews.Remove(playerRole);
				// _gameState.AddNotification("Interaction cleared.");
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

		// Normalized Score Status or similar could go here
		// Removing meter display
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
				// Meters removed. Auto-fail or ignore meter conditions.
				// Console.WriteLine($"[CheckWin] Meter check '{meterName}' ignored (Meters removed).");
				return false; 
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

				// Meters removed

				playerObj["meters"] = metersObj;
				playersObj[kvp.Key.ToString().ToLower()] = playerObj;
			}

			status["players"] = playersObj;
			
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

			return status;
		}
	}
}
// Touched for recompilation
