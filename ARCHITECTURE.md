# Fatal Attraction - Architecture Documentation

## System Overview

```
┌─────────────────────────────────────────────────────┐
│           ChatClient (Interactive UI)                │
│  - Turn loop management                              │
│  - Player I/O & display                              │
│  - Notification rendering                            │
└────────────────┬────────────────────────────────────┘
                 │
                 ↓
┌─────────────────────────────────────────────────────┐
│            GameEngine (Orchestrator)                 │
│  - State management                                  │
│  - Turn progression                                  │
│  - Win condition checking                            │
└────────────────┬────────────────────────────────────┘
                 │
      ┌──────────┼──────────┐
      ↓          ↓          ↓
    ┌──────────────────┐  ┌──────────────────┐
    │   GameState      │  │ Interaction      │
    │ - Players        │  │ Resolver         │
    │ - NPCs           │  │ - Action filter  │
    │ - Notifications  │  │ - Effect apply   │
    │ - Config (JSON)  │  │ - RNG resolve    │
    └──────────────────┘  └──────────────────┘
           │
           ↓
    ┌──────────────────────────────────────┐
    │  game_configuration.json              │
    │  ├─ Roles & powers                    │
    │  ├─ NPCs & interaction trees          │
    │  ├─ Game rules                        │
    │  └─ Meters & effects                  │
    └──────────────────────────────────────┘
```

## Class Hierarchy

### GameEngine.cs

#### `enum Role`
```
Role:Enum
├─ Admirer
├─ Prophet
└─ Producer
```

#### `class Meter`
Represents a player stat (Love, Chaos, Ratings)

**Properties:**
- `Name: string` - Meter identifier
- `Value: double` - Current value
- `MinValue: double` - Lower bound (default: 0)
- `MaxValue: double` - Upper bound (default: 10)

**Methods:**
- `Add(delta: double) → double` - Adds delta, clamped to [Min, Max]
- `ToString() → string` - Returns formatted string

#### `class NPC`
Represents a non-player character

**Properties:**
- `Id: string` - Unique identifier
- `Name: string` - Display name
- `Converted: bool` - Prophet conversion state
- `Alive: bool` - Death state
- `IsLoveInterest: bool` - Admirer target flag

#### `class PlayerState`
Tracks individual player data

**Properties:**
- `Role: Role` - Player's role
- `Meters: Dict<string, Meter>` - Stat tracking
- `ActionsTaken: List<string>` - History
- `GoalsMet: List<string>` - Achievement tracking

**Methods:**
- `GetMeter(name: string) → Meter` - Retrieves a meter or null

#### `class GameState`
Manages overall game state and config

**Properties:**
- `Config: JObject` - Loaded JSON config
- `CurrentTurn: int` - Current turn number
- `MaxTurns: int` - Turn limit
- `Players: Dict<Role, PlayerState>` - All players
- `NPCs: Dict<string, NPC>` - All NPCs
- `Notifications: List<string>` - Event queue
- `EditorialFocus: string` - Producer focus (future)

**Methods:**
- `GameState(configPath: string)` - Constructor, loads config & initializes
- `GetPlayerState(role: Role) → PlayerState`
- `GetNPC(npcId: string) → NPC`
- `AddNotification(message: string) → void`
- `GetAndClearNotifications() → List<string>`
- `AdvanceTurn() → bool`
- `IsGameOver: bool` - Turn limit reached?

#### `class InteractionResolver`
Resolves NPC interactions and applies effects

**Methods:**
- `GetNPCInteractions(npcId: string, role: Role) → List<JToken>`
  - Returns available actions for player
  - Filters by role and NPC status
  
- `ResolveInteraction(npcId: string, optionId: string, role: Role) → bool`
  - Rolls resolution (chance/RPS)
  - Applies effects
  - Returns success/failure
  
- `ApplyEffect(effect: JToken, role: Role, npcId: string, success: bool) → void`
  - Updates meters
  - Changes NPC state
  - Queues notifications

#### `class GameEngine`
Main orchestrator and public API

**Properties:**
- `GameState: GameState`
- `InteractionResolver: InteractionResolver`

**Methods:**
- `GameEngine(configPath: string)` - Initializes everything
- `GetCurrentNPCPrompt() → (string npcId, string prompt)`
- `GetAvailableActions(npcId: string, role: Role) → List<JToken>`
- `PerformAction(npcId: string, actionId: string, role: Role) → bool`
- `StartTurn(role: Role) → void` - Displays turn info
- `EndTurn() → void` - Advances turn counter
- `CheckWinCondition(role: Role) → string` - Returns win message or null
- `GetNotifications() → List<string>` - Clears queue and returns
- `GetGameStatus() → JObject` - Returns JSON status

### ChatClient.cs

#### `class ChatClient`
Interactive CLI interface

**Properties:**
- `_engine: GameEngine` - Game logic
- `_currentPlayerIdx: int` - Active player index
- `_players: List<Role>` - Player rotation
- `_currentNpcId: string` - Current encounter

**Methods:**
- `ChatClient(configPath: string)` - Initializes engine
- `ClearScreen() → void`
- `PrintHeader() → void` - Game title
- `PrintPlayerInfo(role: Role) → void` - Meter display
- `PrintNPCInteraction(npcId: string) → void` - NPC description
- `PrintAvailableActions(npcId: string, role: Role) → List<JToken>`
- `GetPlayerChoice(maxOption: int) → int` - Input validation
- `PrintNotifications() → void` - Display events
- `RunTurn(role: Role) → bool` - Single turn logic
- `ShowGameSummary() → void` - Final stats
- `RunGame() → void` - Main loop

#### `class Program`
Entry point

**Methods:**
- `Main(args: string[]) → void`
  - Locates config file
  - Creates ChatClient
  - Calls RunGame()

## Data Flow

### Game Initialization
```
Program.Main()
  → ChatClient(configPath)
    → GameEngine(configPath)
      → GameState(configPath)
        → LoadConfig() from JSON
        → InitializeGame()
          → Create PlayerState for each Role
            → Create Meters from config
          → Create NPC for each entry
```

### Turn Resolution
```
ChatClient.RunTurn(role)
  → PrintPlayerInfo() - Display current meters
  → GetCurrentNPCPrompt() - Pick random NPC
  → PrintNPCInteraction() - Show NPC text
  → GetAvailableActions() - Filter by role
    → InteractionResolver.GetNPCInteractions()
  → GetPlayerChoice() - Get player input
  → PerformAction()
    → InteractionResolver.ResolveInteraction()
      → Roll chance/RPS
      → ApplyEffect()
        → Update Meter values
        → Modify NPC state
        → AddNotification()
  → PrintNotifications() - Show all events
  → CheckWinCondition() - Check if game won
  → EndTurn() - Advance turn counter
```

## JSON Structure

### Root Objects
```json
{
  "game": { ... },           // Version info
  "roles": { ... },          // Power definitions
  "systems": { ... },        // Editorial attention
  "npcs": { ... },           // NPC interactions
  "gameRules": { ... },      // Win conditions
  "demoClient": { ... }      // UI configuration
}
```

### Role Definition
```json
"admirer": {
  "displayName": "Admirer",
  "powers": {
    "kill_npc": {
      "displayName": "Kill NPCs",
      "constraints": { ... },
      "uiHint": "..."
    }
  },
  "goals": { ... },
  "meters": {
    "love": {
      "displayName": "Love",
      "min": 0,
      "max": 10,
      "start": 0
    }
  }
}
```

### NPC Definition
```json
"katy": {
  "id": "katy",
  "name": "Katy",
  "role": "love_interest",
  "convertedStatus": false,
  "interactionTree": {
    "root": {
      "text": "Katy appears...",
      "options": [
        {
          "id": "talk",
          "text": "Action description",
          "requires": { "role": "admirer" },
          "resolution": { "type": "chance", "successChance": 0.55 },
          "effects": [ ... ],
          "effects_on_success": [ ... ],
          "effects_on_failure": [ ... ]
        }
      ]
    }
  }
}
```

### Effect Types
```json
{
  "meter": "love",
  "delta": 1,
  "clampMax": 10
}

{
  "notification": "You did something!"
}

{
  "npc": "katy",
  "field": "convertedStatus",
  "value": true
}
```

## Resolution Mechanics

### Chance-Based
```
resolution.type = "chance"
resolution.successChance = 0.55

Roll: Random(0, 1) < 0.55
Success: Apply effects_on_success
Failure: Apply effects_on_failure
```

### Rock-Paper-Scissors
```
resolution.type = "rps"

Roll: Random(0, 1) < 0.5 (simplified)
Success: Convert NPC
Failure: Resist notification
```

### Immediate
```
resolution.type = "immediate"

Always succeeds, apply effects
```

### Delayed
```
resolution.type = "delayed" (future)

Store trap in game state
Trigger after N turns
```

## Player Workflow

### Admirer
1. **Goal**: Love → 8 (Katy non-converted) → Marry
2. **Each Turn**:
   - View Love meter
   - Meet random NPC
   - Options:
     - Talk to Katy: +1 Love
     - Unconvert target: 55% chance
     - Kill NPC: Eliminates rival (non-converted only)
3. **Limitations**: Can't harm converted NPCs

### Prophet
1. **Goal**: Chaos → 9/10
2. **Each Turn**:
   - View Chaos meter
   - Meet random NPC
   - Options:
     - Convert: RPS (50% success)
     - Prank: +1 Chaos immediate
     - Set trap: Delayed effect (future)
3. **Mechanics**: Chaos segments affect conversion difficulty

### Producer
1. **Goal**: Ratings → 8/10
2. **Each Turn**:
   - View Ratings meter
   - Meet random NPC
   - Options:
     - Marry NPC: +1 Ratings
     - Unconvert: 75% success
     - Cleanup: -1 Chaos
     - Report Admirer: (future)
3. **Detection**: Watch for Admirer actions

## Extension Points

### Adding a Meter
1. Edit config `roles.role.meters`
2. GameState loads automatically
3. Update effects to reference new meter

### Adding an NPC
1. Add to config `npcs`
2. Add ID to `demoClient.npcPool`
3. Define interaction tree

### Adding an Action
1. Add option to NPC's interaction tree
2. Define `requires`, `resolution`, `effects`
3. No code changes needed

### Adding Win Condition
1. Edit `gameRules.winConditions`
2. Modify `requirement` object
3. Update display string

## Performance Characteristics

| Operation | Time | Notes |
|-----------|------|-------|
| Config load | <10ms | Full JSON into memory |
| NPC selection | O(1) | Random from pool |
| Action filter | O(n) | n = options (~5) |
| Resolution roll | <1ms | RNG + effect apply |
| Turn cycle | <10ms | UI render + logic |
| Full game (10 turns × 3 players) | <1s | Depends on user input |

## Thread Safety

**Current**: Single-threaded

**If multi-threaded needed:**
- Lock GameState for meter updates
- Queue notifications thread-safely
- Use concurrent collections for players/NPCs

## Testing Strategy

### Unit Tests (Future)
```csharp
[Test]
public void TestMeterClamping()
{
  var meter = new Meter("test", 5, 0, 10);
  meter.Add(10);
  Assert.AreEqual(10, meter.Value); // Clamped to max
}
```

### Integration Tests (Future)
```csharp
[Test]
public void TestActionResolution()
{
  var engine = new GameEngine("config.json");
  bool success = engine.PerformAction("katy", "talk", Role.Admirer);
  var player = engine.GameState.GetPlayerState(Role.Admirer);
  Assert.AreEqual(1, player.GetMeter("love").Value);
}
```

### Manual Testing
1. Run game via ChatClient
2. Test each role's actions
3. Verify meters update
4. Confirm win conditions trigger
5. Check role-based filtering
