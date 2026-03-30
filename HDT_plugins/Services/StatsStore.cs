using HearthDb;
using HDTplugins.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using HdtLog = Hearthstone_Deck_Tracker.Utility.Logging.Log;

namespace HDTplugins.Services
{
    public class StatsStore
    {
        private string _dataDir;
        private string _archivesDir;
        private string _finalFilePath;
        private string _pendingFilePath;

        public string CurrentMatchId { get; private set; }
        public string CurrentMatchTimestampUtc { get; private set; }
        public bool PendingWritten { get; private set; }
        public ArchiveVersionInfo CurrentArchive { get; private set; }
        public string FinalFilePath => _finalFilePath;

        public void Initialize()
        {
            try
            {
                string oldDir = null;
                var oldHdtDir = GetHdtInstallDir();
                if (!string.IsNullOrEmpty(oldHdtDir))
                    oldDir = Path.Combine(oldHdtDir, "Plugins", "Data");

                var oldFinal = Path.Combine(oldDir ?? string.Empty, "bg_stats.jsonl");
                var oldPending = Path.Combine(oldDir ?? string.Empty, "bg_stats_pending.jsonl");

                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                _dataDir = Path.Combine(local, "HDT_BGStats", "Data");
                _archivesDir = Path.Combine(_dataDir, "archives");
                Directory.CreateDirectory(_archivesDir);

                SetArchiveInternal(ArchiveKeyProvider.GetDefaultArchive());
                BackupFileIfExists(_finalFilePath);
                MigrateIfNeeded(oldFinal, _finalFilePath);
                MigrateIfNeeded(oldPending, _pendingFilePath);

                HdtLog.Info($"[BGStats] 数据目录(AppData Local)：{_dataDir}");
                HdtLog.Info($"[BGStats] 当前版本归档：{CurrentArchive?.DisplayName}");
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] Initialize 失败: " + ex.Message);
            }
        }

        public IReadOnlyList<ArchiveVersionInfo> GetAvailableArchives()
        {
            var map = new Dictionary<string, ArchiveVersionInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var known in ArchiveKeyProvider.GetKnownArchives())
                map[known.Key] = known;

            if (!string.IsNullOrWhiteSpace(_archivesDir) && Directory.Exists(_archivesDir))
            {
                foreach (var dir in Directory.GetDirectories(_archivesDir))
                {
                    try
                    {
                        var key = Path.GetFileName(dir);
                        var labelPath = Path.Combine(dir, "label.txt");
                        var label = File.Exists(labelPath) ? File.ReadAllText(labelPath, Encoding.UTF8) : null;
                        map[key] = ArchiveKeyProvider.CreateFromStoredLabel(key, label);
                    }
                    catch { }
                }
            }

            return map.Values
                .OrderByDescending(x => string.Equals(x.Key, CurrentArchive?.Key, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public IReadOnlyList<ArchiveVersionInfo> GetRecordedArchives()
        {
            var archives = new List<ArchiveVersionInfo>();
            if (string.IsNullOrWhiteSpace(_archivesDir) || !Directory.Exists(_archivesDir))
                return archives;

            foreach (var dir in Directory.GetDirectories(_archivesDir))
            {
                try
                {
                    var finalFile = Path.Combine(dir, "bg_stats.jsonl");
                    if (!File.Exists(finalFile) || new FileInfo(finalFile).Length <= 0)
                        continue;

                    var key = Path.GetFileName(dir);
                    var labelPath = Path.Combine(dir, "label.txt");
                    var label = File.Exists(labelPath) ? File.ReadAllText(labelPath, Encoding.UTF8) : null;
                    archives.Add(ArchiveKeyProvider.CreateFromStoredLabel(key, label));
                }
                catch { }
            }

            return archives
                .OrderByDescending(x => string.Equals(x.Key, CurrentArchive?.Key, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public ArchiveVersionInfo SetArchiveByKey(string archiveKey)
        {
            var info = GetAvailableArchives().FirstOrDefault(x => string.Equals(x.Key, archiveKey, StringComparison.OrdinalIgnoreCase))
                ?? ArchiveKeyProvider.CreateFromStoredLabel(archiveKey, null);
            SetArchiveInternal(info);
            return CurrentArchive;
        }

        public ArchiveVersionInfo ConfirmCurrentArchiveForMatch()
        {
            var detected = ArchiveKeyProvider.ResolveCurrentArchive();
            SetArchiveInternal(detected);
            HdtLog.Info($"[BGStats] 当前对局版本：{CurrentArchive?.DisplayName}");
            return CurrentArchive;
        }

        public void ResetMatch()
        {
            CurrentMatchId = null;
            CurrentMatchTimestampUtc = null;
            PendingWritten = false;
        }

        public IReadOnlyList<BgMatchRow> LoadMatchRows()
        {
            var rows = new List<BgMatchRow>();
            if (string.IsNullOrWhiteSpace(_finalFilePath) || !File.Exists(_finalFilePath))
                return rows;

            var lines = File.ReadAllLines(_finalFilePath, Encoding.UTF8);
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var row = ParseLine(line);
                if (row != null)
                    rows.Add(row);
            }

            return rows;
        }

        public void WritePendingIfNeeded(string heroCardId, string heroSkinCardId, string heroPowerCardId, int ratingBefore)
        {
            try
            {
                if (PendingWritten || string.IsNullOrEmpty(_pendingFilePath) || string.IsNullOrEmpty(heroCardId))
                    return;

                CurrentMatchId = Guid.NewGuid().ToString("N");
                CurrentMatchTimestampUtc = DateTime.UtcNow.ToString("o");
                PendingWritten = true;

                var pendingLine = "{"
                    + $"\"matchId\":\"{JsonEscape(CurrentMatchId)}\","
                    + $"\"timestamp\":\"{JsonEscape(CurrentMatchTimestampUtc)}\","
                    + $"\"gameVersion\":\"{JsonEscape(CurrentArchive?.DisplayName ?? string.Empty)}\","
                    + $"\"heroCardId\":\"{JsonEscape(heroCardId)}\""
                    + "}";

                File.AppendAllText(_pendingFilePath, pendingLine + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] WritePendingIfNeeded 失败: " + ex.Message);
            }
        }

        public void FinalizeIfPossible(string matchId, string timestamp, string heroCardId, string heroSkinCardId, string heroPowerCardId, int placement, int ratingBefore, int ratingAfter, string[] availableRaces, string anomalyCardId, string[] finalBoardCardIds)
        {
            try
            {
                if (string.IsNullOrEmpty(matchId) || string.IsNullOrEmpty(_pendingFilePath) || string.IsNullOrEmpty(_finalFilePath))
                    return;
                if (string.IsNullOrEmpty(heroCardId) || placement <= 0 || ratingAfter <= 0)
                    return;

                RemovePendingByMatchId(matchId);

                var snapshot = new BgSnapshot
                {
                    MatchId = matchId,
                    Timestamp = string.IsNullOrEmpty(timestamp) ? DateTime.UtcNow.ToString("o") : timestamp,
                    GameVersion = CurrentArchive?.DisplayName ?? string.Empty,
                    HeroCardId = heroCardId,
                    HeroName = GetHeroName(heroCardId),
                    HeroSkinCardId = heroSkinCardId ?? string.Empty,
                    HeroPowerCardId = heroPowerCardId ?? string.Empty,
                    Placement = placement,
                    RatingBefore = ratingBefore,
                    RatingAfter = ratingAfter,
                    RatingDelta = (ratingBefore > 0 && ratingAfter > 0) ? (ratingAfter - ratingBefore) : 0,
                    AvailableRaces = availableRaces ?? Array.Empty<string>(),
                    AnomalyCardId = anomalyCardId ?? string.Empty,
                    AnomalyName = GetCardName(anomalyCardId),
                    FinalBoardCardIds = finalBoardCardIds ?? Array.Empty<string>()
                };

                File.AppendAllText(_finalFilePath, ToJson(snapshot) + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] FinalizeIfPossible 失败: " + ex.Message);
            }
        }

        private void SetArchiveInternal(ArchiveVersionInfo info)
        {
            var target = info ?? ArchiveKeyProvider.GetDefaultArchive();
            if (string.IsNullOrWhiteSpace(target.Key))
                target.Key = ArchiveKeyProvider.BuildArchiveKey(target.DisplayName);
            if (string.IsNullOrWhiteSpace(target.DisplayName))
                target.DisplayName = ArchiveKeyProvider.BuildDisplayNameFromKey(target.Key);

            var archiveDir = Path.Combine(_archivesDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HDT_BGStats", "Data", "archives"), target.Key);
            Directory.CreateDirectory(archiveDir);

            _finalFilePath = Path.Combine(archiveDir, "bg_stats.jsonl");
            _pendingFilePath = Path.Combine(archiveDir, "bg_stats_pending.jsonl");
            CurrentArchive = target;
            File.WriteAllText(Path.Combine(archiveDir, "label.txt"), target.DisplayName ?? string.Empty, Encoding.UTF8);
        }

        private void RemovePendingByMatchId(string matchId)
        {
            if (!File.Exists(_pendingFilePath))
                return;

            var kept = File.ReadAllLines(_pendingFilePath, Encoding.UTF8)
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.Contains($"\"matchId\":\"{matchId}\""))
                .ToArray();
            File.WriteAllLines(_pendingFilePath, kept, Encoding.UTF8);
        }

        private static BgMatchRow ParseLine(string line)
        {
            var placement = GetInt(line, "placement");
            if (placement <= 0)
                return null;

            return new BgMatchRow
            {
                MatchId = GetString(line, "matchId"),
                TimestampLocal = ParseTimestamp(GetString(line, "timestamp")),
                GameVersion = GetString(line, "gameVersion"),
                Placement = placement,
                RatingAfter = GetInt(line, "ratingAfter"),
                RatingDelta = GetInt(line, "ratingDelta"),
                HeroName = NormalizeDisplay(GetString(line, "heroName"), "未知英雄"),
                AnomalyDisplay = NormalizeDisplay(GetString(line, "anomalyName"), "待开发"),
                FinalBoardDisplay = BuildFinalBoardDisplay(GetStringArray(line, "finalBoardCardIds"))
            };
        }

        private static DateTime ParseTimestamp(string value)
        {
            return DateTimeOffset.TryParse(value, out var dto) ? dto.ToLocalTime().DateTime : default(DateTime);
        }

        private static string NormalizeDisplay(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string BuildFinalBoardDisplay(string[] cardIds)
        {
            if (cardIds == null || cardIds.Length == 0)
                return "待开发";

            var names = cardIds.Select(GetCardName).Where(x => !string.IsNullOrWhiteSpace(x)).Take(7).ToArray();
            return names.Length == 0 ? "待开发" : string.Join(" / ", names);
        }

        private static int GetInt(string line, string key)
        {
            var m = Regex.Match(line, "\\\"" + Regex.Escape(key) + "\\\":(-?\\d+)");
            return m.Success && int.TryParse(m.Groups[1].Value, out var value) ? value : 0;
        }

        private static string GetString(string line, string key)
        {
            var m = Regex.Match(line, "\\\"" + Regex.Escape(key) + "\\\":\\\"(.*?)\\\"");
            return m.Success ? UnescapeJson(m.Groups[1].Value) : string.Empty;
        }

        private static string[] GetStringArray(string line, string key)
        {
            var m = Regex.Match(line, "\\\"" + Regex.Escape(key) + "\\\":\\[(.*?)\\]");
            if (!m.Success || string.IsNullOrWhiteSpace(m.Groups[1].Value))
                return Array.Empty<string>();

            return Regex.Matches(m.Groups[1].Value, "\\\"(.*?)\\\"")
                .Cast<Match>()
                .Select(x => UnescapeJson(x.Groups[1].Value))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
        }

        private static string UnescapeJson(string value)
        {
            return value.Replace("\\\\", "\\").Replace("\\\"", "\"").Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t");
        }

        private static string GetHeroName(string cardId)
        {
            return GetCardName(cardId);
        }

        private static string GetCardName(string cardId)
        {
            if (string.IsNullOrEmpty(cardId))
                return string.Empty;
            try
            {
                var card = Cards.All.Values.FirstOrDefault(x => string.Equals(x.Id, cardId, StringComparison.OrdinalIgnoreCase));
                return card?.Name ?? cardId;
            }
            catch { return cardId; }
        }

        private static string ToJson(BgSnapshot x)
        {
            return "{"
                + $"\"matchId\":\"{JsonEscape(x.MatchId)}\","
                + $"\"timestamp\":\"{JsonEscape(x.Timestamp)}\","
                + $"\"gameVersion\":\"{JsonEscape(x.GameVersion)}\","
                + $"\"heroCardId\":\"{JsonEscape(x.HeroCardId)}\","
                + $"\"heroName\":\"{JsonEscape(x.HeroName)}\","
                + $"\"ratingBefore\":{x.RatingBefore},"
                + $"\"ratingAfter\":{x.RatingAfter},"
                + $"\"ratingDelta\":{x.RatingDelta},"
                + $"\"placement\":{x.Placement},"
                + $"\"heroSkinCardId\":\"{JsonEscape(x.HeroSkinCardId)}\","
                + $"\"heroPowerCardId\":\"{JsonEscape(x.HeroPowerCardId)}\","
                + $"\"availableRaces\":{ToJsonArray(x.AvailableRaces)},"
                + $"\"anomalyCardId\":\"{JsonEscape(x.AnomalyCardId)}\","
                + $"\"anomalyName\":\"{JsonEscape(x.AnomalyName)}\","
                + $"\"finalBoardCardIds\":{ToJsonArray(x.FinalBoardCardIds)}"
                + "}";
        }

        private static string ToJsonArray(string[] values)
        {
            if (values == null || values.Length == 0)
                return "[]";
            return "[" + string.Join(",", values.Select(v => $"\"{JsonEscape(v ?? string.Empty)}\"")) + "]";
        }

        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        }

        private static void MigrateIfNeeded(string from, string to)
        {
            try
            {
                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || !File.Exists(from))
                    return;
                var fromLen = new FileInfo(from).Length;
                var toExists = File.Exists(to);
                var toLen = toExists ? new FileInfo(to).Length : 0;
                if (fromLen > 0 && (!toExists || toLen == 0))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    File.Copy(from, to, true);
                }
            }
            catch { }
        }

        private static void BackupFileIfExists(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return;
                var fi = new FileInfo(path);
                if (fi.Length <= 0)
                    return;
                var bakDir = Path.Combine(Path.GetDirectoryName(path), "backups");
                Directory.CreateDirectory(bakDir);
                var bak = Path.Combine(bakDir, $"bg_stats.{DateTime.UtcNow:yyyyMMdd_HHmmss}.bak.jsonl");
                File.Copy(path, bak, false);
            }
            catch { }
        }

        private static string GetHdtInstallDir()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule.FileName;
                if (!string.IsNullOrEmpty(exePath))
                    return Path.GetDirectoryName(exePath);
            }
            catch { }

            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(baseDir))
                    return baseDir.TrimEnd('\\');
            }
            catch { }

            return null;
        }
    }
}
