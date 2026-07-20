namespace SockerGame.Server
{
    public class JerseyUpdateData
    {
        public string TeamId { get; set; } = "";
        public string PrimaryColor { get; set; } = "#FF0000";
        public string SecondaryColor { get; set; } = "#FFFFFF";
        public int[,]? PixelArt { get; set; }
    }
}