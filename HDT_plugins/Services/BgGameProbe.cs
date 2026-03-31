using HDTplugins.Models;
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
                    HdtLog.Info("[BGStats] 已识别为酒馆战棋对局，开始解析数据");
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
                HdtLog.Error("[BGStats] TryResolveOfferedHeroes 失败: " + ex.Message);
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
                    HdtLog.Info($"[BGStats] 已解析英雄：Hero={HeroCardId}, HeroPower={HeroPowerCardId ?? "null"}");
                    if (RatingBefore <= 0)
                        RatingBefore = TryGetCurrentBattlegroundsRating() ?? -1;
                }
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] TryResolveHeroAndHeroPower 失败: " + ex.Message);
            }
        }

        private void TryRefreshHeroPower()
        {
            try
            {
                var playerEntity = Core.Game.PlayerEntity;
                if (playerEntity == null)
                    return;

                var heroPowerEntityId = playerEntity.GetTag(GameTag.HERO_POWER);
                if (heroPowerEntityId <= 0 || !Core.Game.Entities.ContainsKey(heroPowerEntityId))
                    return;

                var hpCardId = Core.Game.Entities[heroPowerEntityId]?.CardId;
                if (string.IsNullOrEmpty(hpCardId))
                    return;

                HeroPowerCardId = hpCardId;
                if (string.IsNullOrEmpty(InitialHeroPowerCardId))
                    InitialHeroPowerCardId = hpCardId;
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] TryRefreshHeroPower 失败: " + ex.Message);
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
                    HdtLog.Info($"[BGStats] 已解析名次 placement={Placement}");
                }
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] TryResolvePlacement 失败: " + ex.Message);
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
                HdtLog.Error("[BGStats] TryCachePlacement 失败: " + ex.Message);
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
                HdtLog.Error("[BGStats] TryCacheAvailableRaces 失败: " + ex.Message);
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
                HdtLog.Error("[BGStats] TryCacheAnomaly 失败: " + ex.Message);
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
                TryCacheFinalBoard();
                TryCacheTavernUpgrade(rawTurn);
            }
            catch (Exception ex)
            {
                HdtLog.Error("[BGStats] TryUpdateTurnScopedSnapshots 失败: " + ex.Message);
            }
        }

        private void TryCacheFinalBoard()
        {
            try
            {
                var board = Core.Game.Player?.Board;
                if (board == null || !board.Any())
                    return;

                var list = board
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
                HdtLog.Error("[BGStats] TryCacheFinalBoard 失败: " + ex.Message);
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
                HdtLog.Error("[BGStats] TryCacheTavernUpgrade 失败: " + ex.Message);
            }
        }

        private static int ConvertTurnToRound(int rawTurn)
        {
            return rawTurn <= 0 ? 0 : (rawTurn + 1) / 2;
        }

        private BgBoardMinionSnapshot BuildBoardMinionSnapshot(dynamic entity)
        {
            try
            {
                if (entity == null || string.IsNullOrEmpty((string)entity.CardId))
                    return null;

                var card = entity.Card;
                var race = card != null && card.Race != Race.INVALID ? card.Race.ToString() : string.Empty;
                return new BgBoardMinionSnapshot
                {
                    CardId = entity.CardId,
                    Name = card?.Name ?? entity.CardId,
                    IsGolden = GetNamedTagValue(entity, "PREMIUM") > 0,
                    Race = race,
                    Position = entity.ZonePosition,
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
                HdtLog.Error("[BGStats] BuildBoardMinionSnapshot 失败: " + ex.Message);
                return null;
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

        private void TryResolveRatingAfter(long nowTs)
        {
            if (nowTs < _nextRatingPollTs)
                return;
            _nextRatingPollTs = nowTs + MsToTicks(RatingPollMs);

            var elapsedMs = (long)((nowTs - _ratingResolveStartTs) * 1000.0 / Stopwatch.Frequency);
            if (elapsedMs > RatingResolveTimeoutMs)
            {
                HdtLog.Warn("[BGStats] 赛后分数获取超时，停止轮询");
                _needResolveRatingAfter = false;
                return;
            }

            var afterA = TryGetBattlegroundsRatingAfterFromBaconChangeData();
            if (afterA.HasValue && afterA.Value > 0)
            {
                RatingAfter = afterA.Value;
                _needResolveRatingAfter = false;
                HdtLog.Info($"[BGStats] 通过 BaconRatingChangeData 拿到 ratingAfter={RatingAfter}");
                return;
            }

            var afterC = TryGetBattlegroundsRatingAfterFromGameStats();
            if (afterC.HasValue && afterC.Value > 0)
            {
                RatingAfter = afterC.Value;
                _needResolveRatingAfter = false;
                HdtLog.Info($"[BGStats] 通过 CurrentGameStats 拿到 ratingAfter={RatingAfter}");
                return;
            }

            var current = TryGetCurrentBattlegroundsRating();
            if (current.HasValue && current.Value > 0 && RatingBefore > 0 && current.Value != RatingBefore)
            {
                RatingAfter = current.Value;
                _needResolveRatingAfter = false;
                HdtLog.Info($"[BGStats] 通过“分数变化检测”拿到 ratingAfter={RatingAfter} (before={RatingBefore})");
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
