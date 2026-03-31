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
        private string _configPath;
        private VersionDisplayConfig _config = new VersionDisplayConfig();

        public string ConfigPath => _configPath;

        public void Initialize(string tablesDir)
        {
            _configPath = Path.Combine(tablesDir, "version_display_mappings.json");
            EnsureConfigFile();
            Reload();
        }

        public string MapVersion(string rawVersion)
        {
            Reload();
            if (string.IsNullOrWhiteSpace(rawVersion))
                return string.Empty;

            var normalized = rawVersion.Trim();
            var mapping = (_config.Versions ?? new List<VersionDisplayMapping>())
                .FirstOrDefault(x => string.Equals(x.RawVersion, normalized, StringComparison.OrdinalIgnoreCase));
            if (mapping != null)
                return string.IsNullOrWhiteSpace(mapping.DisplayName) ? BuildDefaultDisplayName(normalized) : mapping.DisplayName.Trim();

            (_config.Versions ?? (_config.Versions = new List<VersionDisplayMapping>())).Add(new VersionDisplayMapping
            {
                RawVersion = normalized,
                DisplayName = BuildDefaultDisplayName(normalized)
            });
            Save();
            return BuildDefaultDisplayName(normalized);
        }

        private void EnsureConfigFile()
        {
            if (File.Exists(_configPath))
                return;

            var template = new VersionDisplayConfig
            {
                Versions = new List<VersionDisplayMapping>
                {
                    new VersionDisplayMapping { RawVersion = "35.0", DisplayName = "season12 patch35.0" },
                    new VersionDisplayMapping { RawVersion = "34.6", DisplayName = "season12 patch34.6" },
                    new VersionDisplayMapping { RawVersion = "33.2", DisplayName = "season11 patch33.2" },
                    new VersionDisplayMapping { RawVersion = "2022.3", DisplayName = "season12 patch35.0" }
                }
            };

            Directory.CreateDirectory(Path.GetDirectoryName(_configPath));
            File.WriteAllText(_configPath, _serializer.Serialize(template), Encoding.UTF8);
        }

        private void Reload()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_configPath) || !File.Exists(_configPath))
                {
                    _config = new VersionDisplayConfig();
                    return;
                }

                var json = File.ReadAllText(_configPath, Encoding.UTF8);
                _config = string.IsNullOrWhiteSpace(json)
                    ? new VersionDisplayConfig()
                    : (_serializer.Deserialize<VersionDisplayConfig>(json) ?? new VersionDisplayConfig());
                _config.Versions = _config.Versions ?? new List<VersionDisplayMapping>();
                EnsureBuiltInMappings();
            }
            catch (Exception ex)
            {
                _config = new VersionDisplayConfig();
                HdtLog.Error("[BGStats] 读取版本映射失败: " + ex.Message + " path=" + _configPath);
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath));
                EnsureBuiltInMappings();
                var ordered = new VersionDisplayConfig
                {
                    Versions = (_config.Versions ?? new List<VersionDisplayMapping>())
                        .Where(x => !string.IsNullOrWhiteSpace(x.RawVersion))
                        .GroupBy(x => x.RawVersion.Trim(), StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                        .OrderByDescending(x => x.RawVersion, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };
                File.WriteAllText(_configPath, _serializer.Serialize(ordered), Encoding.UTF8);
                _config = ordered;
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] 保存版本映射失败: " + ex.Message);
            }
        }

        private static string BuildDefaultDisplayName(string rawVersion)
        {
            return "patch" + rawVersion;
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
