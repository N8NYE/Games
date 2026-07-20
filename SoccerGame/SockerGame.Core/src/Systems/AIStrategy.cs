using SockerGame.Core.Enums;
using SockerGame.Core.Models;
using System.Numerics;

namespace SockerGame.Core.Systems
{
    /// <summary>
    /// Advanced Soccer AI Strategy System implementing authentic tactical principles
    /// Based on modern soccer tactics including pressing, positional play, offside trap, and team coordination
    /// </summary>
    public static class AIStrategy
    {
        private static readonly Random _random = new();

        /// <summary>
        /// Updates AI player behavior with tactical intelligence
        /// </summary>
        public static void UpdateAIPlayer(Player player, float dt, MatchState match, Dictionary<string, Team> teams, Dictionary<string, List<Player>> teamPlayers)
        {
            // Base movement speed with stat influence
            float baseSpeed = 200f + (player.Stats.Speed / 100f) * 150f;
            baseSpeed *= 0.7f + (player.Stats.Stamina / 100f) * 0.3f;

            float targetX, targetY;
            bool shouldKick = false;

            // Find the ball carrier (if any)
            var ballCarrier = teamPlayers.Values.SelectMany(p => p).FirstOrDefault(p => p.HasBall && !p.IsSubstitute);
            
            // Ball is loose if no one has it but it's moving
            bool ballIsLoose = ballCarrier == null && match.Ball.Speed > 5f;

            if (player.HasBall)
            {
                // BALL CARRIER - Tactical decision making
                (targetX, targetY) = GetBallCarrierTacticalTarget(player, match, teamPlayers, teams);
                
                // Decide whether to kick based on game state
                shouldKick = ShouldBallCarrierKick(player, match, teamPlayers, teams);
            }
            else if (ballIsLoose)
            {
                // BALL IS LOOSE - Chase it with position-specific priority to distribute players
                (targetX, targetY) = GetLooseBallTarget(player, match);
            }
            else
            {
                // NO BALL - Positional play based on possession state
                (targetX, targetY) = GetPositionalTarget(player, match, teamPlayers, teams);
            }

            // Apply movement with tactical awareness
            MoveWithTacticalAwareness(player, targetX, targetY, baseSpeed, dt);

            // Execute kick if decided
            if (shouldKick && match.Ball.KickCooldown <= 0)
            {
                float kickRange = 30f;
                float ballDist = MathF.Sqrt(MathF.Pow(match.Ball.X - player.X, 2) + MathF.Pow(match.Ball.Y - player.Y, 2));
                if (ballDist < kickRange)
                {
                    ExecuteTacticalKick(player, match, teamPlayers, teams);
                }
            }
        }

        /// <summary>
        /// Get target for loose ball - prioritize by position and add variation to prevent bunching
        /// </summary>
        private static (float, float) GetLooseBallTarget(Player player, MatchState match)
        {
            // All players should chase the ball, but with slight offsets to prevent clumping
            float ballX = match.Ball.X + (float)(_random.NextDouble() - 0.5f) * 30f;
            float ballY = match.Ball.Y + (float)(_random.NextDouble() - 0.5f) * 30f;
            
            // Add some position-based variation
            float offsetX = player.Position switch
            {
                PlayerPosition.Goalkeeper => -20f,
                PlayerPosition.Defender => -10f,
                PlayerPosition.Midfielder => 0f,
                PlayerPosition.Forward => 10f,
                _ => 0f
            };
            
            // For home team moving right (toward away goal), add X variation
            // For away team moving left (toward home goal), subtract X variation
            float sign = player.Team == TeamSide.Home ? 1f : -1f;
            
            return (ballX + offsetX * sign, ballY);
        }

        /// <summary>
        /// Ball carrier makes tactical decisions - considers pressure, available passing lanes, and shooting opportunities
        /// </summary>
        private static (float, float) GetBallCarrierTacticalTarget(Player player, MatchState match, Dictionary<string, List<Player>> teamPlayers, Dictionary<string, Team> teams)
        {
            float goalX = player.Team == TeamSide.Home ? PitchDimensions.Width : 0;

            // Get opponents who could pressure this player
            var opponents = teamPlayers.Values.SelectMany(p => p)
                .Where(p => p.Team != player.Team && !p.IsSubstitute && !p.IsKnockedDown)
                .ToList();

            // Find closest opponent for pressure assessment
            var closestOpponent = opponents.OrderBy(p => MathF.Sqrt(MathF.Pow(p.X - player.X, 2) + MathF.Pow(p.Y - player.Y, 2))).FirstOrDefault();
            float pressureDistance = closestOpponent != null ? 
                MathF.Sqrt(MathF.Pow(closestOpponent.X - player.X, 2) + MathF.Pow(closestOpponent.Y - player.Y, 2)) : 999f;

            // HIGH PRESSURE - Look to pass or dribble away from pressure
            if (pressureDistance < 60f && closestOpponent != null)
            {
                // Find best escape pass
                var bestPass = FindBestEscapePass(player, opponents, teamPlayers, match);
                if (bestPass != null && (pressureDistance < 40f || _random.NextDouble() < 0.7f))
                {
                    // Move toward passing lane while looking to pass
                    return (bestPass.X + (player.Team == TeamSide.Home ? -20f : 20f), bestPass.Y);
                }

                // No good pass - dribble away from pressure
                float escapeAngle = MathF.Atan2(player.Y - closestOpponent.Y, player.X - closestOpponent.X);
                return (player.X + MathF.Cos(escapeAngle) * 80f, player.Y + MathF.Sin(escapeAngle) * 80f);
            }

            // MEDIUM PRESSURE - Look for forward passing option
            if (pressureDistance < 120f)
            {
                var forwardPass = FindBestForwardPass(player, opponents, teamPlayers, match);
                if (forwardPass != null && _random.NextDouble() < 0.6f)
                {
                    return (forwardPass.X, forwardPass.Y);
                }
            }

            // LOW PRESSURE - Drive toward goal
            return (goalX, PitchDimensions.CenterY);
        }

        /// <summary>
        /// Determines if ball carrier should kick (pass or shoot) based on tactical situation
        /// </summary>
        private static bool ShouldBallCarrierKick(Player player, MatchState match, Dictionary<string, List<Player>> teamPlayers, Dictionary<string, Team> teams)
        {
            var opponents = teamPlayers.Values.SelectMany(p => p)
                .Where(p => p.Team != player.Team && !p.IsSubstitute && !p.IsKnockedDown)
                .ToList();

            var closestOpponent = opponents.OrderBy(p => MathF.Sqrt(MathF.Pow(p.X - player.X, 2) + MathF.Pow(p.Y - player.Y, 2))).FirstOrDefault();
            float pressureDistance = closestOpponent != null ? 
                MathF.Sqrt(MathF.Pow(closestOpponent.X - player.X, 2) + MathF.Pow(closestOpponent.Y - player.Y, 2)) : 999f;

            // High pressure - must pass or shoot quickly
            if (pressureDistance < 45f)
                return _random.NextDouble() < 0.9f;

            // Close to goal and clear - take shot
            float goalDist = player.Team == TeamSide.Home ? Math.Abs(PitchDimensions.Width - player.X) : player.X;
            if (goalDist < 150f && pressureDistance > 60f)
                return _random.NextDouble() < 0.7f;

            // Medium pressure - look to pass
            if (pressureDistance < 80f)
                return _random.NextDouble() < 0.6f;

            return false;
        }

        /// <summary>
        /// Executes a tactical kick - pass or shot based on situation
        /// </summary>
        private static void ExecuteTacticalKick(Player player, MatchState match, Dictionary<string, List<Player>> teamPlayers, Dictionary<string, Team> teams)
        {
            var opponents = teamPlayers.Values.SelectMany(p => p)
                .Where(p => p.Team != player.Team && !p.IsSubstitute && !p.IsKnockedDown)
                .ToList();

            var closestOpponent = opponents.OrderBy(p => MathF.Sqrt(MathF.Pow(p.X - player.X, 2) + MathF.Pow(p.Y - player.Y, 2))).FirstOrDefault();
            float pressureDistance = closestOpponent != null ? 
                MathF.Sqrt(MathF.Pow(closestOpponent.X - player.X, 2) + MathF.Pow(closestOpponent.Y - player.Y, 2)) : 999f;

            // Close to goal - shoot
            float goalDist = player.Team == TeamSide.Home ? Math.Abs(PitchDimensions.Width - player.X) : player.X;
            if (goalDist < 180f)
            {
                PerformAIShot(player, match);
                return;
            }

            // High pressure - try to find any teammate
            if (pressureDistance < 50f)
            {
                var escapePass = FindBestEscapePass(player, opponents, teamPlayers, match);
                if (escapePass != null)
                {
                    PerformAIPass(player, escapePass, match);
                    return;
                }
            }

            // Look for forward pass
            var forwardPass = FindBestForwardPass(player, opponents, teamPlayers, match);
            if (forwardPass != null && _random.NextDouble() < 0.8f)
            {
                PerformAIPass(player, forwardPass, match);
                return;
            }

            // Safe backward pass
            var safePass = FindSafeBackwardPass(player, teamPlayers, match);
            if (safePass != null && _random.NextDouble() < 0.9f)
            {
                PerformAIPass(player, safePass, match);
                return;
            }
        }

        /// <summary>
        /// Find best escape pass when under high pressure
        /// </summary>
        private static Player? FindBestEscapePass(Player passer, List<Player> opponents, Dictionary<string, List<Player>> teamPlayers, MatchState match)
        {
            var teammates = teamPlayers.Values.SelectMany(p => p)
                .Where(p => p.Team == passer.Team && p.Id != passer.Id && !p.IsSubstitute && !p.IsKnockedDown)
                .ToList();

            Player? bestTarget = null;
            float bestSpaceScore = -1f;

            foreach (var teammate in teammates)
            {
                // Check how much space this teammate has from opponents
                float spaceScore = CalculateTeammateSpace(teammate, opponents);
                
                // Prefer teammates behind the ball carrier (safer)
                bool isBehind = (passer.Team == TeamSide.Home && teammate.X < passer.X) ||
                               (passer.Team == TeamSide.Away && teammate.X > passer.X);

                if (spaceScore > 50f && (isBehind || spaceScore > 100f))
                {
                    if (spaceScore > bestSpaceScore)
                    {
                        bestSpaceScore = spaceScore;
                        bestTarget = teammate;
                    }
                }
            }

            return bestTarget;
        }

        /// <summary>
        /// Find best forward pass option
        /// </summary>
        private static Player? FindBestForwardPass(Player passer, List<Player> opponents, Dictionary<string, List<Player>> teamPlayers, MatchState match)
        {
            var teammates = teamPlayers.Values.SelectMany(p => p)
                .Where(p => p.Team == passer.Team && p.Id != passer.Id && !p.IsSubstitute && !p.IsKnockedDown)
                .ToList();

            Player? bestTarget = null;
            float bestScore = -1f;

            foreach (var teammate in teammates)
            {
                // Must be ahead of ball carrier
                bool isAhead = (passer.Team == TeamSide.Home && teammate.X > passer.X) ||
                               (passer.Team == TeamSide.Away && teammate.X < passer.X);

                if (!isAhead) continue;

                float spaceScore = CalculateTeammateSpace(teammate, opponents);
                
                // Good pass: ahead AND has space
                if (spaceScore > 30f && spaceScore > bestScore)
                {
                    bestScore = spaceScore;
                    bestTarget = teammate;
                }
            }

            return bestTarget;
        }

        /// <summary>
        /// Find safe backward pass for maintaining possession
        /// </summary>
        private static Player? FindSafeBackwardPass(Player passer, Dictionary<string, List<Player>> teamPlayers, MatchState match)
        {
            var teammates = teamPlayers.Values.SelectMany(p => p)
                .Where(p => p.Team == passer.Team && p.Id != passer.Id && !p.IsSubstitute && !p.IsKnockedDown)
                .ToList();

            // Find teammates behind the ball carrier
            var behind = teammates.Where(p => (passer.Team == TeamSide.Home && p.X < passer.X) ||
                                            (passer.Team == TeamSide.Away && p.X > passer.X))
                                  .OrderBy(p => MathF.Sqrt(MathF.Pow(p.X - passer.X, 2) + MathF.Pow(p.Y - passer.Y, 2)))
                                  .FirstOrDefault();

            return behind;
        }

        /// <summary>
        /// Calculate how much space a teammate has from opponents
        /// </summary>
        private static float CalculateTeammateSpace(Player teammate, List<Player> opponents)
        {
            if (opponents.Count == 0) return 500f; // Lots of space if no opponents
            
            float spaceScore = 0f;
            foreach (var opponent in opponents)
            {
                float dist = MathF.Sqrt(MathF.Pow(opponent.X - teammate.X, 2) + MathF.Pow(opponent.Y - teammate.Y, 2));
                spaceScore += Math.Max(0, dist);
            }
            return spaceScore / opponents.Count;
        }

        /// <summary>
        /// Get positional target for player when not involved in play
        /// </summary>
        private static (float, float) GetPositionalTarget(Player player, MatchState match, Dictionary<string, List<Player>> teamPlayers, Dictionary<string, Team> teams)
        {
            var team = teams.Values.FirstOrDefault(t => t.PlayerIds.Contains(player.Id));

            // Find opponent with ball
            var opponentCarrier = teamPlayers.Values.SelectMany(p => p)
                .FirstOrDefault(p => p.HasBall && p.Team != player.Team);

            // If opponent has ball, apply defensive tactics
            if (opponentCarrier != null)
            {
                return GetDefensivePosition(player, opponentCarrier, match, teamPlayers, teams);
            }

            // Check if team has possession (ball carrier exists on our team)
            var ballCarrier = teamPlayers.Values.SelectMany(p => p)
                .FirstOrDefault(p => p.HasBall && p.Team == player.Team);

            // If team has ball, provide support runs
            if (ballCarrier != null)
            {
                return GetSupportingPosition(player, ballCarrier, match, teamPlayers, teams);
            }

            // No possession - return to formation with dynamic ball attraction
            return GetFormationPosition(player, team, teamPlayers, match);
        }

        /// <summary>
        /// Defensive positioning with team coordination
        /// </summary>
        private static (float, float) GetDefensivePosition(Player player, Player opponentCarrier, MatchState match, Dictionary<string, List<Player>> teamPlayers, Dictionary<string, Team> teams)
        {
            var team = teams.Values.FirstOrDefault(t => t.PlayerIds.Contains(player.Id));

            // GOALKEEPER - Stay on goal line, anticipating the ball
            if (player.Position == PlayerPosition.Goalkeeper)
            {
                float gkX = player.Team == TeamSide.Home ? 30f : PitchDimensions.Width - 30f;
                // Position between ball and center of goal
                float targetY = Math.Clamp(opponentCarrier.Y, 
                    PitchDimensions.CenterY - PitchDimensions.GoalWidth / 2 + 15,
                    PitchDimensions.CenterY + PitchDimensions.GoalWidth / 2 - 15);
                return (gkX, targetY);
            }

            // DEFENDERS - Implement offside trap and marking
            if (player.Position == PlayerPosition.Defender)
            {
                return GetDefenderPosition(player, opponentCarrier, match, teamPlayers, teams);
            }

            // MIDFIELDERS - Press but maintain shape
            if (player.Position == PlayerPosition.Midfielder)
            {
                // If close to opponent carrier, press
                float dist = MathF.Sqrt(MathF.Pow(opponentCarrier.X - player.X, 2) + MathF.Pow(opponentCarrier.Y - player.Y, 2));
                if (dist < 150f)
                {
                    // Press at an angle to channel opponent
                    float pressAngle = MathF.Atan2(opponentCarrier.Y - player.Y, opponentCarrier.X - player.X);
                    float pressX = opponentCarrier.X - MathF.Cos(pressAngle) * 40f;
                    float pressY = opponentCarrier.Y - MathF.Sin(pressAngle) * 40f;
                    return (pressX, pressY);
                }
                return GetFormationPosition(player, team, teamPlayers, match);
            }

            // FORWARDS - Stay forward, press high if close
            float forwardDist = MathF.Sqrt(MathF.Pow(opponentCarrier.X - player.X, 2) + MathF.Pow(opponentCarrier.Y - player.Y, 2));
            if (forwardDist < 100f)
            {
                return (opponentCarrier.X, opponentCarrier.Y);
            }

            return GetFormationPosition(player, team, teamPlayers, match);
        }

        /// <summary>
        /// Defender positioning with offside trap implementation
        /// </summary>
        private static (float, float) GetDefenderPosition(Player defender, Player opponentCarrier, MatchState match, Dictionary<string, List<Player>> teamPlayers, Dictionary<string, Team> teams)
        {
            var teammates = teamPlayers.Values.SelectMany(p => p)
                .Where(p => p.Team == defender.Team && p.Id != defender.Id && !p.IsSubstitute)
                .ToList();

            var otherDefenders = teammates.Where(p => p.Position == PlayerPosition.Defender).ToList();

            // OFFSIDE TRAP LOGIC - Coordinate with other defenders
            float avgDefX = otherDefenders.Count > 0 ? otherDefenders.Average(p => p.X) : defender.X;
            float alignmentThreshold = 50f;
            
            bool isAligned = MathF.Abs(defender.X - avgDefX) < alignmentThreshold;

            // Step up for offside trap if aligned and opponent is approaching
            float defLineX = defender.Team == TeamSide.Home ? 200f : PitchDimensions.Width - 200f;
            bool opponentApproachingTheirBox = (defender.Team == TeamSide.Home && opponentCarrier.X > defLineX - 100f) ||
                                              (defender.Team == TeamSide.Away && opponentCarrier.X < defLineX + 100f);

            if (isAligned && opponentApproachingTheirBox)
            {
                // Step forward slightly to catch opponent offside
                float trapX = defLineX + (defender.Team == TeamSide.Home ? 10f : -10f);
                float trapY = Math.Clamp(opponentCarrier.Y, PitchDimensions.CenterY - 150f, PitchDimensions.CenterY + 150f);
                return (trapX, trapY);
            }

            // NORMAL DEFENSIVE POSITIONING
            if (defender.Team == TeamSide.Home)
            {
                float defensiveLineX = 250f;
                
                // Look for opponent to mark
                var markableOpponents = teamPlayers.Values.SelectMany(p => p)
                    .Where(p => p.Team != defender.Team && !p.IsSubstitute && !p.IsKnockedDown)
                    .OrderBy(p => MathF.Sqrt(MathF.Pow(p.X - defender.X, 2) + MathF.Pow(p.Y - defender.Y, 2)))
                    .FirstOrDefault();

                if (markableOpponents != null)
                {
                    float markDist = MathF.Sqrt(MathF.Pow(markableOpponents.X - defender.X, 2) + MathF.Pow(markableOpponents.Y - defender.Y, 2));
                    if (markDist < 250f)
                    {
                        return (markableOpponents.X - (markableOpponents.X - defender.X) * 0.4f,
                                markableOpponents.Y - (markableOpponents.Y - defender.Y) * 0.4f);
                    }
                }
                
                return (defensiveLineX, PitchDimensions.CenterY);
            }
            else
            {
                float defensiveLineX = PitchDimensions.Width - 250f;
                
                var markableOpponents = teamPlayers.Values.SelectMany(p => p)
                    .Where(p => p.Team != defender.Team && !p.IsSubstitute && !p.IsKnockedDown)
                    .OrderBy(p => MathF.Sqrt(MathF.Pow(p.X - defender.X, 2) + MathF.Pow(p.Y - defender.Y, 2)))
                    .FirstOrDefault();

                if (markableOpponents != null)
                {
                    float markDist = MathF.Sqrt(MathF.Pow(markableOpponents.X - defender.X, 2) + MathF.Pow(markableOpponents.Y - defender.Y, 2));
                    if (markDist < 250f)
                    {
                        return (markableOpponents.X - (markableOpponents.X - defender.X) * 0.4f,
                                markableOpponents.Y - (markableOpponents.Y - defender.Y) * 0.4f);
                    }
                }
                
                return (defensiveLineX, PitchDimensions.CenterY);
            }
        }

        /// <summary>
        /// Supporting positioning - intelligent movement to create passing options and prevent bunching
        /// </summary>
        private static (float, float) GetSupportingPosition(Player supporter, Player ballCarrier, MatchState match, Dictionary<string, List<Player>> teamPlayers, Dictionary<string, Team> teams)
        {
            // Calculate position ahead of ball carrier for a pass option
            float aheadDistance = 80f + _random.Next(0, 80);
            
            // Each supporter gets a unique Y offset based on their position index
            var teammates = teamPlayers.Values.SelectMany(p => p)
                .Where(p => p.Team == supporter.Team && !p.IsSubstitute && p.Position != PlayerPosition.Goalkeeper)
                .ToList();
            
            int supporterIndex = teammates.IndexOf(supporter);
            if (supporterIndex < 0) supporterIndex = 0;
            float uniqueOffset = supporterIndex * 80f - teammates.Count * 40f; // Spread vertically

            float aheadAngle = supporter.Team == TeamSide.Home ? 0f : MathF.PI;
            
            float runX = ballCarrier.X + MathF.Cos(aheadAngle) * aheadDistance;
            float runY = ballCarrier.Y + uniqueOffset;

            // Check for congestion with teammates and adjust
            float minTeammateDist = teammates.Where(p => p.Id != supporter.Id).Any() ? teammates.Where(p => p.Id != supporter.Id).Min(p => 
                MathF.Sqrt(MathF.Pow(p.X - runX, 2) + MathF.Pow(p.Y - runY, 2))) : 999f;
            
            // If too crowded with teammates, spread even more
            if (minTeammateDist < 70f)
            {
                runY = Math.Clamp(runY + (float)(_random.NextDouble() - 0.5f) * 200f, 50f, PitchDimensions.Height - 50f);
            }

            runX = Math.Clamp(runX, 30f, PitchDimensions.Width - 30f);
            runY = Math.Clamp(runY, 50f, PitchDimensions.Height - 50f);

            return (runX, runY);
        }

        /// <summary>
        /// Tactical movement considering space and opponent positioning
        /// </summary>
        private static void MoveWithTacticalAwareness(Player player, float targetX, float targetY, float speed, float dt)
        {
            float dx = targetX - player.X;
            float dy = targetY - player.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist > 5)
            {
                player.Direction = MathF.Atan2(dy, dx);
                player.Speed = MathF.Min(speed * 1.1f, dist / 0.03f);
                player.TargetX = player.X + MathF.Cos(player.Direction) * player.Speed * dt;
                player.TargetY = player.Y + MathF.Sin(player.Direction) * player.Speed * dt;
            }
            else
            {
                player.Speed = 0;
            }
        }

        /// <summary>
        /// Formation positions with dynamic adjustment based on ball position
        /// Home team (red) defends left goal (X=0), attacks right (X=Width)
        /// Away team (blue) defends right goal (X=Width), attacks left (X=0)
        /// </summary>
        private static (float, float) GetFormationPosition(Player player, Team? team, Dictionary<string, List<Player>> teamPlayers, MatchState match)
        {
            if (team == null) return (PitchDimensions.CenterX, PitchDimensions.CenterY);

            var samePosition = teamPlayers[team.Id]
                .Where(p => p.Position == player.Position && !p.IsSubstitute)
                .ToList();
            int count = samePosition.Count;
            int index = samePosition.IndexOf(player);
            if (index < 0) index = 0;

            // Adjust formation based on ball position (dynamic shifting)
            float ballInfluence = 0.4f;
            float ballXRatio = match.Ball.X / PitchDimensions.Width;

            if (player.Position == PlayerPosition.Goalkeeper)
            {
                float gkX = team.Side == TeamSide.Home ? 30f : PitchDimensions.Width - 30f;
                return (gkX, PitchDimensions.CenterY);
            }

            // Spread players vertically within formation - unique for each player
            float verticalSpread = (index - (count - 1) / 2f) * 0.7f; // Reduced spread to prevent clumping

            if (player.Position == PlayerPosition.Defender)
            {
                // Defenders stay compact, shift based on ball
                float baseX = team.Side == TeamSide.Home ? PitchDimensions.Width * 0.2f : PitchDimensions.Width * 0.8f;
                float adjustedX = team.Side == TeamSide.Home ? 
                    baseX + (ballXRatio - 0.5f) * PitchDimensions.Width * ballInfluence * 0.5f :
                    baseX - (ballXRatio - 0.5f) * PitchDimensions.Width * ballInfluence * 0.5f;
                adjustedX = Math.Clamp(adjustedX, team.Side == TeamSide.Home ? 20f : PitchDimensions.Width * 0.5f, 
                                       team.Side == TeamSide.Home ? PitchDimensions.Width * 0.4f : PitchDimensions.Width - 20f);
                
                float spread = verticalSpread * (PitchDimensions.Height * 0.35f / Math.Max(1, count - 1));
                return (adjustedX, PitchDimensions.CenterY + spread);
            }

            if (player.Position == PlayerPosition.Midfielder)
            {
                float baseX = team.Side == TeamSide.Home ? PitchDimensions.Width * 0.45f : PitchDimensions.Width * 0.55f;
                float adjustedX = team.Side == TeamSide.Home ? 
                    baseX + (ballXRatio - 0.5f) * PitchDimensions.Width * ballInfluence :
                    baseX - (ballXRatio - 0.5f) * PitchDimensions.Width * ballInfluence;
                adjustedX = Math.Clamp(adjustedX, 100f, PitchDimensions.Width - 100f);
                
                float spread = verticalSpread * (PitchDimensions.Height * 0.45f / Math.Max(1, count - 1));
                return (adjustedX, PitchDimensions.CenterY + spread);
            }

            if (player.Position == PlayerPosition.Forward)
            {
                float baseX = team.Side == TeamSide.Home ? PitchDimensions.Width * 0.7f : PitchDimensions.Width * 0.3f;
                float adjustedX = team.Side == TeamSide.Home ? 
                    baseX + (ballXRatio - 0.5f) * PitchDimensions.Width * ballInfluence * 0.7f :
                    baseX - (ballXRatio - 0.5f) * PitchDimensions.Width * ballInfluence * 0.7f;
                adjustedX = Math.Clamp(adjustedX, team.Side == TeamSide.Home ? PitchDimensions.Width * 0.4f : 100f,
                                       team.Side == TeamSide.Home ? PitchDimensions.Width - 20f : PitchDimensions.Width * 0.6f);
                
                float spread = verticalSpread * (PitchDimensions.Height * 0.35f / Math.Max(1, count - 1));
                return (adjustedX, PitchDimensions.CenterY + spread);
            }

            return (PitchDimensions.CenterX, PitchDimensions.CenterY);
        }

        /// <summary>
        /// Perform a tackle on opponent - based on Defense/Aggression stats
        /// </summary>
        private static void PerformAITackle(Player tackler, Player opponent, MatchState match)
        {
            float tackleChance = 0.6f + (tackler.Stats.Defense / 100f) * 0.3f + (tackler.Stats.Aggression / 100f) * 0.1f;
            
            if (_random.NextDouble() < tackleChance)
            {
                opponent.IsKnockedDown = true;
                opponent.KnockdownTimer = 0.8f + (tackler.Stats.Aggression / 100f) * 0.4f;
                opponent.HasBall = false;

                // Card risk based on aggression and situation
                float cardChance = 0.08f + (tackler.Stats.Aggression / 100f) * 0.25f;
                if (_random.NextDouble() < cardChance)
                {
                    tackler.Cards = 1;
                }

                match.Ball.VelocityX *= 0.5f;
                match.Ball.VelocityY *= 0.5f;
                match.Ball.Speed *= 0.5f;
                match.Ball.KickCooldown = 0.3f;
            }
        }

        /// <summary>
        /// Perform a pass - power based on Passing stat
        /// </summary>
        private static void PerformAIPass(Player passer, Player receiver, MatchState match)
        {
            float power = (passer.Stats.Passing / 100f) * 400f + 200f;
            float accuracy = passer.Stats.Accuracy / 100f;

            float dx = receiver.X - passer.X;
            float dy = receiver.Y - passer.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist > 0)
            {
                float angle = MathF.Atan2(dy, dx);
                float error = (1f - accuracy) * 0.15f;
                angle += (float)(_random.NextDouble() - 0.5) * error;

                match.Ball.VelocityX = MathF.Cos(angle) * power;
                match.Ball.VelocityY = MathF.Sin(angle) * power;
                match.Ball.Speed = power;
                match.Ball.LastTouchedBy = passer;
                match.Ball.IsAerial = true;
                match.Ball.ZPosition = 20f;
                match.Ball.VisualScale = 1.2f;
                match.Ball.KickCooldown = 0.4f;
                passer.HasBall = false;
            }
        }

        /// <summary>
        /// Perform a shot on goal - power based on ShotStrength stat
        /// </summary>
        private static void PerformAIShot(Player shooter, MatchState match)
        {
            float power = (shooter.Stats.ShotStrength / 100f) * 500f + 250f;
            float accuracy = shooter.Stats.Accuracy / 100f;

            float goalY = PitchDimensions.CenterY + (float)(_random.NextDouble() - 0.5) * PitchDimensions.GoalWidth * 0.35f;
            float goalX = shooter.Team == TeamSide.Home ? PitchDimensions.Width : 0;

            float dx = goalX - shooter.X;
            float dy = goalY - shooter.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist > 0)
            {
                float angle = MathF.Atan2(dy, dx);
                float error = (1f - accuracy) * 0.25f;
                angle += (float)(_random.NextDouble() - 0.5) * error;

                match.Ball.VelocityX = MathF.Cos(angle) * power;
                match.Ball.VelocityY = MathF.Sin(angle) * power;
                match.Ball.Speed = power;
                match.Ball.LastTouchedBy = shooter;
                match.Ball.IsAerial = false;
                match.Ball.ZPosition = 0;
                match.Ball.VisualScale = 1.0f;
                match.Ball.KickCooldown = 0.5f;
                shooter.HasBall = false;
            }
        }
    }
}