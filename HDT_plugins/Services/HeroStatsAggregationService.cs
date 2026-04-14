using System;
using System.Collections.Generic;
using System.Linq;
using HDTplugins.Models;

namespace HDTplugins.Services
{
    public class HeroStatsAggregationService
    {
        private const double MinPlacement = 1.0;
        private const double MaxPlacement = 8.0;
        private const double Alpha = 4.0;
        private const double DeadZone = 0.30;
        private const double PositiveScale = 2.0;
        private const double NegativeScale = 2.3;
        private const double Power = 1.15;
        private const double SampleK = 8.0;

        public HeroStatsSummary BuildSummary(IReadOnlyList<BgSnapshot> snapshots, double scoreLine)
        {
            var normalizedSnapshots = (snapshots ?? Array.Empty<BgSnapshot>())
                .Where(x => x != null && x.Placement > 0 && !string.IsNullOrWhiteSpace(x.HeroCardId))
                .ToList();

            var summary = new HeroStatsSummary
            {
                TotalMatches = normalizedSnapshots.Count,
                OverallAveragePlacement = normalizedSnapshots.Count == 0
                    ? 0
                    : normalizedSnapshots.Average(x => ClampPlacement(x.Placement))
            };

            if (normalizedSnapshots.Count == 0)
                return summary;

            foreach (var group in normalizedSnapshots.GroupBy(x => x.HeroCardId, StringComparer.OrdinalIgnoreCase))
            {
                var heroSnapshots = group.ToList();
                var row = new HeroStatsRow
                {
                    HeroCardId = group.Key,
                    HeroName = ResolveHeroName(heroSnapshots[0]),
                    Picks = heroSnapshots.Count,
                    OfferedCount = CountOffered(normalizedSnapshots, group.Key),
                    ValidPickSamples = heroSnapshots.Count(HasValidOfferedHeroData),
                    AveragePlacement = heroSnapshots.Average(x => ClampPlacement(x.Placement)),
                    Firsts = heroSnapshots.Count(x => x.Placement == 1),
                    ScoreFinishes = heroSnapshots.Count(x => ClampPlacement(x.Placement) < NormalizeScoreLine(scoreLine)),
                    Lasts = heroSnapshots.Count(x => ClampPlacement(x.Placement) >= MaxPlacement)
                };

                row.PickRate = row.OfferedCount <= 0 ? 0 : row.ValidPickSamples / (double)row.OfferedCount;
                row.FirstRate = row.Picks <= 0 ? 0 : row.Firsts / (double)row.Picks;
                row.ScoreRate = row.Picks <= 0 ? 0 : row.ScoreFinishes / (double)row.Picks;
                row.LastRate = row.Picks <= 0 ? 0 : row.Lasts / (double)row.Picks;
                row.Contribution = CalculateContribution(row.AveragePlacement, summary.OverallAveragePlacement, row.Picks, scoreLine);
                PopulateRaceAffinities(row, heroSnapshots);
                summary.Heroes.Add(row);
            }

            return summary;
        }

        public HeroContributionResult CalculateContribution(double heroAveragePlacement, double overallAveragePlacement, int heroGames, double ratingLine)
        {
            var result = new HeroContributionResult();
            if (heroGames <= 0)
                return result;

            var normalizedHeroPlacement = ClampPlacement(heroAveragePlacement);
            var normalizedOverallPlacement = ClampPlacement(overallAveragePlacement);
            var normalizedRatingLine = NormalizeScoreLine(ratingLine);

            result.BaseContribution = CalculateBaseContribution(normalizedHeroPlacement, normalizedRatingLine);
            result.Delta = CalculateDelta(normalizedHeroPlacement, normalizedOverallPlacement);
            result.ExtraContributionRaw = CalculateExtraContributionRaw(result.Delta);
            result.SampleWeight = CalculateSampleWeight(heroGames);
            result.ExtraContributionFinal = result.SampleWeight * result.ExtraContributionRaw;
            result.FinalContribution = result.BaseContribution + result.ExtraContributionFinal;
            return result;
        }

        private static double CalculateBaseContribution(double heroAveragePlacement, double ratingLine)
        {
            return Alpha * (NormalizeScoreLine(ratingLine) - ClampPlacement(heroAveragePlacement));
        }

        private static double CalculateDelta(double heroAveragePlacement, double overallAveragePlacement)
        {
            return ClampPlacement(overallAveragePlacement) - ClampPlacement(heroAveragePlacement);
        }

        private static double CalculateExtraContributionRaw(double delta)
        {
            if (delta > DeadZone)
                return PositiveScale * Math.Pow(delta - DeadZone, Power);

            if (Math.Abs(delta) <= DeadZone)
                return 0;

            return -NegativeScale * Math.Pow(Math.Abs(delta) - DeadZone, Power);
        }

        private static double CalculateSampleWeight(int heroGames)
        {
            if (heroGames <= 0)
                return 0;

            return heroGames / (heroGames + SampleK);
        }

        private static void PopulateRaceAffinities(HeroStatsRow row, IReadOnlyList<BgSnapshot> heroSnapshots)
        {
            var raceAffinities = heroSnapshots
                .SelectMany(snapshot => (snapshot.AvailableRaces ?? Array.Empty<string>())
                    .Where(race => !string.IsNullOrWhiteSpace(race))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(race => new { RaceCode = race, Snapshot = snapshot }))
                .GroupBy(x => x.RaceCode, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var averagePlacement = group.Average(x => ClampPlacement(x.Snapshot.Placement));
                    return new HeroRaceAffinityStat
                    {
                        RaceCode = group.Key,
                        RaceName = GameTextService.GetRaceName(group.Key, group.Key),
                        MatchCount = group.Count(),
                        AveragePlacement = averagePlacement,
                        PlacementDelta = averagePlacement - row.AveragePlacement
                    };
                })
                .OrderBy(x => x.PlacementDelta)
                .ThenByDescending(x => x.MatchCount)
                .ThenBy(x => x.RaceName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            row.BestRaces = raceAffinities
                .Where(x => x.PlacementDelta < -0.001)
                .Take(3)
                .ToList();

            var bestRaceCodes = new HashSet<string>(row.BestRaces.Select(x => x.RaceCode ?? string.Empty), StringComparer.OrdinalIgnoreCase);
            row.WorstRaces = raceAffinities
                .Where(x => x.PlacementDelta > 0.001 && !bestRaceCodes.Contains(x.RaceCode ?? string.Empty))
                .OrderByDescending(x => x.PlacementDelta)
                .ThenByDescending(x => x.MatchCount)
                .ThenBy(x => x.RaceName, StringComparer.CurrentCultureIgnoreCase)
                .Take(3)
                .ToList();
        }

        private static int CountOffered(IReadOnlyList<BgSnapshot> snapshots, string heroCardId)
        {
            var normalizedHeroCardId = HeroIdNormalizer.Normalize(heroCardId);
            return snapshots.Count(snapshot => HasValidOfferedHeroData(snapshot) && (snapshot.OfferedHeroCardIds ?? Array.Empty<string>())
                .Any(x => string.Equals(HeroIdNormalizer.Normalize(x), normalizedHeroCardId, StringComparison.OrdinalIgnoreCase)));
        }

        private static bool HasValidOfferedHeroData(BgSnapshot snapshot)
        {
            return snapshot != null
                && (snapshot.OfferedHeroCardIds ?? Array.Empty<string>()).Any(x => !string.IsNullOrWhiteSpace(x));
        }

        private static string ResolveHeroName(BgSnapshot snapshot)
        {
            return GameTextService.GetCardName(snapshot.HeroCardId, string.IsNullOrWhiteSpace(snapshot.HeroName) ? snapshot.HeroCardId : snapshot.HeroName);
        }

        private static double ClampPlacement(double placement)
        {
            if (placement < MinPlacement)
                return MinPlacement;
            if (placement > MaxPlacement)
                return MaxPlacement;
            return placement;
        }

        private static double NormalizeScoreLine(double scoreLine)
        {
            if (Math.Abs(scoreLine - 2.5) < 0.01)
                return 2.5;
            if (Math.Abs(scoreLine - 3.5) < 0.01)
                return 3.5;
            return 4.5;
        }
    }
}
