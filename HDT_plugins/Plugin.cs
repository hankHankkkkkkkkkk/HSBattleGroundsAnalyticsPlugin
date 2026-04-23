using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Plugins;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HDTplugins.Localization;
using HDTplugins.Models;
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
        public Version Version => new Version(2, 1, 1);
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
        private DispatcherTimer _deferredStartupTimer;
        private PluginUpdateService _updateService;
        private bool _wasHearthstoneRunning;
        private string _lastRuntimeAccountKey;
        private BgDraftOverlayService _draftOverlayService;
        private bool _selectedAccountInitialized;

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
            GameTextService.Initialize(false);
            _updateService = new PluginUpdateService(Assembly.GetExecutingAssembly().Location, Version);

            _probe = new BgGameProbe();
            _draftOverlayService = new BgDraftOverlayService(_store, _settingsService);
            _menuItem = CreateQuickOpenMenuItem();
            LocalizationService.LanguageChanged += OnLanguageChanged;

            GameEvents.OnGameStart.Add(OnGameStart);
            GameEvents.OnGameEnd.Add(OnGameEnd);

            if (_settingsService.Settings.AutoOpenOnStartup)
                ShowWindowAsync();

            ScheduleDeferredStartupWork();

            HdtLog.Info("[Hank的log信息] 插件已加载（已订阅事件，GUI 自动启动）");
        }

        public void OnUnload()
        {
            _enabled = false;
            LocalizationService.LanguageChanged -= OnLanguageChanged;
            StopStartupGameTextRefreshTimer();
            StopDeferredStartupTimer();
            _updateService?.Dispose();
            _updateService = null;
            _draftOverlayService?.Dispose();
            _draftOverlayService = null;
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

            UpdateRuntimeAccountContext();
            _probe.Tick();
            _draftOverlayService?.Refresh(_probe);
            if (!_probe.IsBattlegrounds || _finalizedThisMatch)
                return;

            if (_probe.HasResolvedHero && !_store.PendingWritten)
            {
                _store.WritePendingIfNeeded(_probe.HeroCardId, _probe.HeroSkinCardId, _probe.InitialHeroPowerCardIds, _probe.RatingBefore);
            }

            if (_store.PendingWritten && _probe.HasResolvedPlacement && _probe.HasResolvedRatingAfter)
            {
                HdtLog.Info($"[BGStats][HeroPower] finalize args hero={_probe.HeroCardId ?? "null"} initial=[{string.Join(", ", _probe.InitialHeroPowerCardIds)}] combat=[{string.Join(", ", _probe.HeroPowerCardIds)}]");
                _store.FinalizeIfPossible(
                    _store.CurrentMatchId,
                    _store.CurrentMatchTimestampUtc,
                    _probe.HeroCardId,
                    _probe.HeroSkinCardId,
                    _probe.InitialHeroPowerCardIds,
                    _probe.HeroPowerCardIds,
                    _probe.Placement,
                    _probe.RatingBefore,
                    _probe.RatingAfter,
                    _probe.OfferedHeroCardIds,
                    _probe.AvailableRaceNames,
                    _probe.AnomalyCardId,
                    _probe.FinalBoard,
                    _probe.TavernUpgradeTimeline,
                    _probe.LesserTrinketOptionCardIds,
                    _probe.LesserTrinketCardId,
                    _probe.GreaterTrinketOptionCardIds,
                    _probe.GreaterTrinketCardId,
                    _probe.HeroPowerTrinketCardId,
                    _probe.HeroPowerTrinketType,
                    _probe.TimewarpEntries,
                    _probe.QuestCardId,
                    _probe.QuestRewardCardId,
                    _probe.QuestCompleted);

                var latestArchive = _store.RefreshLatestRecordedArchiveForDisplay();
                _statsWindow?.SyncVersionSelection(latestArchive?.Key);
                _statsWindow?.Reload();
                _draftOverlayService?.InvalidateStats();
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
            _draftOverlayService?.Hide();
            _lastRuntimeAccountKey = null;
            _statsWindow?.SyncVersionSelection(null);
        }

        private void OnGameEnd()
        {
            if (!_enabled)
                return;

            _probe.OnGameEnd();
            _draftOverlayService?.Hide();
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
                _statsWindow.Closed += (_, __) => _statsWindow = null;
            }

            TryAttachOwner();
            _statsWindow.Show();
            _statsWindow.Activate();
            _statsWindow.BeginInitialHistoryLoad();
        }

        private void ScheduleDeferredStartupWork()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;

            dispatcher.BeginInvoke(new Action(() =>
            {
                StopDeferredStartupTimer();
                _deferredStartupTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _deferredStartupTimer.Tick += DeferredStartupTimerOnTick;
                _deferredStartupTimer.Start();
            }));
        }

        private void DeferredStartupTimerOnTick(object sender, EventArgs e)
        {
            StopDeferredStartupTimer();
            StartDeferredStartupWork();
        }

        private void StopDeferredStartupTimer()
        {
            if (_deferredStartupTimer == null)
                return;

            _deferredStartupTimer.Stop();
            _deferredStartupTimer.Tick -= DeferredStartupTimerOnTick;
            _deferredStartupTimer = null;
        }

        private void StartDeferredStartupWork()
        {
            var dispatcher = Application.Current?.Dispatcher;
            Task.Run(() =>
            {
                try
                {
                    EnsureSelectedAccountInitialized();
                    GameTextService.ForceRefreshCurrentLanguage();
                    _store.RunDeferredStartupMaintenance();
                    _settingsService.Reload();
                    _store.WarmCaches(_settingsService.Settings.GetNormalizedScoreLine());
                    HdtLog.Info("[BGStats][Startup] 延迟启动任务完成");
                }
                catch (Exception ex)
                {
                    HdtLog.Warn("[BGStats][Startup] 延迟启动任务失败，已忽略: " + ex.Message);
                }

                ScheduleUpdateCheck();
            });
        }

        private void EnsureSelectedAccountInitialized()
        {
            if (_selectedAccountInitialized)
                return;

            _settingsService.Reload();
            _store.EnsureSelectedAccountInitialized(_settingsService.Settings.SelectedAccountKey);
            _selectedAccountInitialized = true;
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

        private void ScheduleUpdateCheck()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || _updateService == null)
                return;

            Task.Run(async () =>
            {
                try
                {
                    var availableUpdate = await _updateService.CheckForUpdateAsync().ConfigureAwait(false);
                    if (availableUpdate == null)
                        return;

                    _ = dispatcher.BeginInvoke(new Action(() => PromptForUpdate(availableUpdate)));
                }
                catch (Exception ex)
                {
                    HdtLog.Warn("[BGStats][Update] 启动检查更新失败，已忽略: " + ex.Message);
                }
            });
        }

        private void PromptForUpdate(PluginUpdateService.AvailableUpdate update)
        {
            if (!_enabled || update == null)
                return;

            var prompt = string.Format(
                Loc.S("Update_AvailableMessage"),
                Version,
                update.VersionText);
            var result = MessageBox.Show(
                prompt,
                Loc.S("Update_Title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (result != MessageBoxResult.Yes)
                return;

            Task.Run(async () =>
            {
                PluginUpdateService.PrepareUpdateResult prepareResult;
                try
                {
                    prepareResult = await _updateService.DownloadAndPrepareUpdateAsync(update).ConfigureAwait(false);
                    prepareResult.Message = prepareResult.Success
                        ? Loc.F("Update_DownloadReadyMessage", update.VersionText)
                        : Loc.F("Update_DownloadFailedMessage", prepareResult.Message);
                }
                catch (Exception ex)
                {
                    prepareResult = new PluginUpdateService.PrepareUpdateResult
                    {
                        Success = false,
                        Message = Loc.F("Update_DownloadFailedMessage", ex.Message)
                    };
                    HdtLog.Error("[BGStats][Update] 下载更新失败: " + ex.Message);
                }

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null)
                    return;

                _ = dispatcher.BeginInvoke(new Action(() =>
                {
                    MessageBox.Show(
                        prepareResult.Message,
                        Loc.S("Update_Title"),
                        MessageBoxButton.OK,
                        prepareResult.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }));
            });
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

        private void UpdateRuntimeAccountContext()
        {
            var isRunning = IsHearthstoneRunning();
            if (!isRunning)
            {
                _wasHearthstoneRunning = false;
                return;
            }

            _probe.RefreshRuntimeAccountContext();
            if (!_probe.HasAttemptedAccountResolution)
                return;

            var account = BuildCurrentAccountRecord();
            if (string.IsNullOrWhiteSpace(account.AccountHi)
                && string.IsNullOrWhiteSpace(account.AccountLo)
                && string.IsNullOrWhiteSpace(account.BattleTag)
                && string.IsNullOrWhiteSpace(account.ServerInfo))
                return;

            var runtimeKey = BuildRuntimeAccountKey(account);
            var forceApply = !_wasHearthstoneRunning;
            _wasHearthstoneRunning = true;
            var hasRicherMetadata = HasRicherAccountMetadata(_store.CurrentAccount, account);
            if (!forceApply && !hasRicherMetadata && string.Equals(runtimeKey, _lastRuntimeAccountKey, StringComparison.OrdinalIgnoreCase))
                return;

            var previousKey = _store.CurrentAccountKey;
            _store.ApplyCurrentAccountFromGame(account);
            _lastRuntimeAccountKey = runtimeKey;
            if (hasRicherMetadata)
                HdtLog.Info("[BGStats][Account] 已用运行时 BattleTag/区服信息刷新当前显示账号: " + (account.BattleTag ?? runtimeKey));
            if (string.Equals(previousKey, _store.CurrentAccountKey, StringComparison.OrdinalIgnoreCase))
            {
                if (hasRicherMetadata)
                    _statsWindow?.Reload();
                return;
            }

            _settingsService.Settings.SelectedAccountKey = _store.CurrentAccountKey;
            _settingsService.Save();
            _statsWindow?.Reload();
        }

        private AccountRecord BuildCurrentAccountRecord()
        {
            return new AccountRecord
            {
                AccountHi = _probe.AccountHi,
                AccountLo = _probe.AccountLo,
                BattleTag = _probe.BattleTag,
                ServerInfo = _probe.ServerInfo,
                RegionCode = _probe.RegionCode,
                RegionName = _probe.RegionName
            };
        }

        private static string BuildRuntimeAccountKey(AccountRecord account)
        {
            if (account == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(account.AccountHi) || !string.IsNullOrWhiteSpace(account.AccountLo))
                return $"{account.AccountHi ?? "0"}|{account.AccountLo ?? "0"}|{account.RegionCode ?? account.RegionName ?? "0"}";

            return account.BattleTag ?? account.ServerInfo ?? string.Empty;
        }

        private static bool HasRicherAccountMetadata(AccountRecord current, AccountRecord incoming)
        {
            if (current == null || incoming == null)
                return false;

            var sameHi = string.Equals(current.AccountHi ?? string.Empty, incoming.AccountHi ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            var sameLo = string.Equals(current.AccountLo ?? string.Empty, incoming.AccountLo ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            var sameBattleTag = !string.IsNullOrWhiteSpace(current.BattleTag) && string.Equals(current.BattleTag, incoming.BattleTag, StringComparison.OrdinalIgnoreCase);
            var sameAccount = sameBattleTag || (sameHi && sameLo && (!string.IsNullOrWhiteSpace(current.AccountHi) || !string.IsNullOrWhiteSpace(current.AccountLo)));
            if (!sameAccount)
                return false;

            if (string.IsNullOrWhiteSpace(current.BattleTag) && !string.IsNullOrWhiteSpace(incoming.BattleTag))
                return true;
            if (string.IsNullOrWhiteSpace(current.RegionName) && !string.IsNullOrWhiteSpace(incoming.RegionName))
                return true;
            return string.IsNullOrWhiteSpace(current.RegionCode) && !string.IsNullOrWhiteSpace(incoming.RegionCode);
        }

        private static bool IsHearthstoneRunning()
        {
            try
            {
                return Process.GetProcessesByName("Hearthstone")
                    .Any(process =>
                    {
                        try
                        {
                            return process != null && !process.HasExited && process.MainWindowHandle != IntPtr.Zero;
                        }
                        catch
                        {
                            return false;
                        }
                    });
            }
            catch
            {
                return false;
            }
        }
    }
}
