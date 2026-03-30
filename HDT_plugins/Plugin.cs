using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Plugins;
using System;
using System.Windows;
using System.Windows.Controls;
using HDTplugins.Services;
using HDTplugins.Views;

// 强制使用 HDT 的 Log，避免和你 Services/Log.cs 冲突
using HdtLog = Hearthstone_Deck_Tracker.Utility.Logging.Log;

namespace HDTplugins
{
    public class Plugin : IPlugin
    {
        public string Name => "Hank的酒馆数据分析";
        public string Description => "统计酒馆战棋英雄选择与排名";
        public string Author => "Hank";
        public Version Version => new Version(0, 4, 0);
        public string ButtonText => "酒馆数据分析";
        public MenuItem MenuItem => null;

        private bool _finalizedThisMatch = false;

        private bool _enabled;

        private BgGameProbe _probe;
        private StatsStore _store;
        private BgStatsWindow _statsWindow;

        public void OnLoad()
        {
            if (_enabled) return;
            _enabled = true;

            _store = new StatsStore();
            _store.Initialize();

            _probe = new BgGameProbe();

            GameEvents.OnGameStart.Add(OnGameStart);
            GameEvents.OnGameEnd.Add(OnGameEnd);

            HdtLog.Info("[Hank的log信息] 插件已加载（已订阅事件）");
        }

        public void OnUnload()
        {
            _enabled = false;
            TryCloseWindow();
            HdtLog.Info("[Hank的log信息] 插件已卸载（已关闭开关）");
        }

        public void OnButtonPress()
        {
            if (_statsWindow == null)
            {
                _statsWindow = new BgStatsWindow(_store.FinalFilePath);
                _statsWindow.Owner = Application.Current?.MainWindow;
                _statsWindow.OpenMatchDetailRequested += OnOpenMatchDetailRequested;
                _statsWindow.Closed += (_, __) => _statsWindow = null;
            }

            _statsWindow.Reload();
            _statsWindow.Show();
            _statsWindow.Activate();

            HdtLog.Info("[Hank的log信息] 打开了酒馆数据分析窗口");
        }

        public void OnUpdate()
        {
            if (!_enabled) return;

            _probe.Tick();

            if (!_probe.IsBattlegrounds)
                return;
            // 本局已 finalize，直接退出，防止重复写
            if (_finalizedThisMatch)
                return;

            // 第一次拿到英雄就写 pending
            if (_probe.HasResolvedHero && !_store.PendingWritten)
            {
                _store.WritePendingIfNeeded(
                    heroCardId: _probe.HeroCardId,
                    heroSkinCardId: _probe.HeroSkinCardId,
                    heroPowerCardId: _probe.HeroPowerCardId,
                    ratingBefore: _probe.RatingBefore
                );
            }

            // 名次 + 赛后分数都拿到后 finalize
            if (_store.PendingWritten
                && _probe.HasResolvedPlacement
                && _probe.HasResolvedRatingAfter)
            {
                _store.FinalizeIfPossible(
                    matchId: _store.CurrentMatchId,
                    timestamp: _store.CurrentMatchTimestampUtc,
                    heroCardId: _probe.HeroCardId,
                    heroSkinCardId: _probe.HeroSkinCardId,
                    heroPowerCardId: _probe.HeroPowerCardId,
                    placement: _probe.Placement,
                    ratingBefore: _probe.RatingBefore,
                    ratingAfter: _probe.RatingAfter,
                    availableRaces: _probe.AvailableRaceNames,
                    anomalyCardId: _probe.AnomalyCardId,
                    finalBoardCardIds: _probe.FinalBoardCardIds
                );

                _statsWindow?.Reload();

                // 标记本局完成 + 停止轮询
                _finalizedThisMatch = true;
                _probe.StopFinalizePolling();
            }
        }

        private void OnGameStart()
        {
            if (!_enabled) return;

            _finalizedThisMatch = false;
            _store.ResetMatch();
            _probe.OnGameStart();

            HdtLog.Info("[Hank的log信息] 对局开始：已重置状态，等待识别BG + 解析英雄/技能/可选英雄/名次/分数");
        }

        private void OnGameEnd()
        {
            if (!_enabled) return;

            _probe.OnGameEnd();

            if (_probe.IsBattlegrounds)
                HdtLog.Info("[Hank的log信息] 对局结束：进入等待名次 + 赛后分数状态");
        }

        private void OnOpenMatchDetailRequested(string matchId)
        {
            HdtLog.Info($"[BGStats] 请求打开对局详情（待开发）matchId={matchId}");
        }

        private void TryCloseWindow()
        {
            try
            {
                _statsWindow?.Close();
                _statsWindow = null;
            }
            catch { }
        }
    }
}
