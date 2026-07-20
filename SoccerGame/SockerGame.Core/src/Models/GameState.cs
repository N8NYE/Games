using SockerGame.Core.Enums;

namespace SockerGame.Core.Models
{
    public class GameState
    {
        public GamePhase CurrentPhase { get; set; } = GamePhase.MainMenu;
        public MatchState? CurrentMatch { get; set; }
        public LobbyState? CurrentLobby { get; set; }
        public Player? LocalPlayer { get; set; }
        public List<Player> Players { get; set; } = new();
        public Dictionary<string, Team> Teams { get; set; } = new();
    }
}