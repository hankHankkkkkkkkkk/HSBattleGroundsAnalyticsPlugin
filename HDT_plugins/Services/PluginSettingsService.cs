using HDTplugins.Models;
using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

using HdtLog = Hearthstone_Deck_Tracker.Utility.Logging.Log;

namespace HDTplugins.Services
{
    public class PluginSettingsService
    {
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private string _settingsPath;

        public PluginSettings Settings { get; private set; } = new PluginSettings();
        public string SettingsPath => _settingsPath;

        public void Initialize(string tablesDir)
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _settingsPath = Path.Combine(local, "HDT_BGStats", "Config", "plugin_settings.json");
            EnsureSettingsFile();
            Reload();
            HdtLog.Info("[BGStats] 插件设置路径: " + _settingsPath);
        }

        public void Reload()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_settingsPath) || !File.Exists(_settingsPath))
                {
                    Settings = new PluginSettings();
                    return;
                }

                var json = File.ReadAllText(_settingsPath, Encoding.UTF8);
                Settings = string.IsNullOrWhiteSpace(json)
                    ? new PluginSettings()
                    : (_serializer.Deserialize<PluginSettings>(json) ?? new PluginSettings());
            }
            catch (Exception ex)
            {
                Settings = new PluginSettings();
                HdtLog.Error("[BGStats] 读取插件设置失败: " + ex.Message + " path=" + _settingsPath);
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath));
                File.WriteAllText(_settingsPath, _serializer.Serialize(Settings ?? new PluginSettings()), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] 保存插件设置失败: " + ex.Message + " path=" + _settingsPath);
            }
        }

        private void EnsureSettingsFile()
        {
            if (File.Exists(_settingsPath))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath));
            File.WriteAllText(_settingsPath, _serializer.Serialize(new PluginSettings()), Encoding.UTF8);
        }
    }
}
