# Fatal Attraction - Chat Client Prototype

A text-based interactive prototype for testing the **Fatal Attraction** game mechanics. This is a single-source-of-truth system where all game configuration is managed through `data/game_configuration.json`.

## Overview

**Fatal Attraction** is a three-player asymmetric social strategy game where each player takes on one of three conflicting roles:

- **Admirer**: Must eliminate romantic rivals and marry their chosen love interest
- **Prophet**: Must convert NPCs to their cause and create chaos
- **Producer**: Must maintain high ratings and catch the Admirer in the act

## Architecture

### Game Configuration (`data/game_configuration.json`)

Single source of truth containing:
- **Roles**: Player abilities, powers, meters, and goals
- **NPCs**: Interactive characters with decision trees
- **Game Rules**: Win conditions and turn structure
- **Editorial Attention System**: Producer's focus mechanics

### Core Modules

#### `GameEngine.cs`
- **GameState**: Manages overall game state and config loading
- **PlayerState**: Tracks individual player meters and actions
- **NPC**: Represents non-player characters with state
- **InteractionResolver**: Resolves player actions against NPCs
- **GameEngine**: Main orchestrator

#### `ChatClient.cs`
- **ChatClient**: Interactive CLI interface
- Turn-based gameplay loop
- NPC interaction display
- Meter visualization with progress bars

## Gameplay Flow

### Per Turn
1. Player views current meters
2. Random NPC appears with interaction prompt
3. Player selects available action based on role
4. Action resolves (chance-based or rock-paper-scissors)
5. Effects applied: meters updated, notifications generated
6. Turn ends, move to next player

### Meters & Effects

**Admirer**
- `love`: Raised by talking to love interest (Katy)
- Win: Get `love` to 8/10 and marry Katy (non-converted)

**Prophet**
- `chaos`: Raised through conversions and pranks
- Win: Reach `chaos` 9/10

**Producer**
- `ratings`: Raised through NPC marriages
- Win: Reach `ratings` 8/10

## Game Mechanics

### Powers & Resolution Types

**Chance-Based**
- Admirer's unconvert: 55% success
- Producer's unconvert: 75% success
- Display: "🎲 Chance Roll: ✅ Success!" or "❌ Failed!"

**Rock-Paper-Scissors**
- Prophet's convert: 50% success (simplified demo)
- Display: "✋ Rock-Paper-Scissors: You won!" or "You lost!"

**Immediate**
- Prophet's pranks
- Producer's cleanup
- Instant effect application

### NPC Interaction Trees

Each NPC has a decision tree with role-specific options:

```json
{
  "id": "katy",
  "name": "Katy",
  "role": "love_interest",
  "interactionTree": {
    "root": {
      "text": "Katy walks up to you, smiling warmly.",
      "options": [
        {
          "id": "talk",
          "text": "Talk and raise love",
          "requires": { "role": "admirer" },
          "effects": [
            { "meter": "love", "delta": 1, "clampMax": 10 },
            { "notification": "You talked with Katy..." }
          ]
        }
      ]
    }
  }
}
```

### Options Filtering

Options are only shown if:
- Player's role matches `requires.role`
- NPC status matches `requires.npcStatus` (converted/nonConverted)

## Running the Prototype

### Prerequisites
- .NET 6.0 or higher
- Windows, macOS, or Linux

### Build & Run

```bash
dotnet restore
dotnet run
```

Or build then run:
```bash
dotnet build
./bin/Debug/net6.0/fatal-attraction
```

### Controls

- Press ENTER to confirm prompts
- Enter action number (1-N) when prompted
- Game auto-advances to next player after each turn

## Example Session

```
======================================================================
FATAL ATTRACTION - Chat Client Prototype
======================================================================

📍 Current Player: ADMIRER
Current Meters:
  LOVE         [░░░░░░░░░░░░░░░░░░░░] 0.0/10

🎭 NPC Interaction: John
John approaches you, looking suspicious.

Available Actions:
  1. Convert (Prophet, rock-paper-scissors)
  2. Unconvert (Producer, 75% chance)
  3. Prank (Prophet, immediate)
  4. Kill (Admirer, non-convert only)
  5. Walk away

👤 Choose action (1-5): 4

📢 Notifications:
  [ADMIRER] raised love to 1.0/10
  John has been eliminated.

--- TURN 1 END ---
```

## Configuration Extension

### Adding New NPC

Edit `data/game_configuration.json`:

```json
"new_npc": {
  "id": "new_npc",
  "name": "New NPC",
  "role": "generic",
  "convertedStatus": false,
  "interactionTree": {
    "root": {
      "text": "New NPC appears...",
      "options": [
        {
          "id": "action_id",
          "text": "Action description",
          "requires": { "role": "admirer" },
          "effects": [
            { "meter": "love", "delta": 1, "clampMax": 10 },
            { "notification": "You interacted with New NPC!" }
          ]
        }
      ]
    }
  }
}
```

### Adjusting Difficulty

**Win Conditions**: Edit `gameRules.winConditions`
```json
"admirer": {
  "primary": { 
    "requirement": { "meter": "love", "minValue": 5 }
  }
}
```

**Meter Values**: Edit role meter config
```json
"meters": {
  "chaos": {
    "max": 15,
    "start": 0
  }
}
```

**Action Success Rates**: Edit power resolution
```json
"resolution": {
  "type": "chance",
  "successChance": 0.6
}
```

## System Design Principles

✅ **Single Source of Truth**: All game config in JSON
✅ **Turn-Based**: Clear sequential play
✅ **Role Clarity**: Each player has distinct options
✅ **Stat Tracking**: Meters show progress visually
✅ **Extensibility**: Easy to add NPCs, actions, effects
✅ **Demo-Friendly**: Text-only, no graphics needed
✅ **C# Native**: Compiled, fast, type-safe

## Project Structure

```
fatal-attraction/
├── FatalAttraction.csproj       # Project file
├── GameEngine.cs                # Game logic & state
├── ChatClient.cs                # Interactive CLI
├── data/
│   └── game_configuration.json   # Single source of truth
├── README.md                     # This file
└── .git/                         # Version control
```

## Dependencies

- **Newtonsoft.Json** (13.0.3): For JSON parsing and manipulation
- **.NET 6.0 SDK**: Runtime and build tools

## Future Enhancements

- [ ] Editorial Attention focus selection for Producer
- [ ] Trap system for Prophet (delayed effects)
- [ ] Report mechanic (Producer reports Admirer)
- [ ] Conversion immunity tracking
- [ ] Multi-game statistics
- [ ] Web UI integration (ASP.NET Core)
- [ ] Networking for remote play
