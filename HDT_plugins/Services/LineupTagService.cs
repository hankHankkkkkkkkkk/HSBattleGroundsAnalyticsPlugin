using HDTplugins.Models;
using System;
using System.Collections;
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
        private string _configFilePath;

        public string ConfigPath => string.IsNullOrWhiteSpace(_configFilePath) ? "embedded:" + ResourceName : _configFilePath;

        public void Initialize(string configDir)
        {
            _configFilePath = string.IsNullOrWhiteSpace(configDir)
                ? null
                : Path.Combine(configDir, "lineup_tags.json");
            EnsureConfigFileExists();
            Reload();
            HdtLog.Info("[BGStats] TAG 配置来源: " + ConfigPath);
        }

        public IReadOnlyList<string> GetAvailableTags(string versionDisplayName = null)
        {
            return GetAvailableTagDefinitions(versionDisplayName)
                .Select(x => x.Name)
                .ToList();
        }

        public IReadOnlyList<LineupTagDefinition> GetAvailableTagDefinitions(string versionDisplayName = null)
        {
            Reload();

            var customTags = new List<LineupTagDefinition>();
            var builtInTags = new List<LineupTagDefinition>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var definition in _config.AvailableTags ?? new List<LineupTagDefinition>())
            {
                if (definition == null || !IsManualTagVisible(definition.Name, versionDisplayName))
                    continue;

                var normalizedName = NormalizeTagName(definition.Name);
                if (string.IsNullOrWhiteSpace(normalizedName) || !seen.Add(normalizedName))
                    continue;

                var tagDefinition = new LineupTagDefinition
                {
                    Name = normalizedName,
                    IsEditable = definition.IsEditable
                };

                if (definition.IsEditable)
                    customTags.Add(tagDefinition);
                else
                    builtInTags.Add(tagDefinition);
            }

            foreach (var rule in _config.Rules ?? new List<LineupTagRule>())
            {
                if (string.IsNullOrWhiteSpace(rule?.Tag) || !IsRuleVisible(rule, versionDisplayName))
                    continue;

                var normalizedTag = NormalizeTagName(rule.Tag);
                if (string.IsNullOrWhiteSpace(normalizedTag) || !seen.Add(normalizedTag))
                    continue;

                builtInTags.Add(new LineupTagDefinition
                {
                    Name = normalizedTag,
                    IsEditable = false
                });
            }

            return customTags
                .AsEnumerable()
                .Reverse()
                .Concat(builtInTags
                .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList())
                .ToList();
        }

        public bool AddCustomTag(string tagName)
        {
            EnsureConfigFileExists();
            Reload();

            var normalizedTag = NormalizeTagName(tagName);
            if (string.IsNullOrWhiteSpace(normalizedTag))
                return false;

            var exists = GetAvailableTagDefinitions()
                .Any(x => string.Equals(x.Name, normalizedTag, StringComparison.OrdinalIgnoreCase));
            if (exists)
                return false;

            _config.AvailableTags = (_config.AvailableTags ?? new List<LineupTagDefinition>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(NormalizeTagName(x.Name)))
                .Select(x => new LineupTagDefinition
                {
                    Name = NormalizeTagName(x.Name),
                    IsEditable = x.IsEditable
                })
                .ToList();

            _config.AvailableTags.Add(new LineupTagDefinition
            {
                Name = normalizedTag,
                IsEditable = true
            });

            SaveConfig();
            Reload();
            return true;
        }

        public bool RemoveCustomTag(string tagName)
        {
            EnsureConfigFileExists();
            Reload();

            var normalizedTag = NormalizeTagName(tagName);
            if (string.IsNullOrWhiteSpace(normalizedTag))
                return false;

            var target = (_config.AvailableTags ?? new List<LineupTagDefinition>())
                .FirstOrDefault(x => x != null
                    && x.IsEditable
                    && string.Equals(NormalizeTagName(x.Name), normalizedTag, StringComparison.OrdinalIgnoreCase));
            if (target == null)
                return false;

            _config.AvailableTags = (_config.AvailableTags ?? new List<LineupTagDefinition>())
                .Where(x => x != null && !ReferenceEquals(x, target))
                .ToList();

            SaveConfig();
            Reload();
            return true;
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

        private static string NormalizeTagName(string tagName)
        {
            return string.IsNullOrWhiteSpace(tagName) ? string.Empty : tagName.Trim();
        }

        private static bool ConvertToBool(object value)
        {
            if (value is bool boolValue)
                return boolValue;
            if (value is string stringValue && bool.TryParse(stringValue, out var parsedBool))
                return parsedBool;
            if (value is int intValue)
                return intValue != 0;
            if (value is long longValue)
                return longValue != 0;
            return false;
        }

        public void Reload()
        {
            try
            {
                var json = LoadConfigJson();
                _config = ParseConfig(json);
            }
            catch (Exception ex)
            {
                _config = new LineupTagConfig();
                HdtLog.Error("[BGStats] 读取 TAG 规则失败: " + ex.Message);
            }
        }

        private void EnsureConfigFileExists()
        {
            if (string.IsNullOrWhiteSpace(_configFilePath))
                return;

            var directory = Path.GetDirectoryName(_configFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            if (File.Exists(_configFilePath))
                return;

            File.WriteAllText(_configFilePath, EmbeddedJsonLoader.ReadRequiredText(ResourceName), Encoding.UTF8);
        }

        private string LoadConfigJson()
        {
            string rawJson = null;
            if (!string.IsNullOrWhiteSpace(_configFilePath) && File.Exists(_configFilePath))
                rawJson = File.ReadAllText(_configFilePath, Encoding.UTF8);

            if (string.IsNullOrWhiteSpace(rawJson))
                rawJson = EmbeddedJsonLoader.ReadRequiredText(ResourceName);

            return string.Join(Environment.NewLine,
                (rawJson ?? string.Empty)
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                    .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        }

        private LineupTagConfig ParseConfig(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new LineupTagConfig();

            var raw = _serializer.DeserializeObject(json) as IDictionary<string, object>;
            if (raw == null)
                return new LineupTagConfig();

            return new LineupTagConfig
            {
                AvailableTags = ParseAvailableTags(raw.ContainsKey("availableTags") ? raw["availableTags"] : null),
                Rules = ParseRules(raw.ContainsKey("rules") ? raw["rules"] : null)
            };
        }

        private List<LineupTagDefinition> ParseAvailableTags(object rawAvailableTags)
        {
            var result = new List<LineupTagDefinition>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = rawAvailableTags as IEnumerable;
            if (items == null)
                return result;

            foreach (var item in items)
            {
                string name = null;
                var isEditable = false;
                if (item is string rawName)
                {
                    name = rawName;
                }
                else if (item is IDictionary<string, object> dictionary)
                {
                    if (dictionary.ContainsKey("name"))
                        name = dictionary["name"] as string;
                    if (dictionary.ContainsKey("isEditable"))
                        isEditable = ConvertToBool(dictionary["isEditable"]);
                }
                else if (item is IDictionary legacyDictionary)
                {
                    if (legacyDictionary.Contains("name"))
                        name = legacyDictionary["name"] as string;
                    if (legacyDictionary.Contains("isEditable"))
                        isEditable = ConvertToBool(legacyDictionary["isEditable"]);
                }

                var normalizedName = NormalizeTagName(name);
                if (string.IsNullOrWhiteSpace(normalizedName) || !seen.Add(normalizedName))
                    continue;

                result.Add(new LineupTagDefinition
                {
                    Name = normalizedName,
                    IsEditable = isEditable
                });
            }

            return result;
        }

        private List<LineupTagRule> ParseRules(object rawRules)
        {
            try
            {
                var rulesJson = _serializer.Serialize(rawRules ?? new ArrayList());
                return _serializer.Deserialize<List<LineupTagRule>>(rulesJson) ?? new List<LineupTagRule>();
            }
            catch
            {
                return new List<LineupTagRule>();
            }
        }

        private void SaveConfig()
        {
            if (string.IsNullOrWhiteSpace(_configFilePath))
                return;

            var serializable = new
            {
                availableTags = (_config.AvailableTags ?? new List<LineupTagDefinition>())
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(NormalizeTagName(x.Name)))
                    .Select(x => new
                    {
                        name = NormalizeTagName(x.Name),
                        isEditable = x.IsEditable
                    })
                    .ToList(),
                rules = _config.Rules ?? new List<LineupTagRule>()
            };

            File.WriteAllText(_configFilePath, _serializer.Serialize(serializable), Encoding.UTF8);
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
                    return CountMinionsByRace(finalBoard, condition.Race) >= condition.Value;
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
                    return string.Equals(
                        HeroIdNormalizer.Normalize(snapshot.HeroCardId),
                        HeroIdNormalizer.Normalize(condition.HeroCardId),
                        StringComparison.OrdinalIgnoreCase);
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

        private static int CountMinionsByRace(IEnumerable<BgBoardMinionSnapshot> finalBoard, string race)
        {
            var normalizedRace = string.IsNullOrWhiteSpace(race) ? string.Empty : race.Trim();
            if (string.Equals(normalizedRace, "NEUTRAL", StringComparison.OrdinalIgnoreCase))
            {
                return (finalBoard ?? Array.Empty<BgBoardMinionSnapshot>())
                    .Count(x => x != null && IsNeutralRaceValue(x.Race));
            }

            return (finalBoard ?? Array.Empty<BgBoardMinionSnapshot>())
                .Count(x => x != null && string.Equals(x.Race, normalizedRace, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsNeutralRaceValue(string race)
        {
            return string.IsNullOrWhiteSpace(race)
                || string.Equals(race, "INVALID", StringComparison.OrdinalIgnoreCase)
                || string.Equals(race, "BLANK", StringComparison.OrdinalIgnoreCase);
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
