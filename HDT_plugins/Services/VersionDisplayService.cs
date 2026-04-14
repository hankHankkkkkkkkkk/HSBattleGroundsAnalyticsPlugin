using HDTplugins.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

using HdtLog = Hearthstone_Deck_Tracker.Utility.Logging.Log;

namespace HDTplugins.Services
{
    public class VersionDisplayService
    {
        private static readonly HashSet<string> LoggedNewVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private const string ResourceName = "HDT_plugins.Tables.version_display_mappings.json";
        private const string RangeResourceName = "HDT_plugins.Tables.version_range.json";
        private VersionDisplayConfig _config = new VersionDisplayConfig();
        private VersionRangeConfig _rangeConfig = new VersionRangeConfig();
        private string _configFilePath;
        private string _rangeConfigFilePath;

        public string ConfigPath => string.IsNullOrWhiteSpace(_configFilePath) ? "embedded:" + ResourceName : _configFilePath;
        public string RangeConfigPath => string.IsNullOrWhiteSpace(_rangeConfigFilePath) ? "embedded:" + RangeResourceName : _rangeConfigFilePath;

        public void Initialize(string tablesDir)
        {
            if (!string.IsNullOrWhiteSpace(tablesDir))
            {
                Directory.CreateDirectory(tablesDir);
                _configFilePath = Path.Combine(tablesDir, "version_display_mappings.json");
                _rangeConfigFilePath = Path.Combine(tablesDir, "version_range.json");
            }

            Reload();
            HdtLog.Info("[BGStats] 版本映射来源: " + ConfigPath);
            HdtLog.Info("[BGStats] 版本范围来源: " + RangeConfigPath);
        }

        public string MapVersion(string rawVersion)
        {
            return MapVersionInternal(rawVersion, false);
        }

        public string RememberAndMapVersion(string rawVersion)
        {
            return MapVersionInternal(rawVersion, true);
        }

        public IReadOnlyList<VersionMenuItem> BuildMenuItems(IEnumerable<ArchiveVersionInfo> recordedArchives)
        {
            Reload();
            var archives = (recordedArchives ?? Array.Empty<ArchiveVersionInfo>())
                .Where(x => x != null)
                .ToList();
            var displayGroups = archives
                .GroupBy(GetDisplayNameForArchive, StringComparer.OrdinalIgnoreCase)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            var items = new List<VersionMenuItem>();
            foreach (var range in _rangeConfig.Versions ?? new List<VersionRangeMapping>())
            {
                var rangeDisplay = NormalizeDisplayName(range.DisplayName);
                if (string.IsNullOrWhiteSpace(rangeDisplay) || range.IsHidden)
                    continue;

                var members = NormalizeVersionRange(range.VersionRange);
                if (members.Count == 0 || !members.Any(displayGroups.ContainsKey))
                    continue;

                items.Add(new VersionMenuItem
                {
                    Key = BuildRangeKey(rangeDisplay),
                    DisplayName = rangeDisplay,
                    IsRange = true,
                    VersionRange = members.ToList(),
                    PatchVersion = GetHighestPatchVersionForDisplays(members, displayGroups)
                });
            }

            foreach (var pair in displayGroups)
            {
                if (IsSingleVersionHidden(pair.Key))
                    continue;

                var representative = pair.Value
                    .OrderByDescending(x => GetArchiveSortKey(x?.PatchVersion))
                    .ThenByDescending(x => x?.Key, StringComparer.OrdinalIgnoreCase)
                    .First();

                items.Add(new VersionMenuItem
                {
                    Key = representative.Key,
                    DisplayName = pair.Key,
                    IsRange = false,
                    VersionRange = new List<string> { pair.Key },
                    PatchVersion = GetHighestPatchVersionForDisplays(new[] { pair.Key }, displayGroups)
                });
            }

            return items
                .OrderByDescending(x => GetDisplaySortKey(x.DisplayName, x.PatchVersion))
                .ThenByDescending(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public VersionMenuItem FindMenuItem(string selectionKey, IEnumerable<ArchiveVersionInfo> recordedArchives)
        {
            if (string.IsNullOrWhiteSpace(selectionKey))
                return null;

            return BuildMenuItems(recordedArchives)
                .FirstOrDefault(x => string.Equals(x.Key, selectionKey.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<string> ResolveDisplayNamesForSelection(string selectionKey, IEnumerable<ArchiveVersionInfo> recordedArchives)
        {
            Reload();
            var selectedItem = FindMenuItem(selectionKey, recordedArchives);
            if (selectedItem != null && selectedItem.IsRange)
                return NormalizeVersionRange(selectedItem.VersionRange);

            var archives = (recordedArchives ?? Array.Empty<ArchiveVersionInfo>())
                .Where(x => x != null)
                .ToList();
            var exact = archives.FirstOrDefault(x => string.Equals(x.Key, selectionKey, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return new[] { GetDisplayNameForArchive(exact) };

            if (selectedItem != null)
                return new[] { selectedItem.DisplayName };

            return Array.Empty<string>();
        }

        public bool SelectionContainsDisplayToken(string selectionKey, IEnumerable<ArchiveVersionInfo> recordedArchives, string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            return ResolveDisplayNamesForSelection(selectionKey, recordedArchives)
                .Any(name => name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private string MapVersionInternal(string rawVersion, bool logUnknownVersion)
        {
            Reload();
            if (string.IsNullOrWhiteSpace(rawVersion))
                return string.Empty;

            var normalized = rawVersion.Trim();
            var mappings = (_config.Versions ?? new List<VersionDisplayMapping>());
            var mapping = mappings
                .FirstOrDefault(x => string.Equals(x.RawVersion, normalized, StringComparison.OrdinalIgnoreCase));
            if (mapping != null)
                return string.IsNullOrWhiteSpace(mapping.DisplayName) ? BuildDefaultDisplayName(normalized) : mapping.DisplayName.Trim();

            var inheritedDisplayName = TryBuildDisplayNameFromBaseMapping(normalized, mappings);
            if (logUnknownVersion)
                LogNewVersionInfo(normalized, inheritedDisplayName ?? BuildDefaultDisplayName(normalized));
            if (!string.IsNullOrWhiteSpace(inheritedDisplayName))
                return inheritedDisplayName;

            return BuildDefaultDisplayName(normalized);
        }

        private void Reload()
        {
            try
            {
                var json = ReadConfigText(_configFilePath, ResourceName);
                _config = string.IsNullOrWhiteSpace(json)
                    ? new VersionDisplayConfig()
                    : (_serializer.Deserialize<VersionDisplayConfig>(json) ?? new VersionDisplayConfig());
                _config.Versions = _config.Versions ?? new List<VersionDisplayMapping>();
                EnsureBuiltInMappings();
            }
            catch (Exception ex)
            {
                _config = new VersionDisplayConfig();
                HdtLog.Error("[BGStats] 读取版本映射失败: " + ex.Message);
            }

            try
            {
                var json = ReadConfigText(_rangeConfigFilePath, RangeResourceName);
                _rangeConfig = string.IsNullOrWhiteSpace(json)
                    ? new VersionRangeConfig()
                    : (_serializer.Deserialize<VersionRangeConfig>(json) ?? new VersionRangeConfig());
                _rangeConfig.Versions = _rangeConfig.Versions ?? new List<VersionRangeMapping>();
                foreach (var range in _rangeConfig.Versions)
                    range.VersionRange = NormalizeVersionRange(range.VersionRange).ToList();
            }
            catch (Exception ex)
            {
                _rangeConfig = new VersionRangeConfig();
                HdtLog.Error("[BGStats] 读取版本范围失败: " + ex.Message);
            }
        }

        private static string BuildDefaultDisplayName(string rawVersion)
        {
            return "Patch" + rawVersion;
        }

        private string ReadConfigText(string filePath, string resourceName)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                    return File.ReadAllText(filePath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                HdtLog.Warn("[BGStats] 读取配置文件失败，将回退到嵌入式配置: " + ex.Message);
            }

            return EmbeddedJsonLoader.ReadRequiredText(resourceName);
        }

        private static string TryBuildDisplayNameFromBaseMapping(string rawVersion, IEnumerable<VersionDisplayMapping> mappings)
        {
            if (string.IsNullOrWhiteSpace(rawVersion) || mappings == null)
                return null;

            var normalized = rawVersion.Trim();
            var baseMapping = mappings
                .Where(x => !string.IsNullOrWhiteSpace(x.RawVersion)
                    && !string.IsNullOrWhiteSpace(x.DisplayName)
                    && normalized.StartsWith(x.RawVersion.Trim() + ".", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.RawVersion.Length)
                .FirstOrDefault();
            if (baseMapping == null)
                return null;

            var baseDisplayName = baseMapping.DisplayName.Trim();
            return string.IsNullOrWhiteSpace(baseDisplayName) ? null : baseDisplayName;
        }

        private static void LogNewVersionInfo(string rawVersion, string displayName)
        {
            if (string.IsNullOrWhiteSpace(rawVersion))
                return;

            lock (LoggedNewVersions)
            {
                if (!LoggedNewVersions.Add(rawVersion.Trim()))
                    return;
            }

            HdtLog.Info($"[BGStats][检测到新版本信息] rawVersion={rawVersion} displayName={displayName}");
        }

        private bool IsSingleVersionHidden(string displayName)
        {
            var normalized = NormalizeDisplayName(displayName);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            return (_config.Versions ?? new List<VersionDisplayMapping>())
                .Any(x => x.IsHidden && string.Equals(NormalizeDisplayName(x.DisplayName), normalized, StringComparison.OrdinalIgnoreCase));
        }

        private string GetDisplayNameForArchive(ArchiveVersionInfo archive)
        {
            if (archive == null)
                return string.Empty;

            var rawVersion = !string.IsNullOrWhiteSpace(archive.PatchVersion)
                ? archive.PatchVersion
                : archive.DisplayName;
            return NormalizeDisplayName(RememberAndMapVersion(rawVersion));
        }

        private static IReadOnlyList<string> NormalizeVersionRange(IEnumerable<string> values)
        {
            return (values ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeDisplayName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NormalizeDisplayName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string BuildRangeKey(string displayName)
        {
            return "version_range_" + ArchiveKeyProvider.BuildArchiveKey(displayName);
        }

        private static string GetHighestPatchVersionForDisplays(IEnumerable<string> displayNames, IDictionary<string, List<ArchiveVersionInfo>> displayGroups)
        {
            return (displayNames ?? Array.Empty<string>())
                .Where(displayGroups.ContainsKey)
                .SelectMany(displayName => displayGroups[displayName])
                .Select(archive => archive?.PatchVersion)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .OrderByDescending(GetArchiveSortKey)
                .FirstOrDefault() ?? string.Empty;
        }

        private static long GetDisplaySortKey(string displayName, string fallbackPatchVersion)
        {
            var season = TryReadNumber(displayName, "Season");
            var patch = TryReadPatchSortKey(displayName);
            if (patch == 0 && !string.IsNullOrWhiteSpace(fallbackPatchVersion))
                patch = GetArchiveSortKey(fallbackPatchVersion);

            return season * 1000000000L + patch;
        }

        private static int TryReadNumber(string text, string label)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(label))
                return 0;

            var match = Regex.Match(text, Regex.Escape(label) + "\\s*(\\d+)", RegexOptions.IgnoreCase);
            if (!match.Success)
                return 0;

            return int.TryParse(match.Groups[1].Value, out var value) ? value : 0;
        }

        private static long TryReadPatchSortKey(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            var match = Regex.Match(text, "Patch\\s*(\\d+(?:\\.\\d+)*)", RegexOptions.IgnoreCase);
            return match.Success ? GetArchiveSortKey(match.Groups[1].Value) : 0;
        }

        private static long GetArchiveSortKey(string patchVersion)
        {
            if (string.IsNullOrWhiteSpace(patchVersion))
                return 0;

            var parts = patchVersion.Split('.');
            long major = 0;
            long minor = 0;
            long patch = 0;
            if (parts.Length > 0)
                long.TryParse(parts[0], out major);
            if (parts.Length > 1)
                long.TryParse(parts[1], out minor);
            if (parts.Length > 2)
                long.TryParse(parts[2], out patch);

            return major * 1000000L + minor * 1000L + patch;
        }

        private void EnsureBuiltInMappings()
        {
            if (_config.Versions == null)
                _config.Versions = new List<VersionDisplayMapping>();

            EnsureMapping("35.0", "Season12 Patch35.0");
            EnsureMapping("34.6", "Season12 Patch34.6");
            EnsureMapping("33.2", "Season11 Patch33.2");
            EnsureMapping("2022.3", "Season12 Patch35.0");
        }

        private void EnsureMapping(string rawVersion, string displayName)
        {
            var existing = _config.Versions.FirstOrDefault(x => string.Equals(x.RawVersion, rawVersion, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.DisplayName = displayName;
                return;
            }

            _config.Versions.Add(new VersionDisplayMapping
            {
                RawVersion = rawVersion,
                DisplayName = displayName
            });
        }
    }
}
