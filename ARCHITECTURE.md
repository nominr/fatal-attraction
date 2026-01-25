# Fatal Attraction - Architecture

## System Overview

```
┌──────────────────────────────────────────────────────────────┐
│                    PRESENTATION LAYER                         │
├──────────────────────────────────────────────────────────────┤
│  GameWorld.cs (Node2D)                                        │
│  - Spawns NPCs and players                                    │
│  - Manages UI layer (HUD, notifications, interaction panel)   │
│  - Handles networking RPCs                                    │
│                                                               │
│  PlayerController.cs (CharacterBody2D)                        │
│  - WASD movement, in "players" group                          │
│                                                               │
│  NPCEntity.cs (Area2D)                                        │
│  - Clickable, proximity detection                             │
│  - Visual state (alive, converted, married)                   │
│                                                               │
│  InteractionPanel.cs (PanelContainer)                         │
│  - Shows role-filtered action buttons                         │
└──────────────────────────┬───────────────────────────────────┘
						   │ RPCs
						   ▼
┌──────────────────────────────────────────────────────────────┐
│                    NETWORKING LAYER                           │
├──────────────────────────────────────────────────────────────┤
│  NetworkManager.cs (Autoload)                                 │
│  - ENet multiplayer peer                                      │
│  - Player registration and role assignment                    │
│  - Connection/disconnection handling                          │
└──────────────────────────┬───────────────────────────────────┘
						   │
						   ▼
┌──────────────────────────────────────────────────────────────┐
│                    GAME LOGIC LAYER                           │
├──────────────────────────────────────────────────────────────┤
│  GameEngine.cs (Pure C#, server-only)                         │
│  ├─ GameState: Players, NPCs, meters, notifications          │
│  ├─ InteractionResolver: Action validation & execution        │
│  └─ Win condition checking                                    │
│                                                               │
│  game_configuration.json                                      │
│  - NPC interaction trees                                      │
│  - Role definitions and powers                                │
│  - Meters and win conditions                                  │
└──────────────────────────────────────────────────────────────┘
```

## Network Flow

```
Client clicks NPC
	   │
	   ▼
InteractionPanel shows actions (from cached state)
	   │
	   ▼
Client clicks action button
	   │
	   ▼
RPC: SubmitAction(npcId, actionId) → Server
	   │
	   ▼
Server: GameEngine.PerformAction()
	   ├─ Revalidates preconditions (race condition prevention)
	   ├─ Rolls resolution (chance/RPS)
	   └─ Applies effects to state
	   │
	   ▼
Server broadcasts updated state to all clients
	   │
	   ▼
All clients update UI and NPC visuals
```

## Key Classes

### NPC (GameEngine.cs)
```csharp
public class NPC
{
	public string Id;
	public string Name;
	public bool Alive = true;
	public bool Converted = false;
	public bool Married = false;
	public bool IsLoveInterest = false;
	public bool IsTarget = false;
}
```

### Action Validation (InteractionResolver)

Actions are validated TWICE:
1. **UI filtering**: `GetNPCInteractions()` - filters what buttons to show
2. **Execution**: `ResolveInteraction()` - revalidates before applying effects

Checks performed:
- NPC alive (dead NPCs blocked)
- NPC converted status (some actions require specific state)
- NPC married status (prevent duplicate marriages)
- Player meter requirements (e.g., love >= 10 for marry)

## File Structure

```
fatal-attraction/
├── GameEngine.cs              # Core logic (server-authoritative)
├── data/
│   └── game_configuration.json
├── scenes/
│   ├── Lobby.tscn             # Entry point
│   └── GameWorld.tscn         # Spatial game world
├── scripts/
│   ├── Lobby.cs               # Host/Join UI
│   ├── NetworkManager.cs      # Multiplayer autoload
│   ├── GameWorld.cs           # World integration
│   ├── NPCEntity.cs           # Clickable NPCs
│   ├── PlayerController.cs    # Player movement
│   └── InteractionPanel.cs    # Action buttons
└── README.md
```
