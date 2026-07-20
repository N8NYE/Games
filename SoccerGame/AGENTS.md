# AGENTS.md

## Project Overview
- Project name: Socker
- Genre: Sports
- Target platform: Desktop
- Current phase:
- Core play loop:

## Primary Goals
- Deliver a fun and polished player experience.
- Keep the codebase maintainable and extensible.
- Optimize for performance and stability.
- Make gameplay systems easy to iterate on.

## Tech Stack
- Engine:
- Language: C#
- Frameworks/libraries:
- Package manager:
- Asset pipeline:
- Testing tools:
- Should be docker containerized.

## Project Structure
- src/ or scripts/ - gameplay logic
- entities/ - game objects and actors
- systems/ - core systems such as input, UI, audio, save/load
- data/ - config, balance data, JSON, definitions
- assets/ - art, audio, levels, prefabs
- tests/ - automated tests
- docs/ - design notes, architecture notes, production docs

## Development Principles
- Prefer simple, readable systems over clever abstractions.
- Keep gameplay logic data-driven where possible.
- Avoid hard-coded values; use config files or constants.
- Separate gameplay rules from presentation and UI.
- Favor composition over deep inheritance chains.
- Keep state management explicit and predictable.
- Make systems easy to debug and inspect.

## Game Design Rules
- Every feature should support the core loop.
- Reuse systems instead of duplicating logic.
- Keep controls intuitive and consistent.
- Maintain clear win/loss/level progression rules.
- Prioritize player feedback for actions, collisions, damage, and rewards.
- Avoid adding features that do not improve gameplay or polish.

## Architecture Guidance
- Use a clear separation between:
  - game state
  - gameplay systems: keep libraries to a minimum.  Try to write any engine or key game components yourself to minimize reliance on other technology outside of the core language
  - UI
  - input
  - persistence
  - rendering/audio
- Keep dependencies directional and minimal.
- Use events, interfaces, or component-based patterns where appropriate.
- Avoid tightly coupling gameplay code to rendering code.

## Coding Standards
- Use descriptive names for systems, scripts, and variables.
- Keep functions focused and small.
- Add comments only where intent is unclear.
- Prefer type safety and validation.
- Handle edge cases gracefully.
- Log errors clearly and avoid silent failures.

## Feature Workflow
1. Define the feature and its player impact.
2. Add or update the relevant data/config.
3. Implement gameplay logic.
4. Connect UI and feedback.
5. Add testing and validation.
6. Playtest and refine.

## Testing Expectations
- Add tests for gameplay rules, state transitions, and data parsing.
- Validate edge cases and regressions.
- Test on target platforms whenever possible.
- Verify that changes do not break save/load or progression.

## Performance Guidelines
- Keep update loops efficient.
- Avoid unnecessary allocations during gameplay.
- Optimize hot paths, especially physics, AI, and rendering.
- Profile before optimizing.
- Maintain stable frame rate and memory usage targets.

## Content and Asset Workflow
- Keep asset naming consistent.
- Version important assets and level data.
- Document scene structure and dependencies.
- Avoid broken references and missing files.
- Keep content production organized by feature or area.

## Build and Run
- Install dependencies:
- Run game locally: dotnet run --project SockerGame.Client
- Use the command dotnet run --project SockerGame.Client when I say "run game"
- Run tests:
- Build for target platform:

## Known Constraints
- Platform limitations:
- Performance targets:
- Scope limits:
- Known technical debt:

## Priority Order
1. Core gameplay loop
2. Player feedback and polish
3. Stability and bug fixing
4. Content and progression
5. Performance optimization
6. Extra features

## Final Reminder
- Build the game as a complete experience, not just isolated systems.
- Keep the player experience at the center of every decision.
- When unsure, choose the simpler and more reliable solution or ask me how to procede if the change is reasonably large and impactful.

## Game Description and requirements
- You are building a standalone application that should be supported on all major OS's.  Linux, IOS, Windows.
- The game is a multi player game and should support one user hosting up to 30 players in a single lobby.
- The game is a soccer game and should follow all of the same rules as soccer.  Players will be in control of 1 of the 11 players on a team with 2 teams on the field per normal soccer rules.  The time scale will be such that a full match takes only 20 minutes instead of 90.  The time will be displayed such that 90 minutes is used, the time displayed just moves faster.
- When launching the game, users will be asked to join or host a lobby.  The lobby will have a name and be password protected.  We will not be incharge of any central server, the game should be hosted by a single individual with players able to connect to that user's server.  
- In the lobby users will be able to generate a jersey, which they can create by drawing on the screen, like pixel art. The players on that team will all share the jersey.  In the lobby players will also claim their positions: defence, midfield, offense, goalie, or substitute. Both team jerseys should be displayed and update as changes are made to them.  Only players assigned x team can modify x jersey.  The team y jersey will be view only. Users will also be able to assign looks to their player, like hair cut, eye color, facial hair, etc.  The build for this game should contain sprite graphics that look human.  I should be able to see my player while changing their look. 
- The art style will be top down 2-D animation.  There should be animations for users when they perform certain key strokes.  For example, bicycle kick, header, any typical soccer move.  
- Once the game starts, there will be a coin toss step, where users will pick what side of the field they want to be on and whether or not they want the ball, similar to soccer rules.  After that the game will begin and players will be in their correct starting positions, just to help.  Players will be able to freely move around completely, so they must be smart enough to stay in their position.  Substitutes' characters will be placed on the side of the field if there are any.  One player on the team will be considered "coach" and can call for substitution.  If substitution is called, a screen should pop up that shows which player should come off the field and which player should be entered.  It might be easier to just include all of the players on the field mapped to their position incase players need to switch positions.  The ref on the field will then allow that player to enter the field, like in real soccer. The substitutes will still be able to enter the field at free will be will risk receiving a card like real rules of soccer dictate.  
- The game should have voice activated chat so that players can freely communicate.  Offer the option to increase or decrease the volume of both teams so that players can hear their team better.  
- For graphical inspiration, you can look at games like slapshot rebound or netsoccer2.  I would prefer the movement of slapshot remound, but like the lofi quality of netsoccer.  The players should be human, the ball should look like a soccer ball. The soccer field should look like a soccerfield with all of the markings as we will be following all soccer rules according to FIFA rules of the game.  There should also be a center ref and line judges that move with the second last defender to check for offside, like a real referee would do in real life.  
- In the lobby, players will be given 100 points that can be applied to various traits that will make players behave differently.  Speed, shot strength, aggressiveness, and any other soccer qualities that you might think are good.  There should only be 10 categories total. Users should start with all stats set to 75, and the extra 100 points can be applied however.  The sliders should each from 0 to 100.  These stats should impact things like a player's speed and kick power.  Map the stats to characteristics that can be fealt in the game.  
- The lobby should be built before allowing me to pick my team/player/stats.  Once I am in a lobby then I can do all the team related things.  I should be able to share my server url with other players to allow them to connect. 
- The controls should be WASD to move but player direction is determined by how the mouse is oriented relative to that player.  When a player is by the ball, they should be able to left click to 'kick' with their left foot and right click to 'kick' with their right foot.  So you have to spam left/right to dribble with the ball.  Passing/shooting would be done by holding down one of those buttons.  Slide tackling can be done by holding shift while moving near a player.  Slide tackling will knock a player down, dependng on if the wall was hit first, they can receive a card like the rules of soccer dictate.  
- There should always be 11 v 11.  Any NPC should have ai movement.  The ai should play their position and work with other players to pass the ball.  
- The game should be full screen.  The score should be located in the upper right corner.  The box displaying the score should look similar to how they do it in world cup, where the team's uniform color matches the background of the team's name listed.  The scores for each team should be in the box next to the team's name.  Substitutes should be placed around a team bench that is located just off the bottom of the field.  The field and gameplay should be left to right.
- Players should always have the ability to walk around the field using WASD controls.
