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

using HdtLog = Hearthstone_Deck_Tracker.Utility.Logging.Log;

namespace HDTplugins.Services
{
    public class BgGameProbe
    {
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

        private const int RatingResolveTimeoutMs = 45_000;
        private const int RatingPollMs = 500;

        private static long MsToTicks(int ms)
            => (long)(Stopwatch.Frequency * (ms / 1000.0));

        public bool IsBattlegrounds => _bgDetected;
        public string HeroCardId { get; private set; }
        public string HeroSkinCardId { get; private set; }
        public string InitialHeroPowerCardId { get; private set; }
        public string HeroPowerCardId { get; private set; }
        public int[] OfferedHeroDbfIds { get; private set; } = Array.Empty<int>();
        public int Placement { get; private set; }
        public bool HasResolvedHero => !string.IsNullOrEmpty(HeroCardId);
        public bool HasResolvedPlacement => Placement > 0;
        public int RatingBefore { get; private set; } = -1;
        public int RatingAfter { get; private set; } = -1;
        public string[] AvailableRaceNames { get; private set; } = Array.Empty<string>();
        public int AnomalyDbfId { get; private set; }
        public string AnomalyCardId { get; private set; }
        public List<BgBoardMinionSnapshot> FinalBoard { get; private set; } = new List<BgBoardMinionSnapshot>();
        public List<BgTavernUpgradePoint> TavernUpgradeTimeline { get; private set; } = new List<BgTavernUpgradePoint>();
        public bool HasResolvedRatingAfter => RatingAfter > 0 && !_needResolveRatingAfter;

        public void OnGameStart()
        {
            _bgDetected = false;
            HeroCardId = null;
            HeroSkinCardId = null;
            InitialHeroPowerCardId = null;
            HeroPowerCardId = null;
            Placement = 0;
            OfferedHeroDbfIds = Array.Empty<int>();
            _needResolveHero = true;
            _needResolvePlacement = false;
            _needResolveOfferedHeroes = true;
            RatingBefore = -1;
            RatingAfter = -1;
            _needResolveRatingAfter = false;
            _nextRatingPollTs = 0;
            AvailableRaceNames = Array.Empty<string>();
            AnomalyDbfId = 0;
            AnomalyCardId = null;
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

            TryCachePlacement();
            TryUpdateTurnScopedSnapshots();
            _needResolvePlacement = true;
            _needResolveRatingAfter = true;
            _ratingResolveStartTs = Stopwatch.GetTimestamp();
            _nextRatingPollTs = 0;
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

            if (_needResolveOfferedHeroes)
                TryResolveOfferedHeroes(nowTs);
            if (_needResolveHero)
                TryResolveHeroAndHeroPower();
            else
                TryRefreshHeroPower();
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

        private void TryResolveOfferedHeroes(long nowTs)
        {
            try
            {
                var heroes = Core.Game.Player?.PlayerEntities?
                    .Where(e => e != null && e.IsHero && (e.HasTag(GameTag.BACON_HERO_CAN_BE_DRAFTED) || e.HasTag(GameTag.BACON_SKIN)) && !e.HasTag(GameTag.BACON_LOCKED_MULLIGAN_HERO) && e.Card != null)
                    .OrderBy(e => e.ZonePosition)
                    .Select(e => e.Card.DbfId)
                    .Where(id => id > 0)
                    .ToArray();

                if (heroes == null || heroes.Length == 0 || SameArray(heroes, OfferedHeroDbfIds))
                    return;

                OfferedHeroDbfIds = heroes;
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
                    HeroCardId = NormalizeBgHeroId(heroA);
                }

                if (!string.IsNullOrEmpty(HeroCardId))
                {
                    _needResolveHero = false;
                    HdtLog.Info($"[BGStats] å·²è§£æžè‹±é›„ï¼šHero={HeroCardId}, HeroPower={HeroPowerCardId ?? "null"}");
                    if (RatingBefore <= 0)
                        RatingBefore = TryGetCurrentBattlegroundsRating() ?? -1;
                }
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] TryResolveHeroAndHeroPower å¤±è´¥: " + ex.Message);
            }
        }

        private void TryRefreshHeroPower()
        {
            try
            {
                var previousHeroPower = HeroPowerCardId;
                var hpCardId = ResolveHeroPowerCardId(out var source);
                if (string.IsNullOrEmpty(hpCardId))
                {
                    HdtLog.Info("[BGStats][HeroPower] æœ¬æ¬¡æœªè¯»å–åˆ°è‹±é›„æŠ€èƒ½");
                    return;
                }

                HeroPowerCardId = hpCardId;
                if (string.IsNullOrEmpty(InitialHeroPowerCardId))
                {
                    InitialHeroPowerCardId = hpCardId;
                    HdtLog.Info($"[BGStats][HeroPower] è®°å½•åˆå§‹è‹±é›„æŠ€èƒ½: {InitialHeroPowerCardId} (source={source})");
                }

                if (!string.Equals(previousHeroPower, HeroPowerCardId, StringComparison.OrdinalIgnoreCase))
                    HdtLog.Info($"[BGStats][HeroPower] è‹±é›„æŠ€èƒ½æ›´æ–°: {previousHeroPower ?? "null"} -> {HeroPowerCardId} (source={source})");
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] TryRefreshHeroPower å¤±è´¥: " + ex.Message);
            }
        }

        private string ResolveHeroPowerCardId(out string source)
        {
            var fromPlayerHeroPower = GetHeroPowerFromPlayerView();
            if (!string.IsNullOrEmpty(fromPlayerHeroPower))
            {
                source = "Player.HeroPower";
                return fromPlayerHeroPower;
            }

            var fromPlayerEntity = GetHeroPowerFromPlayerEntity();
            if (!string.IsNullOrEmpty(fromPlayerEntity))
            {
                source = "PlayerEntity.HERO_POWER";
                return fromPlayerEntity;
            }

            var fromHeroEntity = GetHeroPowerFromHeroEntity();
            if (!string.IsNullOrEmpty(fromHeroEntity))
            {
                source = "HeroEntity.HERO_POWER";
                return fromHeroEntity;
            }

            source = "unresolved";
            return null;
        }

        private string GetHeroPowerFromPlayerView()
        {
            try
            {
                var heroPower = GetPropertyValue(Core.Game.Player, "HeroPower");
                var heroPowerCardId = GetStringProperty(heroPower, "CardId");
                if (!string.IsNullOrEmpty(heroPowerCardId))
                    return heroPowerCardId;

                var card = GetPropertyValue(heroPower, "Card");
                return GetStringProperty(card, "Id");
            }
            catch
            {
                return null;
            }
        }

        private string GetHeroPowerFromPlayerEntity()
        {
            try
            {
                var playerEntity = Core.Game.PlayerEntity;
                if (playerEntity == null)
                    return null;

                var heroPowerEntityId = playerEntity.GetTag(GameTag.HERO_POWER);
                if (heroPowerEntityId <= 0 || !Core.Game.Entities.ContainsKey(heroPowerEntityId))
                    return null;

                var heroPowerEntity = Core.Game.Entities[heroPowerEntityId];
                var hpCardId = GetStringProperty(heroPowerEntity, "CardId");
                if (!string.IsNullOrEmpty(hpCardId))
                    return hpCardId;

                var card = GetPropertyValue(heroPowerEntity, "Card");
                return GetStringProperty(card, "Id");
            }
            catch
            {
                return null;
            }
        }

        private string GetHeroPowerFromHeroEntity()
        {
            try
            {
                var playerEntity = Core.Game.PlayerEntity;
                if (playerEntity == null)
                    return null;

                var heroEntityId = playerEntity.GetTag(GameTag.HERO_ENTITY);
                if (heroEntityId <= 0 || !Core.Game.Entities.ContainsKey(heroEntityId))
                    return null;

                var heroEntity = Core.Game.Entities[heroEntityId];
                var heroPowerEntityId = 0;
                try
                {
                    heroPowerEntityId = heroEntity.GetTag(GameTag.HERO_POWER);
                }
                catch { }

                if (heroPowerEntityId <= 0 || !Core.Game.Entities.ContainsKey(heroPowerEntityId))
                    return null;

                var heroPowerEntity = Core.Game.Entities[heroPowerEntityId];
                var hpCardId = GetStringProperty(heroPowerEntity, "CardId");
                if (!string.IsNullOrEmpty(hpCardId))
                    return hpCardId;

                var card = GetPropertyValue(heroPowerEntity, "Card");
                return GetStringProperty(card, "Id");
            }
            catch
            {
                return null;
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
            TryCacheFinalBoard();
            _combatSnapshotCapturedThisFight = FinalBoard.Count > 0 || !string.IsNullOrWhiteSpace(HeroPowerCardId);
            HdtLog.Info($"[BGStats] å·²åœ¨è¿›å…¥æˆ˜æ–—å‰è®°å½•å½“å‰é˜µå®¹ä¸Žè‹±é›„æŠ€èƒ½ï¼Œstep={_lastObservedStep}, boardCount={FinalBoard.Count}, heroPower={HeroPowerCardId ?? "null"}");
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

                var prop = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                return prop?.GetValue(target, null);
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

        private string NormalizeBgHeroId(string heroCardId)
        {
            if (string.IsNullOrEmpty(heroCardId))
                return heroCardId;

            try
            {
                var asm = typeof(Core).Assembly;
                var t = asm.GetType("Hearthstone_Deck_Tracker.Hearthstone.BattlegroundsUtils");
                var mi = t?.GetMethod("GetOriginalHeroId", new[] { typeof(string) });
                var res = mi?.Invoke(null, new object[] { heroCardId }) as string;
                if (!string.IsNullOrEmpty(res))
                    return res;
            }
            catch { }

            var idx = heroCardId.IndexOf("_SKIN_", StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
                return heroCardId.Substring(0, idx);
            idx = heroCardId.IndexOf("_ALT_", StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
                return heroCardId.Substring(0, idx);
            return heroCardId;
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





