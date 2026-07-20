using SockerGame.Core.Enums;
using SockerGame.Core.Models;
using System.Numerics;

namespace SockerGame.Core.Systems
{
    /// <summary>
    /// Soccer AI Strategy System implementing authentic tactical principles
    /// Based on modern soccer tactics including pressing, overloads, and positional play
    /// </summary>
    public static class AIStrategy
    {
        private static readonly Random _random = new();

        /// <summary>
        /// Updates AI player behavior - aggressive, athletic soccer
        /// </summary>
        public static void UpdateAIPlayer(Player player, float dt, MatchState match, Dictionary<string, Team> teams, Dictionary<string, List<Player>> teamPlayers)
        {
            // Athletic speeds for responsive gameplay
            float baseSpeed = 150f + (player.Stats.Speed / 100f) * 200f;
            baseSpeed *= 0.8f + (player.Stats.Stamina / 100f) * 0.2f;

            float targetX, targetY;

            // Find the ball carrier (if any)
            var ballCarrier = teamPlayers.Values.SelectMany(p => p).FirstOrDefault(p => p.HasBall);
            bool ballIsLoose = ballCarrier == null && match.Ball.Speed > 0;

            if (player.HasBall)
            {
                // BALL CARRIER - ATTACK GOAL NOW
                (targetX, targetY) = GetBallCarrierTarget(player, match, teamPlayers);
            }
            else if (ballIsLoose)
            {
                // BALL IS LOOSE - CHASE IT AGGRESSIVELY
                float distToBall = MathF.Sqrt(MathF.Pow(match.Ball.X - player.X, 2) + MathF.Pow(match.Ball.Y - player.Y, 2));
                if (distToBall < 250f)
                {
                    (targetX, targetY) = (match.Ball.X, match.Ball.Y);
                }
                else
                {
                    var team = teams.Values.FirstOrDefault(t => t.PlayerIds.Contains(player.Id));
                    (targetX, targetY) = team != null ? GetFormationPosition(player, team, teamPlayers) : (PitchDimensions.CenterX, PitchDimensions.CenterY);
                }
            }
            else if (match.Possession == player.Team)
            {
                // TEAM HAS BALL - SUPPORT AGGRESSIVELY
                if (ballCarrier != null)
                {
                    (targetX, targetY) = GetSupportingTarget(player, ballCarrier, match);
                }
                else
                {
                    var team = teams.Values.FirstOrDefault(t => t.PlayerIds.Contains(player.Id));
                    (targetX, targetY) = team != null ? GetFormationPosition(player, team, teamPlayers) : (PitchDimensions.CenterX, PitchDimensions.CenterY);
                }
            }
            else
            {
                // OPPONENT HAS BALL - PRESS HARD
                (targetX, targetY) = DefensiveStrategy(player, match, teamPlayers, teams);
            }

            // MOVE WITH FULL EFFORT
            MoveToPosition(player, targetX, targetY, baseSpeed, dt);
        }

        /// <summary>
        /// Ball carrier attacks goal directly - pass or shoot
        /// </summary>
        private static (float, float) GetBallCarrierTarget(Player player, MatchState match, Dictionary<string, List<Player>> teamPlayers)
        {
            // Always advance toward goal for Home/Away team
            float goalX = player.Team == TeamSide.Home ? PitchDimensions.Width : 0;
            float goalY = PitchDimensions.CenterY;

            // Distance to determine action
            float goalDist = player.Team == TeamSide.Home ? Math.Abs(PitchDimensions.Width - player.X) : player.X;

            // FIND ANY TEammate ahead and pass (high priority)
            var teammates = teamPlayers.Values.SelectMany(p => p)
                .Where(p => p.Team == player.Team && p.Id != player.Id && !p.IsSubstitute)
                .ToList();

            // Look for best pass target - prioritize forward position and manageable distance
            Player? bestPassTarget = null;
            float bestPassScore = -1;

            foreach (var teammate in teammates)
            {
                bool isAhead = (player.Team == TeamSide.Home && teammate.X > player.X) ||
                               (player.Team == TeamSide.Away && teammate.X < player.X);
                float dist = MathF.Sqrt(MathF.Pow(teammate.X - player.X, 2) + MathF.Pow(teammate.Y - player.Y, 2));

                // Score based on being ahead and in passing range
                float score = 0;
                if (isAhead && dist > 20f && dist < 250f) score += 2.0f;
                if (dist > 50f && dist < 200f) score += 1.0f;
                
                if (score > bestPassScore && _random.NextDouble() < 0.7f)
                {
                    bestPassScore = score;
                    bestPassTarget = teammate;
                }
            }

            // PASS if good option found (50% chance)
            if (bestPassTarget != null && _random.NextDouble() < 0.5f)
            {
                PerformAIPass(player, bestPassTarget, match);
                return (player.X, player.Y);
            }

            // SHOOT when close to goal (high chance)
            if (goalDist < 150f && _random.NextDouble() < 0.6f)
            {
                PerformAIShot(player, match);
                return (player.X, player.Y);
            }

            // Otherwise, drive toward goal
            return (goalX, goalY);
        }

        /// <summary>
        /// Supporting players make forward runs to get ahead
        /// </summary>
        private static (float, float) GetSupportingTarget(Player supporter, Player ballCarrier, MatchState match)
        {
            float goalX = supporter.Team == TeamSide.Home ? PitchDimensions.Width : 0;
            
            // Get ahead of the ball carrier for a pass
            float goalDirection = supporter.Team == TeamSide.Home ? 1 : -1;
            float runX = ballCarrier.X + (goalDirection * 100f);
            
            // Spread vertically to create width
            float runY = supporter.Team == TeamSide.Home ? 
                PitchDimensions.CenterY + (float)(_random.NextDouble() - 0.5) * 120f :
                PitchDimensions.CenterY + (float)(_random.NextDouble() - 0.5) * 120f;
            
            runY = Math.Clamp(runY, 50f, PitchDimensions.Height - 50f);
            runX = Math.Clamp(runX, 30f, PitchDimensions.Width - 30f);

            return (runX, runY);
        }

        /// <summary>
        /// Defensive Strategy - all players press the opponent
        /// </summary>
        private static (float, float) DefensiveStrategy(Player player, MatchState match, Dictionary<string, List<Player>> teamPlayers, Dictionary<string, Team> teams)
        {
            var opponentCarrier = teamPlayers.Values.SelectMany(p => p)
                .FirstOrDefault(p => p.HasBall && p.Team != player.Team);

            if (opponentCarrier == null)
            {
                // No ball - stay in formation
                var team = teams.Values.FirstOrDefault(t => t.PlayerIds.Contains(player.Id));
                return team != null ? GetFormationPosition(player, team, teamPlayers) : (PitchDimensions.CenterX, PitchDimensions.CenterY);
            }

            // GOALKEEPER - stay on goal line
            if (player.Position == PlayerPosition.Goalkeeper)
            {
                float gkX = player.Team == TeamSide.Home ? 30f : PitchDimensions.Width - 30f;
                float targetY = Math.Clamp(opponentCarrier.Y, PitchDimensions.CenterY - PitchDimensions.GoalWidth / 2 + 10,
                    PitchDimensions.CenterY + PitchDimensions.GoalWidth / 2 - 10);
                return (gkX, targetY);
            }

            // ALL OTHER PLAYERS - PRESS THE BALL CARRIER
            float distToOpponent = MathF.Sqrt(MathF.Pow(opponentCarrier.X - player.X, 2) + MathF.Pow(opponentCarrier.Y - player.Y, 2));

            // Tackle if close enough
            if (distToOpponent < 45f && !match.Ball.IsAerial && match.Ball.Speed < 150f)
            {
                PerformAITackle(player, opponentCarrier, match);
            }

            // Move toward opponent
            return (opponentCarrier.X, opponentCarrier.Y);
        }

        /// <summary>
        /// Perform a tackle on opponent - based on Defense/Aggression stats
        /// </summary>
        private static void PerformAITackle(Player tackler, Player opponent, MatchState match)
        {
            float tackleChance = 0.5f + (tackler.Stats.Defense / 100f) * 0.4f + (tackler.Stats.Aggression / 100f) * 0.1f;
            
            if (_random.NextDouble() < tackleChance)
            {
                opponent.IsKnockedDown = true;
                opponent.KnockdownTimer = 0.8f;
                opponent.HasBall = false;

                // Card risk
                float cardChance = 0.1f + (tackler.Stats.Aggression / 100f) * 0.2f;
                if (_random.NextDouble() < cardChance)
                {
                    tackler.Cards = 1;
                }

                match.Ball.VelocityX *= 0.6f;
                match.Ball.VelocityY *= 0.6f;
                match.Ball.Speed *= 0.6f;
                match.Ball.KickCooldown = 0.3f;
            }
        }

        /// <summary>
        /// Formation positions - players on correct sides of field
        /// Home team (red) defends left goal, attacks right
        /// Away team (blue) defends right goal, attacks left
        /// </summary>
        private static (float, float) GetFormationPosition(Player player, Team team, Dictionary<string, List<Player>> teamPlayers)
        {
            var samePosition = teamPlayers[team.Id]
                .Where(p => p.Position == player.Position && !p.IsSubstitute)
                .ToList();
            int count = samePosition.Count;
            int index = samePosition.IndexOf(player);
            if (index < 0) index = 0;

            if (player.Position == PlayerPosition.Goalkeeper)
            {
                // Goalkeeper on goal line - Home on left (X=30), Away on right (X=Width-30)
                float gkX = team.Side == TeamSide.Home ? 30f : PitchDimensions.Width - 30f;
                return (gkX, PitchDimensions.CenterY);
            }

            if (player.Position == PlayerPosition.Defender)
            {
                // Home defenders: on left side of field (defending left goal)
                // Away defenders: on right side of field (defending right goal)
                float defX = team.Side == TeamSide.Home ? PitchDimensions.Width * 0.2f : PitchDimensions.Width * 0.8f;
                float spread = count > 1 ? (index - (count - 1) / 2f) * (PitchDimensions.Height * 0.35f / Math.Max(1, count - 1)) : 0;
                return (defX, PitchDimensions.CenterY + spread);
            }

            if (player.Position == PlayerPosition.Midfielder)
            {
                // Home midfielders: center-left, supporting both defense and attack
                // Away midfielders: center-right
                float midX = team.Side == TeamSide.Home ? PitchDimensions.Width * 0.45f : PitchDimensions.Width * 0.55f;
                float spread = count > 1 ? (index - (count - 1) / 2f) * (PitchDimensions.Height * 0.45f / Math.Max(1, count - 1)) : 0;
                return (midX, PitchDimensions.CenterY + spread);
            }

            if (player.Position == PlayerPosition.Forward)
            {
                // Home forwards: on right side, attacking toward away goal
                // Away forwards: on left side, attacking toward home goal
                float fwdX = team.Side == TeamSide.Home ? PitchDimensions.Width * 0.7f : PitchDimensions.Width * 0.3f;
                float spread = count > 1 ? (index - (count - 1) / 2f) * (PitchDimensions.Height * 0.35f / Math.Max(1, count - 1)) : 0;
                return (fwdX, PitchDimensions.CenterY + spread);
            }

            return (PitchDimensions.CenterX, PitchDimensions.CenterY);
        }

        /// <summary>
        /// Move directly to target position
        /// </summary>
        private static void MoveToPosition(Player player, float targetX, float targetY, float speed, float dt)
        {
            float dx = targetX - player.X;
            float dy = targetY - player.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist > 5)
            {
                player.Direction = MathF.Atan2(dy, dx);
                player.Speed = MathF.Min(speed, dist / 0.03f);
                player.TargetX = player.X + MathF.Cos(player.Direction) * player.Speed * dt;
                player.TargetY = player.Y + MathF.Sin(player.Direction) * player.Speed * dt;
            }
            else
            {
                player.Speed = 0;
            }
        }

        /// <summary>
        /// Perform a pass - power based on Passing stat
        /// </summary>
        private static void PerformAIPass(Player passer, Player receiver, MatchState match)
        {
            float power = (passer.Stats.Passing / 100f) * 400f + 150f;
            float accuracy = passer.Stats.Accuracy / 100f;

            float dx = receiver.X - passer.X;
            float dy = receiver.Y - passer.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist > 0)
            {
                float angle = MathF.Atan2(dy, dx);
                float error = (1f - accuracy) * 0.2f;
                angle += (float)(_random.NextDouble() - 0.5) * error;

                match.Ball.VelocityX = MathF.Cos(angle) * power;
                match.Ball.VelocityY = MathF.Sin(angle) * power;
                match.Ball.Speed = power;
                match.Ball.LastTouchedBy = passer;
                match.Ball.IsAerial = false;
                match.Ball.ZPosition = 0;
                match.Ball.VisualScale = 1.0f;
                passer.HasBall = false;
                match.Ball.KickCooldown = 0.3f;
            }
        }

        /// <summary>
        /// Perform a shot on goal - power based on ShotStrength stat
        /// </summary>
        private static void PerformAIShot(Player shooter, MatchState match)
        {
            float power = (shooter.Stats.ShotStrength / 100f) * 550f + 200f;
            float accuracy = shooter.Stats.Accuracy / 100f;

            float goalY = PitchDimensions.CenterY + (float)(_random.NextDouble() - 0.5) * PitchDimensions.GoalWidth * 0.35f;
            float goalX = shooter.Team == TeamSide.Home ? PitchDimensions.Width : 0;

            float dx = goalX - shooter.X;
            float dy = goalY - shooter.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist > 0)
            {
                float angle = MathF.Atan2(dy, dx);
                float error = (1f - accuracy) * 0.3f;
                angle += (float)(_random.NextDouble() - 0.5) * error;

                match.Ball.VelocityX = MathF.Cos(angle) * power;
                match.Ball.VelocityY = MathF.Sin(angle) * power;
                match.Ball.Speed = power;
                match.Ball.LastTouchedBy = shooter;
                match.Ball.IsAerial = false;
                match.Ball.ZPosition = 0;
                match.Ball.VisualScale = 1.0f;
                shooter.HasBall = false;
                match.Ball.KickCooldown = 0.3f;
            }
        }
    }
}