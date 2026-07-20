namespace SockerGame.Core.Models
{
    public class JerseyDesign
    {
        public string PrimaryColor { get; set; } = "#FF0000";
        public string SecondaryColor { get; set; } = "#FFFFFF";
        public int Pattern { get; set; }
        public int[,]? PixelArt { get; set; } // 32x32 pixel art grid
        public int PixelWidth { get; set; } = 32;
        public int PixelHeight { get; set; } = 32;
    }
}