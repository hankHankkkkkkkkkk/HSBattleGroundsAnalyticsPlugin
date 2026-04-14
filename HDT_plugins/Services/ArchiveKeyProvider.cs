using HDTplugins.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using HdtLog = Hearthstone_Deck_Tracker.Utility.Logging.Log;

namespace HDTplugins.Services
{
    public static class ArchiveKeyProvider
    {
        private static readonly ArchiveVersionInfo[] KnownArchives =
        {
            new ArchiveVersionInfo { Key = "season12_patch35_0", DisplayName = "season12 patch35.0", RawVersion = "35.0", PatchVersion = "35.0" },
            new ArchiveVersionInfo { Key = "season12_patch34_6", DisplayName = "season12 patch34.6", RawVersion = "34.6", PatchVersion = "34.6" },
            new ArchiveVersionInfo { Key = "season11_patch33_2", DisplayName = "season11 patch33.2", RawVersion = "33.2", PatchVersion = "33.2" }
        };

        public static IReadOnlyList<ArchiveVersionInfo> GetKnownArchives()
        {
            return KnownArchives.Select(Clone).ToList();
        }

        public static ArchiveVersionInfo GetDefaultArchive()
        {
            return Clone(KnownArchives[0]);
        }

        public static ArchiveVersionInfo ResolveCurrentArchive(Func<string, string> mapDisplayName)
        {
            var detectedRawVersion = NormalizeRawVersion(TryDetectHearthstonePatchVersion());
            if (string.IsNullOrEmpty(detectedRawVersion))
                return null;

            var displayName = mapDisplayName?.Invoke(detectedRawVersion) ?? detectedRawVersion;
            var matched = KnownArchives.FirstOrDefault(x => detectedRawVersion.StartsWith(x.PatchVersion, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                var info = Clone(matched);
                info.DisplayName = displayName;
                info.Key = BuildArchiveKeyFromRawVersion(detectedRawVersion, info.DisplayName);
                info.RawVersion = detectedRawVersion;
                info.PatchVersion = ExtractPatchVersion(detectedRawVersion);
                info.IsDetected = true;
                return info;
            }

            return new ArchiveVersionInfo
            {
                Key = BuildArchiveKeyFromRawVersion(detectedRawVersion, displayName),
                DisplayName = displayName,
                RawVersion = detectedRawVersion,
                PatchVersion = ExtractPatchVersion(detectedRawVersion),
                IsDetected = true
            };
        }

        public static string BuildArchiveKeyFromRawVersion(string rawVersion, string fallbackDisplayName)
        {
            if (!string.IsNullOrWhiteSpace(rawVersion))
                return "version_" + Regex.Replace(rawVersion.Trim().ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
            return BuildArchiveKey(fallbackDisplayName);
        }

        public static ArchiveVersionInfo CreateFromStoredLabel(string key, string displayName)
        {
            var name = string.IsNullOrWhiteSpace(displayName) ? BuildDisplayNameFromKey(key) : displayName.Trim();
            var rawVersionFromKey = ExtractRawVersionFromKey(key);
            var rawVersion = !string.IsNullOrWhiteSpace(rawVersionFromKey)
                ? rawVersionFromKey
                : (IsRawVersionText(name) ? name : string.Empty);
            var patchVersion = ExtractPatchVersion(!string.IsNullOrWhiteSpace(rawVersion) ? rawVersion : name);

            return new ArchiveVersionInfo
            {
                Key = string.IsNullOrWhiteSpace(key) ? BuildArchiveKey(name) : key,
                DisplayName = name,
                RawVersion = rawVersion,
                PatchVersion = patchVersion
            };
        }

        public static string ExtractPatchVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var match = Regex.Match(value, "(\\d+\\.\\d+)");
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        public static bool IsRawVersionText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var trimmed = value.Trim();
            if (trimmed.IndexOf("Season", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (trimmed.IndexOf("Patch", StringComparison.OrdinalIgnoreCase) >= 0 && trimmed.IndexOf(' ') >= 0)
                return false;

            return Regex.IsMatch(trimmed, "^\\d+(?:\\.\\d+)+(?:\\s*\\([^\\)]*\\))?$", RegexOptions.IgnoreCase);
        }

        public static string ExtractRawVersionFromKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("version_", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            var suffix = key.Substring("version_".Length).Trim('_');
            if (string.IsNullOrWhiteSpace(suffix))
                return string.Empty;

            var parts = suffix.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return string.Empty;
            if (parts.Length <= 3)
                return string.Join(".", parts);

            var versionPart = string.Join(".", parts.Take(3));
            var buildPart = string.Join("_", parts.Skip(3));
            return string.IsNullOrWhiteSpace(buildPart)
                ? versionPart
                : versionPart + " (" + buildPart + ")";
        }

        public static string BuildArchiveKey(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return GetDefaultArchive().Key;

            var key = Regex.Replace(displayName.Trim().ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
            return string.IsNullOrWhiteSpace(key) ? GetDefaultArchive().Key : key;
        }

        public static string BuildDisplayNameFromKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return GetDefaultArchive().DisplayName;

            if (key.StartsWith("version_", StringComparison.OrdinalIgnoreCase))
            {
                var suffix = key.Substring("version_".Length).Replace('_', '.').Trim('.');
                if (!string.IsNullOrWhiteSpace(suffix))
                    return suffix;
            }

            return key.Replace('_', ' ');
        }

        private static string TryDetectHearthstonePatchVersion()
        {
            try
            {
                var processes = Process.GetProcesses()
                    .Where(p =>
                    {
                        try { return p.ProcessName.IndexOf("Hearthstone", StringComparison.OrdinalIgnoreCase) >= 0; }
                        catch { return false; }
                    })
                    .ToArray();

                foreach (var process in processes)
                {
                    try
                    {
                        var processName = SafeProcessName(process);
                        if (!string.Equals(processName, "Hearthstone", StringComparison.OrdinalIgnoreCase))
                        {
                            HdtLog.Info($"[BGStats][VersionDebug] process={processName} skipped=process-name");
                            continue;
                        }

                        var modulePath = process.MainModule?.FileName;
                        if (string.IsNullOrWhiteSpace(modulePath) || !File.Exists(modulePath))
                        {
                            HdtLog.Info($"[BGStats][VersionDebug] process={processName} modulePath=<missing> skipped=module-missing");
                            continue;
                        }

                        if (!string.Equals(Path.GetFileName(modulePath), "Hearthstone.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            HdtLog.Info($"[BGStats][VersionDebug] process={processName} modulePath={modulePath} skipped=module-name");
                            continue;
                        }

                        var fileVersion = FileVersionInfo.GetVersionInfo(modulePath);
                        var productVersion = fileVersion.ProductVersion;
                        var fileVersionText = fileVersion.FileVersion;
                        var productPatch = NormalizeRawVersion(productVersion);
                        var filePatch = NormalizeRawVersion(fileVersionText);
                        var selectedSource = !string.IsNullOrWhiteSpace(productVersion) ? "ProductVersion" : "FileVersion";
                        var selectedRaw = productVersion ?? fileVersionText;
                        var patch = NormalizeRawVersion(selectedRaw);
                        LogVersionHitIfInteresting("ProductVersion", process, modulePath, productVersion, productPatch);
                        LogVersionHitIfInteresting("FileVersion", process, modulePath, fileVersionText, filePatch);
                        LogVersionHitIfInteresting(selectedSource + "(selected)", process, modulePath, selectedRaw, patch);

                        HdtLog.Info(
                            $"[BGStats][VersionDebug] process={processName}"
                            + $" modulePath={modulePath}"
                            + $" productVersionRaw={productVersion ?? "<null>"}"
                            + $" productVersionPatch={productPatch ?? "<null>"}"
                            + $" fileVersionRaw={fileVersionText ?? "<null>"}"
                            + $" fileVersionPatch={filePatch ?? "<null>"}"
                            + $" selectedSource={selectedSource}"
                            + $" selectedRaw={selectedRaw ?? "<null>"}"
                            + $" selectedPatch={patch ?? "<null>"}");
                        if (!string.IsNullOrWhiteSpace(patch))
                            return patch;
                    }
                    catch (Exception ex)
                    {
                        HdtLog.Info($"[BGStats][VersionDebug] process={SafeProcessName(process)} detect-failed={ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                HdtLog.Info("[BGStats][VersionDebug] enumerate-process-failed=" + ex.Message);
            }

            return null;
        }

        private static string NormalizeRawVersion(string rawVersion)
        {
            if (string.IsNullOrWhiteSpace(rawVersion))
                return null;

            return rawVersion.Trim();
        }

        private static ArchiveVersionInfo Clone(ArchiveVersionInfo source)
        {
            return new ArchiveVersionInfo
            {
                Key = source.Key,
                DisplayName = source.DisplayName,
                RawVersion = source.RawVersion,
                PatchVersion = source.PatchVersion,
                IsDetected = source.IsDetected
            };
        }

        private static string SafeProcessName(Process process)
        {
            try
            {
                return process?.ProcessName ?? "<null>";
            }
            catch
            {
                return "<unavailable>";
            }
        }

        private static void LogVersionHitIfInteresting(string source, Process process, string modulePath, string rawVersion, string normalizedPatch)
        {
            if (string.IsNullOrWhiteSpace(normalizedPatch))
                return;

            var is35 = normalizedPatch.IndexOf("35.0", StringComparison.OrdinalIgnoreCase) >= 0;
            var is20223 = normalizedPatch.IndexOf("2022.3", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!is35 && !is20223)
                return;

            HdtLog.Info(
                $"[BGStats][VersionDebug][HIT] normalizedPatch={normalizedPatch}"
                + $" source={source}"
                + $" process={SafeProcessName(process)}"
                + $" modulePath={modulePath ?? "<null>"}"
                + $" rawVersion={rawVersion ?? "<null>"}");
        }
    }
}
