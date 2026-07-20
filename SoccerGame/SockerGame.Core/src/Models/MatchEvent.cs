using SockerGame.Core.Enums;

namespace SockerGame.Core.Models
{
    public class MatchEvent
    {
        public MatchEventType Type { get; set; } = MatchEventType.Goal;
        public string PlayerId { get; set; } = "";
        public string Username { get; set; } = "";
        public float Timestamp { get; set; }
        public string Description { get; set; } = "";
    }
}