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

    public class LobbyState
    {
        public string LobbyName { get; set; } = "";
        public string Password { get; set; } = "";
        public string HostPlayerId { get; set; } = "";
        public int MaxPlayers { get; set; } = 30;
        public List<string> ConnectedPlayerIds { get; set; } = new();
        public bool GameStarted { get; set; }
    }

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

    public class PlayerStats
    {
        public int Speed { get; set; } = 75;
        public int ShotStrength { get; set; } = 75;
        public int Passing { get; set; } = 75;
        public int Dribbling { get; set; } = 75;
        public int Defense { get; set; } = 75;
        public int Stamina { get; set; } = 75;
        public int Aggression { get; set; } = 75;
        public int Jumping { get; set; } = 75;
        public int Accuracy { get; set; } = 75;
        public int Reflexes { get; set; } = 75;

        public int StatPointsRemaining { get; set; } = 100;

        public bool TrySetStat(string name, int value)
        {
            int currentValue = name switch
            {
                nameof(Speed) => Speed,
                nameof(ShotStrength) => ShotStrength,
                nameof(Passing) => Passing,
                nameof(Dribbling) => Dribbling,
                nameof(Defense) => Defense,
                nameof(Stamina) => Stamina,
                nameof(Aggression) => Aggression,
                nameof(Jumping) => Jumping,
                nameof(Accuracy) => Accuracy,
                nameof(Reflexes) => Reflexes,
                _ => throw new ArgumentException($"Unknown stat: {name}")
            };

            int pointDiff = value - currentValue;
            if (pointDiff > StatPointsRemaining) return false;

            StatPointsRemaining -= pointDiff;

            switch (name)
            {
                case nameof(Speed): Speed = value; break;
                case nameof(ShotStrength): ShotStrength = value; break;
                case nameof(Passing): Passing = value; break;
                case nameof(Dribbling): Dribbling = value; break;
                case nameof(Defense): Defense = value; break;
                case nameof(Stamina): Stamina = value; break;
                case nameof(Aggression): Aggression = value; break;
                case nameof(Jumping): Jumping = value; break;
                case nameof(Accuracy): Accuracy = value; break;
                case nameof(Reflexes): Reflexes = value; break;
            }
            return true;
        }

        public int GetStat(string name) => name switch
        {
            nameof(Speed) => Speed,
            nameof(ShotStrength) => ShotStrength,
            nameof(Passing) => Passing,
            nameof(Dribbling) => Dribbling,
            nameof(Defense) => Defense,
            nameof(Stamina) => Stamina,
            nameof(Aggression) => Aggression,
            nameof(Jumping) => Jumping,
            nameof(Accuracy) => Accuracy,
            nameof(Reflexes) => Reflexes,
            _ => 0
        };

        public static string[] StatNames => new[]
        {
            nameof(Speed), nameof(ShotStrength), nameof(Passing),
            nameof(Dribbling), nameof(Defense), nameof(Stamina),
            nameof(Aggression), nameof(Jumping), nameof(Accuracy),
            nameof(Reflexes)
        };
    }

    public class PlayerAppearance
    {
        public int HairStyle { get; set; }
        public int HairColor { get; set; }
        public int EyeColor { get; set; }
        public int SkinTone { get; set; }
        public int FacialHair { get; set; }
        public int Height { get; set; } = 50; // 0-100 scale
        public int Build { get; set; } = 50; // 0-100 scale
    }

    public class Team
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public TeamSide Side { get; set; }
        public JerseyDesign Jersey { get; set; } = new();
        public List<string> PlayerIds { get; set; } = new();
        public string? CoachPlayerId { get; set; }
        // Formation: 4-4-2 default
        public int[] Formation { get; set; } = new[] { 1, 4, 4, 2 };
        public int Score { get; set; }
    }

    public class JerseyDesign
    {
        public string PrimaryColor { get; set; } = "#FF0000";
        public string SecondaryColor { get; set; } = "#FFFFFF";
        public int Pattern { get; set; }
        public int[,]? PixelArt { get; set; } // 32x32 pixel art grid
        public int PixelWidth { get; set; } = 32;
        public int PixelHeight { get; set; } = 32;
    }

    // Network messages
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

    public class MatchEventRecord
    {
        public MatchEventType Type { get; set; }
        public string PlayerId { get; set; } = "";
        public string Username { get; set; } = "";
        public float Timestamp { get; set; }
        public string Description { get; set; } = "";
    }

    public enum MatchEventType
    {
        Goal, Foul, Card, Substitution, Corner, Offside, Save
    }

    public class NetworkMessage
    {
        public NetworkMessageType Type { get; set; }
        public string SenderId { get; set; } = "";
        public string Payload { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class PitchDimensions
    {
        public const float Width = 1200f;
        public const float Height = 700f;
        public const float CenterX = Width / 2f;
        public const float CenterY = Height / 2f;
        public const float PenaltyAreaWidth = 160f;
        public const float PenaltyAreaHeight = 330f;
        public const float GoalWidth = 73f;
        public const float GoalHeight = 24f;
        public const float CenterCircleRadius = 91.5f;
        public const float CornerArcRadius = 10f;
        public const float PenaltySpotDistance = 110f;
        public const float GoalAreaWidth = 55f;
        public const float GoalAreaHeight = 165f;
        public const float PlayerRadius = 10f;
        public const float BallRadius = 5f;
        public const float LineWidth = 2f;
    }

    public class InputState
    {
        public float MoveX { get; set; }
        public float MoveY { get; set; }
        public float MouseX { get; set; }
        public float MouseY { get; set; }
        public bool LeftClick { get; set; }
        public bool RightClick { get; set; }
        public bool LeftClickHeld { get; set; }
        public bool RightClickHeld { get; set; }
        public bool ShiftHeld { get; set; }
        public bool SpaceHeld { get; set; }
        public float LeftChargeTime { get; set; }
        public float RightChargeTime { get; set; }
    }
}