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
            new ArchiveVersionInfo { Key = "season12_patch35_0", DisplayName = "season12 patch35.0", PatchVersion = "35.0" },
            new ArchiveVersionInfo { Key = "season12_patch34_6", DisplayName = "season12 patch34.6", PatchVersion = "34.6" },
            new ArchiveVersionInfo { Key = "season11_patch33_2", DisplayName = "season11 patch33.2", PatchVersion = "33.2" }
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
            var detectedVersion = TryDetectHearthstoneVersion();
            if (string.IsNullOrEmpty(detectedVersion))
            {
                HdtLog.Warn("[BGStats] 未检测到可用的炉石版本号，回退到默认归档");
                return GetDefaultArchive();
            }

            if (!IsReasonablePatchVersion(detectedVersion))
            {
                HdtLog.Warn($"[BGStats] 检测到的炉石版本号不在预期范围内: {detectedVersion}，回退到默认归档");
                return GetDefaultArchive();
            }

            var mappedDisplayName = mapDisplayName == null ? null : mapDisplayName(detectedVersion);
            HdtLog.Info($"[BGStats] 版本归档解析: detectedVersion={detectedVersion}, mappedDisplayName={mappedDisplayName ?? "(null)"}");
            var matched = KnownArchives.FirstOrDefault(x => detectedVersion.StartsWith(x.PatchVersion, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                var info = Clone(matched);
                if (!string.IsNullOrWhiteSpace(mappedDisplayName))
                    info.DisplayName = mappedDisplayName;
                info.Key = BuildArchiveKeyFromRawVersion(detectedVersion, info.DisplayName);
                info.IsDetected = true;
                info.PatchVersion = detectedVersion;
                return info;
            }

            return new ArchiveVersionInfo
            {
                Key = BuildArchiveKeyFromRawVersion(detectedVersion, mappedDisplayName),
                DisplayName = string.IsNullOrWhiteSpace(mappedDisplayName) ? "patch" + detectedVersion : mappedDisplayName,
                PatchVersion = detectedVersion,
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
            var match = MatchPatchVersion(name);

            return new ArchiveVersionInfo
            {
                Key = string.IsNullOrWhiteSpace(key) ? BuildArchiveKey(name) : key,
                DisplayName = name,
                PatchVersion = match.Success ? match.Value : string.Empty
            };
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

            return key.Replace('_', ' ');
        }

        private static string TryDetectHearthstoneVersion()
        {
            try
            {
                var processes = Process.GetProcesses()
                    .Where(p =>
                    {
                        try
                        {
                            return p.ProcessName.IndexOf("Hearthstone", StringComparison.OrdinalIgnoreCase) >= 0
                                && p.ProcessName.IndexOf("DeckTracker", StringComparison.OrdinalIgnoreCase) < 0;
                        }
                        catch { return false; }
                    })
                    .OrderByDescending(p =>
                    {
                        try { return string.Equals(p.ProcessName, "Hearthstone", StringComparison.OrdinalIgnoreCase); }
                        catch { return false; }
                    })
                    .ToArray();

                foreach (var process in processes)
                {
                    try
                    {
                        var modulePath = process.MainModule?.FileName;
                        if (string.IsNullOrWhiteSpace(modulePath) || !File.Exists(modulePath))
                            continue;

                        var fileVersion = FileVersionInfo.GetVersionInfo(modulePath);
                        var productVersion = fileVersion.ProductVersion ?? string.Empty;
                        var binaryVersion = fileVersion.FileVersion ?? string.Empty;
                        var normalizedVersion = NormalizePatchVersion(productVersion);
                        if (string.IsNullOrWhiteSpace(normalizedVersion))
                            normalizedVersion = NormalizePatchVersion(binaryVersion);
                        HdtLog.Info($"[BGStats] 读取炉石进程版本: process={process.ProcessName}, path={modulePath}, productVersion={productVersion}, fileVersion={binaryVersion}, normalizedVersion={normalizedVersion ?? "null"}");
                        if (!string.IsNullOrWhiteSpace(normalizedVersion))
                            return normalizedVersion;
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        private static string NormalizePatchVersion(string rawVersion)
        {
            if (string.IsNullOrWhiteSpace(rawVersion))
                return null;

            var match = Regex.Match(rawVersion, "\\d+(?:\\.\\d+)+");
            return match.Success ? match.Value : null;
        }

        private static Match MatchPatchVersion(string value)
        {
            return Regex.Match(value ?? string.Empty, "\\d+\\.\\d+(?:\\.\\d+)?");
        }

        private static bool IsReasonablePatchVersion(string patchVersion)
        {
            var parts = patchVersion.Split('.');
            if (parts.Length < 2)
                return false;

            int major;
            int minor;
            if (!int.TryParse(parts[0], out major) || !int.TryParse(parts[1], out minor))
                return false;

            if (major < 20 && major < 2000)
                return false;

            return minor >= 0;
        }

        private static ArchiveVersionInfo Clone(ArchiveVersionInfo source)
        {
            return new ArchiveVersionInfo
            {
                Key = source.Key,
                DisplayName = source.DisplayName,
                PatchVersion = source.PatchVersion,
                IsDetected = source.IsDetected
            };
        }
    }
}
