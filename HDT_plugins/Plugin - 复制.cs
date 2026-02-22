//using HearthDb.Enums;
//using Hearthstone_Deck_Tracker.API;
//using Hearthstone_Deck_Tracker.Enums.Hearthstone;
//using Hearthstone_Deck_Tracker.Plugins;
//using Hearthstone_Deck_Tracker.Utility.Logging;
//using Hearthstone_Deck_Tracker.Hearthstone; // ✅ 用于 BattlegroundsUtils.GetOriginalHeroId（如果你项目引用里有）
//using System;
//using System.Diagnostics;
//using System.Linq;
//using System.Windows.Controls;

//namespace HDTplugins
//{
//    public class Plugin : IPlugin
//    {
//        public string Name => "Hank的酒馆数据分析";
//        public string Description => "统计酒馆战棋英雄选择与排名";
//        public string Author => "Hank";
//        public Version Version => new Version(0, 1, 10);
//        public string ButtonText => "设置";
//        public MenuItem MenuItem => null;

//        private bool _enabled = false;

//        // =========================
//        // 轮询节流
//        // =========================
//        private int _nextPollTick = 0;
//        private const int PollIntervalMs = 250;

//        // =========================
//        // 本局关键数据缓存
//        // =========================
//        private string _currentHeroCardId = null;          // 本局最终英雄 CardId（可能是皮肤ID）
//        private string _currentHeroOriginalCardId = null;  // 本局英雄“原始ID”（去皮肤）
//        private string _currentHeroPowerCardId = null;     // 本局英雄技能 CardId
//        private int _lastValidPlacement = 0;               // 本局名次（实时缓存）

//        // offered heroes（选英雄界面给你的候选英雄 dbfId）
//        private int[] _lastOfferedHeroDbfIds = Array.Empty<int>();
//        private int _nextOfferedLogTick = 0;
//        private const int OfferedLogThrottleMs = 800;

//        // =========================
//        // 状态机：哪些数据还没解析到
//        // =========================
//        private bool _needResolveHero = false;
//        private bool _needResolveOfferedHeroes = false;
//        private bool _needResolvePlacement = false;

//        // =========================
//        // 关键：缓存己方 PlayerId（避免 GameEnd 后 Core.Game.Player.Id 变动/清空）
//        // =========================
//        private int _friendlyPlayerId = -1;

//        // “选英雄/加载中”占位英雄
//        private const string PlaceholderHeroCardId = "TB_BaconShop_HERO_PH";

//        // 英雄解析等待窗口（避免你说的 30-40s 拿不到就结束）
//        private int _heroResolveStartTick = 0;
//        private const int HeroWarnAfterMs = 45_000;

//        // 排名解析 debug 节流（避免刷屏）
//        private int _nextPlacementDbgTick = 0;
//        private const int PlacementDbgIntervalMs = 1500;

//        // ==========================================================
//        // BG 对局识别：选英雄阶段 IsBattlegroundsMatch 可能还是 false
//        // 所以用 Mode.BACON / GameType 更稳
//        // ==========================================================
//        private static bool IsBgContext()
//        {
//            return Core.Game.CurrentMode == Mode.BACON
//                || Core.Game.IsBattlegroundsMatch
//                || Core.Game.CurrentGameType == GameType.GT_BATTLEGROUNDS
//                || Core.Game.CurrentGameType == GameType.GT_BATTLEGROUNDS_FRIENDLY
//                || Core.Game.CurrentGameType == GameType.GT_BATTLEGROUNDS_DUO
//                || Core.Game.CurrentGameType == GameType.GT_BATTLEGROUNDS_DUO_FRIENDLY;
//        }

//        public void OnLoad()
//        {
//            if (_enabled) return;
//            _enabled = true;

//            GameEvents.OnGameStart.Add(OnGameStart);
//            GameEvents.OnGameEnd.Add(OnGameEnd);

//            Log.Info("[BGStats] 插件已加载（已订阅事件）");
//        }

//        public void OnUnload()
//        {
//            _enabled = false;

//            // ✅ 建议卸载时取消订阅（热重载时避免重复触发）
//            GameEvents.OnGameStart.Remove(OnGameStart);
//            GameEvents.OnGameEnd.Remove(OnGameEnd);

//            Log.Info("[BGStats] 插件已卸载（已取消订阅事件）");
//        }

//        public void OnButtonPress()
//        {
//            Debug.WriteLine("[BGStats] 点击了设置按钮");
//        }

//        public void OnUpdate()
//        {
//            if (!_enabled) return;

//            // 轮询节流：每 250ms 执行一次
//            var now = Environment.TickCount;
//            if (now < _nextPollTick) return;
//            _nextPollTick = now + PollIntervalMs;

//            if (!IsBgContext())
//                return;

//            // =========================
//            // 1) 尽早缓存 friendlyPlayerId
//            //    这个值后面用于 IsControlledBy() 找到“己方英雄实体”
//            // =========================
//            if (_friendlyPlayerId <= 0)
//            {
//                var pid = Core.Game.Player?.Id ?? -1;
//                if (pid > 0)
//                    _friendlyPlayerId = pid;
//            }

//            // =========================
//            // 2) 实时缓存排名（关键修复：从“己方英雄实体”读 place）
//            //    ——这一步做到了，即使 GameEnd 后 reset，也能靠缓存拿到名次
//            // =========================
//            CachePlacementFromHeroEntity();

//            // =========================
//            // 3) 解析 offered heroes（选将候选列表）
//            // =========================
//            if (_needResolveOfferedHeroes)
//                TryResolveOfferedHeroes(now);

//            // =========================
//            // 4) 解析最终英雄 + 英雄技能
//            // =========================
//            if (_needResolveHero)
//                TryResolveHeroAndHeroPower(now);

//            // =========================
//            // 5) GameEnd 后如果仍没缓存到名次，继续追
//            // =========================
//            if (_needResolvePlacement)
//                TryResolvePlacement(now);
//        }

//        private void OnGameStart()
//        {
//            if (!_enabled) return;
//            if (!IsBgContext()) return;

//            // 对局开始：重置本局数据
//            _currentHeroCardId = null;
//            _currentHeroOriginalCardId = null;
//            _currentHeroPowerCardId = null;
//            _lastValidPlacement = 0;
//            _lastOfferedHeroDbfIds = Array.Empty<int>();
//            _friendlyPlayerId = -1;

//            _needResolveOfferedHeroes = true;
//            _needResolveHero = true;
//            _needResolvePlacement = false;

//            _heroResolveStartTick = Environment.TickCount;
//            _nextOfferedLogTick = 0;
//            _nextPlacementDbgTick = 0;

//            Log.Info("[BGStats] 对局开始：已重置状态，等待解析 英雄/英雄技能/可选英雄/名次");
//        }

//        private void OnGameEnd()
//        {
//            if (!_enabled) return;
//            if (!IsBgContext()) return;

//            Log.Info("[BGStats] 对局结束：进入等待名次状态");

//            // ✅ 结束瞬间再尝试缓存一次（有时 place 会在最后一刻写入）
//            CachePlacementFromHeroEntity();

//            // 如果缓存已经有名次了，直接输出结果
//            if (_lastValidPlacement >= 1 && _lastValidPlacement <= 8)
//            {
//                Log.Info($"[BGStats] 本局结果：Hero={_currentHeroOriginalCardId ?? _currentHeroCardId ?? "null"}, " +
//                         $"HeroPower={_currentHeroPowerCardId ?? "null"}, 名次={_lastValidPlacement}（来自缓存）");
//                _needResolvePlacement = false;
//                return;
//            }

//            // 缓存没拿到就继续轮询追名次
//            _needResolvePlacement = true;
//        }

//        /// <summary>
//        /// ✅ 关键修复：排名缓存不再依赖 PlayerEntity
//        /// 而是仿照 HDT 本体逻辑，从 Entities 里找到“己方英雄实体”，读取 PLAYER_LEADERBOARD_PLACE。
//        /// </summary>
//        private void CachePlacementFromHeroEntity()
//        {
//            try
//            {
//                var pid = _friendlyPlayerId > 0 ? _friendlyPlayerId : (Core.Game.Player?.Id ?? -1);
//                if (pid <= 0)
//                    return;

//                // 在所有 entity 里找：带名次 tag 的英雄，并且是我方控制的
//                var heroEntity = Core.Game.Entities.Values
//                    .FirstOrDefault(e => e != null
//                        && e.IsHero
//                        && e.HasTag(GameTag.PLAYER_LEADERBOARD_PLACE)
//                        && e.IsControlledBy(pid));

//                if (heroEntity == null)
//                    return;

//                var duos = Core.Game.IsBattlegroundsDuosMatch;
//                var raw = heroEntity.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE);
//                var placement = Math.Min(raw, duos ? 4 : 8);

//                if (placement >= 1 && placement <= (duos ? 4 : 8))
//                    _lastValidPlacement = placement;
//            }
//            catch (Exception ex)
//            {
//                Log.Error("[BGStats] CachePlacementFromHeroEntity 失败: " + ex.Message);
//            }
//        }

//        /// <summary>
//        /// 解析“选英雄界面给你的候选英雄”（offered heroes）
//        /// </summary>
//        private void TryResolveOfferedHeroes(int nowTick)
//        {
//            try
//            {
//                var heroes = Core.Game.Player?.PlayerEntities?
//                    .Where(e => e != null
//                                && e.IsHero
//                                && (e.HasTag(GameTag.BACON_HERO_CAN_BE_DRAFTED) || e.HasTag(GameTag.BACON_SKIN))
//                                && !e.HasTag(GameTag.BACON_LOCKED_MULLIGAN_HERO)
//                                && e.Card != null)
//                    .OrderBy(e => e.ZonePosition)
//                    .Select(e => e.Card.DbfId)
//                    .Where(id => id > 0)
//                    .ToArray();

//                if (heroes == null || heroes.Length == 0)
//                    return;

//                // 列表没变就不重复输出
//                if (SameArray(heroes, _lastOfferedHeroDbfIds))
//                    return;

//                _lastOfferedHeroDbfIds = heroes;

//                // 节流：避免刷屏
//                if (nowTick < _nextOfferedLogTick)
//                    return;
//                _nextOfferedLogTick = nowTick + OfferedLogThrottleMs;

//                Log.Info($"[BGStats] Offered heroes(dbfId) = [{string.Join(", ", heroes)}]");
//            }
//            catch (Exception ex)
//            {
//                Log.Error("[BGStats] TryResolveOfferedHeroes 失败: " + ex.Message);
//            }
//        }

//        /// <summary>
//        /// 解析最终选择的英雄 + 英雄技能
//        /// </summary>
//        private void TryResolveHeroAndHeroPower(int nowTick)
//        {
//            try
//            {
//                var playerEntity = Core.Game.PlayerEntity;

//                // 先解析英雄技能（很多时候比英雄更早稳定）
//                if (playerEntity != null)
//                {
//                    var heroPowerEntityId = playerEntity.GetTag(GameTag.HERO_POWER);
//                    if (heroPowerEntityId > 0 && Core.Game.Entities.TryGetValue(heroPowerEntityId, out var hpEntity))
//                    {
//                        var hpCardId = hpEntity?.CardId;
//                        if (!string.IsNullOrEmpty(hpCardId))
//                            _currentHeroPowerCardId = hpCardId;
//                    }
//                }

//                // A) PlayerEntity[HERO_ENTITY] -> entity.CardId（最准确）
//                string heroA = null;
//                if (playerEntity != null)
//                {
//                    var heroEntityId = playerEntity.GetTag(GameTag.HERO_ENTITY);
//                    if (heroEntityId > 0 && Core.Game.Entities.TryGetValue(heroEntityId, out var heroEntity))
//                    {
//                        var cid = heroEntity?.CardId;
//                        if (!string.IsNullOrEmpty(cid) && cid != PlaceholderHeroCardId)
//                            heroA = cid;
//                    }
//                }

//                // B) Core.Game.Player.Hero?.CardId（很多时候更快）
//                string heroB = null;
//                var heroCidB = Core.Game.Player?.Hero?.CardId;
//                if (!string.IsNullOrEmpty(heroCidB) && heroCidB != PlaceholderHeroCardId)
//                    heroB = heroCidB;

//                var resolved = heroB ?? heroA;
//                if (!string.IsNullOrEmpty(resolved))
//                {
//                    _currentHeroCardId = resolved;

//                    // ✅ 去皮肤（如果你的 HDT 引用里有 BattlegroundsUtils）
//                    try
//                    {
//                        _currentHeroOriginalCardId = BattlegroundsUtils.GetOriginalHeroId(resolved);
//                    }
//                    catch
//                    {
//                        _currentHeroOriginalCardId = resolved;
//                    }
//                }

//                if (!string.IsNullOrEmpty(_currentHeroCardId))
//                {
//                    _needResolveHero = false;
//                    Log.Info($"[BGStats] 已解析英雄：Hero={_currentHeroOriginalCardId ?? _currentHeroCardId}, HeroPower={_currentHeroPowerCardId ?? "null"}");
//                    return;
//                }

//                // 超过 45s 还没拿到：warn 一次但继续等
//                var elapsed = unchecked(nowTick - _heroResolveStartTick);
//                if (elapsed > HeroWarnAfterMs && elapsed < HeroWarnAfterMs + PollIntervalMs)
//                    Log.Warn("[BGStats] 已等待超过45秒仍未拿到真实英雄ID（可能重连/加载异常/一直占位）。将继续等待直到拿到。");
//            }
//            catch (Exception ex)
//            {
//                Log.Error("[BGStats] TryResolveHeroAndHeroPower 失败: " + ex.Message);
//            }
//        }

//        /// <summary>
//        /// GameEnd 后继续追名次：
//        /// - 每次先用 CachePlacementFromHeroEntity() 尝试更新缓存
//        /// - 如果仍然没有，就每 1.5s 输出一次 debug（你就知道到底有没有 heroEntity / place tag）
//        /// </summary>
//        private void TryResolvePlacement(int nowTick)
//        {
//            try
//            {
//                CachePlacementFromHeroEntity();

//                // 成功了就输出并关闭追踪
//                if (_lastValidPlacement >= 1 && _lastValidPlacement <= 8)
//                {
//                    _needResolvePlacement = false;
//                    Log.Info($"[BGStats] 本局结果：Hero={_currentHeroOriginalCardId ?? _currentHeroCardId ?? "null"}, " +
//                             $"HeroPower={_currentHeroPowerCardId ?? "null"}, 名次={_lastValidPlacement}");
//                    return;
//                }

//                // 还没成功：节流输出 debug，方便你定位
//                if (nowTick >= _nextPlacementDbgTick)
//                {
//                    _nextPlacementDbgTick = nowTick + PlacementDbgIntervalMs;

//                    var pid = _friendlyPlayerId > 0 ? _friendlyPlayerId : (Core.Game.Player?.Id ?? -1);
//                    var heroEntity = (pid > 0)
//                        ? Core.Game.Entities.Values.FirstOrDefault(e => e != null
//                            && e.IsHero
//                            && e.HasTag(GameTag.PLAYER_LEADERBOARD_PLACE)
//                            && e.IsControlledBy(pid))
//                        : null;

//                    var raw = heroEntity?.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE) ?? 0;

//                    Log.Info($"[BGStats][PlaceDBG] still waiting... pid={pid}, heroEntity={(heroEntity != null ? "ok" : "null")}, rawPlace={raw}, cached={_lastValidPlacement}");
//                }
//            }
//            catch (Exception ex)
//            {
//                Log.Error("[BGStats] TryResolvePlacement 失败: " + ex.Message);
//            }
//        }

//        private static bool SameArray(int[] a, int[] b)
//        {
//            if (ReferenceEquals(a, b)) return true;
//            if (a == null || b == null) return false;
//            if (a.Length != b.Length) return false;
//            for (int i = 0; i < a.Length; i++)
//                if (a[i] != b[i]) return false;
//            return true;
//        }
//    }
//}