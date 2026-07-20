namespace SockerGame.Core.Enums
{
    public enum GamePhase
    {
        MainMenu,
        Lobby,
        TeamSelection,
        PlayerCustomization,
        CoinToss,
        Kickoff,
        FirstHalf,
        Halftime,
        SecondHalf,
        FullTime,
        ExtraTime,
        Penalties,
        MatchEnd
    }

    public enum PlayerPosition
    {
        Goalkeeper,
        Defender,
        Midfielder,
        Forward,
        Substitute
    }

    public enum TeamSide
    {
        Home,
        Away
    }

    public enum MatchEvent
    {
        Kickoff,
        Goal,
        Corner,
        FreeKick,
        ThrowIn,
        GoalKick,
        Offside,
        Foul,
        YellowCard,
        RedCard,
        Substitution,
        HalfTime,
        FullTime
    }

    public enum KickDirection
    {
        Left,
        Right
    }

    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        InLobby,
        InGame
    }
}