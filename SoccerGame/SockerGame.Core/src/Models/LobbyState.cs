namespace SockerGame.Core.Models
{
    public class LobbyState
    {
        public string LobbyName { get; set; } = "";
        public string Password { get; set; } = "";
        public string HostPlayerId { get; set; } = "";
        public int MaxPlayers { get; set; } = 30;
        public List<string> ConnectedPlayerIds { get; set; } = new();
        public bool GameStarted { get; set; }
    }
}