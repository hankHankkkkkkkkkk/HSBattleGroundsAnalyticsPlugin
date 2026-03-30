using HDTplugins.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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

        public static ArchiveVersionInfo ResolveCurrentArchive()
        {
            var detectedPatch = TryDetectHearthstonePatchVersion();
            if (string.IsNullOrEmpty(detectedPatch))
                return GetDefaultArchive();

            if (!IsReasonablePatchVersion(detectedPatch))
                return GetDefaultArchive();

            var matched = KnownArchives.FirstOrDefault(x => detectedPatch.StartsWith(x.PatchVersion, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                var info = Clone(matched);
                info.IsDetected = true;
                return info;
            }

            return new ArchiveVersionInfo
            {
                Key = BuildArchiveKey("patch" + detectedPatch),
                DisplayName = "patch" + detectedPatch,
                PatchVersion = detectedPatch,
                IsDetected = true
            };
        }

        public static ArchiveVersionInfo CreateFromStoredLabel(string key, string displayName)
        {
            var name = string.IsNullOrWhiteSpace(displayName) ? BuildDisplayNameFromKey(key) : displayName.Trim();
            var match = Regex.Match(name, "(\\d+\\.\\d+)");

            return new ArchiveVersionInfo
            {
                Key = string.IsNullOrWhiteSpace(key) ? BuildArchiveKey(name) : key,
                DisplayName = name,
                PatchVersion = match.Success ? match.Groups[1].Value : string.Empty
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
                        var modulePath = process.MainModule?.FileName;
                        if (string.IsNullOrWhiteSpace(modulePath) || !File.Exists(modulePath))
                            continue;

                        var fileVersion = FileVersionInfo.GetVersionInfo(modulePath);
                        var patch = NormalizePatchVersion(fileVersion.ProductVersion ?? fileVersion.FileVersion);
                        if (!string.IsNullOrWhiteSpace(patch))
                            return patch;
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

            var match = Regex.Match(rawVersion, "(\\d+\\.\\d+)");
            return match.Success ? match.Groups[1].Value : null;
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

            if (major < 30)
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
