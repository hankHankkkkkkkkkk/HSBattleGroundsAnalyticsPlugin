using System.Collections.Generic;

namespace HDTplugins.Models
{
    public class RaceStatsRow
    {
        public string RaceCode { get; set; }
        public string RaceTag { get; set; }
        public string RaceName { get; set; }
        public int MatchCount { get; set; }
        public double PickRate { get; set; }
        public double? AveragePlacement { get; set; }
        public double FirstRate { get; set; }
        public double ScoreRate { get; set; }
        public List<RaceCardUsage> TopCards { get; set; } = new List<RaceCardUsage>();
        public List<RaceSynergyStat> BestSynergies { get; set; } = new List<RaceSynergyStat>();
        public List<RaceSynergyStat> WorstSynergies { get; set; } = new List<RaceSynergyStat>();
        public List<RaceTagUsage> TopLineups { get; set; } = new List<RaceTagUsage>();

        public bool HasData => MatchCount > 0 && AveragePlacement.HasValue;
    }

    public class RaceCardUsage
    {
        public string CardId { get; set; }
        public string CardName { get; set; }
        public int Count { get; set; }
        public double Rate { get; set; }
    }

    public class RaceSynergyStat
    {
        public string RaceCode { get; set; }
        public string RaceName { get; set; }
        public int MatchCount { get; set; }
        public double AveragePlacement { get; set; }
        public double PlacementDelta { get; set; }
    }

    public class RaceTagUsage
    {
        public string Tag { get; set; }
        public int Count { get; set; }
        public double Rate { get; set; }
    }
}
