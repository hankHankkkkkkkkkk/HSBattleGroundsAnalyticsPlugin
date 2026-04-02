using HDTplugins.Localization;
using HDTplugins.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

using HdtLog = Hearthstone_Deck_Tracker.Utility.Logging.Log;

namespace HDTplugins.Services
{
    public class StatsStore
    {
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private readonly LineupTagService _lineupTagService = new LineupTagService();
        private readonly VersionDisplayService _versionDisplayService = new VersionDisplayService();

        private string _dataDir;
        private string _archivesDir;
        private string _finalFilePath;
        private string _pendingFilePath;
        private string _tablesDir;
        private ArchiveVersionInfo _activeMatchArchive;
        private string _activeFinalFilePath;
        private string _activePendingFilePath;

        public string CurrentMatchId { get; private set; }
        public string CurrentMatchTimestampUtc { get; private set; }
        public bool PendingWritten { get; private set; }
        public ArchiveVersionInfo CurrentArchive { get; private set; }
        public string FinalFilePath => _finalFilePath;
        public string TagConfigPath => _lineupTagService.ConfigPath;
        public string TablesDirectoryPath => _tablesDir;

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
                _tablesDir = Path.Combine(GetPluginDirectory(), "Tables");
                Directory.CreateDirectory(_tablesDir);
                MigrateIfNeeded(Path.Combine(_dataDir, "lineup_tags.json"), Path.Combine(_tablesDir, "lineup_tags.json"));
                _lineupTagService.Initialize(_tablesDir);
                _versionDisplayService.Initialize(_tablesDir);

                SetArchiveInternal(ResolveInitialArchive());
                BackupFileIfExists(_finalFilePath);
                MigrateIfNeeded(oldFinal, _finalFilePath);
                MigrateIfNeeded(oldPending, _pendingFilePath);

                HdtLog.Info($"[BGStats] 数据目录(AppData Local)：{_dataDir}");
                HdtLog.Info($"[BGStats] 当前版本归档：{CurrentArchive?.DisplayName}");
                HdtLog.Info($"[BGStats] 配置目录：{_tablesDir}");
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] Initialize 失败: " + ex.Message);
            }
        }

        public IReadOnlyList<string> GetAvailableTags()
        {
            return _lineupTagService.GetAvailableTags();
        }

        public IReadOnlyList<string> GetDisplayTags(BgSnapshot snapshot)
        {
            return MergeTags(snapshot).Take(5).ToList();
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
            return GetRecordedArchivesInternal();
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
            var detected = ResolveBestArchiveForCurrentVersion();
            SetActiveMatchArchive(detected);
            HdtLog.Info($"[BGStats] 当前对局版本：{_activeMatchArchive?.DisplayName}");
            return _activeMatchArchive;
        }

        public ArchiveVersionInfo GetLatestRecordedArchiveForDisplay()
        {
            return GetLatestRecordedArchive() ?? CurrentArchive;
        }

        public ArchiveVersionInfo RefreshLatestRecordedArchiveForDisplay()
        {
            var latest = GetLatestRecordedArchive();
            if (latest == null)
                return CurrentArchive;

            if (!string.Equals(CurrentArchive?.Key, latest.Key, StringComparison.OrdinalIgnoreCase))
                SetArchiveInternal(latest);

            return CurrentArchive;
        }

        public void ResetMatch()
        {
            CurrentMatchId = null;
            CurrentMatchTimestampUtc = null;
            PendingWritten = false;
            _activeMatchArchive = null;
            _activeFinalFilePath = null;
            _activePendingFilePath = null;
        }

        public IReadOnlyList<BgMatchRow> LoadMatchRows()
        {
            return LoadSnapshots()
                .OrderByDescending(x => ParseTimestamp(x.Timestamp))
                .Select(snapshot => new BgMatchRow
                {
                    MatchId = snapshot.MatchId,
                    TimestampLocal = ParseTimestamp(snapshot.Timestamp),
                    GameVersion = snapshot.GameVersion,
                    Placement = snapshot.Placement,
                    RatingAfter = snapshot.RatingAfter,
                    RatingDelta = snapshot.RatingDelta,
                    HeroName = GameTextService.GetCardName(snapshot.HeroCardId, NormalizeDisplay(snapshot.HeroName, snapshot.HeroCardId)),
                    AnomalyDisplay = string.IsNullOrWhiteSpace(snapshot.AnomalyCardId)
                        ? NormalizeDisplay(snapshot.AnomalyName, Loc.S("Common_Todo"))
                        : GameTextService.GetCardName(snapshot.AnomalyCardId, NormalizeDisplay(snapshot.AnomalyName, Loc.S("Common_Todo"))),
                    FinalBoardDisplay = BuildFinalBoardDisplay(snapshot),
                    Tags = MergeTags(snapshot).Take(5).ToList()
                })
                .ToList();
        }

        public BgSnapshot LoadSnapshot(string matchId)
        {
            if (string.IsNullOrWhiteSpace(matchId))
                return null;
            return LoadSnapshots().FirstOrDefault(x => string.Equals(x.MatchId, matchId, StringComparison.OrdinalIgnoreCase));
        }

        public bool UpdateManualTags(string matchId, IReadOnlyCollection<string> manualTags)
        {
            if (string.IsNullOrWhiteSpace(matchId) || string.IsNullOrWhiteSpace(_finalFilePath) || !File.Exists(_finalFilePath))
                return false;

            try
            {
                var lines = File.ReadAllLines(_finalFilePath, Encoding.UTF8);
                var changed = false;
                for (var i = 0; i < lines.Length; i++)
                {
                    var snapshot = SafeDeserialize(lines[i]);
                    if (snapshot == null || !string.Equals(snapshot.MatchId, matchId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    snapshot.ManualTags = SanitizeTags(manualTags, 5).ToList();
                    lines[i] = _serializer.Serialize(snapshot);
                    changed = true;
                    break;
                }

                if (!changed)
                    return false;

                File.WriteAllLines(_finalFilePath, lines, Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] UpdateManualTags 失败: " + ex.Message);
                return false;
            }
        }

        public void WritePendingIfNeeded(string heroCardId, string heroSkinCardId, string initialHeroPowerCardId, int ratingBefore)
        {
            try
            {
                var pendingFilePath = GetWritePendingFilePath();
                if (PendingWritten || string.IsNullOrEmpty(pendingFilePath) || string.IsNullOrEmpty(heroCardId))
                    return;

                CurrentMatchId = Guid.NewGuid().ToString("N");
                CurrentMatchTimestampUtc = DateTime.UtcNow.ToString("o");
                PendingWritten = true;

                var pendingLine = "{"
                    + $"\"matchId\":\"{JsonEscape(CurrentMatchId)}\"," 
                    + $"\"timestamp\":\"{JsonEscape(CurrentMatchTimestampUtc)}\"," 
                    + $"\"gameVersion\":\"{JsonEscape((_activeMatchArchive ?? CurrentArchive)?.DisplayName ?? string.Empty)}\"," 
                    + $"\"heroCardId\":\"{JsonEscape(heroCardId)}\"," 
                    + $"\"heroSkinCardId\":\"{JsonEscape(heroSkinCardId ?? string.Empty)}\"," 
                    + $"\"initialHeroPowerCardId\":\"{JsonEscape(initialHeroPowerCardId ?? string.Empty)}\""
                    + "}";

                File.AppendAllText(pendingFilePath, pendingLine + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] WritePendingIfNeeded 失败: " + ex.Message);
            }
        }

        public void FinalizeIfPossible(string matchId, string timestamp, string heroCardId, string heroSkinCardId, string initialHeroPowerCardId, string finalHeroPowerCardId, int placement, int ratingBefore, int ratingAfter, string[] availableRaces, string anomalyCardId, IReadOnlyCollection<BgBoardMinionSnapshot> finalBoard, IReadOnlyCollection<BgTavernUpgradePoint> tavernUpgradeTimeline)
        {
            try
            {
                var finalFilePath = GetWriteFinalFilePath();
                if (string.IsNullOrEmpty(matchId) || string.IsNullOrEmpty(GetWritePendingFilePath()) || string.IsNullOrEmpty(finalFilePath))
                    return;
                if (string.IsNullOrEmpty(heroCardId) || placement <= 0 || ratingAfter <= 0)
                    return;

                RemovePendingByMatchId(matchId);

                var normalizedBoard = (finalBoard ?? Array.Empty<BgBoardMinionSnapshot>())
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.CardId))
                    .OrderBy(x => x.Position)
                    .ToList();

                var snapshot = new BgSnapshot
                {
                    MatchId = matchId,
                    Timestamp = string.IsNullOrEmpty(timestamp) ? DateTime.UtcNow.ToString("o") : timestamp,
                    GameVersion = (_activeMatchArchive ?? CurrentArchive)?.DisplayName ?? string.Empty,
                    HeroCardId = heroCardId,
                    HeroName = GetCardName(heroCardId),
                    HeroSkinCardId = heroSkinCardId ?? string.Empty,
                    InitialHeroPowerCardId = initialHeroPowerCardId ?? string.Empty,
                    HeroPowerCardId = finalHeroPowerCardId ?? string.Empty,
                    Placement = placement,
                    RatingBefore = ratingBefore,
                    RatingAfter = ratingAfter,
                    RatingDelta = (ratingBefore > 0 && ratingAfter > 0) ? (ratingAfter - ratingBefore) : 0,
                    AvailableRaces = availableRaces ?? Array.Empty<string>(),
                    AnomalyCardId = anomalyCardId ?? string.Empty,
                    AnomalyName = GetCardName(anomalyCardId),
                    FinalBoard = normalizedBoard,
                    FinalBoardCardIds = normalizedBoard.Select(x => x.CardId).ToArray(),
                    TavernUpgradeTimeline = (tavernUpgradeTimeline ?? Array.Empty<BgTavernUpgradePoint>())
                        .Where(x => x != null && x.Turn > 0 && x.TavernTier > 0)
                        .OrderBy(x => x.Turn)
                        .ThenBy(x => x.TavernTier)
                        .ToList(),
                    ManualTags = new List<string>()
                };
                snapshot.AutoTags = _lineupTagService.Evaluate(snapshot).ToList();

                File.AppendAllText(finalFilePath, _serializer.Serialize(snapshot) + Environment.NewLine, Encoding.UTF8);
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

        private void SetActiveMatchArchive(ArchiveVersionInfo info)
        {
            var target = info ?? ArchiveKeyProvider.GetDefaultArchive();
            if (string.IsNullOrWhiteSpace(target.Key))
                target.Key = ArchiveKeyProvider.BuildArchiveKey(target.DisplayName);
            if (string.IsNullOrWhiteSpace(target.DisplayName))
                target.DisplayName = ArchiveKeyProvider.BuildDisplayNameFromKey(target.Key);

            var archiveDir = Path.Combine(_archivesDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HDT_BGStats", "Data", "archives"), target.Key);
            Directory.CreateDirectory(archiveDir);

            _activeFinalFilePath = Path.Combine(archiveDir, "bg_stats.jsonl");
            _activePendingFilePath = Path.Combine(archiveDir, "bg_stats_pending.jsonl");
            _activeMatchArchive = target;
            File.WriteAllText(Path.Combine(archiveDir, "label.txt"), target.DisplayName ?? string.Empty, Encoding.UTF8);
        }

        private ArchiveVersionInfo ResolveInitialArchive()
        {
            var preferred = ResolveBestArchiveForCurrentVersion();
            if (HasRecordedMatches(preferred?.Key))
                return preferred;

            var latestRecorded = GetLatestRecordedArchive();
            if (latestRecorded != null)
                return latestRecorded;

            return preferred ?? ArchiveKeyProvider.GetDefaultArchive();
        }

        private ArchiveVersionInfo ResolveBestArchiveForCurrentVersion()
        {
            var detected = ArchiveKeyProvider.ResolveCurrentArchive(_versionDisplayService.MapVersion);
            if (detected == null)
                return ArchiveKeyProvider.GetDefaultArchive();

            var recorded = GetRecordedArchivesInternal();
            var exact = recorded.FirstOrDefault(x => string.Equals(x.Key, detected.Key, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            var sameDisplayName = recorded.FirstOrDefault(x => string.Equals(x.DisplayName, detected.DisplayName, StringComparison.OrdinalIgnoreCase));
            if (sameDisplayName != null)
                return sameDisplayName;

            var samePatch = recorded.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.PatchVersion) && string.Equals(x.PatchVersion, detected.PatchVersion, StringComparison.OrdinalIgnoreCase));
            if (samePatch != null)
                return samePatch;

            return detected;
        }

        private IReadOnlyList<BgSnapshot> LoadSnapshots()
        {
            var rows = new List<BgSnapshot>();
            if (string.IsNullOrWhiteSpace(_finalFilePath) || !File.Exists(_finalFilePath))
                return rows;

            foreach (var line in File.ReadAllLines(_finalFilePath, Encoding.UTF8))
            {
                var snapshot = SafeDeserialize(line);
                if (snapshot != null && snapshot.Placement > 0)
                    rows.Add(NormalizeSnapshot(snapshot));
            }

            return rows;
        }

        private List<ArchiveVersionInfo> GetRecordedArchivesInternal()
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
                .ThenByDescending(GetArchiveSortKey)
                .ThenByDescending(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private ArchiveVersionInfo GetLatestRecordedArchive()
        {
            if (string.IsNullOrWhiteSpace(_archivesDir) || !Directory.Exists(_archivesDir))
                return null;

            return GetRecordedArchivesInternal()
                .OrderByDescending(GetArchiveSortKey)
                .ThenByDescending(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private bool HasRecordedMatches(string archiveKey)
        {
            if (string.IsNullOrWhiteSpace(archiveKey) || string.IsNullOrWhiteSpace(_archivesDir))
                return false;

            try
            {
                var finalFile = Path.Combine(_archivesDir, archiveKey, "bg_stats.jsonl");
                return File.Exists(finalFile) && new FileInfo(finalFile).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private BgSnapshot NormalizeSnapshot(BgSnapshot snapshot)
        {
            snapshot.AvailableRaces = snapshot.AvailableRaces ?? Array.Empty<string>();
            snapshot.FinalBoardCardIds = snapshot.FinalBoardCardIds ?? Array.Empty<string>();
            snapshot.FinalBoard = (snapshot.FinalBoard ?? new List<BgBoardMinionSnapshot>())
                .Where(x => x != null)
                .OrderBy(x => x.Position)
                .ToList();
            snapshot.TavernUpgradeTimeline = (snapshot.TavernUpgradeTimeline ?? new List<BgTavernUpgradePoint>())
                .Where(x => x != null)
                .OrderBy(x => x.Turn)
                .ThenBy(x => x.TavernTier)
                .ToList();
            snapshot.AutoTags = SanitizeTags(snapshot.AutoTags, 3).ToList();
            snapshot.ManualTags = SanitizeTags(snapshot.ManualTags, 5).ToList();

            if ((snapshot.FinalBoard == null || snapshot.FinalBoard.Count == 0) && snapshot.FinalBoardCardIds.Length > 0)
            {
                snapshot.FinalBoard = snapshot.FinalBoardCardIds
                    .Select((cardId, index) => new BgBoardMinionSnapshot
                    {
                        CardId = cardId,
                        Name = string.Empty,
                        Position = index + 1,
                        Race = string.Empty,
                        Keywords = new BgKeywordState()
                    })
                    .ToList();
            }

            return snapshot;
        }

        private BgSnapshot SafeDeserialize(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;
            try
            {
                return _serializer.Deserialize<BgSnapshot>(line);
            }
            catch
            {
                return null;
            }
        }

        private IReadOnlyList<string> MergeTags(BgSnapshot snapshot)
        {
            if (snapshot == null)
                return Array.Empty<string>();

            var result = new List<string>();
            foreach (var tag in SanitizeTags(snapshot.AutoTags, 3))
            {
                if (!result.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    result.Add(tag);
            }

            foreach (var tag in SanitizeTags(snapshot.ManualTags, 5))
            {
                if (result.Count >= 5)
                    break;
                if (!result.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    result.Add(tag);
            }

            return result;
        }

        private static IEnumerable<string> SanitizeTags(IEnumerable<string> tags, int maxCount)
        {
            return (tags ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maxCount);
        }

        private void RemovePendingByMatchId(string matchId)
        {
            var pendingFilePath = GetWritePendingFilePath();
            if (!File.Exists(pendingFilePath))
                return;

            var kept = File.ReadAllLines(pendingFilePath, Encoding.UTF8)
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.Contains($"\"matchId\":\"{matchId}\""))
                .ToArray();
            File.WriteAllLines(pendingFilePath, kept, Encoding.UTF8);
        }

        private static DateTime ParseTimestamp(string value)
        {
            return DateTimeOffset.TryParse(value, out var dto) ? dto.ToLocalTime().DateTime : default(DateTime);
        }

        private static string NormalizeDisplay(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private string GetWriteFinalFilePath()
        {
            return string.IsNullOrWhiteSpace(_activeFinalFilePath) ? _finalFilePath : _activeFinalFilePath;
        }

        private string GetWritePendingFilePath()
        {
            return string.IsNullOrWhiteSpace(_activePendingFilePath) ? _pendingFilePath : _activePendingFilePath;
        }

        private static long GetArchiveSortKey(ArchiveVersionInfo archive)
        {
            if (archive == null || string.IsNullOrWhiteSpace(archive.PatchVersion))
                return long.MinValue;

            var parts = archive.PatchVersion.Split('.');
            long major = 0;
            long minor = 0;
            long patch = 0;
            if (parts.Length > 0)
                long.TryParse(parts[0], out major);
            if (parts.Length > 1)
                long.TryParse(parts[1], out minor);
            if (parts.Length > 2)
                long.TryParse(parts[2], out patch);

            return major * 1_000_000L + minor * 1_000L + patch;
        }

        private static string BuildFinalBoardDisplay(BgSnapshot snapshot)
        {
            var board = snapshot?.FinalBoard;
            if (board == null || board.Count == 0)
                return Loc.S("Common_Todo");

            var names = board
                .OrderBy(x => x.Position)
                .Take(7)
                .Select(x => (x.IsGolden ? GetGoldPrefix() : string.Empty) + GameTextService.GetCardName(x.CardId, NormalizeDisplay(x.Name, x.CardId)))
                .ToArray();
            return names.Length == 0 ? Loc.S("Common_Todo") : string.Join(" / ", names);
        }

        private static string GetGoldPrefix()
        {
            return Loc.S("Common_GoldPrefix");
        }

        public static string GetCardName(string cardId)
        {
            return GameTextService.GetCardName(cardId);
        }

        public static string GetRaceNameFromCardId(string cardId, string fallback = null)
        {
            return GameTextService.GetRaceNameFromCardId(cardId, fallback);
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

        private static string GetPluginDirectory()
        {
            try
            {
                var assemblyPath = typeof(StatsStore).Assembly.Location;
                if (!string.IsNullOrWhiteSpace(assemblyPath))
                    return Path.GetDirectoryName(assemblyPath);
            }
            catch { }

            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrWhiteSpace(baseDir))
                    return baseDir.TrimEnd('\\');
            }
            catch { }

            return Environment.CurrentDirectory;
        }
    }
}



