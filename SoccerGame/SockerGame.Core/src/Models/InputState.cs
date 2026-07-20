namespace SockerGame.Core.Models
{
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