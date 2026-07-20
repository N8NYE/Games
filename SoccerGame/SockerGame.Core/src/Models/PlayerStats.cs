namespace SockerGame.Core.Models
{
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
}