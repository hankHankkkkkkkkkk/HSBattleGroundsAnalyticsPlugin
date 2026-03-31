using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Plugins;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HDTplugins.Services;
using HDTplugins.Views;

using HdtLog = Hearthstone_Deck_Tracker.Utility.Logging.Log;

namespace HDTplugins
{
    public class Plugin : IPlugin
    {
        public string Name => "Hank的酒馆数据分析";
        public string Description => "统计酒馆战棋英雄选择与排名";
        public string Author => "Hank";
        public Version Version => new Version(0, 7, 3);
        public string ButtonText => "设置";
        public MenuItem MenuItem => _menuItem;

        private bool _finalizedThisMatch;
        private bool _enabled;
        private BgGameProbe _probe;
        private StatsStore _store;
        private PluginSettingsService _settingsService;
        private BgStatsWindow _statsWindow;
        private SettingsView _settingsView;
        private MenuItem _menuItem;

        public void OnLoad()
        {
            if (_enabled) return;
            _enabled = true;

            _store = new StatsStore();
            _store.Initialize();
            _settingsService = new PluginSettingsService();
            _settingsService.Initialize(_store.TablesDirectoryPath);
            _probe = new BgGameProbe();
            _menuItem = CreateQuickOpenMenuItem();

            GameEvents.OnGameStart.Add(OnGameStart);
            GameEvents.OnGameEnd.Add(OnGameEnd);

            if (_settingsService.Settings.AutoOpenOnStartup)
                ShowWindowAsync();
            HdtLog.Info("[Hank的log信息] 插件已加载（已订阅事件，GUI 自动启动）");
        }

        public void OnUnload()
        {
            _enabled = false;
            TryCloseWindow();
            HdtLog.Info("[Hank的log信息] 插件已卸载（已关闭开关）");
        }

        public void OnButtonPress()
        {
            ShowSettingsWindow();
        }

        public void OnUpdate()
        {
            if (!_enabled) return;

            _probe.Tick();
            if (!_probe.IsBattlegrounds || _finalizedThisMatch)
                return;

            if (_probe.HasResolvedHero && !_store.PendingWritten)
            {
                _store.WritePendingIfNeeded(_probe.HeroCardId, _probe.HeroSkinCardId, _probe.InitialHeroPowerCardId, _probe.RatingBefore);
            }

            if (_store.PendingWritten && _probe.HasResolvedPlacement && _probe.HasResolvedRatingAfter)
            {
                _store.FinalizeIfPossible(
                    _store.CurrentMatchId,
                    _store.CurrentMatchTimestampUtc,
                    _probe.HeroCardId,
                    _probe.HeroSkinCardId,
                    _probe.InitialHeroPowerCardId,
                    _probe.HeroPowerCardId,
                    _probe.Placement,
                    _probe.RatingBefore,
                    _probe.RatingAfter,
                    _probe.AvailableRaceNames,
                    _probe.AnomalyCardId,
                    _probe.FinalBoard,
                    _probe.TavernUpgradeTimeline);
                _statsWindow?.Reload();
                _finalizedThisMatch = true;
                _probe.StopFinalizePolling();
            }
        }

        private void OnGameStart()
        {
            if (!_enabled) return;

            _finalizedThisMatch = false;
            _store.ResetMatch();
            var archive = _store.ConfirmCurrentArchiveForMatch();
            _probe.OnGameStart();
            _statsWindow?.SyncVersionSelection(archive?.Key);
        }

        private void OnGameEnd()
        {
            if (!_enabled) return;
            _probe.OnGameEnd();
        }

        private void OnOpenMatchDetailRequested(string matchId)
        {
            HdtLog.Info($"[BGStats] 请求打开对局详情 matchId={matchId}");
        }

        private void ShowWindowAsync()
        {
            Application.Current?.Dispatcher?.BeginInvoke(DispatcherPriority.Background, new Action(ShowWindow));
        }

        private void ShowWindow()
        {
            if (_statsWindow == null)
            {
                _statsWindow = new BgStatsWindow(_store);
                _statsWindow.OpenMatchDetailRequested += OnOpenMatchDetailRequested;
                _statsWindow.Closed += (_, __) => _statsWindow = null;
            }

            TryAttachOwner();

            _statsWindow.SyncVersionSelection(_store.CurrentArchive?.Key);
            _statsWindow.Show();
            _statsWindow.Activate();
        }

        private MenuItem CreateQuickOpenMenuItem()
        {
            var item = new MenuItem
            {
                Header = "酒馆数据分析"
            };
            item.Click += delegate { ShowWindow(); };
            return item;
        }

        private void ShowSettingsWindow()
        {
            if (_settingsService == null)
                return;

            _settingsService.Reload();
            if (_settingsView == null)
            {
                _settingsView = new SettingsView(_settingsService);
                _settingsView.Closed += delegate { _settingsView = null; };
            }

            try
            {
                var owner = Application.Current?.MainWindow;
                if (owner != null && !ReferenceEquals(owner, _settingsView))
                    _settingsView.Owner = owner;
            }
            catch { }

            _settingsView.ShowDialog();
        }

        private void TryAttachOwner()
        {
            try
            {
                if (_statsWindow == null)
                    return;
                if (_statsWindow.IsVisible)
                    return;

                var owner = Application.Current?.MainWindow;
                if (owner == null || ReferenceEquals(owner, _statsWindow))
                    return;
                if (!owner.IsLoaded || !owner.IsVisible)
                    return;

                _statsWindow.Owner = owner;
            }
            catch (Exception ex)
            {
                HdtLog.Warn("[BGStats] 绑定 Owner 失败，已忽略: " + ex.Message);
            }
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
