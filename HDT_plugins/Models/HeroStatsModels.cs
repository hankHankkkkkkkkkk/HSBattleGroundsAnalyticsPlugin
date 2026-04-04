using System.Collections.Generic;

namespace HDTplugins.Models
{
    public sealed class HeroStatsRow
    {
        public string HeroCardId { get; set; }
        public string HeroName { get; set; }
        public int Picks { get; set; }
        public int OfferedCount { get; set; }
        public int ValidPickSamples { get; set; }
        public double AveragePlacement { get; set; }
        public int Firsts { get; set; }
        public int ScoreFinishes { get; set; }
        public int Lasts { get; set; }
        public double PickRate { get; set; }
        public double FirstRate { get; set; }
        public double ScoreRate { get; set; }
        public double LastRate { get; set; }
        public HeroContributionResult Contribution { get; set; } = new HeroContributionResult();
        public List<HeroRaceAffinityStat> BestRaces { get; set; } = new List<HeroRaceAffinityStat>();
        public List<HeroRaceAffinityStat> WorstRaces { get; set; } = new List<HeroRaceAffinityStat>();

        public double ContributionValue => Contribution == null ? 0 : Contribution.FinalContribution;
        public double ContributionPerGame => Picks <= 0 ? 0 : ContributionValue / Picks;
        public bool HasPickRateData => OfferedCount > 0;
        public bool HasData => Picks > 0;
    }

    public sealed class HeroContributionResult
    {
        public double BaseContribution { get; set; }
        public double Delta { get; set; }
        public double ExtraContributionRaw { get; set; }
        public double SampleWeight { get; set; }
        public double ExtraContributionFinal { get; set; }
        public double FinalContribution { get; set; }
    }

    public sealed class HeroRaceAffinityStat
    {
        public string RaceCode { get; set; }
        public string RaceName { get; set; }
        public int MatchCount { get; set; }
        public double AveragePlacement { get; set; }
        public double PlacementDelta { get; set; }
    }

    public sealed class HeroStatsSummary
    {
        public int TotalMatches { get; set; }
        public double OverallAveragePlacement { get; set; }
        public List<HeroStatsRow> Heroes { get; set; } = new List<HeroStatsRow>();
    }
}
