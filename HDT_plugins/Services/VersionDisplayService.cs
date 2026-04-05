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

        private string MapVersionInternal(string rawVersion, bool rememberUnknownVersion)
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
            if (rememberUnknownVersion)
            {
                EnsureMapping(normalized, inheritedDisplayName ?? BuildDefaultDisplayName(normalized));
                Save();
            }
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
                Save();
            }
            catch (Exception ex)
            {
                _config = new VersionDisplayConfig();
                HdtLog.Error("[BGStats] 读取嵌入式版本映射失败: " + ex.Message);
            }
        }

        private static string BuildDefaultDisplayName(string rawVersion)
        {
            return "patch" + rawVersion;
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

        private void Save()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_configFilePath))
                    return;

                Directory.CreateDirectory(Path.GetDirectoryName(_configFilePath));
                var ordered = (_config.Versions ?? new List<VersionDisplayMapping>())
                    .Where(x => !string.IsNullOrWhiteSpace(x.RawVersion))
                    .OrderByDescending(x => x.RawVersion.Count(c => c == '.'))
                    .ThenByDescending(x => x.RawVersion, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new VersionDisplayMapping
                    {
                        RawVersion = x.RawVersion?.Trim(),
                        DisplayName = x.DisplayName?.Trim()
                    })
                    .ToList();

                var json = _serializer.Serialize(new VersionDisplayConfig { Versions = ordered });
                File.WriteAllText(_configFilePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                HdtLog.Warn("[BGStats] 保存版本映射文件失败: " + ex.Message);
            }
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

            var baseRawVersion = baseMapping.RawVersion.Trim();
            var baseDisplayName = baseMapping.DisplayName.Trim();
            if (baseDisplayName.EndsWith(baseRawVersion, StringComparison.OrdinalIgnoreCase))
                return baseDisplayName.Substring(0, baseDisplayName.Length - baseRawVersion.Length) + normalized;

            return null;
        }

        private void EnsureBuiltInMappings()
        {
            if (_config.Versions == null)
                _config.Versions = new List<VersionDisplayMapping>();

            EnsureMapping("35.0", "season12 patch35.0");
            EnsureMapping("34.6", "season12 patch34.6");
            EnsureMapping("33.2", "season11 patch33.2");
            EnsureMapping("2022.3", "season12 patch35.0");
        }

        private void EnsureMapping(string rawVersion, string displayName)
        {
            if (_config.Versions.Any(x => string.Equals(x.RawVersion, rawVersion, StringComparison.OrdinalIgnoreCase)))
                return;

            _config.Versions.Add(new VersionDisplayMapping
            {
                RawVersion = rawVersion,
                DisplayName = displayName
            });
        }
    }
}
