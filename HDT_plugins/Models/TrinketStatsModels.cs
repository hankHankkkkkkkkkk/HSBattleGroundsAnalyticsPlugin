using System.Collections.Generic;

namespace HDTplugins.Models
{
    public enum TrinketFilter
    {
        All,
        Lesser,
        Greater
    }

    public class TrinketStatsSummary
    {
        public TrinketFilter Filter { get; set; }
        public int EligibleMatches { get; set; }
        public List<TrinketStatsRow> Rows { get; set; } = new List<TrinketStatsRow>();
    }

    public class TrinketStatsRow
    {
        public string CardId { get; set; }
        public string CardName { get; set; }
        public int MatchCount { get; set; }
        public double PickRate { get; set; }
        public double FirstRate { get; set; }
        public double ScoreRate { get; set; }
    }
}
