using HDTplugins.Models;
using HearthDb;
using HearthDb.Enums;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.Hearthstone;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using HdtLog = Hearthstone_Deck_Tracker.Utility.Logging.Log;

namespace HDTplugins.Services
{
    public class BgGameProbe
    {
        private sealed class GameV2TrinketPickSnapshot
        {
            public int ChoiceId { get; set; }
            public int Turn { get; set; }
            public int SourceDbfId { get; set; }
            public string SourceCardId { get; set; }
            public string[] OfferedCardIds { get; set; } = Array.Empty<string>();
            public string[] OfferedDebugEntries { get; set; } = Array.Empty<string>();
            public string ChosenCardId { get; set; }
        }

        private sealed class LegacyTrinketSnapshot
        {
            public int Round { get; set; }
            public string[] Options { get; set; } = Array.Empty<string>();
            public string[] EntityDebugEntries { get; set; } = Array.Empty<string>();
            public string LesserTrinketCardId { get; set; }
            public string GreaterTrinketCardId { get; set; }
            public string HeroPowerTrinketCardId { get; set; }
            public string HeroPowerTrinketType { get; set; }
        }

        private sealed class TimewarpChoiceSnapshot
        {
            public int ChoiceId { get; set; }
            public string[] OfferedCardIds { get; set; } = Array.Empty<string>();
            public string[] SelectedCardIds { get; set; } = Array.Empty<string>();
            public string[] OfferedDebugEntries { get; set; } = Array.Empty<string>();
        }

        private const int PollIntervalMs = 250;
        private const int OfferedLogThrottleMs = 800;
        private const string PlaceholderHeroCardId = "TB_BaconShop_HERO_PH";

        private long _nextPollTs;
        private long _nextOfferedLogTs;
        private bool _bgDetected;
        private bool _needResolveHero;
        private bool _needResolvePlacement;
        private bool _needResolveOfferedHeroes;
        private bool _needResolveRatingAfter;
        private long _ratingResolveStartTs;
        private long _nextRatingPollTs;
        private int _lastRecordedTavernTier;
        private int _lastObservedRawTurn;
        private Step _lastObservedStep = Step.INVALID;
        private bool _combatSnapshotCapturedThisFight;
        private bool _needResolveAccountContext;
        private long _nextAccountResolveTs;
        private long _nextAccountResolveLogTs;
        private string[] _currentHeroPowerCardIds = Array.Empty<string>();
        private string _lastTimewarpOptionsSignature;
        private Dictionary<int, int> _timewarpEntryIndexesByChoiceId = new Dictionary<int, int>();
        private string _lastGameV2TrinketDebugSignature;
        private string _lastLegacyTrinketDebugSignature;
        private bool _loggedMissingGameV2TrinketPath;
        private const string MarinHeroPowerCardId = "BG30_HERO_304p";
        private const string ButtonsHeroPowerCardId = "BG32_HERO_002p";

        private const int RatingResolveTimeoutMs = 45_000;
        private const int RatingPollMs = 500;
        private const int AccountResolveRetryMs = 3000;
        private const int AccountResolveLogThrottleMs = 10000;

        private static long MsToTicks(int ms)
            => (long)(Stopwatch.Frequency * (ms / 1000.0));

        public bool IsBattlegrounds => _bgDetected;
        public string HeroCardId { get; private set; }
        public string HeroSkinCardId { get; private set; }
        public string[] InitialHeroPowerCardIds { get; private set; } = Array.Empty<string>();
        public string InitialHeroPowerCardId { get; private set; }
        public string[] HeroPowerCardIds { get; private set; } = Array.Empty<string>();
        public string HeroPowerCardId { get; private set; }
        public int[] OfferedHeroDbfIds { get; private set; } = Array.Empty<int>();
        public string[] OfferedHeroCardIds { get; private set; } = Array.Empty<string>();
        public int Placement { get; private set; }
        public bool HasResolvedHero => !string.IsNullOrEmpty(HeroCardId);
        public bool HasResolvedPlacement => Placement > 0;
        public int RatingBefore { get; private set; } = -1;
        public int RatingAfter { get; private set; } = -1;
        public string AccountHi { get; private set; }
        public string AccountLo { get; private set; }
        public string BattleTag { get; private set; }
        public string ServerInfo { get; private set; }
        public string RegionCode { get; private set; }
        public string RegionName { get; private set; }
        public bool HasAttemptedAccountResolution { get; private set; }
        public string[] AvailableRaceNames { get; private set; } = Array.Empty<string>();
        public int AnomalyDbfId { get; private set; }
        public string AnomalyCardId { get; private set; }
        public string[] LesserTrinketOptionCardIds { get; private set; } = Array.Empty<string>();
        public string LesserTrinketCardId { get; private set; }
        public string[] GreaterTrinketOptionCardIds { get; private set; } = Array.Empty<string>();
        public string GreaterTrinketCardId { get; private set; }
        public string HeroPowerTrinketCardId { get; private set; }
        public string HeroPowerTrinketType { get; private set; }
        public List<BgTimewarpEntry> TimewarpEntries { get; private set; } = new List<BgTimewarpEntry>();
        public string QuestCardId { get; private set; }
        public string QuestRewardCardId { get; private set; }
        public bool QuestCompleted { get; private set; }
        public List<BgBoardMinionSnapshot> FinalBoard { get; private set; } = new List<BgBoardMinionSnapshot>();
        public List<BgTavernUpgradePoint> TavernUpgradeTimeline { get; private set; } = new List<BgTavernUpgradePoint>();
        public bool HasResolvedRatingAfter => RatingAfter > 0 && !_needResolveRatingAfter;

        public void RefreshRuntimeAccountContext()
        {
            TryResolveAccountContext();
        }

        public void OnGameStart()
        {
            _bgDetected = false;
            HeroCardId = null;
            HeroSkinCardId = null;
            InitialHeroPowerCardIds = Array.Empty<string>();
            InitialHeroPowerCardId = null;
            HeroPowerCardIds = Array.Empty<string>();
            HeroPowerCardId = null;
            _currentHeroPowerCardIds = Array.Empty<string>();
            Placement = 0;
            OfferedHeroDbfIds = Array.Empty<int>();
            OfferedHeroCardIds = Array.Empty<string>();
            _needResolveHero = true;
            _needResolvePlacement = false;
            _needResolveOfferedHeroes = true;
            RatingBefore = -1;
            RatingAfter = -1;
            AccountHi = null;
            AccountLo = null;
            BattleTag = null;
            ServerInfo = null;
            RegionCode = null;
            RegionName = null;
            HasAttemptedAccountResolution = false;
            _needResolveAccountContext = true;
            _nextAccountResolveTs = 0;
            _nextAccountResolveLogTs = 0;
            _needResolveRatingAfter = false;
            _nextRatingPollTs = 0;
            AvailableRaceNames = Array.Empty<string>();
            AnomalyDbfId = 0;
            AnomalyCardId = null;
            LesserTrinketOptionCardIds = Array.Empty<string>();
            LesserTrinketCardId = null;
            GreaterTrinketOptionCardIds = Array.Empty<string>();
            GreaterTrinketCardId = null;
            HeroPowerTrinketCardId = null;
            HeroPowerTrinketType = null;
            TimewarpEntries = new List<BgTimewarpEntry>();
            QuestCardId = null;
            QuestRewardCardId = null;
            QuestCompleted = false;
            _lastTimewarpOptionsSignature = null;
            _timewarpEntryIndexesByChoiceId = new Dictionary<int, int>();
            _lastGameV2TrinketDebugSignature = null;
            _lastLegacyTrinketDebugSignature = null;
            _loggedMissingGameV2TrinketPath = false;
            FinalBoard = new List<BgBoardMinionSnapshot>();
            TavernUpgradeTimeline = new List<BgTavernUpgradePoint>();
            _lastRecordedTavernTier = -1;
            _lastObservedRawTurn = -1;
            _lastObservedStep = Step.INVALID;
            _combatSnapshotCapturedThisFight = false;
        }

        public void OnGameEnd()
        {
            if (!_bgDetected)
                return;

            CaptureFinalHeroState();
            TryCachePlacement();
            TryUpdateTurnScopedSnapshots();
            _needResolvePlacement = true;
            _needResolveRatingAfter = true;
            _ratingResolveStartTs = Stopwatch.GetTimestamp();
            _nextRatingPollTs = 0;
        }

        public void CaptureFinalHeroState()
        {
            try
            {
                TryResolveHeroAndHeroPower();
                TryRefreshHeroPower();
                TryRefreshTrinkets();
                TryCacheFinalBoard();
                HdtLog.Info($"[BGStats][HeroPower] finalize snapshot hero={HeroCardId ?? "null"} initial=[{string.Join(", ", InitialHeroPowerCardIds)}] combat=[{string.Join(", ", HeroPowerCardIds)}] current=[{string.Join(", ", _currentHeroPowerCardIds)}] boardCount={FinalBoard?.Count ?? 0}");
            }
            catch (Exception ex)
            {
                HdtLog.Warn("[BGStats][HeroPower] finalize snapshot failed: " + ex.Message);
            }
        }

        public void StopFinalizePolling()
        {
            _needResolvePlacement = false;
            _needResolveRatingAfter = false;
        }

        public void Tick()
        {
            var nowTs = Stopwatch.GetTimestamp();
            if (nowTs < _nextPollTs)
                return;
            _nextPollTs = nowTs + MsToTicks(PollIntervalMs);

            if (!_bgDetected)
            {
                if (TryDetectBattlegrounds())
                {
                    _bgDetected = true;
                    HdtLog.Info("[BGStats] å·²è¯†åˆ«ä¸ºé…’é¦†æˆ˜æ£‹å¯¹å±€ï¼Œå¼€å§‹è§£æžæ•°æ®");
                }
                else
                {
                    return;
                }
            }

            TryCachePlacement();
            TryCacheAvailableRaces();
            TryCacheAnomaly();
            TryUpdateTurnScopedSnapshots();
            TryCapturePreCombatSnapshot();
            TryRefreshTrinkets();
            TryRefreshTimewarp();
            if (_needResolveAccountContext)
                TryResolveAccountContext();

            if (_needResolveOfferedHeroes)
                TryResolveOfferedHeroes(nowTs);
            if (_needResolveHero)
                TryResolveHeroAndHeroPower();
            if (_needResolvePlacement)
                TryResolvePlacement();
            if (_needResolveRatingAfter)
                TryResolveRatingAfter(nowTs);
        }

        private bool TryDetectBattlegrounds()
        {
            try
            {
                var gt = Core.Game.CurrentGameType.ToString();
                if (gt.IndexOf("BATTLEGROUNDS", StringComparison.OrdinalIgnoreCase) >= 0 || gt.IndexOf("BACON", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                var heroCardId = Core.Game.Player?.Hero?.CardId;
                if (!string.IsNullOrEmpty(heroCardId) && heroCardId.IndexOf("Bacon", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                var hasBaconEntity = Core.Game.Entities?.Values.Any(e => e != null && !string.IsNullOrEmpty(e.CardId) && (e.CardId.IndexOf("Bacon", StringComparison.OrdinalIgnoreCase) >= 0 || e.CardId == PlaceholderHeroCardId)) ?? false;
                if (hasBaconEntity)
                    return true;

                var anyOffered = Core.Game.Player?.PlayerEntities?.Any(e => e != null && e.IsHero && (e.HasTag(GameTag.BACON_HERO_CAN_BE_DRAFTED) || e.HasTag(GameTag.BACON_SKIN))) ?? false;
                return anyOffered;
            }
            catch
            {
                return false;
            }
        }

        private void TryResolveAccountContext()
        {
            var nowTs = Stopwatch.GetTimestamp();
            if (nowTs < _nextAccountResolveTs)
                return;

            HasAttemptedAccountResolution = true;

            try
            {
                var collectionAccount = GetAccountContextFromCollection();
                if (collectionAccount != null)
                {
                    AccountHi = GetNumericPropertyAsString(collectionAccount, "AccountHi") ?? AccountHi;
                    AccountLo = GetNumericPropertyAsString(collectionAccount, "AccountLo") ?? AccountLo;
                    var collectionBattleTag = GetPropertyValueStatic(collectionAccount, "BattleTag");
                    BattleTag = GetBattleTagText(collectionBattleTag) ?? BattleTag;
                    TryPopulateRegionFromAccountHi(AccountHi);
                    if (string.IsNullOrWhiteSpace(BattleTag) && collectionBattleTag != null && nowTs >= _nextAccountResolveLogTs)
                    {
                        _nextAccountResolveLogTs = nowTs + MsToTicks(AccountResolveLogThrottleMs);
                        HdtLog.Info("[BGStats][Account] 收藏数据已返回 BattleTag 原始值，但解析为空: " + collectionBattleTag);
                    }
                }

                var accountId = GetAccountIdFromGameMetaData() ?? GetAccountIdFromReflectionClient();
                if (accountId != null)
                {
                    AccountHi = GetNumericPropertyAsString(accountId, "Hi");
                    AccountLo = GetNumericPropertyAsString(accountId, "Lo");

                    if (TryGetAccountRegionCode(accountId, out var regionCode))
                    {
                        RegionCode = regionCode.ToString();
                        RegionName = MapRegionName(regionCode);
                    }
                }

                ServerInfo = NormalizeAccountValue(GetServerInfoFromGameMetaData()) ?? NormalizeAccountValue(GetReflectionClientValue("GetServerInfo"));
                BattleTag = BattleTag ?? GetBattleTagText(GetReflectionClientValue("GetBattleTag"));

                var hasAccount = !string.IsNullOrWhiteSpace(AccountHi)
                    || !string.IsNullOrWhiteSpace(AccountLo)
                    || !string.IsNullOrWhiteSpace(BattleTag)
                    || !string.IsNullOrWhiteSpace(ServerInfo);

                _needResolveAccountContext = !hasAccount;
                _nextAccountResolveTs = hasAccount ? nowTs + MsToTicks(AccountResolveLogThrottleMs) : nowTs + MsToTicks(AccountResolveRetryMs);

                if (hasAccount && nowTs >= _nextAccountResolveLogTs)
                {
                    _nextAccountResolveLogTs = nowTs + MsToTicks(AccountResolveLogThrottleMs);
                    HdtLog.Info($"[BGStats][Account] hi={AccountHi ?? "null"} lo={AccountLo ?? "null"} region={RegionName ?? RegionCode ?? "null"} server={ServerInfo ?? "null"} tag={BattleTag ?? "null"}");
                }
            }
            catch (Exception ex)
            {
                _needResolveAccountContext = true;
                _nextAccountResolveTs = nowTs + MsToTicks(AccountResolveRetryMs);
                if (nowTs >= _nextAccountResolveLogTs)
                {
                    _nextAccountResolveLogTs = nowTs + MsToTicks(AccountResolveLogThrottleMs);
                    HdtLog.Warn("[BGStats][Account] 读取账号上下文失败: " + ex.Message);
                }
            }
        }

        private void TryResolveOfferedHeroes(long nowTs)
        {
            try
            {
                var heroEntities = Core.Game.Player?.PlayerEntities?
                    .Where(e => e != null && e.IsHero && (e.HasTag(GameTag.BACON_HERO_CAN_BE_DRAFTED) || e.HasTag(GameTag.BACON_SKIN)) && !e.HasTag(GameTag.BACON_LOCKED_MULLIGAN_HERO) && e.Card != null)
                    .OrderBy(e => e.ZonePosition)
                    .ToArray();

                if (heroEntities == null || heroEntities.Length == 0)
                {
                    if (OfferedHeroDbfIds.Length == 0 && OfferedHeroCardIds.Length == 0)
                        return;

                    OfferedHeroDbfIds = Array.Empty<int>();
                    OfferedHeroCardIds = Array.Empty<string>();
                    return;
                }

                var heroes = heroEntities
                    .Select(e => e.Card.DbfId)
                    .Where(id => id > 0)
                    .ToArray();

                var heroCardIds = heroEntities
                    .Select(e => e.Card?.Id)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (SameArray(heroes, OfferedHeroDbfIds) && SameArray(heroCardIds, OfferedHeroCardIds))
                    return;

                OfferedHeroDbfIds = heroes;
                OfferedHeroCardIds = heroCardIds;
                if (nowTs < _nextOfferedLogTs)
                    return;
                _nextOfferedLogTs = nowTs + MsToTicks(OfferedLogThrottleMs);
                HdtLog.Info($"[BGStats] Offered heroes(dbfId) = [{string.Join(", ", heroes)}]");
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] TryResolveOfferedHeroes å¤±è´¥: " + ex.Message);
            }
        }

        private void TryResolveHeroAndHeroPower()
        {
            try
            {
                TryRefreshHeroPower();

                var playerEntity = Core.Game.PlayerEntity;
                string heroA = null;
                if (playerEntity != null)
                {
                    var heroEntityId = playerEntity.GetTag(GameTag.HERO_ENTITY);
                    if (heroEntityId > 0 && Core.Game.Entities.TryGetValue(heroEntityId, out var heroEntity))
                    {
                        var cid = heroEntity?.CardId;
                        if (!string.IsNullOrEmpty(cid) && cid != PlaceholderHeroCardId)
                            heroA = cid;
                    }
                }

                var heroB = Core.Game.Player?.Hero?.CardId;
                if (!string.IsNullOrEmpty(heroB) && heroB != PlaceholderHeroCardId)
                    heroA = heroB;

                if (!string.IsNullOrEmpty(heroA))
                {
                    HeroSkinCardId = heroA;
                    HeroCardId = heroA;
                }

                if (!string.IsNullOrEmpty(HeroCardId))
                {
                    _needResolveHero = false;
                    HdtLog.Info($"[BGStats] Resolved hero: Hero={HeroCardId}, InitialHeroPowers=[{string.Join(", ", InitialHeroPowerCardIds)}], CombatHeroPowers=[{string.Join(", ", HeroPowerCardIds)}], Current=[{string.Join(", ", _currentHeroPowerCardIds)}]");
                    if (RatingBefore <= 0)
                        RatingBefore = TryGetCurrentBattlegroundsRating() ?? -1;
                }
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] TryResolveHeroAndHeroPower failed: " + ex.Message);
            }
        }

        private void TryRefreshHeroPower()
        {
            try
            {
                var currentHeroPowerCardIds = ResolveHeroPowerCardIds(out var source);
                if (currentHeroPowerCardIds.Length == 0)
                {
                    HdtLog.Info("[BGStats][HeroPower] hero power not resolved in this poll");
                    return;
                }

                var previousCurrentHeroPowers = _currentHeroPowerCardIds;
                _currentHeroPowerCardIds = currentHeroPowerCardIds;

                if (InitialHeroPowerCardIds.Length == 0)
                {
                    InitialHeroPowerCardIds = currentHeroPowerCardIds;
                    InitialHeroPowerCardId = InitialHeroPowerCardIds.ElementAtOrDefault(0);
                    HdtLog.Info($"[BGStats][HeroPower] initial hero powers: [{string.Join(", ", InitialHeroPowerCardIds)}] (source={source})");
                }

                if (!SameCardIdSet(previousCurrentHeroPowers, _currentHeroPowerCardIds))
                    HdtLog.Info($"[BGStats][HeroPower] current hero powers: [{string.Join(", ", previousCurrentHeroPowers)}] -> [{string.Join(", ", _currentHeroPowerCardIds)}] (source={source})");
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] TryRefreshHeroPower failed: " + ex.Message);
            }
        }

        private void TryRecordCombatHeroPowers()
        {
            try
            {
                var currentHeroPowerCardIds = ResolveHeroPowerCardIds(out var source);
                if (currentHeroPowerCardIds.Length == 0)
                {
                    HdtLog.Info("[BGStats][HeroPower] combat hero powers not resolved in this poll");
                    return;
                }

                var previousCombatHeroPowers = HeroPowerCardIds;
                HeroPowerCardIds = currentHeroPowerCardIds;
                HeroPowerCardId = HeroPowerCardIds.ElementAtOrDefault(0);
                if (!SameCardIdSet(previousCombatHeroPowers, HeroPowerCardIds))
                    HdtLog.Info($"[BGStats][HeroPower] combat hero powers updated: [{string.Join(", ", previousCombatHeroPowers)}] -> [{string.Join(", ", HeroPowerCardIds)}] (source={source})");
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] TryRecordCombatHeroPowers failed: " + ex.Message);
            }
        }

        private string[] ResolveHeroPowerCardIds(out string source)
        {
            var boardHeroPowers = GetHeroPowersFromBoard(out var boardDetail).ToArray();
            var playerEntityHeroPowers = GetHeroPowersFromPlayerEntityDebug(out var playerEntityDetail).ToArray();
            var heroEntityHeroPowers = GetHeroPowersFromHeroEntityDebug(out var heroEntityDetail).ToArray();

            HdtLog.Info($"[BGStats][HeroPower][Debug] BoardHeroPowers current=[{string.Join(", ", boardHeroPowers)}]");
            HdtLog.Info($"[BGStats][HeroPower][Debug] BoardHeroPowers detail={boardDetail}");
            HdtLog.Info($"[BGStats][HeroPower][Debug] PlayerEntityHeroPowerRefs debug=[{string.Join(", ", playerEntityHeroPowers)}]");
            HdtLog.Info($"[BGStats][HeroPower][Debug] PlayerEntityHeroPowerRefs detail={playerEntityDetail}");
            HdtLog.Info($"[BGStats][HeroPower][Debug] HeroEntityHeroPowerRefs debug=[{string.Join(", ", heroEntityHeroPowers)}]");
            HdtLog.Info($"[BGStats][HeroPower][Debug] HeroEntityHeroPowerRefs detail={heroEntityDetail}");

            source = boardHeroPowers.Length > 0 ? "BoardHeroPowers" : "unresolved";
            HdtLog.Info($"[BGStats][HeroPower][Debug] final stored candidates=[{string.Join(", ", boardHeroPowers)}] source={source}");
            return boardHeroPowers;
        }

        private IEnumerable<string> GetHeroPowersFromBoard(out string detail)
        {
            detail = "not-started";
            try
            {
                var results = new List<string>();
                var boardEntities = Core.Game?.Player?.Board?.Cast<object>()?.ToArray();
                if (boardEntities == null)
                {
                    detail = "board=null";
                    return results;
                }

                var entityDetails = new List<string>();
                foreach (var entity in boardEntities.OrderBy(x => GetIntProperty(x, "Id")))
                {
                    if (entity == null)
                    {
                        entityDetails.Add("entity=null");
                        continue;
                    }

                    var entityId = GetIntProperty(entity, "Id");
                    var cardId = GetStringProperty(entity, "CardId");
                    var cardType = GetCardType(cardId);
                    var isHeroPower = GetPropertyValue(entity, "IsHeroPower") as bool? ?? cardType == CardType.HERO_POWER;
                    var zone = GetZone(entity).ToString();
                    var controllerId = GetNamedTagValue(entity, "CONTROLLER");

                    entityDetails.Add($"id={entityId},card={cardId},type={cardType},zone={zone},controller={controllerId},isHeroPower={isHeroPower}");
                    if (!isHeroPower)
                        continue;

                    AddHeroPowerCandidate(results, cardId);
                }

                detail = $"boardCount={boardEntities.Length}; entities=[{string.Join(" | ", entityDetails)}]";
                return results;
            }
            catch (Exception ex)
            {
                detail = "exception=" + ex.Message;
                return Array.Empty<string>();
            }
        }

        private static string NormalizeAccountValue(object value)
        {
            var text = value?.ToString();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        private static string GetBattleTagText(object battleTag)
        {
            if (battleTag == null)
                return null;

            try
            {
                var type = battleTag.GetType();
                var name = type.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)?.GetValue(battleTag, null)?.ToString();
                var number = type.GetProperty("Number", BindingFlags.Public | BindingFlags.Instance)?.GetValue(battleTag, null)?.ToString();
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(number))
                    return name.Trim() + "#" + number.Trim();
            }
            catch
            {
            }

            return NormalizeAccountValue(battleTag);
        }

        private void TryPopulateRegionFromAccountHi(string accountHi)
        {
            if (string.IsNullOrWhiteSpace(accountHi))
                return;
            if (!string.IsNullOrWhiteSpace(RegionName) && !string.IsNullOrWhiteSpace(RegionCode))
                return;

            try
            {
                if (!ulong.TryParse(accountHi, out var hiValue))
                    return;

                var regionCode = (int)((hiValue >> 32) & 0xFF);
                if (regionCode <= 0)
                    return;

                RegionCode = regionCode.ToString();
                RegionName = MapRegionName(regionCode);
            }
            catch
            {
            }
        }

        private static object GetAccountIdFromGameMetaData()
        {
            try
            {
                var metaData = Core.Game?.MetaData;
                return metaData?.GetType().GetProperty("AccountId", BindingFlags.Public | BindingFlags.Instance)?.GetValue(metaData, null);
            }
            catch
            {
                return null;
            }
        }

        private static object GetServerInfoFromGameMetaData()
        {
            try
            {
                var metaData = Core.Game?.MetaData;
                var type = metaData?.GetType();
                return type?.GetProperty("ServerInfo", BindingFlags.Public | BindingFlags.Instance)?.GetValue(metaData, null)
                    ?? type?.GetField("ServerInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(metaData);
            }
            catch
            {
                return null;
            }
        }

        private static object GetAccountContextFromCollection()
        {
            try
            {
                var asm = typeof(Core).Assembly;
                var helpersType = asm.GetType("Hearthstone_Deck_Tracker.Hearthstone.CollectionHelpers");
                var helper = helpersType?.GetProperty("Hearthstone", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null, null);
                if (helper == null)
                    return null;

                var collection = TryGetCollectionObject(helper);
                if (HasBattleTag(collection))
                    return collection;

                TryUpdateCollection(helper);

                collection = TryGetCollectionObject(helper);
                if (HasBattleTag(collection))
                    return collection;

                return collection;
            }
            catch
            {
                return null;
            }
        }

        private static object TryGetCollectionObject(object helper)
        {
            try
            {
                var tryGetCollection = helper.GetType().GetMethod("TryGetCollection", BindingFlags.Public | BindingFlags.Instance);
                if (tryGetCollection != null)
                {
                    var args = new object[] { null };
                    var ok = tryGetCollection.Invoke(helper, args);
                    if (ok is bool hasCollection && hasCollection && args[0] != null)
                        return args[0];
                }

                var getCollection = helper.GetType().GetMethod("GetCollection", BindingFlags.Public | BindingFlags.Instance);
                return getCollection?.Invoke(helper, null);
            }
            catch
            {
                return null;
            }
        }

        private static bool HasBattleTag(object collection)
        {
            var value = GetPropertyValueStatic(collection, "BattleTag");
            return !string.IsNullOrWhiteSpace(NormalizeAccountValue(value));
        }

        private static void TryUpdateCollection(object helper)
        {
            try
            {
                var updateMethod = helper.GetType().GetMethod("UpdateCollection", new System.Type[] { });
                var task = updateMethod?.Invoke(helper, null) as Task;
                task?.Wait(1000);
            }
            catch
            {
            }
        }

        private static object GetAccountIdFromReflectionClient()
        {
            return GetReflectionClientValue("GetAccountId");
        }

        private static object GetReflectionClientValue(string methodName)
        {
            try
            {
                var clientType = typeof(Core).Assembly.GetType("HearthMirror.Reflection.Client");
                return clientType?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            }
            catch
            {
                return null;
            }
        }

        private static object GetPropertyValueStatic(object target, string propertyName)
        {
            try
            {
                return target?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target, null);
            }
            catch
            {
                return null;
            }
        }

        private static string GetStringPropertyStatic(object target, string propertyName)
        {
            return GetPropertyValueStatic(target, propertyName)?.ToString() ?? string.Empty;
        }

        private static string GetNumericPropertyAsString(object instance, string propertyName)
        {
            try
            {
                var value = instance?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance, null);
                if (value == null)
                    return null;
                return Convert.ToUInt64(value).ToString();
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetAccountRegionCode(object accountId, out int regionCode)
        {
            regionCode = 0;
            try
            {
                var hi = GetUInt64PropertyValue(accountId, "Hi");
                if (!hi.HasValue)
                    return false;

                regionCode = (int)((hi.Value >> 32) & 0xFF);
                return regionCode > 0;
            }
            catch
            {
                return false;
            }
        }

        private static ulong? GetUInt64PropertyValue(object instance, string propertyName)
        {
            try
            {
                var value = instance?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance, null);
                if (value == null)
                    return null;
                return Convert.ToUInt64(value);
            }
            catch
            {
                return null;
            }
        }

        private static string MapRegionName(int regionCode)
        {
            switch (regionCode)
            {
                case 1:
                    return "US";
                case 2:
                    return "EU";
                case 3:
                    return "KR";
                case 5:
                    return "CN";
                default:
                    return "Region-" + regionCode;
            }
        }

        private IEnumerable<string> GetHeroPowersFromPlayerEntityDebug(out string detail)
        {
            detail = "not-started";
            try
            {
                var results = new List<string>();
                var playerEntity = Core.Game.PlayerEntity;
                if (playerEntity == null)
                {
                    detail = "playerEntity=null";
                    return results;
                }

                var heroPowerEntityId = playerEntity.GetTag(GameTag.HERO_POWER);
                if (heroPowerEntityId <= 0 || !Core.Game.Entities.ContainsKey(heroPowerEntityId))
                {
                    detail = $"tag=HERO_POWER,entityId={heroPowerEntityId},entity-missing";
                    return results;
                }

                var heroPowerEntity = Core.Game.Entities[heroPowerEntityId];
                AddHeroPowerCandidate(results, heroPowerEntity);
                var cardId = GetStringProperty(heroPowerEntity, "CardId");
                detail = $"tag=HERO_POWER,entityId={heroPowerEntityId},card={cardId},type={GetCardType(cardId)},zone={GetZone(heroPowerEntity)}";
                return results;
            }
            catch (Exception ex)
            {
                detail = "exception=" + ex.Message;
                return Array.Empty<string>();
            }
        }

        private IEnumerable<string> GetHeroPowersFromHeroEntityDebug(out string detail)
        {
            detail = "not-started";
            try
            {
                var results = new List<string>();
                var refDetails = new List<string>();
                var playerEntity = Core.Game.PlayerEntity;
                if (playerEntity == null)
                {
                    detail = "playerEntity=null";
                    return results;
                }

                var heroEntityId = playerEntity.GetTag(GameTag.HERO_ENTITY);
                if (heroEntityId <= 0 || !Core.Game.Entities.ContainsKey(heroEntityId))
                {
                    detail = $"heroEntityId={heroEntityId}; heroEntity-missing";
                    return results;
                }

                var heroEntity = Core.Game.Entities[heroEntityId];
                foreach (var tag in GetHeroPowerReferenceTags())
                {
                    var entityId = heroEntity.GetTag(tag);
                    if (entityId <= 0)
                    {
                        refDetails.Add($"tag={tag},entityId=0");
                        continue;
                    }

                    if (!Core.Game.Entities.TryGetValue(entityId, out var referencedEntity) || referencedEntity == null)
                    {
                        refDetails.Add($"tag={tag},entityId={entityId},entity-missing");
                        continue;
                    }

                    var cardId = GetStringProperty(referencedEntity, "CardId");
                    refDetails.Add($"tag={tag},entityId={entityId},card={cardId},type={GetCardType(cardId)},zone={GetZone(referencedEntity)}");
                    AddHeroPowerCandidate(results, referencedEntity);
                }

                detail = $"heroEntityId={heroEntityId}; refs=[{string.Join(" | ", refDetails)}]";
                return results;
            }
            catch (Exception ex)
            {
                detail = "exception=" + ex.Message;
                return Array.Empty<string>();
            }
        }

        private void TryResolvePlacement()
        {
            try
            {
                TryCachePlacement();
                var maxPlace = Core.Game.IsBattlegroundsDuosMatch ? 4 : 8;
                if (Placement >= 1 && Placement <= maxPlace)
                {
                    _needResolvePlacement = false;
                    HdtLog.Info($"[BGStats] å·²è§£æžåæ¬¡ placement={Placement}");
                }
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] TryResolvePlacement å¤±è´¥: " + ex.Message);
            }
        }

        private void TryCachePlacement()
        {
            try
            {
                var maxPlace = Core.Game.IsBattlegroundsDuosMatch ? 4 : 8;
                var playerEntity = Core.Game.PlayerEntity;
                if (playerEntity == null)
                    return;

                var heroEntityId = playerEntity.GetTag(GameTag.HERO_ENTITY);
                if (heroEntityId > 0 && Core.Game.Entities.TryGetValue(heroEntityId, out var heroEntity) && heroEntity != null)
                {
                    var raw = heroEntity.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE);
                    var placement = Math.Min(raw, maxPlace);
                    if (placement >= 1 && placement <= maxPlace)
                    {
                        Placement = placement;
                        return;
                    }
                }

                var raw2 = playerEntity.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE);
                var placement2 = Math.Min(raw2, maxPlace);
                if (placement2 >= 1 && placement2 <= maxPlace)
                    Placement = placement2;
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] TryCachePlacement å¤±è´¥: " + ex.Message);
            }
        }

        private void TryCacheAvailableRaces()
        {
            try
            {
                var races = TryGetAvailableRacesFromHdtUtils();
                if (races == null || races.Count == 0)
                    return;

                var raceNames = races.Where(r => r != Race.INVALID).Select(r => r.ToString()).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                if (raceNames.Length == 0 || SameArray(raceNames, AvailableRaceNames))
                    return;

                AvailableRaceNames = raceNames;
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] TryCacheAvailableRaces å¤±è´¥: " + ex.Message);
            }
        }

        private void TryCacheAnomaly()
        {
            try
            {
                if (AnomalyDbfId > 0)
                    return;

                var entity = Core.Game?.GameEntity;
                if (entity == null)
                    return;

                var anomalyDbf = entity.GetTag(GameTag.BACON_GLOBAL_ANOMALY_DBID);
                if (anomalyDbf <= 0)
                    return;

                AnomalyDbfId = anomalyDbf;
                var card = HearthDb.Cards.GetFromDbfId(anomalyDbf);
                AnomalyCardId = card?.Id;
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] TryCacheAnomaly å¤±è´¥: " + ex.Message);
            }
        }

        private void TryRefreshTrinkets()
        {
            try
            {
                TryCacheTrinketsFromGameV2();
                var legacySnapshot = CaptureLegacyTrinketSnapshot();
                LogLegacyTrinketDebug(legacySnapshot);
                ApplyLegacyHeroPowerTrinket(legacySnapshot);
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] TryRefreshTrinkets failed: " + ex.Message);
            }
        }

        private void TryRefreshTimewarp()
        {
            try
            {
                TryCaptureQuestShell();
                TryCaptureTimewarpEntry();
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] TryRefreshTimewarp failed: " + ex.Message);
            }
        }

        private void TryCaptureQuestShell()
        {
            if (!string.IsNullOrWhiteSpace(QuestCardId) && !string.IsNullOrWhiteSpace(QuestRewardCardId))
                return;

            try
            {
                var questEntity = Core.Game?.Entities?.Values
                    .FirstOrDefault(entity => entity != null
                        && !string.IsNullOrWhiteSpace(entity.CardId)
                        && entity.CardId.IndexOf("_Quest_", StringComparison.OrdinalIgnoreCase) >= 0);
                if (questEntity != null)
                {
                    QuestCardId = questEntity.CardId;
                    var rewardCardId = ResolveCardIdFromDbfTag(questEntity, "QUEST_REWARD_DATABASE_ID")
                        ?? ResolveCardIdFromDbfTag(Core.Game?.PlayerEntity, "BACON_HERO_QUEST_REWARD_DATABASE_ID");
                    if (!string.IsNullOrWhiteSpace(rewardCardId))
                        QuestRewardCardId = rewardCardId;
                    QuestCompleted = GetNamedTagValue(questEntity, "BACON_QUEST_COMPLETED") > 0
                        || GetNamedTagValue(Core.Game?.PlayerEntity, "BACON_HERO_QUEST_REWARD_COMPLETED") > 0;
                }
            }
            catch
            {
            }
        }

        private void TryCaptureTimewarpEntry()
        {
            var snapshot = GetCurrentChoiceTimewarpSnapshot();
            if (snapshot == null || snapshot.OfferedCardIds.Length == 0)
                return;

            var type = ResolveCurrentTimewarpType();
            if (string.IsNullOrWhiteSpace(type))
                return;

            var options = snapshot.OfferedCardIds;
            var selected = snapshot.SelectedCardIds ?? Array.Empty<string>();
            var signature = type + "|" + string.Join("|", options.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            if (snapshot.ChoiceId <= 0 && string.Equals(_lastTimewarpOptionsSignature, signature, StringComparison.OrdinalIgnoreCase))
                return;

            if (snapshot.ChoiceId > 0
                && _timewarpEntryIndexesByChoiceId.TryGetValue(snapshot.ChoiceId, out var existingIndex)
                && existingIndex >= 0
                && existingIndex < TimewarpEntries.Count)
            {
                var existingEntry = TimewarpEntries[existingIndex];
                if (existingEntry != null)
                {
                    existingEntry.Type = type;
                    existingEntry.OptionCardIds = options;
                    if (selected.Length > 0)
                        existingEntry.SelectedCardIds = selected;
                    _lastTimewarpOptionsSignature = signature;
                    return;
                }
            }

            var existingSameOptions = TimewarpEntries.Any(entry =>
                entry != null
                && string.Equals(entry.Type, type, StringComparison.OrdinalIgnoreCase)
                && SameCardIdSet(entry.OptionCardIds, options));

            var entryToAdd = new BgTimewarpEntry
            {
                Type = type,
                IsExtra = existingSameOptions || TimewarpEntries.Any(entry => string.Equals(entry.Type, type, StringComparison.OrdinalIgnoreCase)),
                OptionCardIds = options,
                SelectedCardIds = selected
            };
            TimewarpEntries.Add(entryToAdd);
            if (snapshot.ChoiceId > 0)
                _timewarpEntryIndexesByChoiceId[snapshot.ChoiceId] = TimewarpEntries.Count - 1;
            _lastTimewarpOptionsSignature = signature;
            HdtLog.Info($"[BGStats][Timewarp] captured options choice={snapshot.ChoiceId} type={type} extra={entryToAdd.IsExtra} options=[{string.Join(", ", options)}] selected=[{string.Join(", ", selected)}] detail=[{string.Join(" | ", snapshot.OfferedDebugEntries ?? Array.Empty<string>())}]");
        }

        private TimewarpChoiceSnapshot GetCurrentChoiceTimewarpSnapshot()
        {
            try
            {
                var choice = GetPropertyValue(Core.Game, "CurrentChoice");
                if (choice == null)
                    return null;

                var offeredEntityIds = GetEnumerableValues(GetPropertyValue(choice, "OfferedEntityIds"))
                    .Select(ConvertToInt)
                    .Where(id => id > 0)
                    .ToArray();
                if (offeredEntityIds.Length == 0)
                    return null;

                var offeredEntities = offeredEntityIds
                    .Select(id => new
                    {
                        EntityId = id,
                        Entity = TryGetEntityById(id)
                    })
                    .Where(x => x.Entity != null)
                    .ToList();

                var offeredCardIds = offeredEntities
                    .Where(x => IsCurrentChoiceTimewarpEntity(x.Entity))
                    .Select(x => (string)NormalizeCardId(x.Entity.CardId))
                    .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (offeredCardIds.Length == 0)
                    return null;

                var selectedCardIds = GetEnumerableValues(GetPropertyValue(choice, "ChosenEntityIds"))
                    .Select(ConvertToInt)
                    .Where(id => id > 0)
                    .Select(id => TryGetEntityById(id))
                    .Where(entity => entity != null && IsCurrentChoiceTimewarpEntity(entity))
                    .Select(entity => (string)NormalizeCardId(entity.CardId))
                    .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new TimewarpChoiceSnapshot
                {
                    ChoiceId = GetIntProperty(choice, "Id"),
                    OfferedCardIds = offeredCardIds,
                    SelectedCardIds = selectedCardIds,
                    OfferedDebugEntries = offeredEntities
                        .Select(x =>
                        {
                            var cardId = NormalizeCardId(x.Entity.CardId);
                            var isCandidate = IsCurrentChoiceTimewarpEntity(x.Entity);
                            var timewarped = GetNamedTagValue(x.Entity, "BACON_TIMEWARPED");
                            return $"entity={x.EntityId},card={cardId ?? "null"},candidate={isCandidate},timewarped={timewarped},type={GetCardType(cardId)}";
                        })
                        .ToArray()
                };
            }
            catch (Exception ex)
            {
                HdtLog.Warn("[BGStats][Timewarp] failed to read CurrentChoice: " + ex.Message);
                return null;
            }
        }

        private dynamic TryGetEntityById(int entityId)
        {
            try
            {
                if (entityId <= 0)
                    return null;
                if (Core.Game?.Entities == null)
                    return null;
                return Core.Game.Entities.TryGetValue(entityId, out var entity) ? entity : null;
            }
            catch
            {
                return null;
            }
        }

        private bool IsCurrentChoiceTimewarpEntity(dynamic entity)
        {
            try
            {
                var cardId = NormalizeCardId(entity?.CardId);
                if (string.IsNullOrWhiteSpace(cardId))
                    return false;

                var cardType = GetCardType(cardId);
                if (cardType == CardType.HERO
                    || cardType == CardType.HERO_POWER
                    || cardType == CardType.ENCHANTMENT
                    || cardType == CardType.BATTLEGROUND_TRINKET)
                    return false;

                return GetNamedTagValue(entity, "BACON_TIMEWARPED") > 0 || IsTimewarpCardId(cardId);
            }
            catch
            {
                return false;
            }
        }

        private bool IsPotentialTimewarpEntity(dynamic entity)
        {
            try
            {
                var cardId = entity?.CardId as string;
                if (string.IsNullOrWhiteSpace(cardId) || !IsTimewarpCardId(cardId))
                    return false;

                if (GetNamedTagValue(entity, "BACON_TIMEWARPED") > 0)
                    return true;

                var zone = GetZone(entity);
                return zone == Zone.SETASIDE || zone == Zone.PLAY || zone == Zone.HAND || zone == Zone.SECRET || zone == Zone.GRAVEYARD;
            }
            catch
            {
                return false;
            }
        }

        private bool IsControlledByPlayer(dynamic entity)
        {
            try
            {
                var controller = GetNamedTagValue(entity, "CONTROLLER");
                var playerId = Core.Game?.PlayerEntity?.Id ?? 0;
                return controller > 0 && playerId > 0 && controller == playerId;
            }
            catch
            {
                return false;
            }
        }

        private string ResolveCurrentTimewarpType()
        {
            var round = ConvertTurnToRound(Core.Game?.GameEntity?.GetTag(GameTag.TURN) ?? 0);
            var heroPowerIds = (_currentHeroPowerCardIds ?? Array.Empty<string>())
                .Concat(HeroPowerCardIds ?? Array.Empty<string>())
                .Concat(InitialHeroPowerCardIds ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (heroPowerIds.Contains(ButtonsHeroPowerCardId, StringComparer.OrdinalIgnoreCase))
                return "major";
            if (heroPowerIds.Contains(MarinHeroPowerCardId, StringComparer.OrdinalIgnoreCase))
                return "minor";
            if (round >= 8)
                return "major";
            if (round > 0)
                return "minor";

            return string.Empty;
        }

        private static bool IsTimewarpCardId(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId))
                return false;

            try
            {
                var card = Cards.All.TryGetValue(cardId, out var exact) ? exact : Cards.All.Values.FirstOrDefault(x => string.Equals(x.Id, cardId, StringComparison.OrdinalIgnoreCase));
                if (card == null)
                    return false;

                if (card.Type == CardType.HERO
                    || card.Type == CardType.HERO_POWER
                    || card.Type == CardType.ENCHANTMENT
                    || card.Type == CardType.BATTLEGROUND_TRINKET)
                    return false;

                var name = card.Name ?? string.Empty;
                var text = card.Text ?? string.Empty;
                return name.IndexOf("Timewarp", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Timewarped", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("Timewarp", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("Timewarped", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private void TryCacheTrinketsFromGameV2()
        {
            var states = GetGameV2TrinketPickSnapshots();
            var currentChoice = GetCurrentChoiceTrinketSnapshot();
            LogGameV2TrinketDebug(states, currentChoice);

            if (states.Count == 0 && currentChoice == null)
                return;

            var orderedStates = states
                .OrderBy(state => state.Turn > 0 ? state.Turn : int.MaxValue)
                .ThenBy(state => state.ChoiceId > 0 ? state.ChoiceId : int.MaxValue)
                .ToList();

            var resolvedTypes = new List<string>();
            foreach (var state in orderedStates)
            {
                var pickType = ResolveGameV2TrinketPickType(state.Turn, resolvedTypes.Count);
                if (string.IsNullOrWhiteSpace(pickType))
                    continue;

                ApplyGameV2TrinketPick(state.OfferedCardIds, state.ChosenCardId, pickType);
                resolvedTypes.Add(pickType);
            }

            if (currentChoice == null)
                return;

            if (orderedStates.Any(state => state.ChoiceId > 0 && state.ChoiceId == currentChoice.ChoiceId))
                return;

            var currentChoiceType = ResolveGameV2TrinketPickType(currentChoice.Turn, resolvedTypes.Count);
            if (string.IsNullOrWhiteSpace(currentChoiceType))
                return;

            ApplyGameV2TrinketPick(currentChoice.OfferedCardIds, currentChoice.ChosenCardId, currentChoiceType);
        }

        private void ApplyGameV2TrinketPick(string[] offeredCardIds, string chosenCardId, string pickType)
        {
            var normalizedOptions = NormalizeCardIds(offeredCardIds);
            var normalizedChosen = NormalizeCardId(chosenCardId);
            if (string.Equals(pickType, "lesser", StringComparison.OrdinalIgnoreCase))
            {
                if (normalizedOptions.Length > 0)
                    LesserTrinketOptionCardIds = normalizedOptions;
                if (!string.IsNullOrWhiteSpace(normalizedChosen))
                    LesserTrinketCardId = normalizedChosen;
                return;
            }

            if (string.Equals(pickType, "greater", StringComparison.OrdinalIgnoreCase))
            {
                if (normalizedOptions.Length > 0)
                    GreaterTrinketOptionCardIds = normalizedOptions;
                if (!string.IsNullOrWhiteSpace(normalizedChosen))
                    GreaterTrinketCardId = normalizedChosen;
            }
        }

        private LegacyTrinketSnapshot CaptureLegacyTrinketSnapshot()
        {
            var snapshot = new LegacyTrinketSnapshot
            {
                Round = ConvertTurnToRound(Core.Game?.GameEntity?.GetTag(GameTag.TURN) ?? 0)
            };

            var playerEntity = Core.Game?.PlayerEntity;
            if (playerEntity != null)
            {
                snapshot.LesserTrinketCardId = ResolveCardIdFromDbfTag(playerEntity, "BACON_FIRST_TRINKET_DATABASE_ID");
                snapshot.GreaterTrinketCardId = ResolveCardIdFromDbfTag(playerEntity, "BACON_SECOND_TRINKET_DATABASE_ID");
                snapshot.HeroPowerTrinketCardId = ResolveCardIdFromDbfTag(playerEntity, "BACON_HEROPOWER_TRINKET_DATABASE_ID");
                snapshot.HeroPowerTrinketType = ResolveHeroPowerTrinketType();
            }

            try
            {
                var potentialEntities = Core.Game?.Entities?.Values
                    .Where(entity => entity != null && GetNamedTagValue(entity, "BACON_IS_POTENTIAL_TRINKET") > 0)
                    .Select(entity => new
                    {
                        Id = GetIntProperty(entity, "Id"),
                        CardId = NormalizeCardId(entity.CardId),
                        Zone = GetZone(entity).ToString(),
                        Controller = GetNamedTagValue(entity, "CONTROLLER"),
                        CardType = GetCardType(entity.CardId)
                    })
                    .ToList();

                if (potentialEntities != null)
                {
                    snapshot.Options = potentialEntities
                        .Select(entity => entity.CardId)
                        .Where(cardId => !string.IsNullOrWhiteSpace(cardId) && GetCardType(cardId) == CardType.BATTLEGROUND_TRINKET)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(4)
                        .ToArray();
                    snapshot.EntityDebugEntries = potentialEntities
                        .Select(entity => $"id={entity.Id},card={entity.CardId ?? "null"},zone={entity.Zone},controller={entity.Controller},type={entity.CardType}")
                        .ToArray();
                }
            }
            catch
            {
                snapshot.Options = Array.Empty<string>();
                snapshot.EntityDebugEntries = Array.Empty<string>();
            }

            return snapshot;
        }

        private void ApplyLegacyHeroPowerTrinket(LegacyTrinketSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            var heroPowerTrinket = NormalizeCardId(snapshot.HeroPowerTrinketCardId);
            if (!string.IsNullOrWhiteSpace(heroPowerTrinket))
                HeroPowerTrinketCardId = heroPowerTrinket;

            var heroPowerType = NormalizeCardId(snapshot.HeroPowerTrinketType);
            if (!string.IsNullOrWhiteSpace(heroPowerType))
                HeroPowerTrinketType = heroPowerType;
        }

        private void LogGameV2TrinketDebug(IReadOnlyList<GameV2TrinketPickSnapshot> states, GameV2TrinketPickSnapshot currentChoice)
        {
            var game = Core.Game;
            if (game == null)
                return;

            var gameTypeName = game.GetType().FullName ?? game.GetType().Name;
            var hasStateProperty = HasProperty(game, "BattlegroundsTrinketPickStates");
            var hasCurrentChoiceProperty = HasProperty(game, "CurrentChoice");
            var stateDebug = states == null || states.Count == 0
                ? "none"
                : string.Join(" || ", states.Select(BuildGameV2TrinketDebugEntry));
            var currentChoiceDebug = currentChoice == null ? "none" : BuildGameV2TrinketDebugEntry(currentChoice);
            var signature = string.Join("|",
                gameTypeName,
                hasStateProperty,
                hasCurrentChoiceProperty,
                stateDebug,
                currentChoiceDebug);

            if (string.Equals(signature, _lastGameV2TrinketDebugSignature, StringComparison.Ordinal))
                return;

            _lastGameV2TrinketDebugSignature = signature;
            HdtLog.Info($"[BGStats][Trinket][GameV2Debug] gameType={gameTypeName} hasStates={hasStateProperty} hasCurrentChoice={hasCurrentChoiceProperty} states={stateDebug} currentChoice={currentChoiceDebug}");
            if (!hasStateProperty && !hasCurrentChoiceProperty && !_loggedMissingGameV2TrinketPath)
            {
                _loggedMissingGameV2TrinketPath = true;
                HdtLog.Warn("[BGStats][Trinket][GameV2Debug] BattlegroundsTrinketPickStates/CurrentChoice not found on current HDT game object.");
            }
        }

        private void LogLegacyTrinketDebug(LegacyTrinketSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            var signature = string.Join("|",
                snapshot.Round,
                string.Join(",", snapshot.Options ?? Array.Empty<string>()),
                snapshot.LesserTrinketCardId ?? string.Empty,
                snapshot.GreaterTrinketCardId ?? string.Empty,
                snapshot.HeroPowerTrinketCardId ?? string.Empty,
                snapshot.HeroPowerTrinketType ?? string.Empty,
                string.Join(" || ", snapshot.EntityDebugEntries ?? Array.Empty<string>()));

            if (string.Equals(signature, _lastLegacyTrinketDebugSignature, StringComparison.Ordinal))
                return;

            _lastLegacyTrinketDebugSignature = signature;
            HdtLog.Info($"[BGStats][Trinket][LegacyDebug] round={snapshot.Round} options=[{string.Join(", ", snapshot.Options ?? Array.Empty<string>())}] lesser={snapshot.LesserTrinketCardId ?? "null"} greater={snapshot.GreaterTrinketCardId ?? "null"} heroPower={snapshot.HeroPowerTrinketCardId ?? "null"} heroPowerType={snapshot.HeroPowerTrinketType ?? "null"} entities=[{string.Join(" | ", snapshot.EntityDebugEntries ?? Array.Empty<string>())}]");
        }

        private List<GameV2TrinketPickSnapshot> GetGameV2TrinketPickSnapshots()
        {
            var snapshots = new List<GameV2TrinketPickSnapshot>();

            try
            {
                foreach (var state in GetEnumerableValues(GetPropertyValue(Core.Game, "BattlegroundsTrinketPickStates")))
                {
                    var snapshot = BuildGameV2TrinketPickSnapshot(
                        state,
                        GetPropertyValue(state, "Params"),
                        NormalizeNullableCardId(ResolveCardIdFromDbfId(GetNullableIntProperty(state, "ChosenTrinketDbfId"))),
                        GetIntProperty(state, "ChoiceId"));
                    if (snapshot != null)
                        snapshots.Add(snapshot);
                }
            }
            catch (Exception ex)
            {
                HdtLog.Warn("[BGStats][Trinket][GameV2Debug] failed to read BattlegroundsTrinketPickStates: " + ex.Message);
            }

            return snapshots;
        }

        private string ResolveCardIdFromDbfTag(dynamic entity, string tagName)
        {
            var dbfId = GetNamedTagValue(entity, tagName);
            if (dbfId <= 0)
                return null;

            try
            {
                return Cards.GetFromDbfId(dbfId)?.Id;
            }
            catch
            {
                return null;
            }
        }

        private string ResolveHeroPowerTrinketType()
        {
            var heroPowerIds = (_currentHeroPowerCardIds ?? Array.Empty<string>())
                .Concat(HeroPowerCardIds ?? Array.Empty<string>())
                .Concat(InitialHeroPowerCardIds ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (heroPowerIds.Contains(MarinHeroPowerCardId, StringComparer.OrdinalIgnoreCase))
                return "lesser";
            if (heroPowerIds.Contains(ButtonsHeroPowerCardId, StringComparer.OrdinalIgnoreCase))
                return "greater";

            return null;
        }

        private GameV2TrinketPickSnapshot GetCurrentChoiceTrinketSnapshot()
        {
            try
            {
                var choice = GetPropertyValue(Core.Game, "CurrentChoice");
                if (choice == null)
                    return null;

                var offeredEntityIds = GetEnumerableValues(GetPropertyValue(choice, "OfferedEntityIds"))
                    .Select(ConvertToInt)
                    .Where(id => id > 0)
                    .ToArray();
                if (offeredEntityIds.Length == 0)
                    return null;

                var offeredCardIds = offeredEntityIds
                    .Select(ResolveCardIdFromEntityId)
                    .Where(cardId => !string.IsNullOrWhiteSpace(cardId) && GetCardType(cardId) == CardType.BATTLEGROUND_TRINKET)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (offeredCardIds.Length == 0)
                    return null;

                var chosenCardId = GetEnumerableValues(GetPropertyValue(choice, "ChosenEntityIds"))
                    .Select(ConvertToInt)
                    .Where(id => id > 0)
                    .Select(ResolveCardIdFromEntityId)
                    .FirstOrDefault(cardId => !string.IsNullOrWhiteSpace(cardId));

                return new GameV2TrinketPickSnapshot
                {
                    ChoiceId = GetIntProperty(choice, "Id"),
                    Turn = GetCurrentTurnNumber(),
                    SourceCardId = string.Empty,
                    SourceDbfId = 0,
                    OfferedCardIds = offeredCardIds,
                    OfferedDebugEntries = offeredEntityIds
                        .Select(id => $"entity={id},card={ResolveCardIdFromEntityId(id) ?? "null"}")
                        .ToArray(),
                    ChosenCardId = NormalizeNullableCardId(chosenCardId)
                };
            }
            catch (Exception ex)
            {
                HdtLog.Warn("[BGStats][Trinket][GameV2Debug] failed to read CurrentChoice: " + ex.Message);
                return null;
            }
        }

        private GameV2TrinketPickSnapshot BuildGameV2TrinketPickSnapshot(object state, object parameters, string chosenCardId, int choiceId)
        {
            if (parameters == null)
                return null;

            var offered = new List<string>();
            var offeredDebug = new List<string>();
            foreach (var offeredTrinket in GetEnumerableValues(GetPropertyValue(parameters, "OfferedTrinkets")))
            {
                var dbfId = GetNullableIntProperty(offeredTrinket, "TrinketDbfId");
                var cardId = NormalizeNullableCardId(ResolveCardIdFromDbfId(dbfId));
                var extraData = GetNullableIntProperty(offeredTrinket, "ExtraData") ?? 0;
                if (!string.IsNullOrWhiteSpace(cardId))
                    offered.Add(cardId);
                offeredDebug.Add($"dbf={dbfId?.ToString() ?? "null"},card={cardId ?? "null"},extra={extraData}");
            }

            if (offered.Count == 0)
                return null;

            return new GameV2TrinketPickSnapshot
            {
                ChoiceId = choiceId,
                Turn = GetIntProperty(parameters, "Turn"),
                SourceDbfId = GetIntProperty(parameters, "SourceDbfId"),
                SourceCardId = NormalizeNullableCardId(ResolveCardIdFromDbfId(GetNullableIntProperty(parameters, "SourceDbfId"))),
                OfferedCardIds = offered
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                OfferedDebugEntries = offeredDebug.ToArray(),
                ChosenCardId = NormalizeNullableCardId(chosenCardId)
            };
        }

        private string BuildGameV2TrinketDebugEntry(GameV2TrinketPickSnapshot snapshot)
        {
            if (snapshot == null)
                return "null";

            return $"choice={snapshot.ChoiceId},turn={snapshot.Turn},sourceDbf={snapshot.SourceDbfId},sourceCard={snapshot.SourceCardId ?? "null"},offered=[{string.Join(", ", snapshot.OfferedCardIds ?? Array.Empty<string>())}],chosen={snapshot.ChosenCardId ?? "null"},detail=[{string.Join(" | ", snapshot.OfferedDebugEntries ?? Array.Empty<string>())}]";
        }

        private string ResolveGameV2TrinketPickType(int turn, int existingResolvedCount)
        {
            if (turn > 0)
            {
                if (turn <= 6)
                    return "lesser";
                if (turn >= 9)
                    return "greater";
            }

            if (existingResolvedCount == 0)
                return "lesser";
            if (existingResolvedCount == 1)
                return "greater";

            return string.Empty;
        }

        private int GetCurrentTurnNumber()
        {
            try
            {
                var method = Core.Game?.GetType().GetMethod("GetTurnNumber", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (method != null)
                    return ConvertToInt(method.Invoke(Core.Game, null));
            }
            catch
            {
            }

            return ConvertTurnToRound(Core.Game?.GameEntity?.GetTag(GameTag.TURN) ?? 0);
        }

        private string ResolveCardIdFromEntityId(int entityId)
        {
            try
            {
                if (entityId <= 0)
                    return null;

                if (Core.Game?.Entities != null && Core.Game.Entities.TryGetValue(entityId, out var entity))
                    return NormalizeNullableCardId(entity?.CardId);
            }
            catch
            {
            }

            return null;
        }

        private string ResolveCardIdFromDbfId(int? dbfId)
        {
            if (!dbfId.HasValue || dbfId.Value <= 0)
                return null;

            try
            {
                return Cards.GetFromDbfId(dbfId.Value)?.Id;
            }
            catch
            {
                return null;
            }
        }

        private string[] NormalizeCardIds(IEnumerable<string> cardIds)
        {
            return cardIds?
                .Select(NormalizeCardId)
                .Where(cardId => !string.IsNullOrWhiteSpace(cardId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();
        }

        private string NormalizeCardId(string cardId)
        {
            return string.IsNullOrWhiteSpace(cardId) ? string.Empty : cardId.Trim();
        }

        private string NormalizeNullableCardId(string cardId)
        {
            var normalized = NormalizeCardId(cardId);
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private bool HasProperty(object target, string propertyName)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName))
                return false;

            return target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null
                || target.GetType().GetField(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null;
        }

        private IEnumerable<object> GetEnumerableValues(object value)
        {
            if (value is System.Collections.IEnumerable enumerable && !(value is string))
            {
                foreach (var item in enumerable)
                    yield return item;
            }
        }

        private int? GetNullableIntProperty(object target, string propertyName)
        {
            var value = GetPropertyValue(target, propertyName);
            if (value == null)
                return null;

            if (value is int direct)
                return direct;

            if (value is int?)
                return (int?)value;

            var nullableType = Nullable.GetUnderlyingType(value.GetType());
            if (nullableType == typeof(int))
                return (int?)value;

            int parsed;
            return int.TryParse(value.ToString(), out parsed) ? parsed : (int?)null;
        }

        private int ConvertToInt(object value)
        {
            if (value is int direct)
                return direct;

            if (value is int?)
                return ((int?)value) ?? 0;

            int parsed;
            return value != null && int.TryParse(value.ToString(), out parsed) ? parsed : 0;
        }

        private void TryUpdateTurnScopedSnapshots()
        {
            try
            {
                var rawTurn = Core.Game?.GameEntity?.GetTag(GameTag.TURN) ?? 0;
                if (rawTurn <= 0 || rawTurn == _lastObservedRawTurn)
                    return;

                _lastObservedRawTurn = rawTurn;
                TryCacheTavernUpgrade(rawTurn);
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] TryUpdateTurnScopedSnapshots å¤±è´¥: " + ex.Message);
            }
        }

        private void TryCapturePreCombatSnapshot()
        {
            try
            {
                var currentStep = (Step)(Core.Game?.GameEntity?.GetTag(GameTag.STEP) ?? 0);
                if (currentStep == _lastObservedStep && !(IsCombatPreparationStep(currentStep) && !_combatSnapshotCapturedThisFight))
                    return;

                var wasCombatPreparation = IsCombatPreparationStep(_lastObservedStep);
                var isCombatPreparation = IsCombatPreparationStep(currentStep);
                if (!isCombatPreparation)
                    _combatSnapshotCapturedThisFight = false;

                var shouldCapture = isCombatPreparation && (!_combatSnapshotCapturedThisFight || !wasCombatPreparation);
                _lastObservedStep = currentStep;
                if (!shouldCapture)
                    return;

                TryCachePreCombatSnapshot();
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] TryCapturePreCombatSnapshot å¤±è´¥: " + ex.Message);
            }
        }

        private void TryCachePreCombatSnapshot()
        {
            TryRefreshHeroPower();
            TryRecordCombatHeroPowers();
            TryCacheFinalBoard();
            _combatSnapshotCapturedThisFight = FinalBoard.Count > 0
                || InitialHeroPowerCardIds.Length > 0
                || HeroPowerCardIds.Length > 0
                || _currentHeroPowerCardIds.Length > 0;
            HdtLog.Info($"[BGStats] captured pre-combat snapshot: step={_lastObservedStep}, boardCount={FinalBoard.Count}, initial=[{string.Join(", ", InitialHeroPowerCardIds)}], combat=[{string.Join(", ", HeroPowerCardIds)}], current=[{string.Join(", ", _currentHeroPowerCardIds)}]");
        }

        private void TryCacheFinalBoard()
        {
            try
            {
                var list = GetLiveBoardEntities()
                    .Where(e => e != null && e.IsMinion && !string.IsNullOrEmpty(e.CardId))
                    .OrderBy(e => e.ZonePosition)
                    .Select(BuildBoardMinionSnapshot)
                    .Where(x => x != null)
                    .ToList();

                if (list.Count > 0)
                    FinalBoard = list;
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] TryCacheFinalBoard å¤±è´¥: " + ex.Message);
            }
        }

        private void TryCacheTavernUpgrade(int rawTurn)
        {
            try
            {
                var playerEntity = Core.Game.PlayerEntity;
                if (playerEntity == null)
                    return;

                var tavernTier = playerEntity.GetTag(GameTag.PLAYER_TECH_LEVEL);
                var turn = ConvertTurnToRound(rawTurn);
                if (tavernTier <= 0 || turn <= 0)
                    return;

                if (_lastRecordedTavernTier == tavernTier && TavernUpgradeTimeline.Count > 0)
                    return;

                _lastRecordedTavernTier = tavernTier;
                var existing = TavernUpgradeTimeline.LastOrDefault();
                if (existing != null && existing.Turn == turn)
                    existing.TavernTier = tavernTier;
                else
                    TavernUpgradeTimeline.Add(new BgTavernUpgradePoint { Turn = turn, TavernTier = tavernTier });
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] TryCacheTavernUpgrade å¤±è´¥: " + ex.Message);
            }
        }

        private static int ConvertTurnToRound(int rawTurn)
        {
            return rawTurn <= 0 ? 0 : (rawTurn + 1) / 2;
        }

        private IEnumerable<dynamic> GetLiveBoardEntities()
        {
            var playerEntities = Core.Game.Player?.PlayerEntities;
            if (playerEntities != null)
            {
                var fromEntities = playerEntities
                    .Where(e => e != null && e.IsMinion && GetZone(e) == Zone.PLAY && !string.IsNullOrEmpty((string)e.CardId))
                    .OrderBy(e => e.ZonePosition)
                    .ToList();
                if (fromEntities.Count > 0)
                    return fromEntities;
            }

            var board = Core.Game.Player?.Board;
            return board == null
                ? Enumerable.Empty<dynamic>()
                : board.Where(e => e != null).OrderBy(e => e.ZonePosition).ToList();
        }

        private static bool IsCombatPreparationStep(Step step)
        {
            return step == Step.MAIN_READY
                || step == Step.MAIN_START
                || step == Step.MAIN_START_TRIGGERS
                || step == Step.MAIN_COMBAT;
        }

        private Zone GetZone(dynamic entity)
        {
            try
            {
                var raw = entity.GetTag(GameTag.ZONE);
                return Enum.IsDefined(typeof(Zone), raw) ? (Zone)raw : Zone.INVALID;
            }
            catch
            {
                return Zone.INVALID;
            }
        }

        private BgBoardMinionSnapshot BuildBoardMinionSnapshot(dynamic entity)
        {
            try
            {
                var cardId = GetStringProperty(entity, "CardId");
                if (entity == null || string.IsNullOrEmpty(cardId))
                    return null;

                return new BgBoardMinionSnapshot
                {
                    CardId = cardId,
                    Name = GetCardName(entity, cardId),
                    IsGolden = GetNamedTagValue(entity, "PREMIUM") > 0,
                    Race = GetCardRace(entity, cardId),
                    Position = GetIntProperty(entity, "ZonePosition"),
                    Attack = GetNamedTagValue(entity, "ATK"),
                    Health = GetNamedTagValue(entity, "HEALTH"),
                    Keywords = new BgKeywordState
                    {
                        Taunt = GetNamedTagValue(entity, "TAUNT") > 0,
                        DivineShield = GetNamedTagValue(entity, "DIVINE_SHIELD") > 0,
                        Poisonous = GetNamedTagValue(entity, "POISONOUS") > 0 || GetNamedTagValue(entity, "VENOMOUS") > 0,
                        Reborn = GetNamedTagValue(entity, "REBORN") > 0,
                        Windfury = GetNamedTagValue(entity, "WINDFURY") > 0,
                        MegaWindfury = GetNamedTagValue(entity, "MEGA_WINDFURY") > 0,
                        Stealth = GetNamedTagValue(entity, "STEALTH") > 0,
                        Cleave = GetNamedTagValue(entity, "CLEAVE") > 0
                    }
                };
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] BuildBoardMinionSnapshot å¤±è´¥: " + ex.Message);
                return null;
            }
        }

        private string GetCardName(dynamic entity, string cardId)
        {
            try
            {
                var card = GetPropertyValue(entity, "Card");
                var name = GetStringProperty(card, "Name");
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
            catch { }

            try
            {
                return Cards.All.Values.FirstOrDefault(x => string.Equals(x.Id, cardId, StringComparison.OrdinalIgnoreCase))?.Name ?? cardId;
            }
            catch
            {
                return cardId;
            }
        }

        private string GetCardRace(dynamic entity, string cardId)
        {
            try
            {
                var card = GetPropertyValue(entity, "Card");
                var raceValue = GetPropertyValue(card, "Race");
                if (raceValue is Race race && race != Race.INVALID)
                    return race.ToString();

                var raceText = raceValue?.ToString();
                if (!string.IsNullOrWhiteSpace(raceText) && !string.Equals(raceText, Race.INVALID.ToString(), StringComparison.OrdinalIgnoreCase))
                    return raceText;
            }
            catch { }

            try
            {
                var dbCard = Cards.All.Values.FirstOrDefault(x => string.Equals(x.Id, cardId, StringComparison.OrdinalIgnoreCase));
                return dbCard != null && dbCard.Race != Race.INVALID ? dbCard.Race.ToString() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private int GetNamedTagValue(dynamic entity, string tagName)
        {
            try
            {
                if (entity == null || string.IsNullOrWhiteSpace(tagName))
                    return 0;
                if (!Enum.TryParse(tagName, true, out GameTag tag))
                    return 0;
                return entity.GetTag(tag);
            }
            catch
            {
                return 0;
            }
        }


        private object GetPropertyValue(object target, string propertyName)
        {
            try
            {
                if (target == null || string.IsNullOrWhiteSpace(propertyName))
                    return null;

                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var prop = target.GetType().GetProperty(propertyName, flags);
                if (prop != null)
                    return prop.GetValue(target, null);

                var field = target.GetType().GetField(propertyName, flags);
                return field?.GetValue(target);
            }
            catch
            {
                return null;
            }
        }

        private string GetStringProperty(object target, string propertyName)
        {
            return GetPropertyValue(target, propertyName)?.ToString() ?? string.Empty;
        }

        private int GetIntProperty(object target, string propertyName)
        {
            var value = GetPropertyValue(target, propertyName);
            if (value is int direct)
                return direct;

            int parsed;
            return value != null && int.TryParse(value.ToString(), out parsed) ? parsed : 0;
        }

        private static void AddHeroPowerCandidates(ICollection<string> results, ICollection<string> sources, IEnumerable<string> cardIds, string source)
        {
            if (results == null || cardIds == null)
                return;

            var before = results.Count;
            foreach (var cardId in cardIds)
                AddHeroPowerCandidate(results, cardId);

            if (sources != null && results.Count > before && !string.IsNullOrWhiteSpace(source))
                sources.Add(source);
        }

        private static void AddHeroPowerCandidate(ICollection<string> results, object heroPower)
        {
            if (results == null || heroPower == null)
                return;

            var cardId = GetStringPropertyStatic(heroPower, "CardId");
            if (string.IsNullOrWhiteSpace(cardId))
            {
                var card = GetPropertyValueStatic(heroPower, "Card");
                cardId = GetStringPropertyStatic(card, "Id");
            }

            AddHeroPowerCandidate(results, cardId);
        }

        private static void AddHeroPowerCandidate(ICollection<string> results, string cardId)
        {
            if (results == null || string.IsNullOrWhiteSpace(cardId))
                return;
            cardId = cardId.Trim();
            if (!IsHeroPowerCardId(cardId))
                return;
            if (results.Contains(cardId))
                return;
            results.Add(cardId);
        }

        private static bool IsHeroPowerCardId(string cardId)
        {
            return GetCardType(cardId) == CardType.HERO_POWER;
        }

        private static CardType GetCardType(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId))
                return CardType.INVALID;

            try
            {
                var dbCard = Cards.All.Values.FirstOrDefault(x => string.Equals(x.Id, cardId, StringComparison.OrdinalIgnoreCase));
                return dbCard?.Type ?? CardType.INVALID;
            }
            catch
            {
                return CardType.INVALID;
            }
        }

        private static IEnumerable<GameTag> GetHeroPowerReferenceTags()
        {
            yield return GameTag.HERO_POWER;
            yield return GameTag.HERO_POWER_ENTITY;

            foreach (var tag in Enum.GetValues(typeof(GameTag)).Cast<GameTag>().OrderBy(x => x.ToString(), StringComparer.Ordinal))
            {
                if (tag.ToString().StartsWith("ADDITIONAL_HERO_POWER_ENTITY_", StringComparison.OrdinalIgnoreCase))
                    yield return tag;
            }
        }

        private static bool SameCardIdSet(string[] left, string[] right)
        {
            return SameArray(
                (left ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
                (right ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
        }
        private void TryResolveRatingAfter(long nowTs)
        {
            if (nowTs < _nextRatingPollTs)
                return;
            _nextRatingPollTs = nowTs + MsToTicks(RatingPollMs);

            var elapsedMs = (long)((nowTs - _ratingResolveStartTs) * 1000.0 / Stopwatch.Frequency);
            if (elapsedMs > RatingResolveTimeoutMs)
            {
                HdtLog.Warn("[BGStats] èµ›åŽåˆ†æ•°èŽ·å–è¶…æ—¶ï¼Œåœæ­¢è½®è¯¢");
                _needResolveRatingAfter = false;
                return;
            }

            var afterA = TryGetBattlegroundsRatingAfterFromBaconChangeData();
            if (afterA.HasValue && afterA.Value > 0)
            {
                RatingAfter = afterA.Value;
                _needResolveRatingAfter = false;
                HdtLog.Info($"[BGStats] é€šè¿‡ BaconRatingChangeData æ‹¿åˆ° ratingAfter={RatingAfter}");
                return;
            }

            var afterC = TryGetBattlegroundsRatingAfterFromGameStats();
            if (afterC.HasValue && afterC.Value > 0)
            {
                RatingAfter = afterC.Value;
                _needResolveRatingAfter = false;
                HdtLog.Info($"[BGStats] é€šè¿‡ CurrentGameStats æ‹¿åˆ° ratingAfter={RatingAfter}");
                return;
            }

            var current = TryGetCurrentBattlegroundsRating();
            if (current.HasValue && current.Value > 0 && RatingBefore > 0 && current.Value != RatingBefore)
            {
                RatingAfter = current.Value;
                _needResolveRatingAfter = false;
                HdtLog.Info($"[BGStats] é€šè¿‡â€œåˆ†æ•°å˜åŒ–æ£€æµ‹â€æ‹¿åˆ° ratingAfter={RatingAfter} (before={RatingBefore})");
            }
        }

        private int? TryGetBattlegroundsRatingAfterFromBaconChangeData()
        {
            try
            {
                var asm = typeof(Core).Assembly;
                var tClient = asm.GetType("HearthMirror.Reflection.Client");
                var mi = tClient?.GetMethod("GetBaconRatingChangeData", BindingFlags.Public | BindingFlags.Static);
                var data = mi?.Invoke(null, null);
                var v = data?.GetType().GetProperty("NewRating", BindingFlags.Public | BindingFlags.Instance)?.GetValue(data, null);
                return v is int i ? i : (int?)null;
            }
            catch { return null; }
        }

        private int? TryGetBattlegroundsRatingAfterFromGameStats()
        {
            try
            {
                var after = Core.Game.CurrentGameStats?.BattlegroundsRatingAfter;
                if (after.HasValue && after.Value > 0)
                    return after.Value;
            }
            catch { }
            return null;
        }

        private int? TryGetCurrentBattlegroundsRating()
        {
            try
            {
                var game = Core.Game;
                if (game == null)
                    return null;

                var gt = game.GetType();
                var pCurrent = gt.GetProperty("CurrentBattlegroundsRating", BindingFlags.Public | BindingFlags.Instance);
                if (pCurrent != null)
                {
                    var v = pCurrent.GetValue(game, null);
                    if (v is int i)
                        return i;
                }

                var pInfo = gt.GetProperty("BattlegroundsRatingInfo", BindingFlags.Public | BindingFlags.Instance);
                if (pInfo != null)
                {
                    var info = pInfo.GetValue(game, null);
                    if (info != null)
                    {
                        var it = info.GetType();
                        var vv = game.IsBattlegroundsDuosMatch
                            ? it.GetProperty("DuosRating", BindingFlags.Public | BindingFlags.Instance)?.GetValue(info, null)
                            : it.GetProperty("Rating", BindingFlags.Public | BindingFlags.Instance)?.GetValue(info, null);
                        if (vv is int j)
                            return j;
                    }
                }

                var stats = gt.GetProperty("CurrentGameStats", BindingFlags.Public | BindingFlags.Instance)?.GetValue(game, null);
                if (stats != null)
                {
                    var st = stats.GetType();
                    var after = st.GetProperty("BattlegroundsRatingAfter", BindingFlags.Public | BindingFlags.Instance)?.GetValue(stats, null);
                    if (after is int a && a > 0)
                        return a;

                    var baseV = st.GetProperty("BattlegroundsRating", BindingFlags.Public | BindingFlags.Instance)?.GetValue(stats, null);
                    if (baseV is int b && b > 0)
                        return b;
                }
            }
            catch { }

            return null;
        }

        private HashSet<Race> TryGetAvailableRacesFromHdtUtils()
        {
            try
            {
                var asm = typeof(Core).Assembly;
                var t = asm.GetType("Hearthstone_Deck_Tracker.Hearthstone.BattlegroundsUtils");
                var mi = t?.GetMethod("GetAvailableRaces", BindingFlags.Public | BindingFlags.Static, null, global::System.Type.EmptyTypes, null);
                var val = mi?.Invoke(null, null);
                if (val is HashSet<Race> direct)
                    return direct;
                if (val is System.Collections.IEnumerable e)
                {
                    var result = new HashSet<Race>();
                    foreach (var item in e)
                    {
                        if (item is Race race)
                            result.Add(race);
                    }
                    return result;
                }
            }
            catch { }
            return null;
        }

        private static bool SameArray(int[] a, int[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }

        private static bool SameArray(string[] a, string[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++)
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                    return false;
            return true;
        }

    }
}





