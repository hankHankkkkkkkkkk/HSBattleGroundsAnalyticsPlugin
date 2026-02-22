using HearthDb;
using HearthDb.Enums;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Enums.Hearthstone;
using Hearthstone_Deck_Tracker.Hearthstone;
using Hearthstone_Deck_Tracker.Plugins;
using Hearthstone_Deck_Tracker.Utility;
using Hearthstone_Deck_Tracker.Utility.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection; 
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Controls;

namespace HDTplugins
{
    public class Plugin : IPlugin
    {
        public string Name => "Hank的酒馆数据分析";
        public string Description => "统计酒馆战棋英雄选择与排名";
        public string Author => "Hank";
        public Version Version => new Version(0, 2, 7); // ✅ 改版本号，确保加载新DLL
        public string ButtonText => "设置";
        public MenuItem MenuItem => null;

        private bool _enabled = false;

        // =========================
        // 0) BG 识别状态（关键）
        // =========================
        private bool _bgDetected = false;

        // =========================
        // 1) 使用 Stopwatch 做节流（彻底避免 TickCount 溢出导致 OnUpdate 永远 return）
        // =========================
        private const int PollIntervalMs = 250;         // 主循环 250ms
        private const int DetectDbgIntervalMs = 2000;   // 侦测日志 2s
        private const int OfferedLogThrottleMs = 800;   // offered heroes 日志节流 0.8s

        private long _nextPollTs = 0;
        private long _nextDetectDbgTs = 0;
        private long _nextOfferedLogTs = 0;

        private static long MsToTicks(int ms)
            => (long)(Stopwatch.Frequency * (ms / 1000.0));

        // =========================
        // 2) 本局关键数据缓存
        // =========================
        private string _currentHeroCardId = null;

        // 本局英雄 CardId
        private string _currentHeroPowerCardId = null;

        //含英雄皮肤英雄ID
        private string _currentHeroSkinCardId = null;

        private int _lastValidPlacement = 0;               // 本局名次（实时缓存）
        private int[] _lastOfferedHeroDbfIds = Array.Empty<int>(); // offered heroes（dbfId）

        // =========================
        // 3) 状态机：哪些数据还没解析到，需要继续轮询
        // =========================
        private bool _needResolveHero = false;
        private bool _needResolvePlacement = false;
        private bool _needResolveOfferedHeroes = false;

        // =========================
        // 4) 等待英雄超时提示
        // =========================
        private long _heroResolveStartTs = 0;
        private const int HeroWarnAfterMs = 45_000;

        // =========================
        // 5) 数据落盘相关
        // =========================
        // =========================
        // 两阶段写入：pending -> final
        // =========================
        private string _dataDir;
        private string _dataFilePath;

        private string _finalFilePath;     // Data/bg_stats.jsonl
        private string _pendingFilePath;   // Data/bg_stats_pending.jsonl

        // =========================
        // BG 分数（MMR/Rating）相关
        // =========================
        private int _ratingBefore = -1;  // 本局开始/选定英雄后记录的分数
        private int _ratingAfter = -1;   // 赛后记录的分数

        private bool _needResolveRatingAfter = false;
        private long _ratingResolveStartTs = 0;
        private const int RatingResolveTimeoutMs = 45_000;
        private const int RatingPollMs = 500;
        private long _nextRatingPollTs = 0;


        private string _currentMatchId = null;  // 本局唯一ID（用来 finalize）
        private bool _wrotePending = false;     // 防止重复写 pending

        private int _nextPlacementDbgTick = 0;      // 可选：调试节流
        private const int PlacementDbgIntervalMs = 1500;


        // BG 占位英雄
        private const string PlaceholderHeroCardId = "TB_BaconShop_HERO_PH";

        public void OnLoad()
        {
            if (_enabled) return;
            _enabled = true;

            GameEvents.OnGameStart.Add(OnGameStart);
            GameEvents.OnGameEnd.Add(OnGameEnd);

            Log.Info("[Hank的log信息] 插件已加载（已订阅事件）");

            // 初始化数据目录（插件DLL同目录/Data）

            try
            {
                // 旧目录：安装目录\Plugins\Data（你现在用的）
                var oldDir = _dataDir; // 如果你之前没赋值，可直接按下面方式算一次
                var oldHdtDir = GetHdtInstallDir();
                if (!string.IsNullOrEmpty(oldHdtDir))
                    oldDir = Path.Combine(oldHdtDir, "Plugins", "Data");

                var oldFinal = Path.Combine(oldDir ?? "", "bg_stats.jsonl");
                var oldPending = Path.Combine(oldDir ?? "", "bg_stats_pending.jsonl");

                // ✅ 新目录：%LocalAppData%\HDT_BGStats\Data
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                _dataDir = Path.Combine(local, "HDT_BGStats", "Data");
                Directory.CreateDirectory(_dataDir);

                _finalFilePath = Path.Combine(_dataDir, "bg_stats.jsonl");
                _pendingFilePath = Path.Combine(_dataDir, "bg_stats_pending.jsonl");

                // （你已注释 SaveMatchRecord，但保留兼容不坏事）
                _dataFilePath = _finalFilePath;

                // ✅ 启动先备份（防任何意外）
                BackupFileIfExists(_finalFilePath);

                // ✅ 迁移：如果新文件不存在/为空，而旧文件存在且有内容，把旧文件搬过来
                MigrateIfNeeded(oldFinal, _finalFilePath);
                MigrateIfNeeded(oldPending, _pendingFilePath);

                Log.Info($"[Hank的log信息] 数据目录(AppData Local)：{_dataDir}");
                Log.Info($"[Hank的log信息] final：{_finalFilePath}");
                Log.Info($"[Hank的log信息] pending：{_pendingFilePath}");
            }
            catch (Exception ex)
            {
                Log.Error("[Hank的log信息] 初始化数据目录失败: " + ex.Message);
            }
        }

        public void OnUnload()
        {
            // 你的 ActionList 没有 Remove，所以这里只关闭开关
            _enabled = false;
            Log.Info("[Hank的log信息] 插件已卸载（已关闭开关）");
        }

        public void OnButtonPress()
        {
            Debug.WriteLine("[Hank的log信息] 点击了设置按钮");
        }

        public void OnUpdate()
        {
            if (!_enabled) return;

            // =========================
            // ✅ 主循环节流（Stopwatch 版本，不会被 TickCount 溢出卡死）
            // =========================
            var nowTs = Stopwatch.GetTimestamp();
            if (nowTs < _nextPollTs) return;
            _nextPollTs = nowTs + MsToTicks(PollIntervalMs);

            if (_needResolveRatingAfter)
                TryResolveRatingAfter(nowTs);
            // =========================
            // ✅ 每 2 秒必定输出一次侦测信息（即使 BG 未识别也输出）
            // 目的：确认 OnUpdate 真的在跑 + Core.Game 的关键字段是否为空
            // =========================
            //if (nowTs >= _nextDetectDbgTs)
            //{
            //    _nextDetectDbgTs = nowTs + MsToTicks(DetectDbgIntervalMs);

            //    var heroCid = Core.Game.Player?.Hero?.CardId ?? "null";
            //    var peState = Core.Game.PlayerEntity == null ? "null" : "ok";
            //    var entityCount = Core.Game.Entities?.Count ?? 0;

            //    int baconEntityCount = 0;
            //    try
            //    {
            //        baconEntityCount = Core.Game.Entities?.Values.Count(e =>
            //            e != null && !string.IsNullOrEmpty(e.CardId) &&
            //            (e.CardId.IndexOf("Bacon", StringComparison.OrdinalIgnoreCase) >= 0
            //             || e.CardId == PlaceholderHeroCardId)
            //        ) ?? 0;
            //    }
            //    catch { /* ignore */ }

            //    Log.Info($"[BGStats][DetectDBG] bgDetected={_bgDetected}, Mode={Core.Game.CurrentMode}, GameType={Core.Game.CurrentGameType}, PlayerHero={heroCid}, PlayerEntity={peState}, Entities={entityCount}, BaconEntities={baconEntityCount}");
            //}

            // =========================
            // ✅ BG 识别：没识别出来就尝试识别
            // 识别失败则 return，避免影响其他模式
            // =========================
            if (!_bgDetected)
            {
                if (TryDetectBattlegrounds())
                {
                    _bgDetected = true;
                    Log.Info("[Hank的log信息] 已识别为酒馆战棋对局（检测条件满足），开始解析英雄/选将/名次数据");
                }
                else
                {
                    return;
                }
            }

            // =========================
            // 实时缓存名次（只要出现过 1..8 就记下来）
            // =========================
            TryCachePlacement();

            // 解析 offered heroes（选英雄界面给你的 2/4 个英雄）
            if (_needResolveOfferedHeroes)
                TryResolveOfferedHeroes(nowTs);

            // 解析英雄 + 英雄技能
            if (_needResolveHero)
                TryResolveHeroAndHeroPower(nowTs);

            // GameEnd 后解析名次（如果没缓存到）
            if (_needResolvePlacement)
                TryResolvePlacement();
        }

        private void OnGameStart()
        {
            if (!_enabled) return;

            // ✅ 不要在这里判断 IsBattlegroundsMatch/Mode.BACON
            // 因为你日志里 BG 也会显示 GAMEPLAY，且 IsBattlegroundsMatch 可能很晚才 true
            _bgDetected = false;

            _currentHeroCardId = null;
            _currentHeroPowerCardId = null;
            _lastValidPlacement = 0;
            _lastOfferedHeroDbfIds = Array.Empty<int>();

            _needResolveHero = true;
            _needResolvePlacement = false;
            _needResolveOfferedHeroes = true;

            _heroResolveStartTs = Stopwatch.GetTimestamp();

            _currentMatchId = null;
            _wrotePending = false;

            _ratingBefore = -1;
            _ratingAfter = -1;

            Log.Info("[Hank的log信息] 对局开始：已重置状态，等待识别BG + 解析英雄/英雄技能/可选英雄信息");
        }

        private void OnGameEnd()
        {
            if (!_enabled) return;

            // ✅ 没识别成 BG 的对局，不处理
            if (!_bgDetected)
                return;

            Log.Info("[Hank的log信息] 对局结束：进入等待名次状态");

            TryCachePlacement(); // ✅ 结算瞬间再抓一次，常常就在这一刻写入
            _needResolveRatingAfter = true;    // ✅ 开始赛后抓分数
            _ratingResolveStartTs = Stopwatch.GetTimestamp();
            _nextRatingPollTs = 0;

            // 优先用缓存名次（最稳）
            if (_lastValidPlacement >= 1 && _lastValidPlacement <= 8)
            {
                var delta = 0;
                if (_ratingBefore > 0 && _ratingAfter > 0)
                    delta = _ratingAfter - _ratingBefore;

                Log.Info(
                    $"[Hank的log信息] 本局结果：英雄={_currentHeroCardId ?? "null"}, " +
                    $"HeroPower={_currentHeroPowerCardId ?? "null"}, " +
                    $"名次={_lastValidPlacement}（来自缓存），" +
                    $"开始前分数={_ratingBefore}, 开始后分数={_ratingAfter}, 分数变化={delta}"
                );

                // 结束时只开启“等待名次+等待分数”，不要写文件
                _needResolvePlacement = true;
                _needResolveRatingAfter = true;              // 你新增的赛后分数轮询开关
                _ratingResolveStartTs = Stopwatch.GetTimestamp();
                _nextRatingPollTs = 0;

                return;
            }

            _needResolvePlacement = true;
        }

        /// <summary>
        /// ✅ 识别是否为酒馆战棋（兼容不同HDT版本）
        /// 命中任意一个特征即认为是 BG：
        /// 1) CurrentGameType.ToString() 包含 "BATTLEGROUNDS" / "BACON"
        /// 2) 玩家英雄 CardId 包含 "Bacon"
        /// 3) Entities 里出现 CardId 含 "Bacon" 或占位 TB_BaconShop_HERO_PH
        /// 4) 选英雄阶段：PlayerEntities 中存在 BACON_HERO_CAN_BE_DRAFTED / BACON_SKIN tag
        /// </summary>
        private bool TryDetectBattlegrounds()
        {
            try
            {
                var gt = Core.Game.CurrentGameType.ToString();
                if (gt.IndexOf("BATTLEGROUNDS", StringComparison.OrdinalIgnoreCase) >= 0
                    || gt.IndexOf("BACON", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                var heroCardId = Core.Game.Player?.Hero?.CardId;
                if (!string.IsNullOrEmpty(heroCardId) &&
                    heroCardId.IndexOf("Bacon", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                var hasBaconEntity = Core.Game.Entities?.Values.Any(e =>
                    e != null && !string.IsNullOrEmpty(e.CardId) &&
                    (e.CardId.IndexOf("Bacon", StringComparison.OrdinalIgnoreCase) >= 0
                     || e.CardId == PlaceholderHeroCardId)
                ) ?? false;
                if (hasBaconEntity)
                    return true;

                var anyOffered = Core.Game.Player?.PlayerEntities?.Any(e =>
                    e != null && e.IsHero &&
                    (e.HasTag(GameTag.BACON_HERO_CAN_BE_DRAFTED) || e.HasTag(GameTag.BACON_SKIN))
                ) ?? false;
                if (anyOffered)
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 解析 offered heroes（选英雄界面给你的2/4个英雄）
        /// </summary>
        private void TryResolveOfferedHeroes(long nowTs)
        {
            try
            {
                var heroes = Core.Game.Player?.PlayerEntities?
                    .Where(e => e != null
                                && e.IsHero
                                && (e.HasTag(GameTag.BACON_HERO_CAN_BE_DRAFTED) || e.HasTag(GameTag.BACON_SKIN))
                                && !e.HasTag(GameTag.BACON_LOCKED_MULLIGAN_HERO)
                                && e.Card != null)
                    .OrderBy(e => e.ZonePosition)
                    .Select(e => e.Card.DbfId)
                    .Where(id => id > 0)
                    .ToArray();

                if (heroes == null || heroes.Length == 0)
                    return;

                if (SameArray(heroes, _lastOfferedHeroDbfIds))
                    return;

                _lastOfferedHeroDbfIds = heroes;

                // offered heroes 日志节流
                if (nowTs < _nextOfferedLogTs)
                    return;
                _nextOfferedLogTs = nowTs + MsToTicks(OfferedLogThrottleMs);

                Log.Info($"[Hank的log信息] Offered heroes(dbfId) = [{string.Join(", ", heroes)}]");
            }
            catch (Exception ex)
            {
                Log.Error("[Hank的log信息] TryResolveOfferedHeroes 失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 解析英雄 + 英雄技能
        /// A) PlayerEntity[HERO_ENTITY] -> entity.CardId（最准确）
        /// B) Core.Game.Player.Hero?.CardId（很多时候更早可用）
        /// </summary>
        private void TryResolveHeroAndHeroPower(long nowTs)
        {
            try
            {
                var playerEntity = Core.Game.PlayerEntity;

                // 先解析英雄技能
                if (playerEntity != null)
                {
                    var heroPowerEntityId = playerEntity.GetTag(GameTag.HERO_POWER);
                    if (heroPowerEntityId > 0 && Core.Game.Entities.ContainsKey(heroPowerEntityId))
                    {
                        var hpEntity = Core.Game.Entities[heroPowerEntityId];
                        var hpCardId = hpEntity?.CardId;
                        if (!string.IsNullOrEmpty(hpCardId))
                            _currentHeroPowerCardId = hpCardId;
                    }
                }

                // A) HERO_ENTITY
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

                // B) Player.Hero
                string heroB = null;
                var heroCidB = Core.Game.Player?.Hero?.CardId;
                if (!string.IsNullOrEmpty(heroCidB) && heroCidB != PlaceholderHeroCardId)
                    heroB = heroCidB;

                var resolvedHero = heroB ?? heroA;
                if (!string.IsNullOrEmpty(resolvedHero))
                    _currentHeroSkinCardId = resolvedHero;
                // ✅ 统一归一化，避免皮肤 ID 污染统计
                _currentHeroCardId = NormalizeBgHeroId(resolvedHero);

                if (!string.IsNullOrEmpty(_currentHeroCardId))
                {
                    _needResolveHero = false;
                    Log.Info($"[Hank的log信息] 已解析英雄：Hero={_currentHeroCardId}, HeroPower={_currentHeroPowerCardId ?? "null"}");

                    // ✅ 第一次拿到真实英雄时，先写 pending（不含名次）
                    WritePendingRecordIfNeeded();

                    return;
                }

                //// 等待过久提示
                //var elapsedMs = (long)((nowTs - _heroResolveStartTs) * 1000.0 / Stopwatch.Frequency);
                //if (elapsedMs > HeroWarnAfterMs && elapsedMs < HeroWarnAfterMs + PollIntervalMs)
                //{
                //    Log.Warn("[Hank的log信息] 已等待超过45秒仍未拿到真实英雄ID（可能重连/加载异常/一直占位）。将继续等待直到拿到。");
                //}
            }
            catch (Exception ex)
            {
                Log.Error("[Hank的log信息] TryResolveHeroAndHeroPower 失败: " + ex.Message);
            }
        }

        /// <summary>
        /// GameEnd 后继续轮询名次（使用 TryCachePlacement 的最稳路径）
        /// </summary>
        private void TryResolvePlacement()
        {
            try
            {
                // ✅ 继续尝试抓名次
                TryCachePlacement();

                // ✅ 如果缓存已经有效，输出并停止轮询
                var duos = Core.Game.IsBattlegroundsDuosMatch;
                var maxPlace = duos ? 4 : 8;

                if (_lastValidPlacement >= 1 && _lastValidPlacement <= maxPlace)
                {
                    _needResolvePlacement = false;
                    Log.Info($"[Hank的log信息] 本局结果：Hero={_currentHeroCardId ?? "null"}, HeroPower={_currentHeroPowerCardId ?? "null"}, 名次={_lastValidPlacement}");
                    //SaveMatchRecord();
                }
            }
            catch (Exception ex)
            {
                Log.Error("[Hank的log信息] TryResolvePlacement 失败: " + ex.Message);
            }
        }

        /// <summary>
        /// ✅ 尝试缓存名次（最稳路径）：
        /// 1) PlayerEntity -> HERO_ENTITY -> heroEntity -> PLAYER_LEADERBOARD_PLACE
        /// 2) 兜底：直接从 PlayerEntity 读 PLAYER_LEADERBOARD_PLACE
        /// </summary>
        private void TryCachePlacement()
        {
            try
            {
                // 酒馆双打是 1..4，普通是 1..8
                var duos = Core.Game.IsBattlegroundsDuosMatch;
                var maxPlace = duos ? 4 : 8;

                var playerEntity = Core.Game.PlayerEntity;
                if (playerEntity == null)
                    return;

                // ① 最稳：通过 HERO_ENTITY 定位到“你自己的英雄实体”
                var heroEntityId = playerEntity.GetTag(GameTag.HERO_ENTITY);
                if (heroEntityId > 0 && Core.Game.Entities.TryGetValue(heroEntityId, out var heroEntity) && heroEntity != null)
                {
                    var raw = heroEntity.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE);
                    var placement = Math.Min(raw, maxPlace);
                    if (placement >= 1 && placement <= maxPlace)
                    {
                        _lastValidPlacement = placement;
                        return;
                    }
                }

                // ② 兜底：有些情况下 place 会挂在 PlayerEntity 上
                var raw2 = playerEntity.GetTag(GameTag.PLAYER_LEADERBOARD_PLACE);
                var placement2 = Math.Min(raw2, maxPlace);
                if (placement2 >= 1 && placement2 <= maxPlace)
                {
                    _lastValidPlacement = placement2;
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Error("[Hank的log信息] TryCachePlacement 失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 将本局数据追加写入 jsonl 文件（兼容 .NET Framework，无需 Json 库）
        /// </summary>
        //private void SaveMatchRecord()
        //{
        //    try
        //    {
        //        if (string.IsNullOrEmpty(_dataFilePath))
        //            return;

        //        Log.Info("[Hank的log信息] SaveMatchRecord 被调用");

        //        var timestamp = DateTime.UtcNow.ToString("o"); // ISO8601

        //        // 获取游戏版本信息
        //        var gameVersion = TryGetHearthstoneVersionFromLogs();

        //        var heroId = _currentHeroCardId ?? "";
        //        var heroname = GetHeroName(heroId);


        ////var offered = _lastOfferedHeroDbfIds != null && _lastOfferedHeroDbfIds.Length > 0
        ////    ? string.Join(",", _lastOfferedHeroDbfIds)
        ////    : "";

        //var line =
        //            "{"
        //            + $"\"timestamp\":\"{timestamp}\","
        //            + $"\"heroCardId\":\"{_currentHeroCardId ?? ""}\","
        //            + $"\"heroName\":\"{JsonEscape(heroname)}\","
        //            + $"\"placement\":{_lastValidPlacement},"
        //            //+ $"\"offeredHeroes\":[{offered}]"
        //            + $"\"gameVersion\":\"{JsonEscape(gameVersion)}\","
        //            + $"\"heroSkinCardId\":\"{JsonEscape(_currentHeroSkinCardId ?? "")}\","
        //            + "}";

        //        File.AppendAllText(_dataFilePath, line + Environment.NewLine);
        //    }
        //    catch (Exception ex)
        //    {
        //        Log.Error("[Hank的log信息] 写入数据文件失败: " + ex.Message);
        //    }
        //}

        /// <summary>
        /// CardId -> 名字（优先 LocalizedName，其次 Name）
        /// </summary>
        private string GetHeroName(string cardId)
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

        /// <summary>
        /// 将 BG 英雄皮肤 CardId 归一化为“原始英雄 CardId”
        /// 目标：统计时不要把同一英雄的不同皮肤当成不同英雄。
        /// </summary>
        private string NormalizeBgHeroId(string heroCardId)
        {
            if (string.IsNullOrEmpty(heroCardId))
                return heroCardId;

            // 1) 优先使用 HDT 内置工具（如果你的版本有）
            //    用反射避免“没有这个类/方法”导致编译失败
            try
            {
                var asm = typeof(Hearthstone_Deck_Tracker.Core).Assembly;

                // 常见命名空间/类名：Hearthstone_Deck_Tracker.Hearthstone.BattlegroundsUtils
                var t = asm.GetType("Hearthstone_Deck_Tracker.Hearthstone.BattlegroundsUtils");
                if (t != null)
                {
                    var mi = t.GetMethod("GetOriginalHeroId", new[] { typeof(string) });
                    if (mi != null)
                    {
                        var res = mi.Invoke(null, new object[] { heroCardId }) as string;
                        if (!string.IsNullOrEmpty(res))
                            return res;
                    }
                }
            }
            catch
            {
                // ignore，走兜底
            }

            // 2) 兜底：按字符串规则去皮肤后缀
            //    常见：TB_BaconShop_HERO_34_SKIN_01 / ..._SKIN_123
            //    也可能出现 ALT / HEROIC 等变体（不同版本可能不一样）
            var s = heroCardId;

            // 优先去掉 _SKIN_ 后面的所有内容
            var idx = s.IndexOf("_SKIN_", StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
                return s.Substring(0, idx);

            // 其它可能的后缀（不一定都会遇到，安全兜底）
            idx = s.IndexOf("_ALT_", StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
                return s.Substring(0, idx);

            return s;
        }

        /// <summary>
        /// JSON 字符串转义：保证写出的 json 永远合法
        /// </summary>
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

        /// <summary>
        /// 从炉石日志中解析客户端版本号（例如 30.4.0.205184）
        /// 不依赖 HDT 内部 API，兼容性最好。
        /// </summary>
        private string TryGetHearthstoneVersionFromLogs()
        {
            try
            {
                // HDT 通常把炉石日志目录暴露在 Config 里；如果你这里拿不到，也没关系，会走 fallback
                var logDir = Hearthstone_Deck_Tracker.Config.Instance?.HearthstoneDirectory;
                if (string.IsNullOrEmpty(logDir) || !Directory.Exists(logDir))
                    return "";

                // 常见日志文件（不同机器/版本可能不一样）
                var candidates = new[]
                {
            Path.Combine(logDir, "Logs", "Hearthstone.log"),
            Path.Combine(logDir, "Logs", "Power.log"),
            Path.Combine(logDir, "Logs", "Client.log"),
        };

                // 匹配类似 30.4.0.205184 这样的版本号
                var re = new Regex(@"\b(\d{1,2}\.\d{1,2}\.\d{1,2}\.\d{4,7})\b", RegexOptions.Compiled);

                foreach (var path in candidates)
                {
                    if (!File.Exists(path))
                        continue;

                    // 只读末尾一小段，速度快（避免读几百MB）
                    var tail = ReadFileTail(path, 20000);
                    if (string.IsNullOrEmpty(tail))
                        continue;

                    var m = re.Match(tail);
                    if (m.Success)
                        return m.Groups[1].Value;
                }

                return "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 读取文件末尾 N 字符（用于快速从大日志里找版本号）
        /// </summary>
        private static string ReadFileTail(string path, int maxChars)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length <= 0)
                        return "";

                    // 估算：UTF-8 可能 1~4 bytes/char，这里用 bytes 近似即可
                    var bytesToRead = (int)Math.Min(fs.Length, maxChars * 2L);
                    fs.Seek(-bytesToRead, SeekOrigin.End);

                    var buffer = new byte[bytesToRead];
                    var read = fs.Read(buffer, 0, bytesToRead);

                    return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
                }
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 第一次识别到真实英雄时，写入 pending 记录（不含名次）。
        /// 这样拔线/掉线也能保住英雄数据。
        /// </summary>
        private void WritePendingRecordIfNeeded()
        {
            try
            {
                if (_wrotePending) return;
                if (string.IsNullOrEmpty(_pendingFilePath)) return;
                if (string.IsNullOrEmpty(_currentHeroCardId)) return;

                // 生成本局 matchId（够用且唯一）
                _currentMatchId = Guid.NewGuid().ToString("N");
                _wrotePending = true;

                _ratingBefore = TryGetCurrentBattlegroundsRating() ?? -1;
                var ratingBefore = _ratingBefore;

                var timestamp = DateTime.UtcNow.ToString("o");
                var heroId = _currentHeroCardId ?? "";
                var heroName = GetHeroName(heroId);
                var gameVersion = ""; // 你之前版本号解析一直不稳定，这里先留空，后面我们再补

                // placement 用 -1 表示“未知/未完成”
                var line =
                    "{"
                    + $"\"matchId\":\"{JsonEscape(_currentMatchId)}\","
                    + $"\"timestamp\":\"{JsonEscape(timestamp)}\","
                    + $"\"gameVersion\":\"{JsonEscape(gameVersion)}\","
                    + $"\"heroCardId\":\"{JsonEscape(heroId)}\","
                    + $"\"heroName\":\"{JsonEscape(heroName)}\","
                    + $"\"ratingBefore\":{ratingBefore},"
                    + $"\"ratingAfter\":-1,"
                    + $"\"ratingDelta\":0,"
                    + $"\"placement\":-1"
                    + "}";

                File.AppendAllText(_pendingFilePath, line + Environment.NewLine, Encoding.UTF8);

                Log.Info($"[BGStats] 已写入 pending 记录 matchId={_currentMatchId}");
            }
            catch (Exception ex)
            {
                Log.Error("[BGStats] 写入 pending 失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 当名次确定时：
        /// - 从 pending 中找到 matchId 对应记录
        /// - 用最终 placement 改写那条记录并追加到 final
        /// - 同时从 pending 移除该条
        /// </summary>
        private void FinalizeRecordIfPossible()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentMatchId)) return;
                if (string.IsNullOrEmpty(_pendingFilePath) || string.IsNullOrEmpty(_finalFilePath)) return;

                var duos = Core.Game.IsBattlegroundsDuosMatch;
                var maxPlace = duos ? 4 : 8;
                if (!(_lastValidPlacement >= 1 && _lastValidPlacement <= maxPlace))
                    return; // 还没名次就不 finalize

                if (!File.Exists(_pendingFilePath))
                    return;

                var lines = File.ReadAllLines(_pendingFilePath, Encoding.UTF8);
                if (lines.Length == 0)
                    return;

                string target = null;
                var kept = new System.Collections.Generic.List<string>(lines.Length);

                foreach (var l in lines)
                {
                    if (string.IsNullOrWhiteSpace(l))
                        continue;

                    // 简单包含匹配：够用且快（matchId 是 GUID，不会误伤）
                    if (target == null && l.Contains($"\"matchId\":\"{_currentMatchId}\""))
                    {
                        target = l;
                    }
                    else
                    {
                        kept.Add(l);
                    }
                }

                if (target == null)
                    return;

                // 把 placement 从 -1 替换成最终名次（只替换第一次出现）
                var finalized = ReplaceFirst(target, "\"placement\":-1", $"\"placement\":{_lastValidPlacement}");

                //// 赛后抓一次当前分数（更稳：你说的“赛后读当前分数”）
                //_ratingAfter = TryGetCurrentBattlegroundsRating() ?? -1;

                // ratingBefore 如果本局没抓到，就用 -1
                var before = _ratingBefore;
                var after = _ratingAfter;
                var delta = (before >= 0 && after >= 0) ? (after - before) : 0;


                // 先替换名次
                finalized = ReplaceFirst(finalized, "\"placement\":-1", $"\"placement\":{_lastValidPlacement}");

                // 再替换分数
                finalized = ReplaceFirst(finalized, "\"ratingAfter\":-1", $"\"ratingAfter\":{after}");
                finalized = ReplaceFirst(finalized, "\"ratingDelta\":0", $"\"ratingDelta\":{delta}");

                // （可选）如果你希望即使 before 没写到，也在这里补一下：
                finalized = ReplaceFirst(finalized, $"\"ratingBefore\":{before}", $"\"ratingBefore\":{before}");

                // 追加到 final
                File.AppendAllText(_finalFilePath, finalized + Environment.NewLine, Encoding.UTF8);

                // 重写 pending（删掉已 finalize 的那条）
                File.WriteAllLines(_pendingFilePath, kept.ToArray(), Encoding.UTF8);

                Log.Info($"[BGStats] 已 finalize matchId={_currentMatchId}, placement={_lastValidPlacement}");
            }
            catch (Exception ex)
            {
                Log.Error("[BGStats] Finalize 失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 只替换第一次出现的位置
        /// </summary>
        private static string ReplaceFirst(string text, string search, string replace)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(search))
                return text;

            var idx = text.IndexOf(search, StringComparison.Ordinal);
            if (idx < 0) return text;

            return text.Substring(0, idx) + replace + text.Substring(idx + search.Length);
        }

        /// <summary>
        /// 尝试获取当前酒馆分数（MMR/Rating）
        /// - 兼容不同 HDT 版本：用反射读取 Core.Game 的属性
        /// - 返回 null 表示当前拿不到（比如还没加载完成/对局外）
        /// </summary>
        private int? TryGetCurrentBattlegroundsRating()
        {
            try
            {
                var game = Core.Game;
                if (game == null)
                    return null;

                var gt = game.GetType();

                // ① 新版常见：Core.Game.CurrentBattlegroundsRating : int?
                var pCurrent = gt.GetProperty("CurrentBattlegroundsRating", BindingFlags.Public | BindingFlags.Instance);
                if (pCurrent != null)
                {
                    var v = pCurrent.GetValue(game, null);
                    if (v is int i) return i;
                    if (v is int ni) return ni;
                }

                // ② 另一条：Core.Game.BattlegroundsRatingInfo 里有 Rating / DuosRating
                var pInfo = gt.GetProperty("BattlegroundsRatingInfo", BindingFlags.Public | BindingFlags.Instance);
                if (pInfo != null)
                {
                    var info = pInfo.GetValue(game, null);
                    if (info != null)
                    {
                        var it = info.GetType();
                        var isDuos = game.IsBattlegroundsDuosMatch;

                        var pDuos = it.GetProperty("DuosRating", BindingFlags.Public | BindingFlags.Instance);
                        var pSolo = it.GetProperty("Rating", BindingFlags.Public | BindingFlags.Instance);

                        var vv = isDuos ? pDuos?.GetValue(info, null) : pSolo?.GetValue(info, null);
                        if (vv is int j) return j;
                        if (vv is int nj) return nj;
                    }
                }

                // ③ 兜底：CurrentGameStats 里可能有 BattlegroundsRating / BattlegroundsRatingAfter
                var pStats = gt.GetProperty("CurrentGameStats", BindingFlags.Public | BindingFlags.Instance);
                var stats = pStats?.GetValue(game, null);
                if (stats != null)
                {
                    var st = stats.GetType();

                    // 赛后更可能写入 After；对局中可能只有 Rating
                    var pAfter = st.GetProperty("BattlegroundsRatingAfter", BindingFlags.Public | BindingFlags.Instance);
                    var pBase = st.GetProperty("BattlegroundsRating", BindingFlags.Public | BindingFlags.Instance);

                    var after = pAfter?.GetValue(stats, null);
                    if (after is int a && a > 0)
                        return a;

                    var baseV = pBase?.GetValue(stats, null);
                    if (baseV is int b && b > 0)
                        return b;
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        // 获取安装路径
        private static string GetHdtInstallDir()
        {
            try
            {
                // 通常最准确：拿到当前进程的 exe 路径（就是 HDT.exe）
                var exePath = Process.GetCurrentProcess().MainModule.FileName;
                if (!string.IsNullOrEmpty(exePath))
                    return Path.GetDirectoryName(exePath);
            }
            catch { /* 某些权限/环境可能抛异常 */ }

            try
            {
                // 兜底：AppDomain 的 BaseDirectory 通常也是 exe 目录
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(baseDir))
                    return baseDir.TrimEnd('\\');
            }
            catch { }

            return null;
        }

        //获取分数
        private void TryResolveRatingAfter(long nowTs)
        {
            // 节流
            if (nowTs < _nextRatingPollTs) return;
            _nextRatingPollTs = nowTs + MsToTicks(RatingPollMs);

            // 超时退出（避免永远轮询）
            var elapsedMs = (long)((nowTs - _ratingResolveStartTs) * 1000.0 / Stopwatch.Frequency);
            if (elapsedMs > RatingResolveTimeoutMs)
            {
                Log.Warn("[BGStats] 赛后分数获取超时，停止轮询");
                _needResolveRatingAfter = false;
                return;
            }

            // ====== 方式 A：直接拿结算变化数据（最稳）======
            // HearthMirror.Reflection.Client.GetBaconRatingChangeData()
            // 注意：为了兼容不同版本，用反射调用
            var afterA = TryGetBattlegroundsRatingAfterFromBaconChangeData();
            if (afterA.HasValue && afterA.Value > 0)
            {
                _ratingAfter = afterA.Value;
                _needResolveRatingAfter = false;
                Log.Info($"[BGStats] 通过 BaconRatingChangeData 拿到 ratingAfter={_ratingAfter}");
                TryFinalizeIfReady();
                return;
            }

            // ====== 方式 C：HDT 已写入的 CurrentGameStats.BattlegroundsRatingAfter ======
            var afterC = TryGetBattlegroundsRatingAfterFromGameStats();
            if (afterC.HasValue && afterC.Value > 0)
            {
                _ratingAfter = afterC.Value;
                _needResolveRatingAfter = false;
                Log.Info($"[BGStats] 通过 CurrentGameStats 拿到 ratingAfter={_ratingAfter}");
                TryFinalizeIfReady();
                return;
            }

            // ====== 方式 B：持续读当前分数直到变化（兜底）======
            var current = TryGetCurrentBattlegroundsRating();
            if (current.HasValue && current.Value > 0 && _ratingBefore > 0 && current.Value != _ratingBefore)
            {
                _ratingAfter = current.Value;
                _needResolveRatingAfter = false;
                Log.Info($"[BGStats] 通过“分数变化检测”拿到 ratingAfter={_ratingAfter} (before={_ratingBefore})");
                TryFinalizeIfReady();
                return;
            }
        }

        private int? TryGetBattlegroundsRatingAfterFromBaconChangeData()
        {
            try
            {
                var asm = typeof(Hearthstone_Deck_Tracker.Core).Assembly;

                // HearthMirror.Reflection.Client
                var tClient = asm.GetType("HearthMirror.Reflection.Client");
                if (tClient == null) return null;

                var mi = tClient.GetMethod("GetBaconRatingChangeData", BindingFlags.Public | BindingFlags.Static);
                if (mi == null) return null;

                var data = mi.Invoke(null, null);
                if (data == null) return null;

                // data.NewRating
                var pNew = data.GetType().GetProperty("NewRating", BindingFlags.Public | BindingFlags.Instance);
                var v = pNew?.GetValue(data, null);
                if (v is int i) return i;

                return null;
            }
            catch
            {
                return null;
            }
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

        private void TryFinalizeIfReady()
        {
            var duos = Core.Game.IsBattlegroundsDuosMatch;
            var maxPlace = duos ? 4 : 8;

            if (!(_lastValidPlacement >= 1 && _lastValidPlacement <= maxPlace))
                return;

            if (_ratingAfter <= 0)
                return;

            // 可选：你想“直到变化再写”，就保留这句
            if (_ratingBefore > 0 && _ratingAfter == _ratingBefore)
                return;

            FinalizeRecordIfPossible();

            // finalize 成功后把轮询关掉（避免重复）
            _needResolvePlacement = false;
            _needResolveRatingAfter = false;
        }

        private static bool SameArray(int[] a, int[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
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

                // 只有目标不存在或为空才迁移，避免重复复制
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

    }
}