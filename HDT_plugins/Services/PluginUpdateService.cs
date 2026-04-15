using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

using HdtLog = Hearthstone_Deck_Tracker.Utility.Logging.Log;

namespace HDTplugins.Services
{
    internal sealed class PluginUpdateService : IDisposable
    {
        private const string RepoOwner = "hankHankkkkkkkkkk";
        private const string RepoName = "HSBattleGroundsAnalyticsPlugin";
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/" + RepoOwner + "/" + RepoName + "/releases/latest";
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private readonly HttpClient _httpClient;
        private readonly string _pluginAssemblyPath;

        public PluginUpdateService(string pluginAssemblyPath, Version currentVersion)
        {
            _pluginAssemblyPath = pluginAssemblyPath ?? string.Empty;
            CurrentVersion = currentVersion ?? new Version(0, 0, 0, 0);

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _httpClient = new HttpClient(handler, true);
            _httpClient.Timeout = TimeSpan.FromSeconds(20);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BGAnalyzeViaHank-Updater/1.2.0");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }

        public Version CurrentVersion { get; }

        public async Task<AvailableUpdate> CheckForUpdateAsync()
        {
            if (string.IsNullOrWhiteSpace(_pluginAssemblyPath) || !File.Exists(_pluginAssemblyPath))
                return null;

            try
            {
                var json = await _httpClient.GetStringAsync(LatestReleaseApiUrl).ConfigureAwait(false);
                var release = _serializer.Deserialize<GitHubReleaseResponse>(json);
                if (release == null || release.draft || release.prerelease)
                    return null;

                var releaseVersion = ParseVersion(release.tag_name) ?? ParseVersion(release.name);
                if (releaseVersion == null || releaseVersion <= CurrentVersion)
                    return null;

                var pluginFileName = Path.GetFileName(_pluginAssemblyPath);
                var asset = SelectAsset(release, pluginFileName);
                if (asset == null || string.IsNullOrWhiteSpace(asset.DownloadUrl))
                {
                    HdtLog.Warn("[BGStats][Update] 检测到新版本但未找到 DLL 资源。");
                    return null;
                }

                return new AvailableUpdate
                {
                    Version = releaseVersion,
                    VersionText = releaseVersion.ToString(),
                    ReleasePageUrl = string.IsNullOrWhiteSpace(release.html_url)
                        ? "https://github.com/" + RepoOwner + "/" + RepoName + "/releases"
                        : release.html_url,
                    AssetName = asset.Name,
                    AssetDownloadUrl = asset.DownloadUrl
                };
            }
            catch (Exception ex)
            {
                HdtLog.Warn("[BGStats][Update] 读取 GitHub Release 失败: " + ex.Message);
                return null;
            }
        }

        public async Task<PrepareUpdateResult> DownloadAndPrepareUpdateAsync(AvailableUpdate update)
        {
            if (update == null)
            {
                return new PrepareUpdateResult
                {
                    Success = false,
                    Message = "Update information is missing."
                };
            }

            var pluginDirectory = Path.GetDirectoryName(_pluginAssemblyPath);
            var pluginFileName = Path.GetFileName(_pluginAssemblyPath);
            if (string.IsNullOrWhiteSpace(pluginDirectory) || string.IsNullOrWhiteSpace(pluginFileName))
            {
                return new PrepareUpdateResult
                {
                    Success = false,
                    Message = "Plugin path is invalid."
                };
            }

            Directory.CreateDirectory(pluginDirectory);
            var pendingPath = Path.Combine(pluginDirectory, pluginFileName + ".download");
            var backupPath = Path.Combine(pluginDirectory, pluginFileName + ".bak");
            var scriptPath = Path.Combine(pluginDirectory, "apply_plugin_update.cmd");

            try
            {
                var bytes = await _httpClient.GetByteArrayAsync(update.AssetDownloadUrl).ConfigureAwait(false);
                File.WriteAllBytes(pendingPath, bytes);

                var downloadedVersion = ReadAssemblyVersion(pendingPath);
                if (downloadedVersion == null)
                    throw new InvalidOperationException("Downloaded file is not a valid .NET assembly.");
                if (downloadedVersion < update.Version)
                    throw new InvalidOperationException("Downloaded file version is older than the release version.");

                WriteInstallScript(scriptPath, _pluginAssemblyPath, pendingPath, backupPath);
                StartInstallScript(scriptPath);

                HdtLog.Info("[BGStats][Update] 更新已下载，等待 HDT 退出后替换 DLL。 targetVersion=" + update.VersionText);
                return new PrepareUpdateResult
                {
                    Success = true,
                    Message = update.VersionText
                };
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats][Update] 准备更新失败: " + ex.Message);
                TryDeleteFile(pendingPath);
                return new PrepareUpdateResult
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }

        private static GitHubReleaseAssetResponse SelectAsset(GitHubReleaseResponse release, string pluginFileName)
        {
            if (release?.assets == null || release.assets.Length == 0)
                return null;

            foreach (var asset in release.assets)
            {
                if (asset == null || string.IsNullOrWhiteSpace(asset.Name))
                    continue;

                if (string.Equals(asset.Name, pluginFileName, StringComparison.OrdinalIgnoreCase))
                    return asset;
            }

            foreach (var asset in release.assets)
            {
                if (asset == null || string.IsNullOrWhiteSpace(asset.Name))
                    continue;

                if (asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    && (asset.Name.IndexOf("BGAnalyzeViaHank", StringComparison.OrdinalIgnoreCase) >= 0
                        || asset.Name.IndexOf("HDT_plugins", StringComparison.OrdinalIgnoreCase) >= 0))
                    return asset;
            }

            return null;
        }

        private static Version ParseVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var match = Regex.Match(value, @"\d+(?:\.\d+){1,3}");
            if (!match.Success)
                return null;

            Version parsed;
            return Version.TryParse(match.Value, out parsed) ? parsed : null;
        }

        private static Version ReadAssemblyVersion(string assemblyPath)
        {
            try
            {
                return System.Reflection.AssemblyName.GetAssemblyName(assemblyPath).Version;
            }
            catch
            {
                return null;
            }
        }

        private static void WriteInstallScript(string scriptPath, string targetPath, string pendingPath, string backupPath)
        {
            var builder = new StringBuilder();
            builder.AppendLine("@echo off");
            builder.AppendLine("setlocal");
            builder.AppendLine("set \"TARGET=" + targetPath + "\"");
            builder.AppendLine("set \"PENDING=" + pendingPath + "\"");
            builder.AppendLine("set \"BACKUP=" + backupPath + "\"");
            builder.AppendLine(":wait_hdt");
            builder.AppendLine("tasklist /FI \"IMAGENAME eq HearthstoneDeckTracker.exe\" 2>nul | find /I \"HearthstoneDeckTracker.exe\" >nul");
            builder.AppendLine("if %ERRORLEVEL%==0 (");
            builder.AppendLine("  timeout /t 2 /nobreak >nul");
            builder.AppendLine("  goto wait_hdt");
            builder.AppendLine(")");
            builder.AppendLine("if not exist \"%PENDING%\" goto end");
            builder.AppendLine("copy /Y \"%TARGET%\" \"%BACKUP%\" >nul 2>nul");
            builder.AppendLine("copy /Y \"%PENDING%\" \"%TARGET%\" >nul");
            builder.AppendLine("if %ERRORLEVEL%==0 del /F /Q \"%PENDING%\" >nul 2>nul");
            builder.AppendLine(":end");
            File.WriteAllText(scriptPath, builder.ToString(), Encoding.ASCII);
        }

        private static void StartInstallScript(string scriptPath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + scriptPath + "\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(startInfo);
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        internal sealed class AvailableUpdate
        {
            public Version Version { get; set; }
            public string VersionText { get; set; }
            public string ReleasePageUrl { get; set; }
            public string AssetName { get; set; }
            public string AssetDownloadUrl { get; set; }
        }

        internal sealed class PrepareUpdateResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
        }

        private sealed class GitHubReleaseResponse
        {
            public string tag_name { get; set; }
            public string name { get; set; }
            public string html_url { get; set; }
            public bool draft { get; set; }
            public bool prerelease { get; set; }
            public GitHubReleaseAssetResponse[] assets { get; set; }
        }

        private sealed class GitHubReleaseAssetResponse
        {
            public string name { get; set; }
            public string browser_download_url { get; set; }

            public string Name => name;
            public string DownloadUrl => browser_download_url;
        }
    }
}
