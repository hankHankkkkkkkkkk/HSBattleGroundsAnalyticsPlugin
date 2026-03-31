namespace HDTplugins.Models
{
    public class BgBoardMinionSnapshot
    {
        public string CardId { get; set; }
        public string Name { get; set; }
        public bool IsGolden { get; set; }
        public string Race { get; set; }
        public int Position { get; set; }
        public int Attack { get; set; }
        public int Health { get; set; }
        public BgKeywordState Keywords { get; set; } = new BgKeywordState();
    }
}
