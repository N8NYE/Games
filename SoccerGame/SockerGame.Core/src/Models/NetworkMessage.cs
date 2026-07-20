namespace SockerGame.Core.Models
{
    public class NetworkMessage
    {
        public NetworkMessageType Type { get; set; }
        public string SenderId { get; set; } = "";
        public string Payload { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}