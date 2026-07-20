using SockerGame.Core.Enums;

namespace SockerGame.Core.Models
{
    public class Player
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Username { get; set; } = "";
        public PlayerPosition Position { get; set; } = PlayerPosition.Midfielder;
        public TeamSide Team { get; set; }
        public PlayerStats Stats { get; set; } = new();
        public PlayerAppearance Appearance { get; set; } = new();
        public bool IsCoach { get; set; }
        public bool IsHuman { get; set; }
        public bool IsSubstitute { get; set; }
        public bool IsOnField { get; set; } = true;
        // Position on field
        public float X { get; set; }
        public float Y { get; set; }
        public float TargetX { get; set; }
        public float TargetY { get; set; }
        public float Direction { get; set; }
        public float Speed { get; set; }
        public bool HasBall { get; set; }
        public int Cards { get; set; } // 0 none, 1 yellow, 2 red
        public bool IsKnockedDown { get; set; }
        public float KnockdownTimer { get; set; }
    }
}