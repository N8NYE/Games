namespace SockerGame.Core.Models
{
    public class Ball
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float VelocityX { get; set; }
        public float VelocityY { get; set; }
        public float Speed { get; set; }
        public bool IsInPlay { get; set; } = true;
        public Player? LastTouchedBy { get; set; }
        public float KickCooldown { get; set; } = 0; // prevents immediate re-possession after kick
        public bool IsAerial { get; set; } = false; // true when ball is kicked in the air
        public float VisualScale { get; set; } = 1.0f; // scales the ball for aerial effect
        public float ZPosition { get; set; } = 0; // height above ground for aerial kicks
    }
}