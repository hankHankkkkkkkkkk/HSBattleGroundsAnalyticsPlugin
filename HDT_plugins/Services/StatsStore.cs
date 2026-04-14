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
        private const string UnknownAccountKey = "unknown-account";
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private readonly HeroStatsAggregationService _heroStatsService = new HeroStatsAggregationService();
        private readonly LineupTagService _lineupTagService = new LineupTagService();
        private readonly VersionDisplayService _versionDisplayService = new VersionDisplayService();
        private readonly TavernTempoAggregationService _tavernTempoService = new TavernTempoAggregationService();

        private string _dataDir;
        private string _archivesDir;
        private string _currentArchiveDir;
        private string _finalFilePath;
        private string _pendingFilePath;
        private string _tablesDir;
        private ArchiveVersionInfo _activeMatchArchive;
        private string _activeArchiveDir;
        private string _activeFinalFilePath;
        private string _activePendingFilePath;
        private AccountRecord _activeMatchAccount;
        private string _selectedArchiveKey;

        public string CurrentMatchId { get; private set; }
        public string CurrentMatchTimestampUtc { get; private set; }
        public bool PendingWritten { get; private set; }
        public ArchiveVersionInfo CurrentArchive { get; private set; }
        public AccountRecord CurrentAccount { get; private set; }
        public string CurrentAccountKey => CurrentAccount?.Key;
        public string SelectedArchiveKey => _selectedArchiveKey;
        public string FinalFilePath => _finalFilePath;
        public string TagConfigPath => _lineupTagService.ConfigPath;
        public string TablesDirectoryPath => _tablesDir;
        public AccountRecord UnknownAccount => CreateUnknownAccount();


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

                HdtLog.Info($"[BGStats] æ•°æ®ç›®å½•(AppData Local)ï¼š{_dataDir}");
                HdtLog.Info($"[BGStats] å½“å‰ç‰ˆæœ¬å½’æ¡£ï¼š{CurrentArchive?.DisplayName}");
                HdtLog.Info($"[BGStats] é…ç½®ç›®å½•ï¼š{_tablesDir}");
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] Initialize å¤±è´¥: " + ex.Message);
            }
        }

        public IReadOnlyList<string> GetAvailableTags(BgSnapshot snapshot = null)
        {
            return _lineupTagService.GetAvailableTags(GetSnapshotVersionDisplayName(snapshot));
        }

        public AccountRecord InitializeSelectedAccount(string preferredAccountKey)
        {
            var resolved = ResolveExistingAccount(preferredAccountKey) ?? GetMostRecentAccount() ?? GetAvailableAccounts().FirstOrDefault() ?? CreateUnknownAccount();
            SetCurrentAccountInternal(resolved);
            RefreshLatestRecordedArchiveForDisplay();
            return CurrentAccount;
        }

        public AccountRecord ApplyCurrentAccountFromGame(AccountRecord account)
        {
            var normalized = NormalizeAccountRecord(account);
            if (_activeMatchAccount == null)
                SetActiveMatchAccount(normalized);

            var currentKey = CurrentAccount?.Key ?? string.Empty;
            var normalizedKey = normalized?.Key ?? string.Empty;
            var shouldSwitch = CurrentAccount == null
                || CurrentAccount.IsUnknown
                || (!normalized.IsUnknown && !string.Equals(currentKey, normalizedKey, StringComparison.OrdinalIgnoreCase))
                || ShouldRefreshCurrentAccountMetadata(normalized);

            if (shouldSwitch)
                SetCurrentAccountInternal(normalized);

            return CurrentAccount;
        }

        public AccountRecord SetCurrentAccountByKey(string accountKey)
        {
            var resolved = ResolveExistingAccount(accountKey) ?? CreateUnknownAccount();
            SetCurrentAccountInternal(resolved);
            return CurrentAccount;
        }

        public IReadOnlyList<AccountRecord> GetAvailableAccounts()
        {
            var accounts = new Dictionary<string, AccountRecord>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(_archivesDir) || !Directory.Exists(_archivesDir))
                return new[] { CreateUnknownAccount() };

            foreach (var archiveDir in Directory.GetDirectories(_archivesDir))
            {
                EnsureLegacyArchiveMigratedToUnknown(archiveDir);

                var accountsDir = Path.Combine(archiveDir, "accounts");
                if (!Directory.Exists(accountsDir))
                    continue;

                foreach (var accountDir in Directory.GetDirectories(accountsDir))
                {
                    var finalFilePath = Path.Combine(accountDir, "bg_stats.jsonl");
                    if (!File.Exists(finalFilePath) || new FileInfo(finalFilePath).Length <= 0)
                        continue;

                    var key = Path.GetFileName(accountDir);
                    var record = TryReadAccountRecord(finalFilePath, key) ?? CreateUnknownAccount();
                    record.MatchCount += CountSnapshots(finalFilePath);
                    MergeAccountRecord(accounts, record);
                }
            }

            if (accounts.Count == 0)
                MergeAccountRecord(accounts, CreateUnknownAccount());

            return accounts.Values
                .OrderByDescending(x => string.Equals(x.Key, CurrentAccount?.Key, StringComparison.OrdinalIgnoreCase))
                .ThenBy(x => x.IsUnknown ? 1 : 0)
                .ThenByDescending(x => x.MatchCount)
                .ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public AccountRecord GetMostRecentAccount()
        {
            AccountRecord bestAccount = null;
            DateTime bestTimestamp = default(DateTime);

            if (string.IsNullOrWhiteSpace(_archivesDir) || !Directory.Exists(_archivesDir))
                return null;

            foreach (var archiveDir in Directory.GetDirectories(_archivesDir))
            {
                EnsureLegacyArchiveMigratedToUnknown(archiveDir);
                var accountsDir = Path.Combine(archiveDir, "accounts");
                if (!Directory.Exists(accountsDir))
                    continue;

                foreach (var accountDir in Directory.GetDirectories(accountsDir))
                {
                    var finalFilePath = Path.Combine(accountDir, "bg_stats.jsonl");
                    if (!File.Exists(finalFilePath) || new FileInfo(finalFilePath).Length <= 0)
                        continue;

                    var latestSnapshot = TryGetLatestSnapshot(finalFilePath);
                    if (latestSnapshot == null)
                        continue;

                    var timestamp = ParseTimestamp(latestSnapshot.Timestamp);
                    if (timestamp <= bestTimestamp)
                        continue;

                    bestTimestamp = timestamp;
                    bestAccount = NormalizeAccountRecord(new AccountRecord
                    {
                        Key = Path.GetFileName(accountDir),
                        AccountHi = latestSnapshot.AccountHi,
                        AccountLo = latestSnapshot.AccountLo,
                        BattleTag = latestSnapshot.BattleTag,
                        ServerInfo = latestSnapshot.ServerInfo,
                        RegionCode = latestSnapshot.RegionCode,
                        RegionName = latestSnapshot.RegionName
                    });
                }
            }

            return bestAccount;
        }

        public IReadOnlyList<string> GetDisplayTags(BgSnapshot snapshot)
        {
            return MergeTags(snapshot).Take(5).ToList();
        }

        public IReadOnlyList<RaceStatsRow> LoadRaceStats(double scoreLine)
        {
            var normalizedScoreLine = NormalizeScoreLine(scoreLine);
            var snapshots = LoadSnapshots();
            var raceDefs = GetRaceDefinitions();
            var result = new List<RaceStatsRow>();

            foreach (var raceDef in raceDefs)
            {
                var raceSnapshots = snapshots
                    .Where(snapshot => SnapshotHasRaceTag(snapshot, raceDef))
                    .ToList();

                var row = new RaceStatsRow
                {
                    RaceCode = raceDef.Code,
                    RaceTag = raceDef.Tag,
                    RaceName = GameTextService.GetRaceName(raceDef.Code, raceDef.DisplayFallback),
                    MatchCount = raceSnapshots.Count
                };

                if (raceSnapshots.Count > 0)
                {
                    row.PickRate = snapshots.Count == 0 ? 0 : raceSnapshots.Count / (double)snapshots.Count;
                    row.AveragePlacement = raceSnapshots.Average(x => x.Placement);
                    row.FirstRate = raceSnapshots.Count(x => x.Placement == 1) / (double)raceSnapshots.Count;
                    row.ScoreRate = raceSnapshots.Count(x => x.Placement < normalizedScoreLine) / (double)raceSnapshots.Count;
                    row.TopCards = BuildRaceTopCards(raceSnapshots);
                    row.TopLineups = BuildRaceTopLineups(raceSnapshots, raceDef);
                    PopulateRaceSynergies(row, raceSnapshots, snapshots, raceDefs);
                }

                result.Add(row);
            }

            return result
                .OrderBy(x => x.HasData ? 0 : 1)
                .ThenBy(x => x.AveragePlacement ?? double.MaxValue)
                .ThenBy(x => x.RaceName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
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
            return BuildDisplayArchives(GetRecordedArchivesInternal());
        }

        public ArchiveVersionInfo SetArchiveByKey(string archiveKey)
        {
            var normalizedKey = string.IsNullOrWhiteSpace(archiveKey) ? string.Empty : archiveKey.Trim();
            var displayItem = BuildDisplayArchives(GetRecordedArchivesInternal())
                .FirstOrDefault(x => string.Equals(x.Key, normalizedKey, StringComparison.OrdinalIgnoreCase));
            if (displayItem != null && IsVirtualArchiveSelection(displayItem.Key))
            {
                _selectedArchiveKey = displayItem.Key;
                return displayItem;
            }

            var info = GetAvailableArchives().FirstOrDefault(x => string.Equals(x.Key, normalizedKey, StringComparison.OrdinalIgnoreCase))
                ?? ArchiveKeyProvider.CreateFromStoredLabel(normalizedKey, null);
            SetArchiveInternal(info);
            _selectedArchiveKey = info.Key;
            return GetSelectedArchiveForDisplay();
        }

        public ArchiveVersionInfo ConfirmCurrentArchiveForMatch()
        {
            var detected = ResolveBestArchiveForCurrentVersion();
            SetActiveMatchArchive(detected);
            HdtLog.Info($"[BGStats] å½“å‰å¯¹å±€ç‰ˆæœ¬ï¼š{_activeMatchArchive?.DisplayName}");
            return _activeMatchArchive;
        }

        public ArchiveVersionInfo GetLatestRecordedArchiveForDisplay()
        {
            return GetMostRecentRecordedArchive(CurrentAccountKey) ?? GetMostRecentRecordedArchive() ?? GetSelectedArchiveForDisplay() ?? CurrentArchive;
        }

        public ArchiveVersionInfo GetSelectedArchiveForDisplay()
        {
            var displayItem = BuildDisplayArchives(GetRecordedArchivesInternal())
                .FirstOrDefault(x => string.Equals(x.Key, _selectedArchiveKey, StringComparison.OrdinalIgnoreCase));
            if (displayItem != null)
                return displayItem;

            return CurrentArchive;
        }

        public string GetArchiveDisplayName(ArchiveVersionInfo archive)
        {
            if (archive == null)
                return string.Empty;

            if (IsVirtualArchiveSelection(archive.Key))
                return archive.DisplayName ?? string.Empty;

            var rawVersion = GetArchiveRawVersion(archive);
            if (!string.IsNullOrWhiteSpace(rawVersion))
                return _versionDisplayService.RememberAndMapVersion(rawVersion);

            return archive.DisplayName ?? string.Empty;
        }

        public string GetGameVersionDisplayName(string rawVersion)
        {
            if (string.IsNullOrWhiteSpace(rawVersion))
                return string.Empty;

            return _versionDisplayService.RememberAndMapVersion(rawVersion);
        }

        public bool ShouldDefaultToTrinketStatsPage()
        {
            return _versionDisplayService.SelectionContainsDisplayToken(_selectedArchiveKey, GetRecordedArchivesInternal(), "Season13");
        }

        public bool ShouldShowTrinketDetails(BgSnapshot snapshot)
        {
            return GetSnapshotVersionDisplayName(snapshot).IndexOf("Season13", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public ArchiveVersionInfo RefreshLatestRecordedArchiveForDisplay()
        {
            var latest = GetMostRecentRecordedArchive(CurrentAccountKey) ?? GetMostRecentRecordedArchive();
            if (latest == null)
                return GetSelectedArchiveForDisplay() ?? CurrentArchive;

            if (!string.Equals(CurrentArchive?.Key, latest.Key, StringComparison.OrdinalIgnoreCase))
                SetArchiveInternal(latest);

            _selectedArchiveKey = latest.Key;
            return GetSelectedArchiveForDisplay();
        }

        public void ResetMatch()
        {
            CurrentMatchId = null;
            CurrentMatchTimestampUtc = null;
            PendingWritten = false;
            _activeMatchArchive = null;
            _activeArchiveDir = null;
            _activeMatchAccount = null;
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

        public HeroStatsSummary LoadHeroStats(double scoreLine)
        {
            return _heroStatsService.BuildSummary(LoadSnapshots(), scoreLine);
        }

        public TavernTempoSummary LoadTavernTempoSummary()
        {
            return _tavernTempoService.BuildSummary(LoadSnapshots());
        }

        public TrinketStatsSummary LoadTrinketStats(double scoreLine, TrinketFilter filter)
        {
            var normalizedScoreLine = NormalizeScoreLine(scoreLine);
            var snapshots = LoadSnapshots();
            var rows = new Dictionary<string, List<BgSnapshot>>(StringComparer.OrdinalIgnoreCase);
            var eligibleSnapshots = snapshots
                .Where(snapshot => ShouldShowTrinketDetails(snapshot))
                .ToList();

            foreach (var snapshot in eligibleSnapshots)
            {
                foreach (var cardId in GetSnapshotTrinketCardIds(snapshot, filter))
                {
                    if (!rows.TryGetValue(cardId, out var group))
                    {
                        group = new List<BgSnapshot>();
                        rows[cardId] = group;
                    }

                    if (!group.Contains(snapshot))
                        group.Add(snapshot);
                }
            }

            return new TrinketStatsSummary
            {
                Filter = filter,
                EligibleMatches = eligibleSnapshots.Count,
                Rows = rows
                    .Select(pair => new TrinketStatsRow
                    {
                        CardId = pair.Key,
                        CardName = GameTextService.GetCardName(pair.Key, pair.Key),
                        MatchCount = pair.Value.Count,
                        PickRate = eligibleSnapshots.Count == 0 ? 0 : pair.Value.Count / (double)eligibleSnapshots.Count,
                        FirstRate = pair.Value.Count == 0 ? 0 : pair.Value.Count(x => x.Placement == 1) / (double)pair.Value.Count,
                        ScoreRate = pair.Value.Count == 0 ? 0 : pair.Value.Count(x => x.Placement < normalizedScoreLine) / (double)pair.Value.Count
                    })
                    .OrderByDescending(x => x.MatchCount)
                    .ThenBy(x => x.CardName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()
            };
        }

        public TimewarpStatsSummary LoadTimewarpStats(double scoreLine, TimewarpFilter filter)
        {
            var normalizedScoreLine = NormalizeScoreLine(scoreLine);
            var snapshots = LoadSnapshots();
            var eligibleSnapshots = snapshots
                .Where(snapshot => (snapshot.TimewarpEntries ?? new List<BgTimewarpEntry>()).Any(entry => IsMatchingTimewarpEntry(entry, filter)))
                .ToList();
            var appearances = new Dictionary<string, List<BgSnapshot>>(StringComparer.OrdinalIgnoreCase);
            var picks = new Dictionary<string, List<BgSnapshot>>(StringComparer.OrdinalIgnoreCase);

            foreach (var snapshot in eligibleSnapshots)
            {
                var entries = (snapshot.TimewarpEntries ?? new List<BgTimewarpEntry>())
                    .Where(entry => IsMatchingTimewarpEntry(entry, filter))
                    .ToList();

                foreach (var cardId in entries.SelectMany(entry => entry.OptionCardIds ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!appearances.TryGetValue(cardId, out var group))
                    {
                        group = new List<BgSnapshot>();
                        appearances[cardId] = group;
                    }

                    if (!group.Contains(snapshot))
                        group.Add(snapshot);
                }

                foreach (var cardId in entries.SelectMany(entry => entry.SelectedCardIds ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!picks.TryGetValue(cardId, out var group))
                    {
                        group = new List<BgSnapshot>();
                        picks[cardId] = group;
                    }

                    if (!group.Contains(snapshot))
                        group.Add(snapshot);
                }
            }

            var cardIds = appearances.Keys
                .Concat(picks.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new TimewarpStatsSummary
            {
                Filter = filter,
                EligibleMatches = eligibleSnapshots.Count,
                Rows = cardIds
                    .Select(cardId =>
                    {
                        appearances.TryGetValue(cardId, out var appearanceSnapshots);
                        picks.TryGetValue(cardId, out var pickSnapshots);
                        appearanceSnapshots = appearanceSnapshots ?? new List<BgSnapshot>();
                        pickSnapshots = pickSnapshots ?? new List<BgSnapshot>();
                        return new TimewarpStatsRow
                        {
                            CardId = cardId,
                            CardName = GameTextService.GetCardName(cardId, cardId),
                            AppearanceCount = appearanceSnapshots.Count,
                            PickCount = pickSnapshots.Count,
                            AppearanceRate = eligibleSnapshots.Count == 0 ? 0 : appearanceSnapshots.Count / (double)eligibleSnapshots.Count,
                            PickRate = appearanceSnapshots.Count == 0 ? 0 : pickSnapshots.Count / (double)appearanceSnapshots.Count,
                            FirstRate = pickSnapshots.Count == 0 ? 0 : pickSnapshots.Count(x => x.Placement == 1) / (double)pickSnapshots.Count,
                            ScoreRate = pickSnapshots.Count == 0 ? 0 : pickSnapshots.Count(x => x.Placement < normalizedScoreLine) / (double)pickSnapshots.Count
                        };
                    })
                    .OrderByDescending(x => x.AppearanceCount)
                    .ThenByDescending(x => x.PickCount)
                    .ThenBy(x => x.CardName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()
            };
        }

        public BgSnapshot LoadSnapshot(string matchId)
        {
            if (string.IsNullOrWhiteSpace(matchId))
                return null;
            return LoadSnapshots().FirstOrDefault(x => string.Equals(x.MatchId, matchId, StringComparison.OrdinalIgnoreCase));
        }

        public bool UpdateManualTags(string matchId, IReadOnlyCollection<string> manualTags)
        {
            if (string.IsNullOrWhiteSpace(matchId))
                return false;

            try
            {
                var candidateFiles = GetSelectedArchiveFinalFilePaths()
                    .Concat(new[] { _finalFilePath })
                    .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var finalFilePath in candidateFiles)
                {
                    var lines = File.ReadAllLines(finalFilePath, Encoding.UTF8);
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
                        continue;

                    File.WriteAllLines(finalFilePath, lines, Encoding.UTF8);
                    return true;
                }

                HdtLog.Warn("[BGStats] UpdateManualTags 未找到匹配对局: " + matchId);
                return false;
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] UpdateManualTags å¤±è´¥: " + ex.Message);
                return false;
            }
        }

        public void WritePendingIfNeeded(string heroCardId, string heroSkinCardId, string[] initialHeroPowerCardIds, int ratingBefore)
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
                    + $"\"gameVersion\":\"{JsonEscape(GetArchiveRawVersion(_activeMatchArchive ?? CurrentArchive))}\"," 
                    + $"\"heroCardId\":\"{JsonEscape(HeroIdNormalizer.Normalize(heroCardId))}\"," 
                    + $"\"heroSkinCardId\":\"{JsonEscape(heroSkinCardId ?? string.Empty)}\"," 
                    + $"\"initialHeroPowerCardIds\":{SerializeStringArray(initialHeroPowerCardIds)}," 
                    + $"\"initialHeroPowerCardId\":\"{JsonEscape((initialHeroPowerCardIds ?? Array.Empty<string>()).FirstOrDefault() ?? string.Empty)}\""
                    + "}";

                File.AppendAllText(pendingFilePath, pendingLine + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] WritePendingIfNeeded å¤±è´¥: " + ex.Message);
            }
        }

        public void FinalizeIfPossible(string matchId, string timestamp, string heroCardId, string heroSkinCardId, string[] initialHeroPowerCardIds, string[] finalHeroPowerCardIds, int placement, int ratingBefore, int ratingAfter, string[] offeredHeroCardIds, string[] availableRaces, string anomalyCardId, IReadOnlyCollection<BgBoardMinionSnapshot> finalBoard, IReadOnlyCollection<BgTavernUpgradePoint> tavernUpgradeTimeline, string[] lesserTrinketOptionCardIds, string lesserTrinketCardId, string[] greaterTrinketOptionCardIds, string greaterTrinketCardId, string heroPowerTrinketCardId, string heroPowerTrinketType, IReadOnlyCollection<BgTimewarpEntry> timewarpEntries, string questCardId, string questRewardCardId, bool questCompleted)
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
                var normalizedHeroCardId = HeroIdNormalizer.Normalize(heroCardId);
                var normalizedOfferedHeroCardIds = (offeredHeroCardIds ?? Array.Empty<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(HeroIdNormalizer.Normalize)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var snapshot = new BgSnapshot
                {
                    MatchId = matchId,
                    Timestamp = string.IsNullOrEmpty(timestamp) ? DateTime.UtcNow.ToString("o") : timestamp,
                    GameVersion = GetArchiveRawVersion(_activeMatchArchive ?? CurrentArchive),
                    AccountKey = (_activeMatchAccount ?? CurrentAccount ?? CreateUnknownAccount()).Key,
                    AccountHi = (_activeMatchAccount ?? CurrentAccount)?.AccountHi ?? string.Empty,
                    AccountLo = (_activeMatchAccount ?? CurrentAccount)?.AccountLo ?? string.Empty,
                    BattleTag = (_activeMatchAccount ?? CurrentAccount)?.BattleTag ?? string.Empty,
                    ServerInfo = (_activeMatchAccount ?? CurrentAccount)?.ServerInfo ?? string.Empty,
                    RegionCode = (_activeMatchAccount ?? CurrentAccount)?.RegionCode ?? string.Empty,
                    RegionName = (_activeMatchAccount ?? CurrentAccount)?.RegionName ?? string.Empty,
                    HeroCardId = normalizedHeroCardId,
                    HeroName = GetCardName(normalizedHeroCardId),
                    HeroSkinCardId = heroSkinCardId ?? string.Empty,
                    InitialHeroPowerCardIds = (initialHeroPowerCardIds ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray(),
                    InitialHeroPowerCardId = (initialHeroPowerCardIds ?? Array.Empty<string>()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
                    HeroPowerCardIds = (finalHeroPowerCardIds ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray(),
                    HeroPowerCardId = (finalHeroPowerCardIds ?? Array.Empty<string>()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
                    Placement = placement,
                    RatingBefore = ratingBefore,
                    RatingAfter = ratingAfter,
                    RatingDelta = (ratingBefore > 0 && ratingAfter > 0) ? (ratingAfter - ratingBefore) : 0,
                    OfferedHeroCardIds = normalizedOfferedHeroCardIds,
                    AvailableRaces = availableRaces ?? Array.Empty<string>(),
                    LesserTrinketOptionCardIds = NormalizeCardIdArray(lesserTrinketOptionCardIds),
                    LesserTrinketCardId = NormalizeText(lesserTrinketCardId) ?? string.Empty,
                    GreaterTrinketOptionCardIds = NormalizeCardIdArray(greaterTrinketOptionCardIds),
                    GreaterTrinketCardId = NormalizeText(greaterTrinketCardId) ?? string.Empty,
                    HeroPowerTrinketCardId = NormalizeText(heroPowerTrinketCardId) ?? string.Empty,
                    HeroPowerTrinketType = NormalizeTrinketType(heroPowerTrinketType),
                    TimewarpEntries = (timewarpEntries ?? Array.Empty<BgTimewarpEntry>())
                        .Where(x => x != null)
                        .Select(entry => new BgTimewarpEntry
                        {
                            Type = NormalizeTimewarpType(entry.Type),
                            IsExtra = entry.IsExtra,
                            OptionCardIds = NormalizeCardIdArray(entry.OptionCardIds),
                            SelectedCardIds = NormalizeCardIdArray(entry.SelectedCardIds)
                        })
                        .Where(entry => entry.OptionCardIds.Length > 0 || entry.SelectedCardIds.Length > 0)
                        .ToList(),
                    QuestCardId = NormalizeText(questCardId) ?? string.Empty,
                    QuestRewardCardId = NormalizeText(questRewardCardId) ?? string.Empty,
                    QuestCompleted = questCompleted,
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
                snapshot.AutoTags = _lineupTagService.Evaluate(snapshot, GetSnapshotVersionDisplayName(snapshot)).ToList();

                File.AppendAllText(finalFilePath, _serializer.Serialize(snapshot) + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] FinalizeIfPossible å¤±è´¥: " + ex.Message);
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
            _currentArchiveDir = archiveDir;
            EnsureLegacyArchiveMigratedToUnknown(archiveDir);
            CurrentArchive = target;
            _selectedArchiveKey = target.Key;
            File.WriteAllText(Path.Combine(archiveDir, "label.txt"), string.IsNullOrWhiteSpace(GetArchiveRawVersion(target)) ? (target.DisplayName ?? string.Empty) : GetArchiveRawVersion(target), Encoding.UTF8);
            SetCurrentAccountInternal(CurrentAccount ?? CreateUnknownAccount());
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
            _activeArchiveDir = archiveDir;
            EnsureLegacyArchiveMigratedToUnknown(archiveDir);
            _activeMatchArchive = target;
            File.WriteAllText(Path.Combine(archiveDir, "label.txt"), string.IsNullOrWhiteSpace(GetArchiveRawVersion(target)) ? (target.DisplayName ?? string.Empty) : GetArchiveRawVersion(target), Encoding.UTF8);
        }

        private ArchiveVersionInfo ResolveInitialArchive()
        {
            var preferred = ResolveBestArchiveForCurrentVersion();
            if (HasRecordedMatches(preferred?.Key))
                return preferred;

            var latestRecorded = GetMostRecentRecordedArchive(CurrentAccountKey) ?? GetMostRecentRecordedArchive();
            if (latestRecorded != null)
                return latestRecorded;

            return preferred ?? new ArchiveVersionInfo
            {
                Key = "unknown_version",
                DisplayName = "unknown version",
                PatchVersion = string.Empty
            };
        }

        private ArchiveVersionInfo ResolveBestArchiveForCurrentVersion()
        {
            var detected = ArchiveKeyProvider.ResolveCurrentArchive(_versionDisplayService.RememberAndMapVersion);
            if (detected == null)
                return null;

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
            var seenMatchIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var finalFilePath in GetSelectedArchiveFinalFilePaths())
            {
                if (string.IsNullOrWhiteSpace(finalFilePath) || !File.Exists(finalFilePath))
                    continue;

                foreach (var line in File.ReadAllLines(finalFilePath, Encoding.UTF8))
                {
                    var snapshot = SafeDeserialize(line);
                    if (snapshot == null || snapshot.Placement <= 0)
                        continue;

                    snapshot = NormalizeSnapshot(snapshot);
                    if (!string.IsNullOrWhiteSpace(snapshot.MatchId) && !seenMatchIds.Add(snapshot.MatchId))
                        continue;

                    rows.Add(snapshot);
                }
            }

            return rows;
        }

        private IReadOnlyList<string> GetSelectedArchiveFinalFilePaths()
        {
            var selectedArchives = GetSelectedArchivesForCurrentDisplay();
            if (selectedArchives.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(_finalFilePath))
                    return new[] { _finalFilePath };
                return Array.Empty<string>();
            }

            return selectedArchives
                .Select(archive =>
                {
                    var archiveDir = Path.Combine(_archivesDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HDT_BGStats", "Data", "archives"), archive.Key);
                    EnsureLegacyArchiveMigratedToUnknown(archiveDir);
                    return GetAccountFinalFilePath(archiveDir, CurrentAccountKey);
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private IReadOnlyList<ArchiveVersionInfo> GetSelectedArchivesForCurrentDisplay()
        {
            var recordedArchives = GetRecordedArchivesInternal();
            if (recordedArchives.Count == 0)
                return Array.Empty<ArchiveVersionInfo>();

            var selectionKey = string.IsNullOrWhiteSpace(_selectedArchiveKey) ? CurrentArchive?.Key : _selectedArchiveKey;
            var selectedDisplayNames = _versionDisplayService.ResolveDisplayNamesForSelection(selectionKey, recordedArchives);
            if (selectedDisplayNames == null || selectedDisplayNames.Count == 0)
                return Array.Empty<ArchiveVersionInfo>();

            return recordedArchives
                .Where(x => selectedDisplayNames.Contains(GetArchiveDisplayName(x), StringComparer.OrdinalIgnoreCase))
                .ToList();
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
                    EnsureLegacyArchiveMigratedToUnknown(dir);
                    var finalFile = GetAccountFinalFilePath(dir, CurrentAccountKey);
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

        private IReadOnlyList<ArchiveVersionInfo> BuildDisplayArchives(IReadOnlyList<ArchiveVersionInfo> recordedArchives)
        {
            return _versionDisplayService.BuildMenuItems(recordedArchives)
                .Select(item => new ArchiveVersionInfo
                {
                    Key = item.Key,
                    DisplayName = item.DisplayName,
                    RawVersion = item.RawVersion,
                    PatchVersion = item.PatchVersion
                })
                .ToList();
        }

        private static bool IsVirtualArchiveSelection(string archiveKey)
        {
            return !string.IsNullOrWhiteSpace(archiveKey)
                && archiveKey.StartsWith("version_range_", StringComparison.OrdinalIgnoreCase);
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

        private ArchiveVersionInfo GetMostRecentRecordedArchive(string accountKey = null)
        {
            if (string.IsNullOrWhiteSpace(_archivesDir) || !Directory.Exists(_archivesDir))
                return null;

            ArchiveVersionInfo bestArchive = null;
            DateTime bestTimestamp = default(DateTime);
            var normalizedAccountKey = NormalizeArchiveLookupAccountKey(accountKey);

            foreach (var archiveDir in Directory.GetDirectories(_archivesDir))
            {
                EnsureLegacyArchiveMigratedToUnknown(archiveDir);
                var accountsDir = Path.Combine(archiveDir, "accounts");
                if (!Directory.Exists(accountsDir))
                    continue;

                IEnumerable<string> accountDirs;
                if (normalizedAccountKey == null)
                {
                    accountDirs = Directory.GetDirectories(accountsDir);
                }
                else
                {
                    var targetAccountDir = Path.Combine(accountsDir, normalizedAccountKey);
                    accountDirs = Directory.Exists(targetAccountDir)
                        ? new[] { targetAccountDir }
                        : Array.Empty<string>();
                }

                foreach (var accountDir in accountDirs)
                {
                    var finalFilePath = Path.Combine(accountDir, "bg_stats.jsonl");
                    if (!File.Exists(finalFilePath) || new FileInfo(finalFilePath).Length <= 0)
                        continue;

                    var latestSnapshot = TryGetLatestSnapshot(finalFilePath);
                    if (latestSnapshot == null)
                        continue;

                    var timestamp = ParseTimestamp(latestSnapshot.Timestamp);
                    if (timestamp <= bestTimestamp)
                        continue;

                    bestTimestamp = timestamp;
                    var key = Path.GetFileName(archiveDir);
                    var labelPath = Path.Combine(archiveDir, "label.txt");
                    var label = File.Exists(labelPath) ? File.ReadAllText(labelPath, Encoding.UTF8) : null;
                    bestArchive = ArchiveKeyProvider.CreateFromStoredLabel(key, label);
                }
            }

            return bestArchive;
        }

        private string NormalizeArchiveLookupAccountKey(string accountKey)
        {
            var normalized = NormalizeText(accountKey);
            if (string.IsNullOrWhiteSpace(normalized))
                return null;
            return normalized;
        }

        private bool HasRecordedMatches(string archiveKey)
        {
            if (string.IsNullOrWhiteSpace(archiveKey) || string.IsNullOrWhiteSpace(_archivesDir))
                return false;

            try
            {
                var archiveDir = Path.Combine(_archivesDir, archiveKey);
                EnsureLegacyArchiveMigratedToUnknown(archiveDir);
                var finalFile = GetAccountFinalFilePath(archiveDir, CurrentAccountKey);
                return File.Exists(finalFile) && new FileInfo(finalFile).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private BgSnapshot NormalizeSnapshot(BgSnapshot snapshot)
        {
            var normalizedAccount = NormalizeAccountRecord(new AccountRecord
            {
                Key = snapshot.AccountKey,
                AccountHi = snapshot.AccountHi,
                AccountLo = snapshot.AccountLo,
                BattleTag = snapshot.BattleTag,
                ServerInfo = snapshot.ServerInfo,
                RegionCode = snapshot.RegionCode,
                RegionName = snapshot.RegionName
            });
            snapshot.AccountKey = normalizedAccount.Key;
            snapshot.AccountHi = normalizedAccount.AccountHi;
            snapshot.AccountLo = normalizedAccount.AccountLo;
            snapshot.BattleTag = normalizedAccount.BattleTag;
            snapshot.ServerInfo = normalizedAccount.ServerInfo;
            snapshot.RegionCode = normalizedAccount.RegionCode;
            snapshot.RegionName = normalizedAccount.RegionName;
            snapshot.HeroCardId = HeroIdNormalizer.Normalize(snapshot.HeroCardId);
            snapshot.InitialHeroPowerCardIds = NormalizeHeroPowerCardIds(snapshot.InitialHeroPowerCardIds, snapshot.InitialHeroPowerCardId);
            snapshot.InitialHeroPowerCardId = snapshot.InitialHeroPowerCardIds.FirstOrDefault() ?? string.Empty;
            snapshot.HeroPowerCardIds = NormalizeHeroPowerCardIds(snapshot.HeroPowerCardIds, snapshot.HeroPowerCardId);
            snapshot.HeroPowerCardId = snapshot.HeroPowerCardIds.FirstOrDefault() ?? string.Empty;
            snapshot.OfferedHeroCardIds = (snapshot.OfferedHeroCardIds ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(HeroIdNormalizer.Normalize)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            snapshot.AvailableRaces = snapshot.AvailableRaces ?? Array.Empty<string>();
            snapshot.LesserTrinketOptionCardIds = NormalizeCardIdArray(snapshot.LesserTrinketOptionCardIds);
            snapshot.LesserTrinketCardId = NormalizeText(snapshot.LesserTrinketCardId) ?? string.Empty;
            snapshot.GreaterTrinketOptionCardIds = NormalizeCardIdArray(snapshot.GreaterTrinketOptionCardIds);
            snapshot.GreaterTrinketCardId = NormalizeText(snapshot.GreaterTrinketCardId) ?? string.Empty;
            snapshot.HeroPowerTrinketCardId = NormalizeText(snapshot.HeroPowerTrinketCardId) ?? string.Empty;
            snapshot.HeroPowerTrinketType = NormalizeTrinketType(snapshot.HeroPowerTrinketType);
            snapshot.TimewarpEntries = (snapshot.TimewarpEntries ?? new List<BgTimewarpEntry>())
                .Where(x => x != null)
                .Select(entry => new BgTimewarpEntry
                {
                    Type = NormalizeTimewarpType(entry.Type),
                    IsExtra = entry.IsExtra,
                    OptionCardIds = NormalizeCardIdArray(entry.OptionCardIds),
                    SelectedCardIds = NormalizeCardIdArray(entry.SelectedCardIds)
                })
                .Where(entry => entry.OptionCardIds.Length > 0 || entry.SelectedCardIds.Length > 0)
                .ToList();
            snapshot.QuestCardId = NormalizeText(snapshot.QuestCardId) ?? string.Empty;
            snapshot.QuestRewardCardId = NormalizeText(snapshot.QuestRewardCardId) ?? string.Empty;
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

        private static double NormalizeScoreLine(double scoreLine)
        {
            if (Math.Abs(scoreLine - 2.5) < 0.01)
                return 2.5;
            if (Math.Abs(scoreLine - 3.5) < 0.01)
                return 3.5;
            return 4.5;
        }

        private static IReadOnlyList<RaceDefinition> GetRaceDefinitions()
        {
            return new[]
            {
                new RaceDefinition("BEAST", "野兽"),
                new RaceDefinition("DEMON", "恶魔"),
                new RaceDefinition("DRAGON", "龙"),
                new RaceDefinition("ELEMENTAL", "元素"),
                new RaceDefinition("MECHANICAL", "机械", "Mech"),
                new RaceDefinition("MURLOC", "鱼人"),
                new RaceDefinition("NAGA", "娜迦", "Naga"),
                new RaceDefinition("PIRATE", "海盗"),
                new RaceDefinition("QUILBOAR", "野猪人"),
                new RaceDefinition("UNDEAD", "亡灵")
            };
        }

        private bool SnapshotHasRaceTag(BgSnapshot snapshot, RaceDefinition raceDef)
        {
            if (snapshot == null || raceDef == null)
                return false;

            return MergeTags(snapshot).Any(tag => string.Equals(tag, raceDef.Tag, StringComparison.OrdinalIgnoreCase));
        }

        private List<RaceCardUsage> BuildRaceTopCards(IReadOnlyList<BgSnapshot> snapshots)
        {
            return snapshots
                .Select(snapshot => (snapshot.FinalBoard ?? new List<BgBoardMinionSnapshot>())
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.CardId))
                    .Select(x => x.CardId)
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                .SelectMany(x => x)
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(group => new RaceCardUsage
                {
                    CardId = group.Key,
                    CardName = GameTextService.GetCardName(group.Key, group.Key),
                    Count = group.Count(),
                    Rate = snapshots.Count == 0 ? 0 : group.Count() / (double)snapshots.Count
                })
                .OrderByDescending(x => x.Rate)
                .ThenByDescending(x => x.Count)
                .ThenBy(x => x.CardName, StringComparer.CurrentCultureIgnoreCase)
                .Take(3)
                .ToList();
        }

        private List<RaceTagUsage> BuildRaceTopLineups(IReadOnlyList<BgSnapshot> snapshots, RaceDefinition currentRace)
        {
            return snapshots
                .SelectMany(snapshot => MergeTags(snapshot)
                    .Where(tag => !string.Equals(tag, currentRace.Tag, StringComparison.OrdinalIgnoreCase)))
                .GroupBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                .Select(group => new RaceTagUsage
                {
                    Tag = group.Key,
                    Count = group.Count(),
                    Rate = snapshots.Count == 0 ? 0 : group.Count() / (double)snapshots.Count
                })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Tag, StringComparer.CurrentCultureIgnoreCase)
                .Take(3)
                .ToList();
        }

        private void PopulateRaceSynergies(RaceStatsRow row, IReadOnlyList<BgSnapshot> raceSnapshots, IReadOnlyList<BgSnapshot> allSnapshots, IReadOnlyList<RaceDefinition> raceDefs)
        {
            if (row == null || !row.AveragePlacement.HasValue)
                return;

            var synergies = new List<RaceSynergyStat>();
            foreach (var raceDef in raceDefs)
            {
                if (string.Equals(raceDef.Code, row.RaceCode, StringComparison.OrdinalIgnoreCase))
                    continue;

                var synergySnapshots = raceSnapshots
                    .Where(snapshot => SnapshotHasRaceTag(snapshot, raceDef))
                    .ToList();
                if (synergySnapshots.Count == 0)
                    continue;

                var avgPlacement = synergySnapshots.Average(x => x.Placement);
                synergies.Add(new RaceSynergyStat
                {
                    RaceCode = raceDef.Code,
                    RaceName = GameTextService.GetRaceName(raceDef.Code, raceDef.DisplayFallback),
                    MatchCount = synergySnapshots.Count,
                    AveragePlacement = avgPlacement,
                    PlacementDelta = avgPlacement - row.AveragePlacement.Value
                });
            }

            row.BestSynergies = synergies
                .Where(x => x.PlacementDelta < -0.001)
                .OrderBy(x => x.PlacementDelta)
                .ThenByDescending(x => x.MatchCount)
                .Take(2)
                .ToList();

            var bestRaceCodes = new HashSet<string>(
                row.BestSynergies.Select(x => x.RaceCode ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);

            row.WorstSynergies = synergies
                .Where(x => x.PlacementDelta > 0.001 && !bestRaceCodes.Contains(x.RaceCode ?? string.Empty))
                .OrderByDescending(x => x.PlacementDelta)
                .ThenByDescending(x => x.MatchCount)
                .Take(2)
                .ToList();
        }

        private void SetCurrentAccountInternal(AccountRecord account)
        {
            var normalized = NormalizeAccountRecord(account);
            CurrentAccount = normalized;

            var archiveDir = _currentArchiveDir
                ?? Path.Combine(_archivesDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HDT_BGStats", "Data", "archives"), CurrentArchive?.Key ?? ArchiveKeyProvider.GetDefaultArchive().Key);
            Directory.CreateDirectory(archiveDir);
            EnsureLegacyArchiveMigratedToUnknown(archiveDir);

            _finalFilePath = GetAccountFinalFilePath(archiveDir, normalized.Key);
            _pendingFilePath = GetAccountPendingFilePath(archiveDir, normalized.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(_finalFilePath));
        }

        private void SetActiveMatchAccount(AccountRecord account)
        {
            var normalized = NormalizeAccountRecord(account);
            _activeMatchAccount = normalized;
            var archiveDir = _activeArchiveDir ?? _currentArchiveDir;
            if (string.IsNullOrWhiteSpace(archiveDir))
                return;

            Directory.CreateDirectory(archiveDir);
            EnsureLegacyArchiveMigratedToUnknown(archiveDir);
            _activeFinalFilePath = GetAccountFinalFilePath(archiveDir, normalized.Key);
            _activePendingFilePath = GetAccountPendingFilePath(archiveDir, normalized.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(_activeFinalFilePath));
        }

        private AccountRecord ResolveExistingAccount(string accountKey)
        {
            if (string.IsNullOrWhiteSpace(accountKey))
                return null;
            return GetAvailableAccounts().FirstOrDefault(x => string.Equals(x.Key, accountKey, StringComparison.OrdinalIgnoreCase));
        }

        private static AccountRecord NormalizeAccountRecord(AccountRecord account)
        {
            var normalized = account ?? new AccountRecord();
            normalized.AccountHi = NormalizeText(normalized.AccountHi);
            normalized.AccountLo = NormalizeText(normalized.AccountLo);
            normalized.BattleTag = NormalizeText(normalized.BattleTag);
            normalized.ServerInfo = NormalizeText(normalized.ServerInfo);
            normalized.RegionCode = NormalizeText(normalized.RegionCode);
            normalized.RegionName = NormalizeText(normalized.RegionName);

            var isUnknown = string.IsNullOrWhiteSpace(normalized.AccountHi)
                && string.IsNullOrWhiteSpace(normalized.AccountLo)
                && string.IsNullOrWhiteSpace(normalized.BattleTag)
                && string.IsNullOrWhiteSpace(normalized.ServerInfo);

            normalized.IsUnknown = isUnknown;
            normalized.Key = isUnknown
                ? UnknownAccountKey
                : BuildAccountKey(normalized.AccountHi, normalized.AccountLo, normalized.RegionCode, normalized.BattleTag, normalized.ServerInfo);
            return normalized;
        }

        private static string BuildAccountKey(string accountHi, string accountLo, string regionCode, string battleTag, string serverInfo)
        {
            if (!string.IsNullOrWhiteSpace(accountHi) || !string.IsNullOrWhiteSpace(accountLo))
                return SanitizePathSegment($"{accountHi ?? "0"}_{accountLo ?? "0"}_{regionCode ?? "0"}");

            var fallback = !string.IsNullOrWhiteSpace(battleTag) ? battleTag : serverInfo;
            return string.IsNullOrWhiteSpace(fallback) ? UnknownAccountKey : SanitizePathSegment(fallback);
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return UnknownAccountKey;

            var builder = new StringBuilder(value.Length);
            foreach (var ch in value.Trim())
            {
                if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
                    builder.Append(ch);
                else
                    builder.Append('_');
            }

            var result = builder.ToString().Trim('_');
            return string.IsNullOrWhiteSpace(result) ? UnknownAccountKey : result;
        }

        private static string NormalizeText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string[] NormalizeCardIdArray(IEnumerable<string> values)
        {
            return (values ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToArray();
        }

        private static string NormalizeTrinketType(string value)
        {
            if (string.Equals(value, "lesser", StringComparison.OrdinalIgnoreCase))
                return "lesser";
            if (string.Equals(value, "greater", StringComparison.OrdinalIgnoreCase))
                return "greater";
            return string.Empty;
        }

        private static string NormalizeTimewarpType(string value)
        {
            if (string.Equals(value, "major", StringComparison.OrdinalIgnoreCase))
                return "major";
            if (string.Equals(value, "minor", StringComparison.OrdinalIgnoreCase))
                return "minor";
            return string.Empty;
        }

        private static void MergeAccountRecord(IDictionary<string, AccountRecord> accounts, AccountRecord record)
        {
            if (record == null)
                return;

            if (!accounts.TryGetValue(record.Key, out var existing))
            {
                accounts[record.Key] = record;
                return;
            }

            existing.MatchCount += record.MatchCount;
            if (string.IsNullOrWhiteSpace(existing.BattleTag) && !string.IsNullOrWhiteSpace(record.BattleTag))
                existing.BattleTag = record.BattleTag;
            if (string.IsNullOrWhiteSpace(existing.ServerInfo) && !string.IsNullOrWhiteSpace(record.ServerInfo))
                existing.ServerInfo = record.ServerInfo;
            if (string.IsNullOrWhiteSpace(existing.RegionCode) && !string.IsNullOrWhiteSpace(record.RegionCode))
                existing.RegionCode = record.RegionCode;
            if (string.IsNullOrWhiteSpace(existing.RegionName) && !string.IsNullOrWhiteSpace(record.RegionName))
                existing.RegionName = record.RegionName;
            if (string.IsNullOrWhiteSpace(existing.AccountHi) && !string.IsNullOrWhiteSpace(record.AccountHi))
                existing.AccountHi = record.AccountHi;
            if (string.IsNullOrWhiteSpace(existing.AccountLo) && !string.IsNullOrWhiteSpace(record.AccountLo))
                existing.AccountLo = record.AccountLo;
            existing.IsUnknown &= record.IsUnknown;
        }

        private bool ShouldRefreshCurrentAccountMetadata(AccountRecord normalized)
        {
            if (CurrentAccount == null || normalized == null)
                return false;
            if (!string.Equals(CurrentAccount.Key, normalized.Key, StringComparison.OrdinalIgnoreCase))
                return false;
            if (CurrentAccount.MatchCount == 0 && !string.IsNullOrWhiteSpace(normalized.BattleTag))
                return true;
            if (string.IsNullOrWhiteSpace(CurrentAccount.BattleTag) && !string.IsNullOrWhiteSpace(normalized.BattleTag))
                return true;
            if (string.IsNullOrWhiteSpace(CurrentAccount.RegionName) && !string.IsNullOrWhiteSpace(normalized.RegionName))
                return true;
            return string.IsNullOrWhiteSpace(CurrentAccount.RegionCode) && !string.IsNullOrWhiteSpace(normalized.RegionCode);
        }

        private AccountRecord TryReadAccountRecord(string finalFilePath, string accountKey)
        {
            try
            {
                var snapshot = File.ReadLines(finalFilePath, Encoding.UTF8)
                    .Select(SafeDeserialize)
                    .FirstOrDefault(x => x != null);
                if (snapshot == null)
                    return string.Equals(accountKey, UnknownAccountKey, StringComparison.OrdinalIgnoreCase) ? CreateUnknownAccount() : null;

                var normalizedSnapshot = NormalizeSnapshot(snapshot);
                return NormalizeAccountRecord(new AccountRecord
                {
                    Key = accountKey,
                    AccountHi = normalizedSnapshot.AccountHi,
                    AccountLo = normalizedSnapshot.AccountLo,
                    BattleTag = normalizedSnapshot.BattleTag,
                    ServerInfo = normalizedSnapshot.ServerInfo,
                    RegionCode = normalizedSnapshot.RegionCode,
                    RegionName = normalizedSnapshot.RegionName
                });
            }
            catch
            {
                return string.Equals(accountKey, UnknownAccountKey, StringComparison.OrdinalIgnoreCase) ? CreateUnknownAccount() : null;
            }
        }

        private static int CountSnapshots(string finalFilePath)
        {
            try
            {
                return File.ReadLines(finalFilePath, Encoding.UTF8).Count(x => !string.IsNullOrWhiteSpace(x));
            }
            catch
            {
                return 0;
            }
        }

        private BgSnapshot TryGetLatestSnapshot(string finalFilePath)
        {
            try
            {
                BgSnapshot best = null;
                DateTime bestTimestamp = default(DateTime);
                foreach (var line in File.ReadLines(finalFilePath, Encoding.UTF8))
                {
                    var snapshot = SafeDeserialize(line);
                    if (snapshot == null || snapshot.Placement <= 0)
                        continue;

                    snapshot = NormalizeSnapshot(snapshot);
                    var timestamp = ParseTimestamp(snapshot.Timestamp);
                    if (timestamp <= bestTimestamp)
                        continue;

                    bestTimestamp = timestamp;
                    best = snapshot;
                }

                return best;
            }
            catch
            {
                return null;
            }
        }

        private void EnsureLegacyArchiveMigratedToUnknown(string archiveDir)
        {
            if (string.IsNullOrWhiteSpace(archiveDir) || !Directory.Exists(archiveDir))
                return;

            var legacyFinal = Path.Combine(archiveDir, "bg_stats.jsonl");
            var legacyPending = Path.Combine(archiveDir, "bg_stats_pending.jsonl");
            if (!File.Exists(legacyFinal) && !File.Exists(legacyPending))
                return;

            var unknownFinal = GetAccountFinalFilePath(archiveDir, UnknownAccountKey);
            var unknownPending = GetAccountPendingFilePath(archiveDir, UnknownAccountKey);
            MigrateIfNeeded(legacyFinal, unknownFinal);
            MigrateIfNeeded(legacyPending, unknownPending);
        }

        private static AccountRecord CreateUnknownAccount()
        {
            return new AccountRecord
            {
                Key = UnknownAccountKey,
                IsUnknown = true,
                RegionName = "Unknown"
            };
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

        private string GetSnapshotVersionDisplayName(BgSnapshot snapshot)
        {
            return snapshot == null ? string.Empty : GetGameVersionDisplayName(snapshot.GameVersion);
        }

        private static IReadOnlyList<string> GetSnapshotTrinketCardIds(BgSnapshot snapshot, TrinketFilter filter)
        {
            if (snapshot == null)
                return Array.Empty<string>();

            var cardIds = new List<string>();
            if (filter == TrinketFilter.All || filter == TrinketFilter.Lesser)
            {
                if (!string.IsNullOrWhiteSpace(snapshot.LesserTrinketCardId))
                    cardIds.Add(snapshot.LesserTrinketCardId);
                if (string.Equals(snapshot.HeroPowerTrinketType, "lesser", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(snapshot.HeroPowerTrinketCardId))
                    cardIds.Add(snapshot.HeroPowerTrinketCardId);
            }

            if (filter == TrinketFilter.All || filter == TrinketFilter.Greater)
            {
                if (!string.IsNullOrWhiteSpace(snapshot.GreaterTrinketCardId))
                    cardIds.Add(snapshot.GreaterTrinketCardId);
                if (string.Equals(snapshot.HeroPowerTrinketType, "greater", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(snapshot.HeroPowerTrinketCardId))
                    cardIds.Add(snapshot.HeroPowerTrinketCardId);
            }

            return cardIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsMatchingTimewarpEntry(BgTimewarpEntry entry, TimewarpFilter filter)
        {
            if (entry == null)
                return false;

            if (filter == TimewarpFilter.All)
                return true;
            if (filter == TimewarpFilter.Major)
                return string.Equals(entry.Type, "major", StringComparison.OrdinalIgnoreCase);
            return string.Equals(entry.Type, "minor", StringComparison.OrdinalIgnoreCase);
        }

        private string GetWriteFinalFilePath()
        {
            return string.IsNullOrWhiteSpace(_activeFinalFilePath) ? _finalFilePath : _activeFinalFilePath;
        }

        private string GetWritePendingFilePath()
        {
            return string.IsNullOrWhiteSpace(_activePendingFilePath) ? _pendingFilePath : _activePendingFilePath;
        }

        private static string GetAccountDirectory(string archiveDir, string accountKey)
        {
            return Path.Combine(archiveDir, "accounts", string.IsNullOrWhiteSpace(accountKey) ? UnknownAccountKey : accountKey);
        }

        private static string GetAccountFinalFilePath(string archiveDir, string accountKey)
        {
            return Path.Combine(GetAccountDirectory(archiveDir, accountKey), "bg_stats.jsonl");
        }

        private static string GetAccountPendingFilePath(string archiveDir, string accountKey)
        {
            return Path.Combine(GetAccountDirectory(archiveDir, accountKey), "bg_stats_pending.jsonl");
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

        private static string GetArchiveRawVersion(ArchiveVersionInfo archive)
        {
            if (archive == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(archive.RawVersion))
                return archive.RawVersion.Trim();

            if (!string.IsNullOrWhiteSpace(archive.DisplayName) && ArchiveKeyProvider.IsRawVersionText(archive.DisplayName))
                return archive.DisplayName.Trim();

            if (!string.IsNullOrWhiteSpace(archive.DisplayName))
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(archive.PatchVersion))
                return archive.PatchVersion.Trim();

            return string.IsNullOrWhiteSpace(archive.DisplayName) ? string.Empty : archive.DisplayName.Trim();
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

        private static string[] NormalizeHeroPowerCardIds(IEnumerable<string> cardIds, string fallbackCardId)
        {
            var normalized = (cardIds ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(2)
                .ToList();

            if (normalized.Count == 0 && !string.IsNullOrWhiteSpace(fallbackCardId))
                normalized.Add(fallbackCardId.Trim());

            return normalized.ToArray();
        }

        private static string SerializeStringArray(IEnumerable<string> values)
        {
            var items = (values ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => "\"" + JsonEscape(x.Trim()) + "\"")
                .ToArray();
            return "[" + string.Join(",", items) + "]";
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

        private sealed class RaceDefinition
        {
            public RaceDefinition(string code, string tag, string displayFallback = null)
            {
                Code = code;
                Tag = tag;
                DisplayFallback = string.IsNullOrWhiteSpace(displayFallback) ? tag : displayFallback;
            }

            public string Code { get; }
            public string Tag { get; }
            public string DisplayFallback { get; }
        }
    }
}




