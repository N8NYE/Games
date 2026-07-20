namespace SockerGame.Core.Models
{
    public enum NetworkMessageType
    {
        JoinLobby,
        LeaveLobby,
        LobbyUpdate,
        PlayerJoined,
        PlayerLeft,
        GameStart,
        GameStateUpdate,
        PlayerInput,
        MatchEvent,
        JerseyUpdate,
        PlayerCustomization,
        PlayerStatsUpdate,
        Kickoff,
        Substitution,
        ChatMessage,
        VoiceChatEnabled
    }
}