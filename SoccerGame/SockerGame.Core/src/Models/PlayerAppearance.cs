namespace SockerGame.Core.Models
{
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
}