using HDTplugins.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HDTplugins.Services
{
    public class TavernTempoAggregationService
    {
        public TavernTempoSummary BuildSummary(IReadOnlyList<BgSnapshot> snapshots)
        {
            var normalized = (snapshots ?? Array.Empty<BgSnapshot>())
                .Where(x => x != null && x.Placement > 0)
                .ToList();

            var summary = new TavernTempoSummary
            {
                TotalMatches = normalized.Count,
                OverallAveragePlacement = normalized.Count == 0 ? 0 : normalized.Average(x => ClampPlacement(x.Placement))
            };

            summary.Sections.Add(BuildTierSection(normalized, summary, 3, new[]
            {
                new TavernTempoBucketDefinition("TavernTempo_Tier3_BeforeTurn3", turn => turn > 0 && turn < 3),
                new TavernTempoBucketDefinition("TavernTempo_Tier3_Turn3", turn => turn == 3),
                new TavernTempoBucketDefinition("TavernTempo_Tier3_Turn4", turn => turn == 4),
                new TavernTempoBucketDefinition("TavernTempo_Tier3_Turn5", turn => turn == 5),
                new TavernTempoBucketDefinition("TavernTempo_Tier3_AfterTurn5", turn => turn > 5)
            }));

            summary.Sections.Add(BuildTierSection(normalized, summary, 4, new[]
            {
                new TavernTempoBucketDefinition("TavernTempo_Tier4_Turn4", turn => turn == 4),
                new TavernTempoBucketDefinition("TavernTempo_Tier4_Turn5", turn => turn == 5),
                new TavernTempoBucketDefinition("TavernTempo_Tier4_Turn6", turn => turn == 6),
                new TavernTempoBucketDefinition("TavernTempo_Tier4_AfterTurn6", turn => turn > 6)
            }));

            summary.Sections.Add(BuildTierSection(normalized, summary, 5, new[]
            {
                new TavernTempoBucketDefinition("TavernTempo_Tier5_Turn6", turn => turn == 6),
                new TavernTempoBucketDefinition("TavernTempo_Tier5_Turn7", turn => turn == 7),
                new TavernTempoBucketDefinition("TavernTempo_Tier5_AfterTurn7", turn => turn > 7)
            }));

            return summary;
        }

        private static TavernTempoTierSection BuildTierSection(
            IReadOnlyList<BgSnapshot> snapshots,
            TavernTempoSummary summary,
            int tavernTier,
            IReadOnlyList<TavernTempoBucketDefinition> buckets)
        {
            var reached = snapshots
                .Select(snapshot => new
                {
                    Snapshot = snapshot,
                    Turn = GetUpgradeTurn(snapshot, tavernTier)
                })
                .Where(x => x.Turn.HasValue)
                .ToList();

            var section = new TavernTempoTierSection
            {
                TavernTier = tavernTier
            };

            foreach (var bucket in buckets)
            {
                var bucketMatches = reached
                    .Where(x => bucket.MatchTurn(x.Turn.Value))
                    .Select(x => x.Snapshot)
                    .ToList();

                var averagePlacement = bucketMatches.Count == 0
                    ? (double?)null
                    : bucketMatches.Average(x => ClampPlacement(x.Placement));

                section.Buckets.Add(new TavernTempoBucketRow
                {
                    BucketKey = bucket.Key,
                    MatchCount = bucketMatches.Count,
                    MatchRate = summary.TotalMatches == 0 ? 0 : bucketMatches.Count / (double)summary.TotalMatches,
                    AveragePlacement = averagePlacement,
                    PlacementDelta = averagePlacement.HasValue ? averagePlacement.Value - summary.OverallAveragePlacement : (double?)null
                });
            }

            return section;
        }

        private static int? GetUpgradeTurn(BgSnapshot snapshot, int tavernTier)
        {
            if (snapshot?.TavernUpgradeTimeline == null || snapshot.TavernUpgradeTimeline.Count == 0)
                return null;

            var match = snapshot.TavernUpgradeTimeline
                .Where(x => x != null && x.Turn > 0)
                .OrderBy(x => x.Turn)
                .FirstOrDefault(x => x.TavernTier == tavernTier);

            return match?.Turn;
        }

        private static double ClampPlacement(int placement)
        {
            if (placement < 1)
                return 1;
            if (placement > 8)
                return 8;
            return placement;
        }

        private sealed class TavernTempoBucketDefinition
        {
            public TavernTempoBucketDefinition(string key, Func<int, bool> matchTurn)
            {
                Key = key;
                MatchTurn = matchTurn;
            }

            public string Key { get; }
            public Func<int, bool> MatchTurn { get; }
        }
    }
}
