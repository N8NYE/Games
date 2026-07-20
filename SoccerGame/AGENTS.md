# AGENTS.md

## Project Overview
- **Project name:** Socker (SockerGame)
- **Genre:** Sports (Soccer/Association Football)
- **Target platform:** Cross-platform Desktop (Windows, Linux, macOS)
- **Current phase:** Active Development
- **Core play loop:** Host/join lobby → Customize team/appearance/stats → Start match → Play 11v11 soccer → Score goals → Win

## Primary Goals
- Deliver a fun and polished player experience with authentic soccer gameplay
- Keep the codebase maintainable and extensible with clear separation of concerns
- Optimize for performance and stability with 60 FPS target
- Make gameplay systems easy to iterate on with data-driven design
- Support up to 30 players per lobby with P2P hosting model

## Tech Stack
- **Language:** C# (.NET 9.0)
- **Rendering:** Raylib-cs 8.0 (top-down 2D graphics)
- **Networking:** TCP sockets with JSON serialization (no external networking library)
- **Package manager:** NuGet
- **Build system:** MSBuild / dotnet CLI
- **Container:** Docker support planned but not yet implemented

## Project Structure
```
SockerGame/
├── SockerGame.Client/          # Main executable, rendering, input handling
│   ├── src/
│   │   └── Program.cs          # Game loop, UI, drawing
│   ├── assets/
│   │   └── icon.ico
│   └── SockerGame.Client.csproj
├── SockerGame.Core/            # Shared models, enums, systems
│   └── src/
│       ├── Enums/
│       │   └── GameEnums.cs    # GamePhase, PlayerPosition, TeamSide, KickDirection, ConnectionState, MatchEventType
│       ├── Models/
│       │   ├── GameState.cs      # Root game state container
│       │   ├── MatchState.cs     # Match state (scores, time, ball, players)
│       │   ├── Ball.cs           # Ball physics and state
│       │   ├── LobbyState.cs     # Lobby information
│       │   ├── Player.cs         # Player data (position, stats, appearance)
│       │   ├── PlayerStats.cs    # Player statistics system
│       │   ├── PlayerAppearance.cs # Player appearance customization
│       │   ├── Team.cs           # Team data structure
│       │   ├── JerseyDesign.cs   # Jersey pixel art design
│       │   ├── MatchEvent.cs     # Match event record for history
│       │   ├── NetworkMessage.cs # Network message wrapper
│       │   ├── NetworkMessageType.cs # Network message type enum
│       │   ├── InputState.cs     # Player input state
│       │   └── PitchDimensions.cs # Pitch constants and measurements
│       └── Systems/
│           ├── MatchEngine.cs    # Core match simulation, physics, rules
│           └── AIStrategy.cs     # Tactical AI behavior (no rendering dependencies)
├── SockerGame.Server/          # Server logic, networking
│   └── src/
│       ├── GameServer.cs         # TCP server, lobby management, broadcasting
│       ├── JoinLobbyData.cs      # Join lobby message data
│       ├── JerseyUpdateData.cs   # Jersey update message data
│       └── SubstitutionData.cs   # Substitution message data
├── docs/
│   └── AIStrategyRedesign.md     # AI technical documentation
└── SockerGame.sln              # Visual Studio solution
```

## Development Principles
- Prefer simple, readable systems over clever abstractions
- Keep gameplay logic data-driven where possible
- Avoid hard-coded values; use constants defined in PitchDimensions class
- Separate gameplay rules from presentation and UI
- Favor composition over deep inheritance chains
- Keep state management explicit and predictable
- Make systems easy to debug and inspect
- **One class per file** for better organization and maintainability

## Game Design Rules
- Every feature should support the core soccer loop
- Reuse systems instead of duplicating logic
- Keep controls intuitive and consistent (WASD + Mouse)
- Maintain authentic FIFA rules: offside, throw-ins, goal kicks, substitutions
- Prioritize player feedback for actions, collisions, and goals
- Avoid adding features that do not improve gameplay or polish

## Architecture Guidance
Three distinct layers with minimal dependencies:

### Client Layer (`SockerGame.Client`)
- Rendering: Raylib drawing calls, camera management
- UI: Menus, dialogs, HUD, customization screens
- Input: Keyboard/mouse handling, input buffering
- Network client: TCP connection to host

### Core Layer (`SockerGame.Core`)
- **Models:** All serializable data structures (one class per file)
- **MatchEngine:** Core simulation, ball physics, collision, rules
- **AIStrategy:** Tactical AI behavior (no rendering dependencies)
- Pure logic that can run on host or be used for prediction

### Server Layer (`SockerGame.Server`)
- TCP lobby server, handles up to 30 connections
- Lobby management, player auth, jersey/team assignment
- Broadcasts state updates to all clients
- Runs match simulation when game starts

## Coding Standards
- Use descriptive names for systems, scripts, and variables
- Keep functions focused and small (under 50 lines where possible)
- Add comments only where intent is unclear (AIStrategy well-documented)
- Prefer type safety with nullable reference types enabled
- Handle edge cases gracefully (check for null team/player)
- Log errors clearly via OnLog event
- **One public class per file** - each .cs file contains a single public type

## Controls Reference
| Key | Action |
|-----|--------|
| WASD | Move player |
| Mouse | Aim direction (player faces cursor) |
| Left Click (hold) | Charge ground kick (shoot/pass) |
| Right Click (hold) | Charge aerial kick (header/bicycle kick effect) |
| Shift | Sprint (with aggression risk on tackle) |
| Space | Slide tackle |
| Enter | Toggle text chat |
| Page Up/Down | Increase/decrease team voice volume |
| Home/End | Increase/decrease opponent voice volume |
| F11 | Toggle fullscreen |
| Escape | Pause menu / Back |
| U | Open substitution menu (coach only) |

## Player Stats (10 Categories)
All stats start at **75**, players get **100 points** to distribute:
1. **Speed** - Movement velocity multiplier
2. **ShotStrength** - Shooting power (used for shots and passes)
3. **Passing** - Pass accuracy and power
4. **Dribbling** - Ball control (affects kick cooldown)
5. **Defense** - Tackle success rate, marking ability
6. **Stamina** - Movement speed modifier
7. **Aggression** - Tackle frequency, card risk, knockdown duration
8. **Jumping** - Aerial challenge ability (not yet fully implemented)
9. **Accuracy** - Kick aim precision
10. **Reflexes** - Goalkeeper save ability (not yet fully implemented)

## Match Rules (FIFA Compliant)
- **Match duration:** 90 in-game minutes (mapped to 20 real minutes)
- **Teams:** 11v11 (AI fills missing players)
- **Substitutes:** 4 per team, coach-managed substitutions
- **Offside:** Detected when player receives ball past second-last defender
- **Throw-ins:** Awarded to opposing team when ball crosses goal line
- **Goal kicks:** Awarded for out-of-bounds on defensive end
- **Slide tackle:** Knockdown effect, card risk based on aggression
- **Cards:** Yellow card on aggressive tackles, no second yellow yet

## Architecture Patterns Used
- **Entity Component:** Player has Stats, Appearance, Position components
- **State machine:** GamePhase enum manages main menu, lobby, match flow
- **Event-driven networking:** NetworkMessage types with JSON payloads
- **Strategy pattern:** AIStrategy separates tactical decisions from simulation

## Feature Workflow
1. Define the feature and its player impact
2. Add or update relevant data/config in appropriate Model file
3. Implement core logic in MatchEngine.cs or AIStrategy.cs
4. Connect UI in Program.cs Draw/Update methods
5. Test with `dotnet run --project SockerGame.Client`
6. Playtest and refine

## Build and Run
```bash
# Install dependencies (already configured in csproj)
dotnet restore

# Run game locally
dotnet run --project SockerGame.Client

# Build for release
dotnet publish -c Release -r win-x64 --self-contained true
dotnet publish -c Release -r linux-x64 --self-contained true
dotnet publish -c Release -r osx-x64 --self-contained true

# Run tests (not yet implemented)
dotnet test
```

## Known Constraints
- **Platform limitations:** Desktop only (no mobile/web)
- **Performance targets:** 60 FPS at 1280x720 fullscreen
- **Network model:** P2P hosting (host runs server + simulation)
- **Physics:** Simplified 2D - no spin, wind, or complex ball trajectory
- **Voice chat:** UI present but not implemented (stub for future)

## Known Technical Debt
- AI UpdateGameLoop method in GameServer uses placeholder AI inputs instead of proper AI integration
- Reflexes stat exists but goalkeeper diving saves not implemented
- Jumping stat exists but heading mechanics not implemented
- Corner kicks not implemented (falls back to goal kick logic)
- No penalty shootout when match ends in draw
- Chat system is local only (no network broadcast)
- Voice chat UI exists but no actual voice implementation

## Priority Order
1. Core gameplay loop (✓ implemented)
2. Player feedback and polish
3. Stability and bug fixing
4. Content and progression
5. Performance optimization
6. Extra features (voice chat, animations, advanced tactics)

## Helpful Links
- **FIFA Rules of the Game:** https://digitalhub.fifa.com/m/5371a6dcc42fbb44/original/d6g1medsi8jrrd3e4imp-pdf.pdf
- **Football Tactics:** https://en.wikipedia.org/wiki/Association_football_tactics
- **Raylib-cs Docs:** https://github.com/ChrisDill/Raylib-cs

## Match Flow
1. **MainMenu → Host/Join:** Create or connect to TCP lobby
2. **Lobby:** Select team, position, customize jersey/appearance/stats, add AI players
3. **CoinToss:** Select field side and ball possession (currently bypassed - starts at center)
4. **Kickoff:** Ball starts at center, first kick begins play
5. **FirstHalf:** 45 minutes game time, real-time scaled
6. **Halftime:** Brief pause, teams switch sides
7. **SecondHalf:** 45 minutes game time, real-time scaled
8. **FullTime:** Match ends, show final score

## Game Description and Requirements (Detailed)
- You are building a standalone application that should be supported on all major OS's: Windows, Linux, macOS
- The game is a multiplayer game supporting up to 30 players in a single lobby with P2P hosting model
- 11v11 soccer with authentic FIFA rules; time scale: 20 real minutes = 90 game minutes
- Lobby features: password protection, player jersey pixel art editing, position claims, coach designation
- Player customization: hair style/color, eye color, skin tone, facial hair (visual preview available)
- Top-down 2D art style with simple player sprites (circle body, circle head, direction indicator)