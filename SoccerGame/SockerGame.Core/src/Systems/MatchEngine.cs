using SockerGame.Core.Enums;
using SockerGame.Core.Models;
using System.Numerics;

namespace SockerGame.Core.Systems
{
    public class MatchEngine
    {
        private MatchState _match;
        private Dictionary<string, Team> _teams;
        private Random _random = new();
        private float _accumulator;
        private bool _matchStarted; // true after kickoff
        private Vector2 _throwInPosition;
        private bool _throwInPending;
        private TeamSide _throwInTeam;
        private string _lastTouchPlayerId = ""; // preserve last touch before it's cleared in ball physics
        private TeamSide _lastTouchTeam = TeamSide.Home;
        private string _refCall = ""; // referee call text
        private float _refCallTimer = 0; // how long to display it

        public MatchEngine(MatchState match, Dictionary<string, Team> teams)
        {
            _match = match;
            _teams = teams;
        }

        public void Update(float deltaTime, Dictionary<string, InputState> playerInputs)
        {
            if (_match.CurrentHalf == GamePhase.FullTime) return;

            // Mark match as started once ball is kicked
            if (!_matchStarted && _match.Ball.Speed > 10)
            {
                _matchStarted = true;
            }

            _accumulator += deltaTime;
            float tickRate = 1f / 60f;

            while (_accumulator >= tickRate)
            {
                Tick(tickRate, playerInputs);
                _accumulator -= tickRate;
            }
        }

        private void Tick(float dt, Dictionary<string, InputState> playerInputs)
        {
            // Only advance game time if match has started
            if (_matchStarted)
            {
                _match.RealTimeElapsed += dt;
                float gameSecondsPerRealSecond = (90f * 60f) / (20f * 60f);
                float gameSecondsElapsed = dt * gameSecondsPerRealSecond;

                _match.MatchSecond += (int)gameSecondsElapsed;
                while (_match.MatchSecond >= 60)
                {
                    _match.MatchSecond -= 60;
                    _match.MatchMinute++;
                }

                if (_match.MatchMinute >= 45 && _match.CurrentHalf == GamePhase.FirstHalf)
                {
                    _match.CurrentHalf = GamePhase.Halftime;
                    return;
                }

                if (_match.MatchMinute >= 90 && _match.CurrentHalf == GamePhase.SecondHalf)
                {
                    _match.CurrentHalf = GamePhase.FullTime;
                    return;
                }
            }

            foreach (var (teamId, players) in _match.TeamPlayers)
            {
                var team = _teams.GetValueOrDefault(teamId);
                if (team == null) continue;

                foreach (var player in players)
                {
                    if (_match.Ball.LastTouchedBy?.Id == player.Id)
                        player.HasBall = CheckBallPossession(player);

                    if (player.IsHuman && playerInputs.ContainsKey(player.Id))
                    {
                        HandleHumanInput(player, playerInputs[player.Id], dt);
                    }
                    else if (!player.IsHuman && !player.IsSubstitute)
                    {
                        UpdateAIPlayer(player, dt, team);
                    }

                    UpdatePlayerPhysics(player, dt);
                }
            }

            UpdateBallPhysics(dt);
            CheckForGoal();
            CheckForThrowIn();
            CheckOffside();
            UpdatePossession();
        }

        private bool CheckBallPossession(Player player)
        {
            float dx = _match.Ball.X - player.X;
            float dy = _match.Ball.Y - player.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            return dist < PitchDimensions.PlayerRadius + PitchDimensions.BallRadius;
        }

        private void HandleHumanInput(Player player, InputState input, float dt)
        {
            float baseMoveSpeed = 100f + (player.Stats.Speed / 100f) * 100f;
            float staminaMod = 0.5f + (player.Stats.Stamina / 100f) * 0.5f;
            float sprintMultiplier = input.ShiftHeld ? 2.5f : 1.0f;
            float finalSpeed = baseMoveSpeed * staminaMod * sprintMultiplier;

            if (input.MoveX != 0 || input.MoveY != 0)
            {
                float len = MathF.Sqrt(input.MoveX * input.MoveX + input.MoveY * input.MoveY);
                if (len > 0) { input.MoveX /= len; input.MoveY /= len; }
                player.Speed = finalSpeed;
            }
            else
            {
                player.Speed = 0;
            }

            float mdx = input.MouseX - player.X;
            float mdy = input.MouseY - player.Y;
            player.Direction = MathF.Atan2(mdy, mdx);

            player.TargetX = player.X + input.MoveX * player.Speed * dt;
            player.TargetY = player.Y + input.MoveY * player.Speed * dt;

            // Check if player is near the ball
            float ballDx = _match.Ball.X - player.X;
            float ballDy = _match.Ball.Y - player.Y;
            float ballDist = MathF.Sqrt(ballDx * ballDx + ballDy * ballDy);
            const float kickRange = 30f; // how close player must be to kick the ball
            bool nearBall = ballDist < kickRange;

            // KICK MECHANIC: Charge on hold, kick on release
            // Left click: ground kick - when ball hits player, it stops automatically
            // THROW-IN: When ball is out of bounds, allow any player from the team awarded the throw-in
            // to kick the ball back into play. Treat like an aerial kick.
            // SOCCER RULE: The team that DID NOT touch the ball last gets the throw-in
            bool isThrowInContext = _throwInPending && player.Team == _throwInTeam;
            
            if (input.LeftClickHeld && nearBall && (!_throwInPending || isThrowInContext))
            {
                // Charge up the kick
                input.LeftChargeTime += dt;
                if (input.LeftChargeTime > 2.0f) input.LeftChargeTime = 2.0f; // Max 2 seconds charge
            }
            // Detect release: LeftClickHeld was true, now it's false, and we had charge time
            else if (!input.LeftClickHeld && input.LeftChargeTime > 0 && nearBall && (!_throwInPending || isThrowInContext))
            {
                // Release = ground kick with charge multiplier
                float chargeRatio = input.LeftChargeTime / 2.0f; // 0 to 1 based on charge time
                float chargeMultiplier = 0.5f + chargeRatio * 1.5f; // 0.5x to 2.0x power - more pronounced effect
                bool isThrowIn = _throwInPending;
                PerformKick(player, KickDirection.Left, input, chargeMultiplier, isAerial: isThrowIn);
                if (isThrowIn) _throwInPending = false;
                input.LeftChargeTime = 0;
            }
            else if (!input.LeftClickHeld)
            {
                // No click happening, reset charge
                input.LeftChargeTime = 0;
            }
            
            // Right click: aerial kick - ball goes in the air, grows significantly, then shrinks as it falls
            if (input.RightClickHeld && nearBall && (!_throwInPending || isThrowInContext))
            {
                input.RightChargeTime += dt;
                if (input.RightChargeTime > 2.0f) input.RightChargeTime = 2.0f;
            }
            else if (!input.RightClickHeld && input.RightChargeTime > 0 && nearBall && (!_throwInPending || isThrowInContext))
            {
                float chargeRatio = input.RightChargeTime / 2.0f;
                float chargeMultiplier = 0.5f + chargeRatio * 1.5f;
                bool isThrowIn = _throwInPending;
                PerformKick(player, KickDirection.Right, input, chargeMultiplier, isAerial: true); // Right click = aerial kick
                if (isThrowIn) _throwInPending = false;
                input.RightChargeTime = 0;
            }
            else if (!input.RightClickHeld)
            {
                input.RightChargeTime = 0;
            }

            float tackleRange = 20f + (player.Stats.Aggression / 100f) * 40f;
            float tackleSuccess = 0.2f + (player.Stats.Defense / 100f) * 0.6f;
            if (input.SpaceHeld && player.Speed > 50) PerformSlideTackle(player, tackleRange, tackleSuccess);

            // Ball is a free element - only influence it if close and clicking
            // No longer latching the ball to the player. The ball rolls freely with physics.
            // Player proximity to the ball allows kicking interaction, but ball moves independently.
        }

        private void PerformKick(Player player, KickDirection direction, InputState input, float chargeMultiplier = 1.0f, bool isAerial = false)
        {
            float basePower = 150f + (player.Stats.ShotStrength / 100f) * 600f;
            float accuracy = player.Stats.Accuracy / 100f;

            float goalDist = MathF.Min(MathF.Abs(_match.Ball.X - 0), MathF.Abs(_match.Ball.X - PitchDimensions.Width));
            bool isShot = goalDist < 200f;
            float power = isShot ? basePower : (120f + (player.Stats.Passing / 100f) * 400f);
            power *= chargeMultiplier;

            float targetAngle = player.Direction;
            float angleVariation = (1f - accuracy) * (isShot ? 0.25f : 0.15f);
            targetAngle += (float)(_random.NextDouble() - 0.5) * angleVariation;

            _match.Ball.VelocityX = MathF.Cos(targetAngle) * power;
            _match.Ball.VelocityY = MathF.Sin(targetAngle) * power;
            _match.Ball.Speed = power;
            _match.Ball.LastTouchedBy = player;
            _match.Ball.IsAerial = isAerial;
            
            // For aerial kicks, add vertical component with ramp visual effect
            if (isAerial)
            {
                _match.Ball.ZPosition = 60f; // ball starts 60 pixels high (max height)
                _match.Ball.VisualScale = 1.0f; // Start at normal size, will grow as it falls (ramp effect)
            }
            else
            {
                _match.Ball.ZPosition = 0;
                _match.Ball.VisualScale = 1.0f;
            }
            
            player.HasBall = false;
            
            // Set kick cooldown to prevent immediate re-possession
            _match.Ball.KickCooldown = 0.5f;
            
            // Reset charge time after kick
            input.LeftChargeTime = 0;
            input.RightChargeTime = 0;
        }

        private void PerformSlideTackle(Player player, float tackleRange, float tackleSuccess)
        {
            player.IsKnockedDown = true;
            player.KnockdownTimer = 1.5f;

            foreach (var (_, players) in _match.TeamPlayers)
            {
                foreach (var other in players)
                {
                    if (other.Team == player.Team || !other.HasBall) continue;

                    float dx = other.X - player.X;
                    float dy = other.Y - player.Y;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);

                    if (dist < tackleRange)
                    {
                        if (_random.NextDouble() < tackleSuccess)
                        {
                            other.IsKnockedDown = true;
                            other.KnockdownTimer = 1.0f;
                            other.HasBall = false;

                            bool hitBallFirst = _random.NextDouble() < (0.3f + player.Stats.Defense / 100f * 0.3f);
                            if (!hitBallFirst)
                            {
                                float cardChance = 0.1f + (player.Stats.Aggression / 100f) * 0.3f;
                                if (_random.NextDouble() < cardChance)
                                {
                                    player.Cards = 1;
                                    _match.Events.Add(new MatchEvent());
                                }
                            }
                        }
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Updated AI player using the AIStrategy class for tactical behavior
        /// </summary>
        public void UpdateAIPlayer(Player player, float dt, Team team)
        {
            // Use the new AIStrategy system for better tactical behavior
            AIStrategy.UpdateAIPlayer(player, dt, _match, _teams, _match.TeamPlayers);
        }


        private (float, float) GetFormationPosition(Player player, Team team)
        {
            // Get all players of same position on this team to calculate spread
            var samePosition = _match.TeamPlayers[team.Id]
                .Where(p => p.Position == player.Position && !p.IsSubstitute)
                .ToList();
            int count = samePosition.Count;
            int index = samePosition.IndexOf(player);
            if (index < 0) index = 0;

            if (player.Position == PlayerPosition.Goalkeeper)
            {
                // Goalkeeper on goal line
                float gkX = team.Side == TeamSide.Home ? 30f : PitchDimensions.Width - 30f;
                return (gkX, PitchDimensions.CenterY);
            }

            if (player.Position == PlayerPosition.Defender)
            {
                // Home defenders: X around 20% of field width (on home side, defending left goal)
                // Away defenders: X around 80% of field width (on away side, defending right goal)
                float defX = team.Side == TeamSide.Home ? PitchDimensions.Width * 0.2f : PitchDimensions.Width * 0.8f;
                float spread = count > 1 ? (index - (count - 1) / 2f) * (PitchDimensions.Height * 0.35f / Math.Max(1, count - 1)) : 0;
                return (defX, PitchDimensions.CenterY + spread);
            }

            if (player.Position == PlayerPosition.Midfielder)
            {
                // Central midfield - positioned to support both defense and attack
                float midX = team.Side == TeamSide.Home ? PitchDimensions.Width * 0.45f : PitchDimensions.Width * 0.55f;
                float spread = count > 1 ? (index - (count - 1) / 2f) * (PitchDimensions.Height * 0.45f / Math.Max(1, count - 1)) : 0;
                return (midX, PitchDimensions.CenterY + spread);
            }

            if (player.Position == PlayerPosition.Forward)
            {
                // Home forwards: attacking right side (toward away goal at right)
                // Away forwards: attacking left side (toward home goal at left)
                float fwdX = team.Side == TeamSide.Home ? PitchDimensions.Width * 0.7f : PitchDimensions.Width * 0.3f;
                float spread = count > 1 ? (index - (count - 1) / 2f) * (PitchDimensions.Height * 0.35f / Math.Max(1, count - 1)) : 0;
                return (fwdX, PitchDimensions.CenterY + spread);
            }

            return (PitchDimensions.CenterX, PitchDimensions.CenterY);
        }

        private void UpdatePlayerPhysics(Player player, float dt)
        {
            if (player.IsKnockedDown)
            {
                player.KnockdownTimer -= dt;
                if (player.KnockdownTimer <= 0) player.IsKnockedDown = false;
                return;
            }

            // Goalkeeper restriction - stay in penalty area
            if (player.Position == PlayerPosition.Goalkeeper)
            {
                float penaltyLeft = player.Team == TeamSide.Home ? 0 : PitchDimensions.Width - PitchDimensions.PenaltyAreaWidth;
                float penaltyRight = player.Team == TeamSide.Home ? PitchDimensions.PenaltyAreaWidth : PitchDimensions.Width;
                float penaltyTop = PitchDimensions.CenterY - PitchDimensions.PenaltyAreaHeight / 2;
                float penaltyBottom = PitchDimensions.CenterY + PitchDimensions.PenaltyAreaHeight / 2;

                // Clamp target and position to penalty area
                player.TargetX = Math.Clamp(player.TargetX, penaltyLeft + 5, penaltyRight - 5);
                player.TargetY = Math.Clamp(player.TargetY, penaltyTop + 5, penaltyBottom - 5);
            }

            float dx = player.TargetX - player.X;
            float dy = player.TargetY - player.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist > 1)
            {
                float moveAmount = player.Speed * dt;
                if (moveAmount > dist) moveAmount = dist;
                player.X += (dx / dist) * moveAmount;
                player.Y += (dy / dist) * moveAmount;
            }

            // Removed field boundary clamping - players can now leave the field
            // They can go around for throw-ins as in real soccer
        }

        private void UpdateBallPhysics(float dt)
        {
            // Decrease kick cooldown
            if (_match.Ball.KickCooldown > 0)
                _match.Ball.KickCooldown -= dt;

            if (_match.Ball.Speed > 0)
            {
                float friction = 0.98f;
                _match.Ball.VelocityX *= friction;
                _match.Ball.VelocityY *= friction;
                _match.Ball.Speed *= friction;

                _match.Ball.X += _match.Ball.VelocityX * dt;
                _match.Ball.Y += _match.Ball.VelocityY * dt;

                // Handle aerial ball physics with ramp visual effect
                if (_match.Ball.IsAerial)
                {
                    // Gravity effect on Z position - ball falls from maxHeight to ground
                    _match.Ball.ZPosition -= 150f * dt;
                    if (_match.Ball.ZPosition <= 0)
                    {
                        _match.Ball.ZPosition = 0;
                        _match.Ball.IsAerial = false;
                        _match.Ball.VisualScale = 1.0f;
                    }
                    else
                    {
                        // Ramp effect: ball grows from 1.0 to 2.0 as it falls
                        // Z starts at maxHeight (60) and decreases to 0 as ball falls
                        // VisualScale should grow from 1.0 to 2.0 as height decreases
                        float maxHeight = 60f; // Initial Z position when kicked
                        float heightRatio = _match.Ball.ZPosition / maxHeight; // 1.0 at kick, 0.0 at ground
                        // Smooth ramp: scale grows as height decreases
                        _match.Ball.VisualScale = 1.0f + (1.0f - heightRatio) * 1.0f;
                    }
                }

                // For ground ball (left click), stop when hitting player
                if (!_match.Ball.IsAerial && _match.Ball.Speed < 100)
                {
                    float radius = PitchDimensions.BallRadius;
                    foreach (var (_, players) in _match.TeamPlayers)
                    {
                        foreach (var player in players)
                        {
                            if (player.IsSubstitute) continue;
                            float dx = _match.Ball.X - player.X;
                            float dy = _match.Ball.Y - player.Y;
                            float dist = MathF.Sqrt(dx * dx + dy * dy);
                            
                            if (dist < PitchDimensions.PlayerRadius + radius)
                            {
                                // Ball stops automatically on player contact (ground ball)
                                _match.Ball.VelocityX = 0;
                                _match.Ball.VelocityY = 0;
                                _match.Ball.Speed = 0;
                                break;
                            }
                        }
                    }
                }

                if (_match.Ball.Speed < 1)
                {
                    _match.Ball.VelocityX = 0;
                    _match.Ball.VelocityY = 0;
                    _match.Ball.Speed = 0;
                }

                // Removed field boundary clamping - ball can leave the field for throw-ins
            }

            // CRITICAL: Save last touch info BEFORE nulling it, for CheckForThrowIn to use
            if (_match.Ball.LastTouchedBy != null)
            {
                _lastTouchPlayerId = _match.Ball.LastTouchedBy.Id;
                _lastTouchTeam = _match.Ball.LastTouchedBy.Team;
            }
            _match.Ball.LastTouchedBy = null;
            foreach (var (_, players) in _match.TeamPlayers)
            {
                foreach (var player in players)
                {
                    if (player.IsKnockedDown || player.IsSubstitute) continue;
                    float dx = _match.Ball.X - player.X;
                    float dy = _match.Ball.Y - player.Y;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);

                    if (dist < PitchDimensions.PlayerRadius + PitchDimensions.BallRadius + 2)
                    {
                        // Skip possession if ball was just kicked (cooldown active)
                        if (_match.Ball.KickCooldown > 0) continue;

                        if (_match.Ball.Speed < 100)
                        {
                            player.HasBall = true;
                            _match.Ball.LastTouchedBy = player;
                            _match.Ball.VelocityX = 0;
                            _match.Ball.VelocityY = 0;
                            _match.Ball.Speed = 0;
                            _match.Ball.IsAerial = false;
                            _match.Ball.ZPosition = 0;
                            _match.Ball.VisualScale = 1.0f;
                            _match.Possession = player.Team;
                        }
                        else
                        {
                            _match.Ball.VelocityX *= 0.3f;
                            _match.Ball.VelocityY *= 0.3f;
                            _match.Ball.Speed *= 0.3f;
                            _match.Ball.LastTouchedBy = player;
                        }
                        break;
                    }
                }
            }
        }

        private void CheckForGoal()
        {
            float goalTop = PitchDimensions.CenterY - PitchDimensions.GoalWidth / 2;
            float goalBottom = PitchDimensions.CenterY + PitchDimensions.GoalWidth / 2;

            if (_match.Ball.X < 0 && _match.Ball.Y > goalTop && _match.Ball.Y < goalBottom)
            {
                // Away team scored (ball went into Home team's goal)
                _match.AwayScore++;
                ResetAfterGoal(TeamSide.Home);
            }
            if (_match.Ball.X > PitchDimensions.Width && _match.Ball.Y > goalTop && _match.Ball.Y < goalBottom)
            {
                // Home team scored (ball went into Away team's goal)
                _match.HomeScore++;
                ResetAfterGoal(TeamSide.Away);
            }
        }

        private void SetRefCall(string call)
        {
            _refCall = call;
            _refCallTimer = 4.0f; // Show for 4 seconds
            _match.RefereeCall = call;
            _match.RefereeCallTimer = 4.0f;
        }

        private void CheckForThrowIn()
        {
            float fieldTop = 0;
            float fieldBottom = PitchDimensions.Height;
            float fieldLeft = 0;
            float fieldRight = PitchDimensions.Width;

            // Update ref call timer
            if (_refCallTimer > 0)
            {
                _refCallTimer -= 1f / 60f;
                _match.RefereeCallTimer = _refCallTimer;
                if (_refCallTimer <= 0)
                {
                    _refCall = "";
                    _match.RefereeCall = "";
                    _match.RefereeCallTimer = 0;
                }
            }

            // Ball went out of bounds on top or bottom (throw-in)
            if ((_match.Ball.Y < fieldTop || _match.Ball.Y > fieldBottom) && _match.Ball.Speed > 0 && !_match.Ball.IsAerial && !_throwInPending)
            {
                // SOCCER RULE: The team that DID NOT touch the ball last gets the throw-in
                TeamSide throwInTeam = _lastTouchTeam == TeamSide.Home ? TeamSide.Away : TeamSide.Home;
                string teamName = throwInTeam == TeamSide.Home ? "Home" : "Away";
                SetRefCall($"Throw In - {teamName} Team");
                
                // Clamp ball to the boundary line so players can reach it
                float clampedY = Math.Clamp(_match.Ball.Y, fieldTop + 1, fieldBottom - 1);
                float clampedX = Math.Clamp(_match.Ball.X, fieldLeft + 1, fieldRight - 1);
                _match.Ball.X = clampedX;
                _match.Ball.Y = clampedY;
                
                // Store the throw-in position - only the opposing team can take it
                _throwInPosition = new Vector2(_match.Ball.X, _match.Ball.Y);
                _throwInTeam = throwInTeam;
                _throwInPending = true;
                
                // Stop the ball
                _match.Ball.VelocityX = 0;
                _match.Ball.VelocityY = 0;
                _match.Ball.Speed = 0;
            }

            // Ball went out of bounds on left or right side (goal kick / corner)
            if ((_match.Ball.X < fieldLeft || _match.Ball.X > fieldRight) && _match.Ball.Speed > 0 && !_match.Ball.IsAerial && !_throwInPending)
            {
                bool isGoal = false;
                // Check if this is actually a goal (handled in CheckForGoal)
                float goalTop = PitchDimensions.CenterY - PitchDimensions.GoalWidth / 2;
                float goalBottom = PitchDimensions.CenterY + PitchDimensions.GoalWidth / 2;
                
                if ((_match.Ball.X < fieldLeft && _match.Ball.Y >= goalTop && _match.Ball.Y <= goalBottom) ||
                    (_match.Ball.X > fieldRight && _match.Ball.Y >= goalTop && _match.Ball.Y <= goalBottom))
                {
                    isGoal = true;
                }

                if (!isGoal)
                {
                    // SOCCER RULE: If attacking team kicks it out on offense end = goal kick to defense
                    // If defending team kicks it out on their own end = corner kick to offense
                    // For simplicity: the team that DID NOT touch it last gets a goal kick
                    TeamSide goalKickTeam = _lastTouchTeam == TeamSide.Home ? TeamSide.Away : TeamSide.Home;
                    string teamName = goalKickTeam == TeamSide.Home ? "Home" : "Away";
                    SetRefCall($"Goal Kick - {teamName} Team");
                    
                    // Place ball on the field near the goal area of the team taking the kick
                    float goalKickX = goalKickTeam == TeamSide.Home ? PitchDimensions.PenaltyAreaWidth + 10 : PitchDimensions.Width - PitchDimensions.PenaltyAreaWidth - 10;
                    _match.Ball.X = goalKickX;
                    _match.Ball.Y = PitchDimensions.CenterY;
                    
                    _match.Ball.VelocityX = 0;
                    _match.Ball.VelocityY = 0;
                    _match.Ball.Speed = 0;
                    _match.Ball.IsAerial = false;
                }
            }
        }

        private void CheckOffside()
        {
            foreach (var (teamId, players) in _match.TeamPlayers)
            {
                var team = _teams.GetValueOrDefault(teamId);
                if (team == null) continue;

                float secondLastDefenderX = team.Side == TeamSide.Home ? PitchDimensions.Width : 0;
                var defendingTeam = _teams.Values.FirstOrDefault(t => t.Id != teamId);
                if (defendingTeam != null && _match.TeamPlayers.ContainsKey(defendingTeam.Id))
                {
                    var defenders = _match.TeamPlayers[defendingTeam.Id]
                        .Where(p => !p.IsSubstitute && !p.IsKnockedDown)
                        .OrderByDescending(p => team.Side == TeamSide.Home ? p.X : -p.X)
                        .ToList();

                    if (defenders.Count >= 2)
                        secondLastDefenderX = defenders[1].X;
                }

                foreach (var player in players)
                {
                    if (player.IsHuman || player.IsSubstitute) continue;
                    float offsideLine = secondLastDefenderX;
                    bool isOffside = (team.Side == TeamSide.Home && player.X > offsideLine + 10) ||
                                     (team.Side == TeamSide.Away && player.X < offsideLine - 10);

                    if (isOffside && player.HasBall)
                    {
                        player.HasBall = false;
                        _match.Ball.LastTouchedBy = null;
                    }
                }
            }
        }

        private void UpdatePossession()
        {
            foreach (var (_, players) in _match.TeamPlayers)
            {
                foreach (var player in players)
                {
                    if (player.HasBall)
                    {
                        _match.Possession = player.Team;
                        return;
                    }
                }
            }
        }

        private void ResetAfterGoal(TeamSide scoringTeam)
        {
            _match.Ball.X = PitchDimensions.CenterX;
            _match.Ball.Y = PitchDimensions.CenterY;
            _match.Ball.VelocityX = 0;
            _match.Ball.VelocityY = 0;
            _match.Ball.Speed = 0;
            _match.Ball.LastTouchedBy = null;
            _match.Ball.IsAerial = false;
            _match.Ball.ZPosition = 0;
            _match.Ball.VisualScale = 1.0f;
            _throwInPending = false;
            _matchStarted = false; // Wait for next kickoff

            foreach (var (_, players) in _match.TeamPlayers)
            {
                foreach (var player in players)
                {
                    if (player.IsSubstitute) continue;
                    var team = _teams.GetValueOrDefault(player.Team == TeamSide.Home ? _match.HomeTeamId! : _match.AwayTeamId!);
                    var pos = team != null ? GetFormationPosition(player, team) : (PitchDimensions.CenterX, PitchDimensions.CenterY);
                    player.X = pos.Item1;
                    player.Y = pos.Item2;
                    player.HasBall = false;
                    player.Speed = 0;
                    player.TargetX = player.X;
                    player.TargetY = player.Y;
                }
            }
        }

        public void SetupKickoff()
        {
            _matchStarted = false;
            _throwInPending = false;
            _match.Ball.X = PitchDimensions.CenterX;
            _match.Ball.Y = PitchDimensions.CenterY;
            _match.Ball.VelocityX = 0;
            _match.Ball.VelocityY = 0;
            _match.Ball.Speed = 0;
            _match.Ball.IsAerial = false;
            _match.Ball.ZPosition = 0;
            _match.Ball.VisualScale = 1.0f;
            _match.MatchMinute = 0;
            _match.MatchSecond = 0;
            _match.CurrentHalf = GamePhase.FirstHalf;

            foreach (var (_, players) in _match.TeamPlayers)
            {
                foreach (var player in players)
                {
                    var team = _teams.GetValueOrDefault(player.Team == TeamSide.Home ? _match.HomeTeamId! : _match.AwayTeamId!);
                    var pos = team != null ? GetFormationPosition(player, team) : (PitchDimensions.CenterX, PitchDimensions.CenterY);
                    player.X = pos.Item1;
                    player.Y = pos.Item2;
                    player.TargetX = player.X;
                    player.TargetY = player.Y;
                    player.HasBall = false;
                    player.Speed = 0;
                }
            }

            // Give ball to home team midfielder at center
            var homeTeam = _teams.GetValueOrDefault(_match.HomeTeamId!);
            if (homeTeam != null && _match.TeamPlayers.ContainsKey(homeTeam.Id))
            {
                var centerPlayer = _match.TeamPlayers[homeTeam.Id]
                    .Where(p => !p.IsSubstitute && p.Position == PlayerPosition.Midfielder)
                    .OrderBy(_ => _random.Next())
                    .FirstOrDefault();

                if (centerPlayer != null)
                {
                    centerPlayer.HasBall = true;
                    _match.Ball.X = centerPlayer.X;
                    _match.Ball.Y = centerPlayer.Y;
                }
            }
        }

        public void HandleSubstitution(Player playerOff, Player playerOn)
        {
            playerOff.IsOnField = false;
            playerOff.IsSubstitute = true;
            playerOn.IsOnField = true;
            playerOn.IsSubstitute = false;
            playerOn.X = playerOff.X;
            playerOn.Y = playerOff.Y;
            _match.Events.Add(new MatchEvent());
        }

        public void HandleHalfTime()
        {
            _match.CurrentHalf = GamePhase.SecondHalf;
            _match.MatchMinute = 45;
            _match.MatchSecond = 0;
            _matchStarted = false; // Wait for second half kickoff

            foreach (var (_, players) in _match.TeamPlayers)
            {
                foreach (var player in players)
                {
                    if (player.IsSubstitute) continue;
                    player.X = PitchDimensions.Width - player.X;
                    player.TargetX = player.X;
                    player.TargetY = player.Y;
                }
            }

            _match.Ball.X = PitchDimensions.CenterX;
            _match.Ball.Y = PitchDimensions.CenterY;
            _match.Ball.VelocityX = 0;
            _match.Ball.VelocityY = 0;
            _match.Ball.Speed = 0;
            _match.Ball.IsAerial = false;
            _match.Ball.ZPosition = 0;
            _match.Ball.VisualScale = 1.0f;
        }
    }
}