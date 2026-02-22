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
using System.Text.RegularExpressions;
using System.Linq;
using System.Windows.Controls;
using HearthDb;

namespace HDTplugins
{
    public class Plugin : IPlugin
    {
        public string Name => "Hank的酒馆数据分析";
        public string Description => "统计酒馆战棋英雄选择与排名";
        public string Author => "Hank";
        public Version Version => new Version(0, 2, 0); // ✅ 改版本号，确保加载新DLL
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
        private string _currentHeroCardId = null;          // 本局英雄 CardId
        private string _currentHeroPowerCardId = null;     // 本局英雄技能 CardId
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
        private string _dataDir;
        private string _dataFilePath;

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

            Log.Info("[BGStats] 插件已加载（已订阅事件）");

            // 初始化数据目录（插件DLL同目录/Data）
            try
            {
                var pluginDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);

                _dataDir = Path.Combine(pluginDir, "Data");
                Directory.CreateDirectory(_dataDir);

                _dataFilePath = Path.Combine(_dataDir, "bg_stats.jsonl");

                Log.Info($"[BGStats] 数据文件路径：{_dataFilePath}");
            }
            catch (Exception ex)
            {
                Log.Error("[BGStats] 初始化数据目录失败: " + ex.Message);
            }
        }

        public void OnUnload()
        {
            // 你的 ActionList 没有 Remove，所以这里只关闭开关
            _enabled = false;
            Log.Info("[BGStats] 插件已卸载（已关闭开关）");
        }

        public void OnButtonPress()
        {
            Debug.WriteLine("[BGStats] 点击了设置按钮");
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
                    Log.Info("[BGStats] 已识别为酒馆战棋对局（检测条件满足），开始解析英雄/选将/名次数据");
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

            Log.Info("[BGStats] 对局开始：已重置状态，等待识别BG + 解析英雄/英雄技能/可选英雄信息");
        }

        private void OnGameEnd()
        {
            if (!_enabled) return;

            // ✅ 没识别成 BG 的对局，不处理
            if (!_bgDetected)
                return;

            Log.Info("[BGStats] 对局结束：进入等待名次状态");

            TryCachePlacement(); // ✅ 结算瞬间再抓一次，常常就在这一刻写入

            // 优先用缓存名次（最稳）
            if (_lastValidPlacement >= 1 && _lastValidPlacement <= 8)
            {
                Log.Info($"[BGStats] 本局结果：Hero={_currentHeroCardId ?? "null"}, HeroPower={_currentHeroPowerCardId ?? "null"}, 名次={_lastValidPlacement}（来自缓存）");
                _needResolvePlacement = false;
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

                Log.Info($"[BGStats] Offered heroes(dbfId) = [{string.Join(", ", heroes)}]");
            }
            catch (Exception ex)
            {
                Log.Error("[BGStats] TryResolveOfferedHeroes 失败: " + ex.Message);
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
                    _currentHeroCardId = resolvedHero;

                if (!string.IsNullOrEmpty(_currentHeroCardId))
                {
                    _needResolveHero = false;

                    var heroName = GetHeroName(_currentHeroCardId);

                    Log.Info($"[BGStats] 已解析英雄：Hero={heroName} ({_currentHeroCardId}), HeroPower={_currentHeroPowerCardId ?? "null"}");
                    return;
                }

                // 等待过久提示
                var elapsedMs = (long)((nowTs - _heroResolveStartTs) * 1000.0 / Stopwatch.Frequency);
                if (elapsedMs > HeroWarnAfterMs && elapsedMs < HeroWarnAfterMs + PollIntervalMs)
                {
                    Log.Warn("[BGStats] 已等待超过45秒仍未拿到真实英雄ID（可能重连/加载异常/一直占位）。将继续等待直到拿到。");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[BGStats] TryResolveHeroAndHeroPower 失败: " + ex.Message);
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
                    Log.Info($"[BGStats] 本局结果：Hero={_currentHeroCardId ?? "null"}, HeroPower={_currentHeroPowerCardId ?? "null"}, 名次={_lastValidPlacement}");
                    SaveMatchRecord();
                }
            }
            catch (Exception ex)
            {
                Log.Error("[BGStats] TryResolvePlacement 失败: " + ex.Message);
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
                Log.Error("[BGStats] TryCachePlacement 失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 将本局数据追加写入 jsonl 文件（兼容 .NET Framework，无需 Json 库）
        /// </summary>
        private void SaveMatchRecord()
        {
            try
            {
                if (string.IsNullOrEmpty(_dataFilePath))
                    return;

                var timestamp = DateTime.UtcNow.ToString("o"); // ISO8601

                // 获取游戏版本信息
                var gameVersion = TryGetHearthstoneVersionFromLogs();

                var heroId = _currentHeroCardId ?? "";
                var heroname = GetHeroName(heroId);

                //var offered = _lastOfferedHeroDbfIds != null && _lastOfferedHeroDbfIds.Length > 0
                //    ? string.Join(",", _lastOfferedHeroDbfIds)
                //    : "";

                var line =
                    "{"
                    + $"\"timestamp\":\"{timestamp}\","
                    + $"\"heroCardId\":\"{_currentHeroCardId ?? ""}\","
                    + $"\"heroName\":\"{JsonEscape(heroname)}\","
                    + $"\"placement\":{_lastValidPlacement},"
                    //+ $"\"offeredHeroes\":[{offered}]"
                    + $"\"gameVersion\":\"{JsonEscape(gameVersion)}\","
                    + "}";

                File.AppendAllText(_dataFilePath, line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Log.Error("[BGStats] 写入数据文件失败: " + ex.Message);
            }
        }

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

        private static bool SameArray(int[] a, int[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}