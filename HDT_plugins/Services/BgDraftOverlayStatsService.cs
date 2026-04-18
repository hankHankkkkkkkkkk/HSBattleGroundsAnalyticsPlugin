using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HDTplugins.Localization;
using HDTplugins.Models;

namespace HDTplugins.Services
{
    internal sealed class BgDraftOverlayStatsService
    {
        private readonly StatsStore _store;
        private readonly PluginSettingsService _settingsService;
        private HeroStatsSummary _heroSummary;
        private Dictionary<string, HeroStatsRow> _heroRows;
        private DateTime _heroCacheUtc = DateTime.MinValue;
        private double _heroScoreLine = double.MinValue;
        private TrinketFilter _trinketFilter = (TrinketFilter)(-1);
        private TrinketStatsSummary _trinketSummary;
        private Dictionary<string, TrinketStatsRow> _trinketRows;
        private DateTime _trinketCacheUtc = DateTime.MinValue;
        private double _trinketScoreLine = double.MinValue;
        private const int CacheSeconds = 10;

        public BgDraftOverlayStatsService(StatsStore store, PluginSettingsService settingsService)
        {
            _store = store;
            _settingsService = settingsService;
        }

        public IReadOnlyList<BgDraftOverlayStatsRow> GetHeroStats(IReadOnlyList<string> heroCardIds)
        {
            EnsureHeroCache();
            return (heroCardIds ?? Array.Empty<string>())
                .Select(cardId => BuildHeroStats(HeroIdNormalizer.Normalize(cardId)))
                .ToList();
        }

        public IReadOnlyList<BgDraftOverlayStatsRow> GetTrinketStats(IReadOnlyList<string> cardIds, TrinketFilter filter)
        {
            EnsureTrinketCache(filter);
            return (cardIds ?? Array.Empty<string>())
                .Select(cardId => BuildTrinketStats(cardId))
                .ToList();
        }

        public void Invalidate()
        {
            _heroCacheUtc = DateTime.MinValue;
            _trinketCacheUtc = DateTime.MinValue;
        }

        private void EnsureHeroCache()
        {
            var scoreLine = _settingsService.Settings.GetNormalizedScoreLine();
            if (_heroRows != null
                && (DateTime.UtcNow - _heroCacheUtc).TotalSeconds < CacheSeconds
                && Math.Abs(_heroScoreLine - scoreLine) < double.Epsilon)
            {
                return;
            }

            _heroScoreLine = scoreLine;
            _heroSummary = _store.LoadHeroStats(scoreLine);
            _heroRows = (_heroSummary?.Heroes ?? new List<HeroStatsRow>())
                .Where(x => !string.IsNullOrWhiteSpace(x.HeroCardId))
                .GroupBy(x => x.HeroCardId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            _heroCacheUtc = DateTime.UtcNow;
        }

        private void EnsureTrinketCache(TrinketFilter filter)
        {
            var scoreLine = _settingsService.Settings.GetNormalizedScoreLine();
            if (_trinketRows != null
                && _trinketFilter == filter
                && (DateTime.UtcNow - _trinketCacheUtc).TotalSeconds < CacheSeconds
                && Math.Abs(_trinketScoreLine - scoreLine) < double.Epsilon)
            {
                return;
            }

            _trinketScoreLine = scoreLine;
            _trinketFilter = filter;
            _trinketSummary = _store.LoadTrinketStats(scoreLine, filter);
            _trinketRows = (_trinketSummary?.Rows ?? new List<TrinketStatsRow>())
                .Where(x => !string.IsNullOrWhiteSpace(x.CardId))
                .GroupBy(x => x.CardId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            _trinketCacheUtc = DateTime.UtcNow;
        }

        private BgDraftOverlayStatsRow BuildHeroStats(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId) || _heroRows == null || !_heroRows.TryGetValue(cardId, out var row) || row == null || !row.HasData)
                return BuildNoDataRow(cardId);

            return new BgDraftOverlayStatsRow
            {
                CardId = cardId,
                HasData = true,
                PickRateText = FormatRate(row.PickRate, row.HasPickRateData),
                AveragePlacementText = row.AveragePlacement.ToString("F2", CultureInfo.CurrentCulture),
                FirstRateText = FormatRate(row.FirstRate, row.Picks > 0)
            };
        }

        private BgDraftOverlayStatsRow BuildTrinketStats(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId) || _trinketRows == null || !_trinketRows.TryGetValue(cardId, out var row) || row == null || row.MatchCount <= 0)
                return BuildNoDataRow(cardId);

            return new BgDraftOverlayStatsRow
            {
                CardId = cardId,
                HasData = true,
                PickRateText = FormatRate(row.PickRate, row.MatchCount > 0),
                AveragePlacementText = row.AveragePlacement.ToString("F2", CultureInfo.CurrentCulture),
                FirstRateText = FormatRate(row.FirstRate, row.MatchCount > 0)
            };
        }

        private static BgDraftOverlayStatsRow BuildNoDataRow(string cardId)
        {
            return new BgDraftOverlayStatsRow
            {
                CardId = cardId,
                HasData = false,
                PickRateText = Loc.S("Common_NoData"),
                AveragePlacementText = Loc.S("Common_NoData"),
                FirstRateText = Loc.S("Common_NoData")
            };
        }

        private static string FormatRate(double value, bool hasData)
        {
            return hasData ? value.ToString("P1", CultureInfo.CurrentCulture) : Loc.S("Common_NoData");
        }
    }
}
