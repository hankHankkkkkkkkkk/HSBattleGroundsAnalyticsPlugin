using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Plugins;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HDTplugins.Localization;
using HDTplugins.Services;
using HDTplugins.Views;

using HdtLog = Hearthstone_Deck_Tracker.Utility.Logging.Log;

namespace HDTplugins
{
    public class Plugin : IPlugin
    {
        public string Name => Loc.S("Plugin_Name");
        public string Description => Loc.S("Plugin_Description");
        public string Author => "Hank";
        public Version Version => new Version(0, 10, 6);
        public string ButtonText => Loc.S("Plugin_ButtonText");
        public MenuItem MenuItem => _menuItem;

        private bool _finalizedThisMatch;
        private bool _enabled;
        private BgGameProbe _probe;
        private StatsStore _store;
        private PluginSettingsService _settingsService;
        private BgStatsWindow _statsWindow;
        private MenuItem _menuItem;
        private DispatcherTimer _startupGameTextRefreshTimer;

        public void OnLoad()
        {
            if (_enabled)
                return;

            _enabled = true;
            _store = new StatsStore();
            _store.Initialize();

            _settingsService = new PluginSettingsService();
            _settingsService.Initialize(_store.TablesDirectoryPath);
            LocalizationService.Initialize(_settingsService.Settings.Language);
            GameTextService.Initialize();
            ScheduleStartupGameTextRefresh();

            _probe = new BgGameProbe();
            _menuItem = CreateQuickOpenMenuItem();
            LocalizationService.LanguageChanged += OnLanguageChanged;

            GameEvents.OnGameStart.Add(OnGameStart);
            GameEvents.OnGameEnd.Add(OnGameEnd);

            if (_settingsService.Settings.AutoOpenOnStartup)
                ShowWindowAsync();

            HdtLog.Info("[Hank的log信息] 插件已加载（已订阅事件，GUI 自动启动）");
        }

        public void OnUnload()
        {
            _enabled = false;
            LocalizationService.LanguageChanged -= OnLanguageChanged;
            StopStartupGameTextRefreshTimer();
            TryCloseWindow();
            HdtLog.Info("[Hank的log信息] 插件已卸载（已关闭开关）");
        }

        public void OnButtonPress()
        {
            ShowWindow();
            _statsWindow?.ShowSettings();
        }

        public void OnUpdate()
        {
            if (!_enabled)
                return;

            _probe.Tick();
            if (!_probe.IsBattlegrounds || _finalizedThisMatch)
                return;

            if (_probe.HasResolvedHero && !_store.PendingWritten)
            {
                _store.WritePendingIfNeeded(_probe.HeroCardId, _probe.HeroSkinCardId, _probe.InitialHeroPowerCardId, _probe.InitialSecondHeroPowerCardId, _probe.RatingBefore);
            }

            if (_store.PendingWritten && _probe.HasResolvedPlacement && _probe.HasResolvedRatingAfter)
            {
                _store.FinalizeIfPossible(
                    _store.CurrentMatchId,
                    _store.CurrentMatchTimestampUtc,
                    _probe.HeroCardId,
                    _probe.HeroSkinCardId,
                    _probe.InitialHeroPowerCardId,
                    _probe.InitialSecondHeroPowerCardId,
                    _probe.HeroPowerCardId,
                    _probe.SecondHeroPowerCardId,
                    _probe.Placement,
                    _probe.RatingBefore,
                    _probe.RatingAfter,
                    _probe.OfferedHeroCardIds,
                    _probe.AvailableRaceNames,
                    _probe.AnomalyCardId,
                    _probe.FinalBoard,
                    _probe.TavernUpgradeTimeline);

                var latestArchive = _store.RefreshLatestRecordedArchiveForDisplay();
                _statsWindow?.SyncVersionSelection(latestArchive?.Key);
                _statsWindow?.Reload();
                _finalizedThisMatch = true;
                _probe.StopFinalizePolling();
            }
        }

        private void OnGameStart()
        {
            if (!_enabled)
                return;

            _finalizedThisMatch = false;
            _store.ResetMatch();
            _store.ConfirmCurrentArchiveForMatch();
            _probe.OnGameStart();
            _statsWindow?.SyncVersionSelection(null);
        }

        private void OnGameEnd()
        {
            if (!_enabled)
                return;

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
                _statsWindow = new BgStatsWindow(_store, _settingsService);
                _statsWindow.OpenMatchDetailRequested += OnOpenMatchDetailRequested;
                _statsWindow.Closed += (_, __) => _statsWindow = null;
            }

            TryAttachOwner();
            _statsWindow.SyncVersionSelection(_store.CurrentArchive?.Key);
            _statsWindow.Show();
            _statsWindow.Activate();
            ScheduleStartupGameTextRefresh();
        }

        private void ScheduleStartupGameTextRefresh()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;

            dispatcher.BeginInvoke(new Action(() =>
            {
                StopStartupGameTextRefreshTimer();
                _startupGameTextRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
                {
                    Interval = TimeSpan.FromSeconds(1.5)
                };
                _startupGameTextRefreshTimer.Tick += StartupGameTextRefreshTimerOnTick;
                _startupGameTextRefreshTimer.Start();
            }));
        }

        private void StartupGameTextRefreshTimerOnTick(object sender, EventArgs e)
        {
            StopStartupGameTextRefreshTimer();
            GameTextService.ForceRefreshCurrentLanguage();
            _statsWindow?.Reload();
        }

        private void StopStartupGameTextRefreshTimer()
        {
            if (_startupGameTextRefreshTimer == null)
                return;

            _startupGameTextRefreshTimer.Stop();
            _startupGameTextRefreshTimer.Tick -= StartupGameTextRefreshTimerOnTick;
            _startupGameTextRefreshTimer = null;
        }

        private MenuItem CreateQuickOpenMenuItem()
        {
            var item = new MenuItem
            {
                Header = Loc.S("Plugin_MenuHeader")
            };
            item.Click += delegate { ShowWindow(); };
            return item;
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            if (_menuItem != null)
                _menuItem.Header = Loc.S("Plugin_MenuHeader");
        }

        private void TryAttachOwner()
        {
            try
            {
                if (_statsWindow == null || _statsWindow.IsVisible)
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
            catch
            {
            }
        }
    }
}
