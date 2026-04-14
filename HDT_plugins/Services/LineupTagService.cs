using HDTplugins.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

using HdtLog = Hearthstone_Deck_Tracker.Utility.Logging.Log;

namespace HDTplugins.Services
{
    public class LineupTagService
    {
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private const string ResourceName = "HDT_plugins.Tables.lineup_tags.json";
        private LineupTagConfig _config = new LineupTagConfig();

        public string ConfigPath => "embedded:" + ResourceName;

        public void Initialize(string tablesDir)
        {
            Reload();
            HdtLog.Info("[BGStats] TAG 配置来源: " + ConfigPath);
        }

        public IReadOnlyList<string> GetAvailableTags(string versionDisplayName = null)
        {
            Reload();
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in _config.AvailableTags ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(tag) && IsManualTagVisible(tag, versionDisplayName))
                    tags.Add(tag.Trim());
            }

            foreach (var rule in _config.Rules ?? new List<LineupTagRule>())
            {
                if (!string.IsNullOrWhiteSpace(rule.Tag) && IsRuleVisible(rule, versionDisplayName))
                    tags.Add(rule.Tag.Trim());
            }

            return tags.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public IReadOnlyList<string> Evaluate(BgSnapshot snapshot, string versionDisplayName = null)
        {
            Reload();
            if (snapshot == null)
                return Array.Empty<string>();

            return (_config.Rules ?? new List<LineupTagRule>())
                .Where(rule => !string.IsNullOrWhiteSpace(rule.Tag)
                    && IsRuleVisible(rule, versionDisplayName)
                    && EvaluateCondition(rule.Conditions, snapshot))
                .OrderByDescending(rule => rule.Priority)
                .ThenBy(rule => rule.Tag, StringComparer.CurrentCultureIgnoreCase)
                .Select(rule => rule.Tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
        }

        private static bool IsRuleVisible(LineupTagRule rule, string versionDisplayName)
        {
            if (rule == null)
                return false;

            var normalizedVersion = string.IsNullOrWhiteSpace(versionDisplayName) ? string.Empty : versionDisplayName.Trim();
            var ranges = (rule.VersionRange ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (ranges.Count == 0)
                return true;

            return ranges.Any(x => string.Equals(x, normalizedVersion, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsManualTagVisible(string tag, string versionDisplayName)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return false;

            var relatedRules = (_config.Rules ?? new List<LineupTagRule>())
                .Where(rule => string.Equals(rule.Tag, tag, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (relatedRules.Count == 0)
                return true;

            return relatedRules.Any(rule => IsRuleVisible(rule, versionDisplayName));
        }

        private void Reload()
        {
            try
            {
                var json = string.Join(Environment.NewLine,
                    EmbeddedJsonLoader.ReadRequiredText(ResourceName)
                        .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                        .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

                _config = string.IsNullOrWhiteSpace(json)
                    ? new LineupTagConfig()
                    : (_serializer.Deserialize<LineupTagConfig>(json) ?? new LineupTagConfig());
            }
            catch (Exception ex)
            {
                _config = new LineupTagConfig();
                HdtLog.Error("[BGStats] 读取嵌入式 TAG 规则失败: " + ex.Message);
            }
        }

        private static bool EvaluateCondition(LineupTagCondition condition, BgSnapshot snapshot)
        {
            if (condition == null || snapshot == null)
                return false;

            if (!string.IsNullOrWhiteSpace(condition.Op))
            {
                var items = condition.Items ?? new List<LineupTagCondition>();
                if (string.Equals(condition.Op, "all", StringComparison.OrdinalIgnoreCase))
                    return items.Count > 0 && items.All(item => EvaluateCondition(item, snapshot));
                if (string.Equals(condition.Op, "any", StringComparison.OrdinalIgnoreCase))
                    return items.Any(item => EvaluateCondition(item, snapshot));
                return false;
            }

            var finalBoard = snapshot.FinalBoard ?? new List<BgBoardMinionSnapshot>();
            var loweredType = (condition.Type ?? string.Empty).Trim().ToLowerInvariant();
            switch (loweredType)
            {
                case "minionracecountatleast":
                    return finalBoard.Count(x => string.Equals(x.Race, condition.Race, StringComparison.OrdinalIgnoreCase)) >= condition.Value;
                case "cardidexists":
                    return finalBoard.Any(x => string.Equals(x.CardId, condition.CardId, StringComparison.OrdinalIgnoreCase));
                case "cardidcountatleast":
                    var cardIds = new HashSet<string>((condition.CardIds ?? Array.Empty<string>())
                        .Concat(string.IsNullOrWhiteSpace(condition.CardId) ? Array.Empty<string>() : new[] { condition.CardId }), StringComparer.OrdinalIgnoreCase);
                    return finalBoard.Count(x => cardIds.Contains(x.CardId ?? string.Empty)) >= condition.Value;
                case "goldencountatleast":
                    return finalBoard.Count(x => x.IsGolden) >= condition.Value;
                case "isgolden":
                    var expectedGolden = condition.IsGolden ?? true;
                    if (!string.IsNullOrWhiteSpace(condition.CardId))
                        return finalBoard.Any(x => string.Equals(x.CardId, condition.CardId, StringComparison.OrdinalIgnoreCase) && x.IsGolden == expectedGolden);
                    return finalBoard.Any(x => x.IsGolden == expectedGolden);
                case "herois":
                    return string.Equals(snapshot.HeroCardId, condition.HeroCardId, StringComparison.OrdinalIgnoreCase);
                case "heropoweris":
                    return (snapshot.InitialHeroPowerCardIds ?? new[] { snapshot.InitialHeroPowerCardId })
                               .Any(x => string.Equals(x, condition.HeroPowerCardId, StringComparison.OrdinalIgnoreCase))
                        || (snapshot.HeroPowerCardIds ?? new[] { snapshot.HeroPowerCardId })
                               .Any(x => string.Equals(x, condition.HeroPowerCardId, StringComparison.OrdinalIgnoreCase));
                case "taverntieratleast":
                    return (snapshot.TavernUpgradeTimeline ?? new List<BgTavernUpgradePoint>()).DefaultIfEmpty(new BgTavernUpgradePoint()).Max(x => x == null ? 0 : x.TavernTier) >= condition.Value;
                case "totalminionsatleast":
                    return finalBoard.Count >= condition.Value;
                case "keywordcountatleast":
                    return finalBoard.Count(x => HasKeyword(x.Keywords, condition.Keyword)) >= condition.Value;
                default:
                    return false;
            }
        }

        private static bool HasKeyword(BgKeywordState keywords, string keyword)
        {
            if (keywords == null || string.IsNullOrWhiteSpace(keyword))
                return false;

            switch (keyword.Trim().ToLowerInvariant())
            {
                case "taunt": return keywords.Taunt;
                case "divineshield": return keywords.DivineShield;
                case "poisonous": return keywords.Poisonous;
                case "reborn": return keywords.Reborn;
                case "windfury": return keywords.Windfury;
                case "megawindfury": return keywords.MegaWindfury;
                case "stealth": return keywords.Stealth;
                case "cleave": return keywords.Cleave;
                default: return false;
            }
        }
    }
}
