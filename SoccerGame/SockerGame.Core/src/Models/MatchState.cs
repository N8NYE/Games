using SockerGame.Core.Enums;

namespace SockerGame.Core.Models
{
    public class MatchState
    {
        public int HomeScore { get; set; }
        public int AwayScore { get; set; }
        public int MatchMinute { get; set; } = 0;
        public int MatchSecond { get; set; } = 0;
        public float RealTimeElapsed { get; set; }
        public const int TotalMatchMinutes = 90;
        public const float RealTimePerMatchSecond = 20f / 90f / 60f; // 20 minutes real time = 90 game minutes
        public GamePhase CurrentHalf { get; set; } = GamePhase.FirstHalf;
        public TeamSide Possession { get; set; }
        public Ball Ball { get; set; } = new();
        public Dictionary<string, List<Player>> TeamPlayers { get; set; } = new();
        public List<MatchEvent> Events { get; set; } = new();
        public string? HomeTeamId { get; set; }
        public string? AwayTeamId { get; set; }
        // Referee call display
        public string RefereeCall { get; set; } = "";
        public float RefereeCallTimer { get; set; } = 0;
    }
}