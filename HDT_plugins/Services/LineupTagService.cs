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
        private string _configPath;
        private LineupTagConfig _config = new LineupTagConfig();

        public string ConfigPath => _configPath;

        public void Initialize(string tablesDir)
        {
            _configPath = Path.Combine(tablesDir, "lineup_tags.json");
            EnsureConfigFile();
            Reload();
        }

        public IReadOnlyList<string> GetAvailableTags()
        {
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in _config.AvailableTags ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(tag))
                    tags.Add(tag.Trim());
            }

            foreach (var rule in _config.Rules ?? new List<LineupTagRule>())
            {
                if (!string.IsNullOrWhiteSpace(rule.Tag))
                    tags.Add(rule.Tag.Trim());
            }

            return tags.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public IReadOnlyList<string> Evaluate(BgSnapshot snapshot)
        {
            Reload();
            if (snapshot == null)
                return Array.Empty<string>();

            return (_config.Rules ?? new List<LineupTagRule>())
                .Where(rule => !string.IsNullOrWhiteSpace(rule.Tag) && EvaluateCondition(rule.Conditions, snapshot))
                .OrderByDescending(rule => rule.Priority)
                .ThenBy(rule => rule.Tag, StringComparer.CurrentCultureIgnoreCase)
                .Select(rule => rule.Tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
        }

        private void EnsureConfigFile()
        {
            if (File.Exists(_configPath))
                return;

            var template = string.Join(Environment.NewLine, new[]
            {
                "// lineup_tags.json 支持手动维护可选 TAG 和自动识别规则。",
                "// 注释写法：仅支持整行 // 注释，请不要把注释放在 JSON 行尾。",
                "// 规则结构说明：",
                "// 1. availableTags: 可在详情页下拉中手动添加的 TAG 列表。",
                "// 2. rules: 自动识别规则列表，命中后会自动追加 TAG，最多取优先级最高的 3 个。",
                "// 3. conditions 支持两类写法：",
                "//    A. 组合条件：{ \"op\": \"all\" | \"any\", \"items\": [ ... ] }",
                "//    B. 原子条件：{ \"type\": \"minionRaceCountAtLeast\", \"race\": \"MECHANICAL\", \"value\": 4 }",
                "// 可用 type：",
                "// - minionRaceCountAtLeast: 终局阵容中某种族数量至少 value",
                "// - cardIdExists: 终局阵容中存在指定 cardId",
                "// - cardIdCountAtLeast: 终局阵容中 cardId 或 cardIds 的总数量至少 value",
                "// - goldenCountAtLeast: 终局阵容中金色随从数量至少 value",
                "// - heroIs: 英雄 cardId 等于 heroCardId",
                "// - heroPowerIs: 终局英雄技能 cardId 等于 heroPowerCardId",
                "// - tavernTierAtLeast: 本局曾到达的最高酒馆等级至少 value",
                "// - totalMinionsAtLeast: 终局阵容随从数量至少 value",
                "// - keywordCountAtLeast: 带指定关键词(keyword)的随从数量至少 value，keyword 可写 taunt/divineshield/poisonous/reborn/windfury/megawindfury/stealth/cleave",
                "{",
                "  \"availableTags\": [",
                "    \"机械体系\",",
                "    \"亡灵体系\",",
                "    \"圣盾流\",",
                "    \"龙体系\"",
                "  ],",
                "  \"rules\": [",
                "    {",
                "      \"tag\": \"机械体系\",",
                "      \"priority\": 100,",
                "      \"conditions\": {",
                "        \"type\": \"minionRaceCountAtLeast\",",
                "        \"race\": \"MECHANICAL\",",
                "        \"value\": 4",
                "      }",
                "    },",
                "    {",
                "      \"tag\": \"圣盾流\",",
                "      \"priority\": 80,",
                "      \"conditions\": {",
                "        \"type\": \"keywordCountAtLeast\",",
                "        \"keyword\": \"divineshield\",",
                "        \"value\": 3",
                "      }",
                "    }",
                "  ]",
                "}"
            });

            Directory.CreateDirectory(Path.GetDirectoryName(_configPath));
            File.WriteAllText(_configPath, template, Encoding.UTF8);
        }

        private void Reload()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_configPath) || !File.Exists(_configPath))
                {
                    _config = new LineupTagConfig();
                    return;
                }

                var json = string.Join(Environment.NewLine,
                    File.ReadAllLines(_configPath, Encoding.UTF8)
                        .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

                _config = string.IsNullOrWhiteSpace(json)
                    ? new LineupTagConfig()
                    : (_serializer.Deserialize<LineupTagConfig>(json) ?? new LineupTagConfig());
            }
            catch (Exception ex)
            {
                _config = new LineupTagConfig();
                HdtLog.Error("[BGStats] 读取 TAG 规则失败: " + ex.Message);
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
                case "herois":
                    return string.Equals(snapshot.HeroCardId, condition.HeroCardId, StringComparison.OrdinalIgnoreCase);
                case "heropoweris":
                    return string.Equals(snapshot.HeroPowerCardId, condition.HeroPowerCardId, StringComparison.OrdinalIgnoreCase);
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
