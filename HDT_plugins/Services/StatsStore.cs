using HearthDb;
using Hearthstone_Deck_Tracker.Hearthstone;
using HDTplugins.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

using HdtLog = Hearthstone_Deck_Tracker.Utility.Logging.Log;

namespace HDTplugins.Services
{
    /// <summary>
    /// 负责：数据目录初始化/迁移/备份 + pending/final 两阶段写盘（jsonl）
    /// 不直接访问 Core.Game（由 probe 提供数据）。
    /// </summary>
    public class StatsStore
    {
        private string _dataDir;
        private string _finalFilePath;
        private string _pendingFilePath;

        public string CurrentMatchId { get; private set; }
        public string CurrentMatchTimestampUtc { get; private set; }
        public bool PendingWritten { get; private set; }

        public void Initialize()
        {
            try
            {
                string oldDir = null;
                var oldHdtDir = GetHdtInstallDir();
                if (!string.IsNullOrEmpty(oldHdtDir))
                    oldDir = Path.Combine(oldHdtDir, "Plugins", "Data");

                var oldFinal = Path.Combine(oldDir ?? "", "bg_stats.jsonl");
                var oldPending = Path.Combine(oldDir ?? "", "bg_stats_pending.jsonl");

                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                _dataDir = Path.Combine(local, "HDT_BGStats", "Data");
                Directory.CreateDirectory(_dataDir);

                _finalFilePath = Path.Combine(_dataDir, "bg_stats.jsonl");
                _pendingFilePath = Path.Combine(_dataDir, "bg_stats_pending.jsonl");

                BackupFileIfExists(_finalFilePath);

                MigrateIfNeeded(oldFinal, _finalFilePath);
                MigrateIfNeeded(oldPending, _pendingFilePath);

                HdtLog.Info($"[BGStats] 数据目录(AppData Local)：{_dataDir}");
                HdtLog.Info($"[BGStats] final：{_finalFilePath}");
                HdtLog.Info($"[BGStats] pending：{_pendingFilePath}");
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] Initialize 失败: " + ex.Message);
            }
        }

        public void ResetMatch()
        {
            CurrentMatchId = null;
            CurrentMatchTimestampUtc = null;
            PendingWritten = false;
        }

        public void WritePendingIfNeeded(string heroCardId, string heroSkinCardId, string heroPowerCardId, int ratingBefore)
        {
            try
            {
                if (PendingWritten) return;
                if (string.IsNullOrEmpty(_pendingFilePath)) return;
                if (string.IsNullOrEmpty(heroCardId)) return;

                CurrentMatchId = Guid.NewGuid().ToString("N");
                CurrentMatchTimestampUtc = DateTime.UtcNow.ToString("o");
                PendingWritten = true;

                var pendingLine = "{"
                    + $"\"matchId\":\"{JsonEscape(CurrentMatchId)}\","
                    + $"\"timestamp\":\"{JsonEscape(CurrentMatchTimestampUtc)}\","
                    + $"\"heroCardId\":\"{JsonEscape(heroCardId)}\""
                    + "}";

                File.AppendAllText(_pendingFilePath, pendingLine + Environment.NewLine, Encoding.UTF8);
                HdtLog.Info($"[BGStats] 已写入 pending 记录 matchId={CurrentMatchId}");
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] WritePendingIfNeeded 失败: " + ex.Message);
            }
        }

        public void FinalizeIfPossible(
            string matchId,
            string timestamp,
            string heroCardId,
            string heroSkinCardId,
            string heroPowerCardId,
            int placement,
            int ratingBefore,
            int ratingAfter,
            string[] availableRaces,
            string anomalyCardId,
            string[] finalBoardCardIds)
        {
            try
            {
                if (string.IsNullOrEmpty(matchId)) return;
                if (string.IsNullOrEmpty(_pendingFilePath) || string.IsNullOrEmpty(_finalFilePath)) return;
                if (string.IsNullOrEmpty(heroCardId)) return;
                if (placement <= 0) return;
                if (ratingAfter <= 0) return;

                RemovePendingByMatchId(matchId);

                var snapshot = new BgSnapshot
                {
                    MatchId = matchId,
                    Timestamp = string.IsNullOrEmpty(timestamp) ? DateTime.UtcNow.ToString("o") : timestamp,
                    GameVersion = string.Empty,
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

                var line = ToJson(snapshot);
                File.AppendAllText(_finalFilePath, line + Environment.NewLine, Encoding.UTF8);

                HdtLog.Info($"[BGStats] 已 finalize matchId={matchId}, placement={placement}, after={ratingAfter}, races={snapshot.AvailableRaces.Length}, board={snapshot.FinalBoardCardIds.Length}");
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] FinalizeIfPossible 失败: " + ex.Message);
            }
        }

        private void RemovePendingByMatchId(string matchId)
        {
            if (!File.Exists(_pendingFilePath))
                return;

            var lines = File.ReadAllLines(_pendingFilePath, Encoding.UTF8);
            if (lines.Length == 0)
                return;

            var kept = lines
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.Contains($"\"matchId\":\"{matchId}\""))
                .ToArray();

            File.WriteAllLines(_pendingFilePath, kept, Encoding.UTF8);
        }

        private static string GetHeroName(string cardId)
        {
            if (string.IsNullOrEmpty(cardId))
                return "";

            try
            {
                var card = Database.GetCardFromId(cardId);
                if (card == null)
                    return cardId;

                return card.LocalizedName ?? card.Name ?? cardId;
            }
            catch
            {
                return cardId;
            }
        }

        private static string GetCardName(string cardId)
        {
            if (string.IsNullOrEmpty(cardId))
                return "";

            try
            {
                var card = Database.GetCardFromId(cardId);
                if (card == null)
                    return cardId;

                return card.LocalizedName ?? card.Name ?? cardId;
            }
            catch
            {
                return cardId;
            }
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
                return "";

            return s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private static void MigrateIfNeeded(string from, string to)
        {
            try
            {
                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                    return;

                if (!File.Exists(from))
                    return;

                var fromLen = new FileInfo(from).Length;
                if (fromLen <= 0)
                    return;

                var toExists = File.Exists(to);
                var toLen = toExists ? new FileInfo(to).Length : 0;

                if (!toExists || toLen == 0)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    File.Copy(from, to, overwrite: true);
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

                var dir = Path.GetDirectoryName(path);
                var bakDir = Path.Combine(dir, "backups");
                Directory.CreateDirectory(bakDir);

                var ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                var bak = Path.Combine(bakDir, $"bg_stats.{ts}.bak.jsonl");
                File.Copy(path, bak, overwrite: false);
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
