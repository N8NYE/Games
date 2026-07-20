using SockerGame.Core.Enums;

namespace SockerGame.Core.Models
{
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
}