using HearthDb;
using Hearthstone_Deck_Tracker.Hearthstone;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

// ✅ 强制使用 HDT 的 Log，避免和你 Services/Log.cs 冲突
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
        private string _finalFilePath;     // Data/bg_stats.jsonl
        private string _pendingFilePath;   // Data/bg_stats_pending.jsonl

        public string CurrentMatchId { get; private set; }
        public bool PendingWritten { get; private set; }

        public void Initialize()
        {
            try
            {
                // 旧目录：安装目录\Plugins\Data
                string oldDir = null;
                var oldHdtDir = GetHdtInstallDir();
                if (!string.IsNullOrEmpty(oldHdtDir))
                    oldDir = Path.Combine(oldHdtDir, "Plugins", "Data");

                var oldFinal = Path.Combine(oldDir ?? "", "bg_stats.jsonl");
                var oldPending = Path.Combine(oldDir ?? "", "bg_stats_pending.jsonl");

                // 新目录：%LocalAppData%\HDT_BGStats\Data
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                _dataDir = Path.Combine(local, "HDT_BGStats", "Data");
                Directory.CreateDirectory(_dataDir);

                _finalFilePath = Path.Combine(_dataDir, "bg_stats.jsonl");
                _pendingFilePath = Path.Combine(_dataDir, "bg_stats_pending.jsonl");

                // 启动先备份（防止意外）
                BackupFileIfExists(_finalFilePath);

                // 迁移：新文件不存在/为空时才迁移
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
            PendingWritten = false;
        }

        /// <summary>
        /// 第一次拿到英雄时写 pending（不含名次）。
        /// </summary>
        public void WritePendingIfNeeded(string heroCardId, string heroSkinCardId, string heroPowerCardId, int ratingBefore)
        {
            try
            {
                if (PendingWritten) return;
                if (string.IsNullOrEmpty(_pendingFilePath)) return;
                if (string.IsNullOrEmpty(heroCardId)) return;

                CurrentMatchId = Guid.NewGuid().ToString("N");
                PendingWritten = true;

                var timestamp = DateTime.UtcNow.ToString("o");
                var heroName = GetHeroName(heroCardId);
                var gameVersion = ""; // 先留空

                // placement=-1 表示未知
                var line =
                    "{"
                    + $"\"matchId\":\"{JsonEscape(CurrentMatchId)}\","
                    + $"\"timestamp\":\"{JsonEscape(timestamp)}\","
                    + $"\"gameVersion\":\"{JsonEscape(gameVersion)}\","
                    + $"\"heroCardId\":\"{JsonEscape(heroCardId)}\","
                    + $"\"heroName\":\"{JsonEscape(heroName)}\","
                    + $"\"ratingBefore\":{ratingBefore},"
                    + $"\"ratingAfter\":-1,"
                    + $"\"ratingDelta\":0,"
                    + $"\"placement\":-1,"
                    + $"\"heroSkinCardId\":\"{JsonEscape(heroSkinCardId ?? "")}\","
                    + $"\"heroPowerCardId\":\"{JsonEscape(heroPowerCardId ?? "")}\""
                    + "}";

                File.AppendAllText(_pendingFilePath, line + Environment.NewLine, Encoding.UTF8);

                HdtLog.Info($"[BGStats] 已写入 pending 记录 matchId={CurrentMatchId}");
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] WritePendingIfNeeded 失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 名次 + 赛后分数都拿到后：把 pending 对应记录 finalize 到 final，并从 pending 移除。
        /// </summary>
        public void FinalizeIfPossible(string matchId, int placement, int ratingBefore, int ratingAfter)
        {
            try
            {
                if (string.IsNullOrEmpty(matchId)) return;
                if (string.IsNullOrEmpty(_pendingFilePath) || string.IsNullOrEmpty(_finalFilePath)) return;
                if (placement <= 0) return;
                if (ratingAfter <= 0) return;

                if (!File.Exists(_pendingFilePath))
                    return;

                var lines = File.ReadAllLines(_pendingFilePath, Encoding.UTF8);
                if (lines.Length == 0)
                    return;

                string target = null;
                var kept = new List<string>(lines.Length);

                foreach (var l in lines)
                {
                    if (string.IsNullOrWhiteSpace(l))
                        continue;

                    if (target == null && l.Contains($"\"matchId\":\"{matchId}\""))
                        target = l;
                    else
                        kept.Add(l);
                }

                if (target == null)
                    return;

                var delta = (ratingBefore > 0 && ratingAfter > 0) ? (ratingAfter - ratingBefore) : 0;

                var finalized = ReplaceFirst(target, "\"placement\":-1", $"\"placement\":{placement}");
                finalized = ReplaceFirst(finalized, "\"ratingAfter\":-1", $"\"ratingAfter\":{ratingAfter}");
                finalized = ReplaceFirst(finalized, "\"ratingDelta\":0", $"\"ratingDelta\":{delta}");

                File.AppendAllText(_finalFilePath, finalized + Environment.NewLine, Encoding.UTF8);
                File.WriteAllLines(_pendingFilePath, kept.ToArray(), Encoding.UTF8);

                HdtLog.Info($"[BGStats] 已 finalize matchId={matchId}, placement={placement}, after={ratingAfter}, delta={delta}");
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] FinalizeIfPossible 失败: " + ex.Message);
            }
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

        private static string ReplaceFirst(string text, string search, string replace)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(search))
                return text;

            var idx = text.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0) return text;

            return text.Substring(0, idx) + replace + text.Substring(idx + search.Length);
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
            catch { /* ignore */ }
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
            catch { /* ignore */ }
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