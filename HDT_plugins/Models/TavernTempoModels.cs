using System.Collections.Generic;

namespace HDTplugins.Models
{
    public class TavernTempoSummary
    {
        public int TotalMatches { get; set; }
        public double OverallAveragePlacement { get; set; }
        public List<TavernTempoTierSection> Sections { get; set; } = new List<TavernTempoTierSection>();
    }

    public class TavernTempoTierSection
    {
        public int TavernTier { get; set; }
        public List<TavernTempoBucketRow> Buckets { get; set; } = new List<TavernTempoBucketRow>();
    }

    public class TavernTempoBucketRow
    {
        public string BucketKey { get; set; }
        public int MatchCount { get; set; }
        public double MatchRate { get; set; }
        public double? AveragePlacement { get; set; }
        public double? PlacementDelta { get; set; }
        public bool HasData => MatchCount > 0;
    }
}
