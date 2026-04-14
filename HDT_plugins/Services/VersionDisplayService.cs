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
    public class VersionDisplayService
    {
        private static readonly HashSet<string> LoggedNewVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private const string ResourceName = "HDT_plugins.Tables.version_display_mappings.json";
        private VersionDisplayConfig _config = new VersionDisplayConfig();
        private string _configFilePath;

        public string ConfigPath => string.IsNullOrWhiteSpace(_configFilePath) ? "embedded:" + ResourceName : _configFilePath;

        public void Initialize(string tablesDir)
        {
            if (!string.IsNullOrWhiteSpace(tablesDir))
            {
                Directory.CreateDirectory(tablesDir);
                _configFilePath = Path.Combine(tablesDir, "version_display_mappings.json");
            }

            Reload();
            HdtLog.Info("[BGStats] 版本映射来源: " + ConfigPath);
        }

        public string MapVersion(string rawVersion)
        {
            return MapVersionInternal(rawVersion, false);
        }

        public string RememberAndMapVersion(string rawVersion)
        {
            return MapVersionInternal(rawVersion, true);
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
                var json = ReadConfigText();
                _config = string.IsNullOrWhiteSpace(json)
                    ? new VersionDisplayConfig()
                    : (_serializer.Deserialize<VersionDisplayConfig>(json) ?? new VersionDisplayConfig());
                _config.Versions = _config.Versions ?? new List<VersionDisplayMapping>();
                EnsureBuiltInMappings();
            }
            catch (Exception ex)
            {
                _config = new VersionDisplayConfig();
                HdtLog.Error("[BGStats] 读取嵌入式版本映射失败: " + ex.Message);
            }
        }

        private static string BuildDefaultDisplayName(string rawVersion)
        {
            return "Patch" + rawVersion;
        }

        private string ReadConfigText()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_configFilePath) && File.Exists(_configFilePath))
                    return File.ReadAllText(_configFilePath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                HdtLog.Warn("[BGStats] 读取版本映射文件失败，将回退到嵌入式配置: " + ex.Message);
            }

            return EmbeddedJsonLoader.ReadRequiredText(ResourceName);
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
