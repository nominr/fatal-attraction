# Fatal Attraction - C# Implementation Guide

## Quick Start

```bash
# Restore dependencies
dotnet restore

# Run the game
dotnet run

# Or build first, then run
dotnet build
./bin/Debug/net6.0/fatal-attraction
```

## File Structure

```
fatal-attraction/
├── FatalAttraction.csproj           # .NET project file with dependencies
├── GameEngine.cs                    # Core game logic (~400 lines)
│   ├── Role enum                    # Admirer, Prophet, Producer
│   ├── Meter class                  # Tracks player stats
│   ├── NPC class                    # Non-player character data
│   ├── PlayerState class            # Individual player state
│   ├── GameState class              # Overall game state & config loading
│   ├── InteractionResolver class    # Resolves NPC interactions
│   └── GameEngine class             # Main orchestrator
├── ChatClient.cs                    # Interactive CLI (~350 lines)
│   ├── ChatClient class             # Game UI & loop
│   └── Program class                # Entry point
├── data/
│   └── game_configuration.json       # Single source of truth (JSON)
├── README.md                        # Full documentation
└── QUICKSTART.md                    # This file
```

## Game Flow

```
Start Game
    ↓
[TURN LOOP] for each turn ≤ 10
    ├─ Player 1 (Admirer)
    │   ├─ View meters
    │   ├─ Get random NPC
    │   ├─ Choose action
    │   ├─ Resolve action (chance/RPS)
    │   └─ Apply effects
    │
    ├─ Player 2 (Prophet)
    │   └─ [same as Admirer]
    │
    └─ Player 3 (Producer)
        └─ [same as Admirer]

Check win conditions
Show final summary
```

## Key Classes

### GameEngine
```csharp
var engine = new GameEngine("data/game_configuration.json");

// Get current NPC prompt
var (npcId, prompt) = engine.GetCurrentNPCPrompt();

// Get available actions for player
var actions = engine.GetAvailableActions(npcId, Role.Admirer);

// Perform action
bool success = engine.PerformAction(npcId, "talk", Role.Admirer);

// Check win condition
string winMsg = engine.CheckWinCondition(Role.Admirer);
```

### GameState
```csharp
var gameState = engine.GameState;

// Access player state
var player = gameState.GetPlayerState(Role.Admirer);
var loveMeter = player.GetMeter("love");
loveMeter.Add(1); // Raise by 1

// Access NPC
var katy = gameState.GetNPC("katy");
Console.WriteLine(katy.Name); // "Katy"
katy.Converted = true;

// Notifications
gameState.AddNotification("Player did something!");
var notifs = gameState.GetAndClearNotifications();
```

### Meter
```csharp
var meter = new Meter("love", startValue: 0, min: 0, max: 10);
meter.Add(1);  // Raises to 1
meter.Add(10); // Clamps to 10
Console.WriteLine(meter); // "love: 10/10"
```

## Configuration (JSON)

### Roles
```json
"roles": {
  "admirer": {
    "displayName": "Admirer",
    "powers": {
      "kill_npc": { ... },
      "unconvert_target": { ... }
    },
    "goals": {
      "kill_targets": { ... }
    },
    "meters": {
      "love": { "min": 0, "max": 10, "start": 0 }
    }
  }
}
```

### NPCs
```json
"npcs": {
  "katy": {
    "id": "katy",
    "name": "Katy",
    "role": "love_interest",
    "convertedStatus": false,
    "interactionTree": {
      "root": {
        "text": "Katy walks up to you...",
        "options": [
          {
            "id": "talk",
            "text": "Talk",
            "requires": { "role": "admirer" },
            "effects": [
              { "meter": "love", "delta": 1 }
            ]
          }
        ]
      }
    }
  }
}
```

### Game Rules
```json
"gameRules": {
  "turnsPerGame": 10,
  "winConditions": {
    "admirer": {
      "primary": {
        "goal": "marry_love_interest",
        "requirement": { "meter": "love", "minValue": 8 }
      }
    }
  }
}
```

## Interaction Options

Each option can have:
- `id`: Unique identifier for the action
- `text`: Display text for player
- `requires`: Role/NPC status constraints
- `resolution`: "chance", "rps", "immediate", "delayed"
- `effects`: Applied on default success
- `effects_on_success`: Applied if resolution succeeds
- `effects_on_failure`: Applied if resolution fails

### Effect Types

**Meter Effect**
```json
{ "meter": "love", "delta": 1, "clampMax": 10 }
```

**Notification Effect**
```json
{ "notification": "You raised love!" }
```

**NPC Status Effect**
```json
{ "npc": "katy", "field": "convertedStatus", "value": true }
```

## Extending the Game

### Add New NPC
1. Edit `data/game_configuration.json`
2. Add to `npcs` object:
```json
"marcus": {
  "id": "marcus",
  "name": "Marcus",
  "role": "generic",
  "convertedStatus": false,
  "interactionTree": { ... }
}
```
3. Add to `demoClient.npcPool`:
```json
"npcPool": ["katy", "john", "rebecca", "marcus"]
```

### Add New Meter
1. Edit role meters in config:
```json
"meters": {
  "charm": { "min": 0, "max": 10, "start": 0 }
}
```
2. GameState automatically initializes it

### Change Win Conditions
Edit `gameRules.winConditions`:
```json
"admirer": {
  "primary": {
    "requirement": { "meter": "love", "minValue": 5 }
  }
}
```

### Adjust Success Rates
Edit power resolution:
```json
"resolution": {
  "type": "chance",
  "successChance": 0.7  // 70% chance
}
```

## Dependencies

- **Newtonsoft.Json** (v13.0.3): NuGet package for JSON handling
- **.NET Runtime** (6.0+): For C# execution

Install via:
```bash
dotnet add package Newtonsoft.Json --version 13.0.3
```

Or it's auto-installed when you run `dotnet restore`.

## Testing

### Manual Test
1. Run `dotnet run`
2. Play through a few turns
3. Check:
   - Meters update correctly
   - Actions filter by role
   - Success/failure probabilities work
   - Win conditions trigger

### Code Test (Future)
```csharp
// Example unit test structure
[Test]
public void TestMeterAddition()
{
    var meter = new Meter("test", 5, 0, 10);
    meter.Add(3);
    Assert.AreEqual(8, meter.Value);
}
```

## Troubleshooting

**Error: "Configuration file not found"**
- Ensure `data/game_configuration.json` exists
- Project file includes: `<None Update="data\game_configuration.json"><CopyToOutputDirectory>Always</CopyToOutputDirectory></None>`

**Error: "Could not find Newtonsoft.Json"**
```bash
dotnet restore
```

**Game exits immediately**
- Check console output for exceptions
- Ensure all JSON is valid (use JSON linter)
- Check file paths are correct

## Performance Notes

- GameState loads entire JSON config into memory (fine for demo)
- Random NPC selection is O(1)
- Interaction resolution is O(n) where n = options count (~5)
- No persistence layer (resets each game)

## Next Steps

1. **Editorial Attention**: Producer chooses focus tokens
2. **Delayed Effects**: Traps trigger after N turns
3. **Reporting**: Producer can report Admirer
4. **Statistics**: Track wins across multiple games
5. **Networking**: Turn-based multiplayer via TCP/WebSockets
6. **Web UI**: ASP.NET Core with Blazor frontend
