using System;
using System.Collections.Generic;
using FatalAttraction.Engine;

namespace FatalAttraction.Tests 
{
    public class MurderTest
    {
        public static void RunTests()
        {
            Console.WriteLine("\n=== RUNNING MURDER DETECTION TESTS ===");
            
            // 1. Setup Mock Engine
            // Use relative path assuming we run from project root
            string configPath = "data/game_configuration.json"; 
            if (!System.IO.File.Exists(configPath))
            {
                // Fallback for debug/build dirs
                configPath = "../../data/game_configuration.json"; 
            }
            
            Console.WriteLine($"Loading config from: {configPath}");
            var engine = new GameEngine(configPath);
            var state = engine.GameState;
            
            // Setup an NPC for testing
            string testNpcId = "john";
            var npc = state.GetNPC(testNpcId);
            if (npc == null) 
            {
                Console.WriteLine("[FATAL] Could not find NPC 'john' for testing.");
                return;
            }
            npc.Alive = true;

            // List of rooms to test
            // These MUST match the Ids used in GameWorld.cs active camera list
            string[] rooms = { "Room1", "Room2", "Room3", "Room4", "Room5", "Hallways" };
            
            int passed = 0;
            int failed = 0;

            foreach (var room in rooms)
            {
                Console.WriteLine($"\n--- Testing Room: {room} ---");
                
                // Reset State
                state.ActiveCameraRoomIds.Clear();
                state.AdmirerCaught = false; 
                state.AdmirerEliminated = false;
                npc.Alive = true; // Reset NPC life

                // 1. Activate Camera for this room
                state.ActiveCameraRoomIds.Add(room);
                Console.WriteLine($"Producer Active Cameras: {string.Join(", ", state.ActiveCameraRoomIds)}");

                // 2. Place NPC in the room (Simulate OnBodyEnteredRoom)
                npc.CurrentRoomId = room;
                Console.WriteLine($"NPC '{npc.Name}' is in '{npc.CurrentRoomId}'");
                
                // 3. Attempt Kill (Mocking ResolveInteraction call)
                // We use "kill_john" assuming it adheres to naming convention
                var resolver = new InteractionResolver(state);
                var result = resolver.ResolveInteraction(testNpcId, $"kill_{testNpcId}", Role.Admirer);
                
                Console.WriteLine($"Action Result: Success={result.success}, Reason='{result.failReason}'");

                // 4. Verify Capture
                if (state.AdmirerCaught)
                {
                     Console.WriteLine($"[PASS] Murder CAUGHT in {room}");
                     passed++;
                }
                else
                {
                     Console.WriteLine($"[FAIL] Murder NOT caught in {room}!");
                     Console.WriteLine($"       Expected: {room} in Active List");
                     Console.WriteLine($"       Actual Active: {string.Join(",", state.ActiveCameraRoomIds)}");
                     Console.WriteLine($"       Diff: '{room}' vs '{npc.CurrentRoomId}'");
                     failed++;
                }
            }
            
            Console.WriteLine($"\n=== TESTS COMPLETE: {passed} PASS, {failed} FAIL ===");
        }
    }
}
