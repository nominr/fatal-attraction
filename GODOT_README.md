# Fatal Attraction - Godot Implementation

## Quick Start

1. Open the project in Godot 4.x
2. Press F5 or click "Run Project"
3. Read player goals, then click "START GAME"
4. Take turns choosing actions for each role

## What's New

### ✅ Implemented Features

**Before Game Start**
- Full goals display for all 3 roles (Admirer, Prophet, Producer)
- Shows powers, win conditions, and objectives
- Clear "START GAME" button to begin

**Multiple NPCs Per Turn**
- 3 NPCs appear each turn (up from 1)
- Each NPC shows their status (Love Interest, Target, Converted)
- All 3 can be interacted with before turn ends

**Enhanced Actions**
- Every role has 3+ relevant actions per NPC
- Multiple ways to increase meters for each role
- "Walk away" is just one option among many

**Admirer Enhancements**
- ❤️ Katy marked as LOVE INTEREST
- 🎯 John and Rebecca marked as TARGETS
- Kill action only works on targets
- Multiple ways to raise love meter (talk, flirt, charm)

**Prophet Features**
- 🔄 NPC conversion status shown visually
- Notifications show "CONVERTED" / "UNCONVERTED" status
- Multiple chaos-raising options (convert, prank, trap, manipulate)

**Producer Systems**
- 📺 Editorial Attention panel appears on Producer turns
- Can focus on: Sudden deaths, Romantic escalations, Chaos spikes, Public areas
- Multiple ratings-raising options (marry, interview, cleanup)
- Cleanup action reduces chaos AND raises ratings

**Game Flow**
- 15 turns total (up from 10)
- Visual progress bars for all meters
- Real-time notifications panel
- Win conditions trigger automatically
- Game over dialog shows final scores

## File Structure

```
fatal-attraction/
├── scenes/
│   └── MainGame.tscn              # Main game scene
├── scripts/
│   ├── GameEngine.gd              # Core game logic (~450 lines)
│   └── MainGame.gd                # UI controller (~350 lines)
├── data/
│   └── game_configuration.json     # Enhanced config with targets
└── project.godot                   # Godot project file
```

## Gameplay

### Turn Structure

1. **Goal Screen** (first time only)
   - Shows all 3 player roles
   - Lists powers and win conditions
   - Click "START GAME" to begin

2. **Each Turn**
   - Current player and turn number shown at top
   - Meters displayed with progress bars
   - 3 NPCs appear with status icons:
     - ❤️ = Love Interest (Katy)
     - 🎯 = Target (John, Rebecca)
     - 🔄 = Converted by Prophet
   
3. **Producer Special**
   - Editorial Attention panel pops up
   - Choose focus area (affects detection, future feature)
   - Panel closes after selection

4. **Actions**
   - Each NPC shows role-specific actions
   - Click button to perform action
   - Chance/RPS rolls happen automatically
   - Meters update in real-time
   - Notifications appear in bottom-right panel

5. **Turn End**
   - All 3 roles go per round
   - Game auto-advances to next turn
   - Win conditions checked after each action

### Win Conditions

**Admirer**: Love meter reaches 8/10
**Prophet**: Chaos meter reaches 9/10
**Producer**: Ratings meter reaches 8/10

### Action Examples Per Role

**Admirer Actions:**
- Talk to Katy → +1 Love
- Flirt with Katy → +2 Love
- Kill John (Target) → Eliminates rival
- Kill Rebecca (Target) → Eliminates rival
- Charm others → +1 Love

**Prophet Actions:**
- Convert NPC (RPS) → +1 Chaos, converts NPC
- Prank NPC → +1 Chaos
- Manipulate → +2 Chaos
- Set trap → +1 Chaos

**Producer Actions:**
- Marry NPC → +1-2 Ratings
- Interview NPC → +1 Ratings
- Cleanup → -1 Chaos, +1 Ratings
- Unconvert (75% chance) → Removes conversion

## JSON Configuration

### NPC Structure

```json
"katy": {
  "id": "katy",
  "name": "Katy",
  "role": "love_interest",
  "isTarget": false,
  "convertedStatus": false,
  "interactionTree": {
    "root": {
      "text": "Katy walks up to you...",
      "options": [
        {
          "id": "talk",
          "text": "Talk to Katy",
          "requires": { "role": "admirer" },
          "effects": [
            { "meter": "love", "delta": 1 }
          ]
        }
      ]
    }
  }
}
```

### Game Rules

```json
"gameRules": {
  "turnsPerGame": 15,
  "npcsPerTurn": 3,
  "winConditions": {
    "admirer": {
      "primary": {
        "requirement": { "meter": "love", "minValue": 8 }
      }
    }
  }
}
```

## Extending the Game

### Add New NPC

1. Edit `data/game_configuration.json`
2. Add NPC to `npcs` section with interaction tree
3. Add ID to `npcPool` in `demoClient`
4. Set `isTarget` or `role: "love_interest"` as needed

### Add New Action

1. Add option to NPC's `interactionTree.root.options`
2. Set `requires` for role restriction
3. Set `resolution` type (chance, rps, immediate)
4. Define `effects` or `effects_on_success`/`effects_on_failure`

### Modify Meters

Edit role config:
```json
"meters": {
  "love": {
    "min": 0,
    "max": 10,
    "start": 0
  }
}
```

### Change Win Conditions

Edit `gameRules.winConditions`:
```json
"admirer": {
  "primary": {
    "requirement": { "meter": "love", "minValue": 5 }
  }
}
```

## Signals & Events

The GameEngine emits signals for UI updates:

- `notification_added(message: String)` - New notification
- `meter_updated(role, meter, value, max)` - Meter changed
- `turn_started(turn, role)` - Turn begins
- `turn_ended(turn)` - Turn ends
- `game_state_changed()` - State updated

## Code Structure

### GameEngine.gd

**Classes:**
- `Meter` - Tracks stat values (Love, Chaos, Ratings)
- `NPC` - NPC data and status
- `PlayerState` - Player meters and history

**Key Methods:**
- `load_config()` - Loads JSON configuration
- `initialize_game()` - Sets up players and NPCs
- `get_available_actions()` - Filters actions by role
- `perform_action()` - Resolves action and applies effects
- `check_win_condition()` - Returns win message if met

### MainGame.gd

**Key Methods:**
- `show_all_goals()` - Displays goal screen
- `start_new_turn()` - Begins turn, gets 3 NPCs
- `display_npcs()` - Creates NPC panels with actions
- `show_editorial_attention()` - Producer focus UI
- `_on_action_pressed()` - Handles action clicks
- `show_game_over()` - End screen with scores

## Testing Checklist

- [ ] Goals display correctly on start
- [ ] All 3 roles show their powers and win conditions
- [ ] 3 NPCs appear each turn
- [ ] Katy shows ❤️ Love Interest icon
- [ ] John & Rebecca show 🎯 Target icons
- [ ] Each role has 3+ actions per NPC
- [ ] At least one action increases meters (not just "walk away")
- [ ] Admirer can kill targets (John, Rebecca) when non-converted
- [ ] Prophet conversions show 🔄 status change
- [ ] Producer gets Editorial Attention panel
- [ ] Meters update with progress bars
- [ ] Notifications appear in real-time
- [ ] Win condition triggers at correct values
- [ ] 15 turns play out completely

## Known Issues / Future

- [ ] Editorial Attention doesn't affect gameplay yet (just UI)
- [ ] Trap system not fully implemented (shows immediate effect)
- [ ] Report Admirer mechanic not implemented
- [ ] No save/load functionality
- [ ] No AI opponents
- [ ] Single device only (no networking)

## Performance

- Loads entire JSON config at start (~1KB)
- Instantiates UI nodes dynamically per turn
- No persistent storage between runs
- Suitable for 1-3 players on same device

---

**Ready to play?** Press F5 in Godot!
