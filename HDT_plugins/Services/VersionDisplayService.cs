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

        public string ConfigPath => "embedded:" + ResourceName;

        public void Initialize(string tablesDir)
        {
            Reload();
            HdtLog.Info("[BGStats] 版本映射来源: " + ConfigPath);
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

            return BuildDefaultDisplayName(normalized);
        }

        private void Reload()
        {
            try
            {
                var json = EmbeddedJsonLoader.ReadRequiredText(ResourceName);
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
