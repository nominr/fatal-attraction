using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json.Linq;
using FatalAttraction.Engine;

namespace FatalAttraction.Client
{
    public class ChatClient
    {
        private readonly GameEngine _engine;
        private int _currentPlayerIdx = 0;
        private List<Role> _players = new() { Role.Admirer, Role.Prophet, Role.Producer };
        private string _currentNpcId;

        public ChatClient(string configPath)
        {
            _engine = new GameEngine(configPath);
        }

        private void ClearScreen()
        {
            Console.Clear();
        }

        private void PrintHeader()
        {
            Console.WriteLine(new string('=', 70));
            Console.WriteLine("FATAL ATTRACTION - Chat Client Prototype".PadLeft(48));
            Console.WriteLine(new string('=', 70));
        }

        private void PrintPlayerInfo(Role playerRole)
        {
            var player = _engine.GameState.GetPlayerState(playerRole);
            Console.WriteLine($"\n📍 Current Player: {playerRole.ToString().ToUpper()}");
            Console.WriteLine(new string('-', 70));
            Console.WriteLine("Current Meters:");

            foreach (var meter in player.Meters.Values)
            {
                int barLength = 20;
                int filled = (int)((meter.Value / meter.MaxValue) * barLength);
                string bar = new string('█', filled) + new string('░', barLength - filled);
                Console.WriteLine($"  {meter.Name.ToUpper(),-12} [{bar}] {meter.Value:F1}/{meter.MaxValue}");
            }
        }

        private void PrintNPCInteraction(string npcId)
        {
            var npc = _engine.GameState.GetNPC(npcId);
            var npcConfig = _engine.GameState.Config["npcs"][npcId];

            Console.WriteLine($"\n🎭 NPC Interaction: {npc.Name}");
            Console.WriteLine(new string('-', 70));
            Console.WriteLine(npcConfig["interactionTree"]["root"]["text"].Value<string>());
            Console.WriteLine();
        }

        private List<JToken> PrintAvailableActions(string npcId, Role playerRole)
        {
            var actions = _engine.GetAvailableActions(npcId, playerRole);

            if (actions.Count == 0)
            {
                Console.WriteLine("❌ No available actions for your role with this NPC.");
                return actions;
            }

            Console.WriteLine("Available Actions:");
            for (int i = 0; i < actions.Count; i++)
            {
                Console.WriteLine($"  {i + 1}. {actions[i]["text"].Value<string>()}");
            }

            return actions;
        }

        private int GetPlayerChoice(int maxOption)
        {
            while (true)
            {
                Console.Write($"\n👤 Choose action (1-{maxOption}): ");
                string choice = Console.ReadLine();

                if (int.TryParse(choice, out int choiceInt) && choiceInt >= 1 && choiceInt <= maxOption)
                {
                    return choiceInt - 1;
                }

                Console.WriteLine($"❌ Please enter a number between 1 and {maxOption}.");
            }
        }

        private void PrintNotifications()
        {
            var notifs = _engine.GetNotifications();
            if (notifs.Count > 0)
            {
                Console.WriteLine("\n📢 Notifications:");
                Console.WriteLine(new string('-', 70));
                foreach (var notif in notifs)
                {
                    Console.WriteLine($"  {notif}");
                }
            }
        }

        private bool RunTurn(Role playerRole)
        {
            _engine.StartTurn(playerRole);
            PrintNotifications();

            // Get NPC and interactions
            var (npcId, prompt) = _engine.GetCurrentNPCPrompt();
            _currentNpcId = npcId;

            PrintNPCInteraction(npcId);
            var actions = PrintAvailableActions(npcId, playerRole);

            if (actions.Count == 0)
            {
                Console.WriteLine("Skipping turn...");
                _engine.EndTurn();
                return false;
            }

            int choiceIdx = GetPlayerChoice(actions.Count);
            var action = actions[choiceIdx];

            // Resolve action
            bool success = _engine.PerformAction(npcId, action["id"].Value<string>(), playerRole);

            if (action["resolution"] != null)
            {
                string resolutionType = action["resolution"]["type"]?.Value<string>();
                if (resolutionType == "rps")
                {
                    string result = success ? "You won!" : "You lost!";
                    Console.WriteLine($"\n✋ Rock-Paper-Scissors: {result}");
                }
                else if (resolutionType == "chance")
                {
                    string result = success ? "✅ Success!" : "❌ Failed!";
                    Console.WriteLine($"\n🎲 Chance Roll: {result}");
                }
            }

            PrintNotifications();

            // Check win condition
            string winMsg = _engine.CheckWinCondition(playerRole);
            if (winMsg != null)
            {
                Console.WriteLine($"\n🏆 {winMsg}");
                return true;
            }

            _engine.EndTurn();
            return false;
        }

        private void ShowGameSummary()
        {
            var status = _engine.GetGameStatus();
            Console.WriteLine("\n" + new string('=', 70));
            Console.WriteLine("GAME SUMMARY".PadLeft(45));
            Console.WriteLine(new string('=', 70));

            var players = (JObject)status["players"];
            foreach (var player in players)
            {
                Console.WriteLine($"\n{player.Key.ToUpper()}:");
                var meters = (JObject)player.Value["meters"];
                foreach (var meter in meters)
                {
                    double value = meter.Value["value"].Value<double>();
                    double max = meter.Value["max"].Value<double>();
                    Console.WriteLine($"  {meter.Key}: {value}/{max}");
                }
            }
        }

        public void RunGame()
        {
            ClearScreen();
            PrintHeader();

            Console.WriteLine("\n📖 Game Configuration Loaded!");
            Console.WriteLine($"Total Turns: {_engine.GameState.MaxTurns}");
            Console.WriteLine($"Players: {string.Join(", ", _players.Select(p => p.ToString().ToUpper()))}");
            Console.WriteLine("\n⏱️  Press ENTER to start the game...");
            Console.ReadLine();

            Role gameWinner = Role.Admirer;
            bool hasWinner = false;

            while (!_engine.GameState.IsGameOver)
            {
                var currentPlayer = _players[_currentPlayerIdx];

                ClearScreen();
                PrintHeader();
                PrintPlayerInfo(currentPlayer);

                // Run turn
                if (RunTurn(currentPlayer))
                {
                    gameWinner = currentPlayer;
                    hasWinner = true;
                    break;
                }

                // Move to next player
                _currentPlayerIdx = (_currentPlayerIdx + 1) % _players.Count;

                // After all players have gone, prompt for next round
                if (_currentPlayerIdx == 0)
                {
                    Console.WriteLine("\n⏱️  All players have taken their turn. Press ENTER for next round...");
                    Console.ReadLine();
                }
            }

            // Game end
            ClearScreen();
            PrintHeader();

            if (hasWinner)
            {
                Console.WriteLine($"\n🏆 GAME OVER - {gameWinner.ToString().ToUpper()} WINS!");
            }
            else
            {
                Console.WriteLine($"\n🏁 GAME OVER - No winner reached by turn {_engine.GameState.CurrentTurn}");
            }

            ShowGameSummary();
            Console.WriteLine("\n" + new string('=', 70));
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var configPath = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                "data",
                "game_configuration.json"
            );

            if (!File.Exists(configPath))
            {
                Console.WriteLine($"❌ Error: Configuration file not found at {configPath}");
                return;
            }

            var client = new ChatClient(configPath);
            client.RunGame();
        }
    }
}
