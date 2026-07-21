using SockerGame.Core.Enums;
using SockerGame.Core.Models;

namespace SockerGame.Core.Systems
{
    /// <summary>
    /// Soccer AI with authentic positional play and movement patterns
    /// Inspired by real soccer: maintain shape, make support runs, frequent passing
    /// </summary>
    public static class AIStrategy
    {
        private static readonly Random _random = new();

        // Formation positions - Home vs Away side
        // Home attacks RIGHT (goal at X=Width), Away attacks LEFT (goal at X=0)
        private static float GetFormationX(PlayerPosition position, TeamSide team)
        {
            float ratio = position switch
            {
                PlayerPosition.Goalkeeper => team == TeamSide.Home ? 0.02f : 0.98f,
                PlayerPosition.Defender => team == TeamSide.Home ? 0.18f : 0.82f,
                PlayerPosition.Midfielder => team == TeamSide.Home ? 0.42f : 0.58f,
                PlayerPosition.Forward => team == TeamSide.Home ? 0.72f : 0.28f,
                _ => 0.5f
            };
            return PitchDimensions.Width * ratio;
        }

        public static void UpdateAIPlayer(Player player, float dt, MatchState match, Dictionary<string, Team> teams, Dictionary<string, List<Player>> teamPlayers)
        {
            float baseSpeed = 200f + (player.Stats.Speed / 100f) * 150f;
            baseSpeed *= 0.7f + (player.Stats.Stamina / 100f) * 0.3f;

            // Find ball carrier
            var ballCarrier = teamPlayers.Values.SelectMany(p => p).FirstOrDefault(p => p.HasBall && !p.IsSubstitute);
            bool ballIsLoose = ballCarrier == null && match.Ball.Speed > 15f;

            float targetX, targetY;
            bool shouldKick = false;

            if (player.HasBall)
            {
                // PLAYER WITH BALL - must act immediately
                (targetX, targetY) = GetAttackingTarget(player, match, teamPlayers);
                shouldKick = true; // Always try to pass/shoot when we have ball
            }
            else if (ballIsLoose)
            {
                // BALL LOOSE - limited chasers based on position
                (targetX, targetY) = GetLooseBallTarget(player, match, teamPlayers, ballCarrier);
            }
            else if (ballCarrier != null)
            {
                // OPPONENT OR TEammate HAS BALL
                if (ballCarrier.Team != player.Team)
                {
                    // Opponent has ball - defend but maintain shape
                    (targetX, targetY) = GetDefensiveTarget(player, ballCarrier, match, teamPlayers);
                }
                else
                {
                    // Teammate has ball - MAKE A RUN to create space
                    (targetX, targetY) = GetSupportRunTarget(player, ballCarrier, match, teamPlayers);
                }
            }
            else
            {
                // NO BALL - maintain formation shape
                (targetX, targetY) = GetFormationTarget(player, match, teamPlayers);
            }

            MovePlayer(player, targetX, targetY, baseSpeed, dt);

            // Kick the ball if we have it and decided to
            if (shouldKick && match.Ball.KickCooldown <= 0 && player.HasBall)
            {
                ExecuteKick(player, match, teamPlayers, teams);
            }
        }

        private static (float, float) GetAttackingTarget(Player player, MatchState match, Dictionary<string, List<Player>> teamPlayers)
        {
            var opponents = teamPlayers.Values.SelectMany(p => p)
                .Where(p => p.Team != player.Team && !p.IsSubstitute && !p.IsKnockedDown)
                .ToList();

            // Find best teammate to pass to
            var teammates = teamPlayers.Values.SelectMany(p => p)
                .Where(p => p.Team == player.Team && p.Id != player.Id && !p.IsSubstitute && !p.IsKnockedDown && p.Position != PlayerPosition.Goalkeeper)
                .ToList();

            Player? bestPass = null;
            float bestScore = -1;

            foreach (var teammate in teammates)
            {
                // Must be ahead (support the attack)
                bool isAhead = (player.Team == TeamSide.Home && teammate.X > player.X) ||
                               (player.Team == TeamSide.Away && teammate.X < player.X);

                if (!isAhead) continue;

                float space = CalculateSpace(teammate, opponents);
                float forwardProgress = player.Team == TeamSide.Home ? teammate.X / PitchDimensions.Width : (PitchDimensions.Width - teammate.X) / PitchDimensions.Width;
                
                // Prioritize players with space who are forward
                float score = space * 0.5f + forwardProgress * 100f;
                if (score > bestScore && space > 40f)
                {
                    bestScore = score;
                    bestPass = teammate;
                }
            }

            // If we have a good pass, move toward them
            if (bestPass != null)
            {
                return (bestPass.X + (player.Team == TeamSide.Home ? -25f : 25f), bestPass.Y);
            }

            // Otherwise drive toward goal
            float goalX = player.Team == TeamSide.Home ? PitchDimensions.Width : 0;
            return (goalX, PitchDimensions.CenterY);
        }

        private static (float, float) GetLooseBallTarget(Player player, MatchState match, Dictionary<string, List<Player>> teamPlayers, Player? ballCarrier)
        {
            // Goalkeeper stays on goal line
            if (player.Position == PlayerPosition.Goalkeeper)
            {
                float goalX = player.Team == TeamSide.Home ? 30f : PitchDimensions.Width - 30f;
                return (goalX, PitchDimensions.CenterY);
            }

            // Count how many of our players are already chasing
            var ourPlayers = teamPlayers.Values.SelectMany(p => p)
                .Where(p => p.Team == player.Team && !p.HasBall && !p.IsSubstitute && !p.IsKnockedDown)
                .ToList();

            var chasers = ourPlayers.Where(p => 
                MathF.Sqrt(MathF.Pow(match.Ball.X - p.X, 2) + MathF.Pow(match.Ball.Y - p.Y, 2)) < 150f).ToList();

            // Position-based limits on who can chase
            if (player.Position == PlayerPosition.Defender || player.Position == PlayerPosition.Forward)
            {
                var positionChasers = chasers.Where(p => p.Position == player.Position).ToList();
                
                // Forwards and defenders: max 1-2 per position type chasing
                // BUT forwards should NOT chase back to their own half
                if (player.Position == PlayerPosition.Forward)
                {
                    bool ballInAttackingThird = (player.Team == TeamSide.Home && match.Ball.X > PitchDimensions.Width * 0.5f) ||
                                              (player.Team == TeamSide.Away && match.Ball.X < PitchDimensions.Width * 0.5f);
                    
                    if (!ballInAttackingThird && positionChasers.Count >= 1)
                    {
                        // Stay forward, make attacking run
                        return GetFormationTarget(player, match, teamPlayers);
                    }
                }
                else if (player.Position == PlayerPosition.Defender)
                {
                    // Defenders shouldn't chase forward passes
                    bool ballNearOurGoal = (player.Team == TeamSide.Home && match.Ball.X > PitchDimensions.Width * 0.4f) ||
                                          (player.Team == TeamSide.Away && match.Ball.X < PitchDimensions.Width * 0.6f);
                    
                    if (!ballNearOurGoal && positionChasers.Count >= 2)
                    {
                        return GetFormationTarget(player, match, teamPlayers);
                    }
                }
            }

            // Chase with variation to prevent clumping
            float angle = MathF.Atan2(match.Ball.Y - player.Y, match.Ball.X - player.X);
            float offset = _random.Next(-50, 50);
            float chaseX = match.Ball.X - MathF.Cos(angle) * offset;
            float chaseY = match.Ball.Y - MathF.Sin(angle) * offset;

            return (Math.Clamp(chaseX, 30f, PitchDimensions.Width - 30f), 
                    Math.Clamp(chaseY, 50f, PitchDimensions.Height - 50f));
        }

        private static (float, float) GetSupportRunTarget(Player supporter, Player ballCarrier, MatchState match, Dictionary<string, List<Player>> teamPlayers)
        {
            // Don't make support runs if too close to ball carrier
            float distToCarrier = MathF.Sqrt(MathF.Pow(ballCarrier.X - supporter.X, 2) + MathF.Pow(ballCarrier.Y - supporter.Y, 2));
            if (distToCarrier < 80f)
            {
                // Too close - maintain distance
                return GetFormationTarget(supporter, match, teamPlayers);
            }

            // Position ahead of ball carrier
            float aheadDistance = 70f + _random.Next(20, 80);
            float aheadAngle = supporter.Team == TeamSide.Home ? 0f : MathF.PI;
            
            float runX = ballCarrier.X + (float)(MathF.Cos(aheadAngle) * aheadDistance);
            float runY = ballCarrier.Y + (supporter.Id.GetHashCode() % 200) - 100f; // Use player ID for consistent spread

            // Check for congestion with teammates
            var teammates = teamPlayers.Values.SelectMany(p => p)
                .Where(p => p.Team == supporter.Team && !p.IsSubstitute && p.Id != supporter.Id)
                .ToList();

            foreach (var tm in teammates)
            {
                float toTm = MathF.Sqrt(MathF.Pow(tm.X - runX, 2) + MathF.Pow(tm.Y - runY, 2));
                if (toTm < 60f)
                {
                    // Too close to teammate - adjust
                    runY += _random.Next(80, 120) * (tm.Id.GetHashCode() % 2 == 0 ? 1 : -1);
                    runX += _random.Next(-30, 30);
                    break;
                }
            }

            return (Math.Clamp(runX, 30f, PitchDimensions.Width - 30f), 
                    Math.Clamp(runY, 60f, PitchDimensions.Height - 60f));
        }

        private static (float, float) GetDefensiveTarget(Player player, Player opponentCarrier, MatchState match, Dictionary<string, List<Player>> teamPlayers)
        {
            // Goalkeeper stays on goal line
            if (player.Position == PlayerPosition.Goalkeeper)
            {
                float goalX = player.Team == TeamSide.Home ? 30f : PitchDimensions.Width - 30f;
                float targetY = Math.Clamp(opponentCarrier.Y, 
                    PitchDimensions.CenterY - PitchDimensions.GoalWidth / 2 + 10,
                    PitchDimensions.CenterY + PitchDimensions.GoalWidth / 2 - 10);
                return (goalX, targetY);
            }

            // Check if opponent is in dangerous position
            bool opponentThreatening = (player.Team == TeamSide.Home && opponentCarrier.X > PitchDimensions.Width * 0.6f) ||
                                     (player.Team == TeamSide.Away && opponentCarrier.X < PitchDimensions.Width * 0.4f);

            if (opponentThreatening)
            {
                float dist = MathF.Sqrt(MathF.Pow(opponentCarrier.X - player.X, 2) + MathF.Pow(opponentCarrier.Y - player.Y, 2));
                if (dist < 200f)
                {
                    // Position to intercept or delay
                    float angle = MathF.Atan2(opponentCarrier.Y - player.Y, opponentCarrier.X - player.X);
                    return (opponentCarrier.X - MathF.Cos(angle) * 40f,
                            opponentCarrier.Y - MathF.Sin(angle) * 40f);
                }
            }

            // Hold defensive shape
            return GetFormationTarget(player, match, teamPlayers);
        }

        private static (float, float) GetFormationTarget(Player player, MatchState match, Dictionary<string, List<Player>> teamPlayers)
        {
            float baseX = GetFormationX(player.Position, player.Team);
            
            // Spread players vertically to cover space
            var samePosition = teamPlayers.Values.SelectMany(p => p)
                .Where(p => p.Team == player.Team && p.Position == player.Position && !p.IsSubstitute)
                .ToList();

            int index = samePosition.IndexOf(player);
            if (index < 0) index = 0;

            // Spread based on position: more spread for wider positions
            float verticalSpread = samePosition.Count > 1
                ? (index - (samePosition.Count - 1) / 2f) * (PitchDimensions.Height * 0.35f / Math.Max(1, samePosition.Count - 1))
                : (_random.Next(-30, 30));

            // Add slight variation to avoid perfect lines
            baseX += _random.Next(-15, 15);

            return (Math.Clamp(baseX, 20f, PitchDimensions.Width - 20f), 
                    Math.Clamp(PitchDimensions.CenterY + verticalSpread, 60f, PitchDimensions.Height - 60f));
        }

        private static void MovePlayer(Player player, float targetX, float targetY, float speed, float dt)
        {
            float dx = targetX - player.X;
            float dy = targetY - player.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist > 5)
            {
                player.Direction = MathF.Atan2(dy, dx);
                player.Speed = MathF.Min(speed, dist / 0.05f);
                player.TargetX = player.X + MathF.Cos(player.Direction) * player.Speed * dt;
                player.TargetY = player.Y + MathF.Sin(player.Direction) * player.Speed * dt;
            }
            else
            {
                player.Speed = 0;
            }
        }

        private static float CalculateSpace(Player player, List<Player> opponents)
        {
            if (opponents.Count == 0) return 500f;
            return opponents.Average(o => MathF.Sqrt(MathF.Pow(o.X - player.X, 2) + MathF.Pow(o.Y - player.Y, 2)));
        }

        private static void ExecuteKick(Player player, MatchState match, Dictionary<string, List<Player>> teamPlayers, Dictionary<string, Team> teams)
        {
            // Close to goal - shoot
            float goalDist = player.Team == TeamSide.Home ? Math.Abs(PitchDimensions.Width - player.X) : player.X;
            
            if (goalDist < 180f)
            {
                PerformShot(player, match);
                return;
            }

            // Find forward pass
            var opponents = teamPlayers.Values.SelectMany(p => p)
                .Where(p => p.Team != player.Team && !p.IsSubstitute && !p.IsKnockedDown)
                .ToList();

            var teammates = teamPlayers.Values.SelectMany(p => p)
                .Where(p => p.Team == player.Team && p.Id != player.Id && !p.IsSubstitute && !p.IsKnockedDown && p.Position != PlayerPosition.Goalkeeper)
                .ToList();

            Player? bestPass = null;
            float bestSpace = 0;

            foreach (var tm in teammates)
            {
                bool isAhead = (player.Team == TeamSide.Home && tm.X > player.X + 20) ||
                               (player.Team == TeamSide.Away && tm.X < player.X - 20);
                if (!isAhead) continue;

                float space = CalculateSpace(tm, opponents);
                if (space > 50f && space > bestSpace)
                {
                    bestSpace = space;
                    bestPass = tm;
                }
            }

            if (bestPass != null)
            {
                PerformPass(player, bestPass, match);
            }
            else
            {
                // Long ball forward
                PerformLongBall(player, match);
            }
        }

        private static void PerformPass(Player passer, Player receiver, MatchState match)
        {
            float power = (passer.Stats.Passing / 100f) * 400f + 200f;
            float accuracy = passer.Stats.Accuracy / 100f;

            float dx = receiver.X - passer.X;
            float dy = receiver.Y - passer.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist > 5)
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

        private static void PerformShot(Player shooter, MatchState match)
        {
            float power = (shooter.Stats.ShotStrength / 100f) * 500f + 250f;
            float accuracy = shooter.Stats.Accuracy / 100f;

            float goalY = PitchDimensions.CenterY + (shooter.Id.GetHashCode() % 50) - 25;
            float goalX = shooter.Team == TeamSide.Home ? PitchDimensions.Width : 0;

            float dx = goalX - shooter.X;
            float dy = goalY - shooter.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist > 5)
            {
                float angle = MathF.Atan2(dy, dx);
                float error = (1f - accuracy) * 0.2f;
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

        private static void PerformLongBall(Player player, MatchState match)
        {
            float power = (player.Stats.Passing / 100f) * 350f + 250f;
            float goalX = player.Team == TeamSide.Home ? PitchDimensions.Width : 0;

            float dx = goalX - player.X;
            float dy = PitchDimensions.CenterY - player.Y + (player.Id.GetHashCode() % 40) - 20;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist > 5)
            {
                float angle = MathF.Atan2(dy, dx);
                float error = (1f - player.Stats.Accuracy / 100f) * 0.2f;
                angle += (float)(_random.NextDouble() - 0.5) * error;

                match.Ball.VelocityX = MathF.Cos(angle) * power;
                match.Ball.VelocityY = MathF.Sin(angle) * power;
                match.Ball.Speed = power;
                match.Ball.LastTouchedBy = player;
                match.Ball.IsAerial = true;
                match.Ball.ZPosition = 30f;
                match.Ball.VisualScale = 1.3f;
                match.Ball.KickCooldown = 0.5f;
                player.HasBall = false;
            }
        }
    }
}