# Fatal Attraction

A networked multiplayer social deduction game built with Godot 4 and C#.

## Overview

Three players compete as **Admirer**, **Prophet**, and **Producer** in a reality TV setting. Players move through a spatial game world, approach NPCs, and perform role-specific actions to achieve their win conditions.

## Quick Start

1. Open project in Godot 4.x with .NET support
2. Run the **Lobby** scene
3. **Host**: Enter name → Click "Host" → Select role → "Start Game"
4. **Join**: Enter name + host IP → Click "Join" → Select role

## Roles

| Role | Goal | Powers |
|------|------|--------|
| **Admirer** | Marry love interest (Love 10/10) | Kill NPCs, Unconvert, Flirt |
| **Prophet** | Reach Chaos 9/10 | Convert NPCs (RPS), Prank, Manipulate |
| **Producer** | Reach Ratings 10/10 | Marry NPCs, Unconvert (75%), Cleanup |

## Controls

- **WASD / Arrow Keys** - Move player
- **Click NPC** - Open interaction panel (must be in range)
- **ESC** - Close interaction panel

## Architecture

```
Lobby.cs          →  GameWorld.cs (spatial Node2D world)
                           │
         ┌─────────────────┼─────────────────┐
         ↓                 ↓                 ↓
   PlayerController   NPCEntity        InteractionPanel
         │                 │                 │
         └────────── NetworkManager ────────┘
                           │
                     GameEngine.cs (authoritative logic)
                           │
                  game_configuration.json
```

## Key Files

| File | Purpose |
|------|---------|
| `GameEngine.cs` | Core game logic, validation, state |
| `scripts/GameWorld.cs` | Spatial world, NPC spawning, networking |
| `scripts/NPCEntity.cs` | Clickable NPC with proximity detection |
| `scripts/PlayerController.cs` | WASD movement |
| `scripts/InteractionPanel.cs` | Action buttons modal |
| `scripts/NetworkManager.cs` | Multiplayer networking |
| `data/game_configuration.json` | NPC definitions, actions, meters |

## Networking

- **Server-authoritative**: All game logic runs on host
- **Race condition prevention**: Actions are revalidated at execution time
- **State sync**: Full game state broadcast on every action

## Building

```bash
# Build C# project
dotnet build

# Run in Godot
# Open project → Run Lobby scene
```

## Configuration

All gameplay is data-driven via `data/game_configuration.json`:
- NPC interaction trees
- Role powers and meters
- Win conditions
- Success probabilities
