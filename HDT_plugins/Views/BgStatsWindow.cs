
using HDTplugins.Models;
using HDTplugins.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace HDTplugins.Views
{
    public class BgStatsWindow : Window
    {
        private enum SidebarSection
        {
            History,
            Races,
            Heroes,
            Matches
        }

        private enum HistoryRange
        {
            Season,
            Week,
            Day
        }

        private readonly StatsStore _store;
        private readonly Dictionary<SidebarSection, Button> _sectionButtons = new Dictionary<SidebarSection, Button>();
        private readonly Dictionary<HistoryRange, Button> _rangeButtons = new Dictionary<HistoryRange, Button>();
        private readonly Dictionary<string, FrameworkElement> _historyCards = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase);
        private readonly Button _versionButton;
        private readonly TextBlock _sectionTitle;
        private readonly Grid _historyToolbar;
        private readonly StackPanel _dateNavigator;
        private readonly Label _dateLabel;
        private readonly Label _summaryText;
        private readonly Border _contentHost;
        private readonly StackPanel _historyList;
        private readonly Button _prevDateButton;
        private readonly Button _nextDateButton;
        private ScrollViewer _historyScrollViewer;
        private string _selectedMatchId;
        private SidebarSection _currentSection = SidebarSection.History;
        private HistoryRange _currentRange = HistoryRange.Season;
        private DateTime _anchorDate = DateTime.Today;

        public event Action<string> OpenMatchDetailRequested;

        public Func<BgMatchRow, string> ResolveAnomalyDisplay { get; set; }
        public Func<BgMatchRow, string> ResolveFinalBoardDisplay { get; set; }

        public BgStatsWindow(StatsStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            ResolveAnomalyDisplay = row => string.IsNullOrWhiteSpace(row?.AnomalyDisplay) ? "待开发" : row.AnomalyDisplay;
            ResolveFinalBoardDisplay = row => string.IsNullOrWhiteSpace(row?.FinalBoardDisplay) ? "待开发" : row.FinalBoardDisplay;

            Title = "酒馆数据分析";
            Width = 1120;
            Height = 700;
            MinWidth = 980;
            MinHeight = 560;
            Background = new SolidColorBrush(Color.FromRgb(35, 37, 39));
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _versionButton = new Button();
            _sectionTitle = new TextBlock();
            _historyToolbar = new Grid();
            _dateNavigator = new StackPanel();
            _dateLabel = new Label();
            _summaryText = new Label();
            _contentHost = new Border();
            _historyList = new StackPanel();
            _prevDateButton = CreateToolbarButton("<");
            _nextDateButton = CreateToolbarButton(">");

            var root = new Grid { Margin = new Thickness(36, 28, 36, 28) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Content = root;

            var sidebar = BuildSidebar();
            Grid.SetColumn(sidebar, 0);
            root.Children.Add(sidebar);

            var mainPanel = BuildMainPanel();
            Grid.SetColumn(mainPanel, 1);
            root.Children.Add(mainPanel);

            Loaded += delegate
            {
                RefreshVersionButton();
                RefreshSectionButtons();
                RebuildContent();
            };
        }

        public void Reload()
        {
            RefreshVersionButton();
            if (_currentSection != SidebarSection.History)
                return;

            if (!string.IsNullOrWhiteSpace(_selectedMatchId))
            {
                ShowMatchDetails(_selectedMatchId, false);
                return;
            }

            RenderHistoryView();
        }

        public void SyncVersionSelection(string archiveKey)
        {
            if (!string.IsNullOrWhiteSpace(archiveKey))
                _store.SetArchiveByKey(archiveKey);

            RefreshVersionButton();
            RefreshHistoryToolbar();
            Reload();
        }

        private Border BuildSidebar()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(190, 183, 172)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(118, 255, 240)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 18, 16, 18)
            };

            var stack = new StackPanel();
            border.Child = stack;

            _versionButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            _versionButton.Padding = new Thickness(10, 12, 10, 12);
            _versionButton.Margin = new Thickness(0, 0, 0, 34);
            _versionButton.Background = Brushes.Transparent;
            _versionButton.BorderBrush = Brushes.Transparent;
            _versionButton.Cursor = Cursors.Hand;
            _versionButton.Click += delegate { OpenVersionMenu(); };
            stack.Children.Add(_versionButton);

            stack.Children.Add(CreateSectionButton(SidebarSection.History, "历史战绩"));
            stack.Children.Add(CreateSectionButton(SidebarSection.Races, "种族数据"));
            stack.Children.Add(CreateSectionButton(SidebarSection.Heroes, "英雄数据"));
            stack.Children.Add(CreateSectionButton(SidebarSection.Matches, "对局数据"));
            return border;
        }

        private Border BuildMainPanel()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(241, 238, 233)),
                Padding = new Thickness(24, 22, 24, 22)
            };

            var dock = new DockPanel();
            border.Child = dock;

            var header = new StackPanel { Orientation = Orientation.Vertical };
            DockPanel.SetDock(header, Dock.Top);
            dock.Children.Add(header);

            _sectionTitle.Text = "历史战绩";
            _sectionTitle.FontSize = 22;
            _sectionTitle.FontWeight = FontWeights.SemiBold;
            _sectionTitle.Foreground = new SolidColorBrush(Color.FromRgb(88, 80, 70));
            _sectionTitle.Margin = new Thickness(0, 0, 0, 14);
            header.Children.Add(_sectionTitle);

            ConfigureHistoryToolbar();
            header.Children.Add(_historyToolbar);

            _contentHost.Background = Brushes.Transparent;
            dock.Children.Add(_contentHost);
            return border;
        }

        private void ConfigureHistoryToolbar()
        {
            _historyToolbar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _historyToolbar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _historyToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _historyToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _historyToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _historyToolbar.Margin = new Thickness(0, 0, 0, 18);

            var rangePanel = new StackPanel { Orientation = Orientation.Horizontal };
            rangePanel.Children.Add(CreateRangeButton(HistoryRange.Season, "赛季战绩"));
            rangePanel.Children.Add(CreateRangeButton(HistoryRange.Week, "周战绩"));
            rangePanel.Children.Add(CreateRangeButton(HistoryRange.Day, "日战绩"));
            Grid.SetRow(rangePanel, 0);
            Grid.SetColumn(rangePanel, 0);
            _historyToolbar.Children.Add(rangePanel);

            _dateNavigator.Orientation = Orientation.Horizontal;
            _dateNavigator.VerticalAlignment = VerticalAlignment.Center;
            _prevDateButton.Click += delegate { ShiftAnchor(-1); };
            _dateNavigator.Children.Add(_prevDateButton);

            _dateLabel.MinWidth = 220;
            _dateLabel.Margin = new Thickness(10, 0, 10, 0);
            _dateLabel.Padding = new Thickness(10, 2, 10, 2);
            _dateLabel.Background = new SolidColorBrush(Color.FromRgb(196, 189, 177));
            _dateLabel.Foreground = Brushes.White;
            _dateLabel.HorizontalContentAlignment = HorizontalAlignment.Center;
            _dateLabel.FontSize = 14;
            _dateLabel.FontWeight = FontWeights.SemiBold;
            _dateNavigator.Children.Add(_dateLabel);

            _nextDateButton.Click += delegate { ShiftAnchor(1); };
            _dateNavigator.Children.Add(_nextDateButton);

            Grid.SetRow(_dateNavigator, 0);
            Grid.SetColumn(_dateNavigator, 2);
            _historyToolbar.Children.Add(_dateNavigator);

            _summaryText.Margin = new Thickness(0, 10, 0, 0);
            _summaryText.Padding = new Thickness(12, 4, 12, 4);
            _summaryText.Background = new SolidColorBrush(Color.FromRgb(196, 189, 177));
            _summaryText.Foreground = Brushes.White;
            _summaryText.FontSize = 14;
            _summaryText.HorizontalContentAlignment = HorizontalAlignment.Right;
            Grid.SetRow(_summaryText, 1);
            Grid.SetColumn(_summaryText, 2);
            _historyToolbar.Children.Add(_summaryText);
        }
        private Button CreateSectionButton(SidebarSection section, string text)
        {
            var button = new Button
            {
                Content = text,
                Padding = new Thickness(6, 10, 6, 10),
                Margin = new Thickness(0, 0, 0, 6),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand
            };
            button.Click += delegate
            {
                _currentSection = section;
                if (section != SidebarSection.History)
                    _selectedMatchId = null;
                RefreshSectionButtons();
                RebuildContent();
            };
            _sectionButtons[section] = button;
            return button;
        }

        private Button CreateRangeButton(HistoryRange range, string text)
        {
            var button = new Button
            {
                Content = text,
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 10, 0),
                Foreground = Brushes.White,
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand
            };
            button.Click += delegate
            {
                _currentRange = range;
                _anchorDate = DateTime.Today;
                _selectedMatchId = null;
                RefreshHistoryToolbar();
                RenderHistoryView();
            };
            _rangeButtons[range] = button;
            return button;
        }

        private static Button CreateToolbarButton(string text)
        {
            return new Button
            {
                Content = text,
                Width = 26,
                Height = 26,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(196, 189, 177)),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.Bold
            };
        }

        private void RebuildContent()
        {
            if (_currentSection == SidebarSection.History)
            {
                _sectionTitle.Text = string.IsNullOrWhiteSpace(_selectedMatchId) ? "历史战绩" : "对局详情";
                _historyToolbar.Visibility = string.IsNullOrWhiteSpace(_selectedMatchId) ? Visibility.Visible : Visibility.Collapsed;
                if (string.IsNullOrWhiteSpace(_selectedMatchId))
                    RenderHistoryView();
                else
                    ShowMatchDetails(_selectedMatchId, false);
                return;
            }

            _historyToolbar.Visibility = Visibility.Collapsed;
            _sectionTitle.Text = _currentSection == SidebarSection.Races ? "种族数据" : _currentSection == SidebarSection.Heroes ? "英雄数据" : "对局数据";
            _contentHost.Child = BuildPlaceholder();
        }

        private void RenderHistoryView()
        {
            _sectionTitle.Text = "历史战绩";
            _historyToolbar.Visibility = Visibility.Visible;
            RefreshHistoryToolbar();
            _historyCards.Clear();
            _historyScrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _historyList
            };
            _contentHost.Child = _historyScrollViewer;

            var rows = FilterRows(_store.LoadMatchRows());
            foreach (var row in rows)
            {
                row.AnomalyDisplay = ResolveAnomalyDisplay(row) ?? row.AnomalyDisplay;
                row.FinalBoardDisplay = ResolveFinalBoardDisplay(row) ?? row.FinalBoardDisplay;
            }

            RenderHistoryRows(rows);
            UpdateSummary(rows);
            RestoreSelectedHistoryCard();
        }

        private UIElement BuildPlaceholder()
        {
            var grid = new Grid();
            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = 420
            };
            grid.Children.Add(stack);
            stack.Children.Add(new TextBlock
            {
                Text = _sectionTitle.Text,
                FontSize = 26,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(95, 88, 79)),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "入口已新增，具体功能待开发。",
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromRgb(136, 127, 117)),
                TextAlignment = TextAlignment.Center
            });
            return grid;
        }

        private void RenderHistoryRows(IReadOnlyList<BgMatchRow> rows)
        {
            _historyList.Children.Clear();
            if (rows.Count == 0)
            {
                _historyList.Children.Add(new TextBlock
                {
                    Text = "当前筛选下暂无战绩",
                    Foreground = new SolidColorBrush(Color.FromRgb(126, 118, 108)),
                    FontSize = 15,
                    Margin = new Thickness(0, 18, 0, 0)
                });
                return;
            }

            foreach (var row in rows)
            {
                var card = BuildHistoryCard(row);
                _historyCards[row.MatchId] = card;
                _historyList.Children.Add(card);
            }
        }

        private Border BuildHistoryCard(BgMatchRow row)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(196, 189, 177)),
                Margin = new Thickness(0, 0, 0, 18),
                Padding = new Thickness(16, 12, 16, 12),
                Cursor = Cursors.Hand
            };

            var stack = new StackPanel();
            border.Child = stack;

            var topGrid = new Grid();
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topGrid.Children.Add(new TextBlock
            {
                Text = row.HeroName,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });
            var tagsPanel = BuildTagWrapPanel(row.Tags, false);
            Grid.SetColumn(tagsPanel, 1);
            topGrid.Children.Add(tagsPanel);
            stack.Children.Add(topGrid);

            stack.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = Brushes.White,
                FontSize = 13,
                Text = string.Format("时间: {0}    名次: {1}    分数: {2}    变动: {3}", row.TimestampText, row.Placement, row.RatingAfter, row.RatingDeltaText)
            });
            stack.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = Brushes.White,
                FontSize = 13,
                Text = "畸变: " + row.AnomalyDisplay
            });
            stack.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = Brushes.White,
                FontSize = 13,
                Text = "终局阵容: " + row.FinalBoardDisplay,
                TextWrapping = TextWrapping.Wrap
            });

            border.MouseLeftButtonUp += delegate { OpenRowDetails(row); };
            return border;
        }

        private void UpdateSummary(IReadOnlyList<BgMatchRow> rows)
        {
            if (rows.Count == 0)
            {
                _summaryText.Content = "对局数: 0    平均排名: -    分数变化: -";
                return;
            }

            var averagePlacement = rows.Average(x => x.Placement);
            var totalDelta = rows.Sum(x => x.RatingDelta);
            _summaryText.Content = string.Format("对局数: {0}  平均排名: {1:F2}  分数变化: {2}", rows.Count, averagePlacement, totalDelta > 0 ? "+" + totalDelta : totalDelta.ToString());
        }

        private List<BgMatchRow> FilterRows(IReadOnlyList<BgMatchRow> rows)
        {
            if (_currentRange == HistoryRange.Season)
                return rows.ToList();
            if (_currentRange == HistoryRange.Day)
                return rows.Where(x => x.TimestampLocal.Date == _anchorDate.Date).ToList();

            var weekStart = GetWeekStart(_anchorDate);
            var weekEnd = weekStart.AddDays(7);
            return rows.Where(x => x.TimestampLocal >= weekStart && x.TimestampLocal < weekEnd).ToList();
        }

        private void RefreshSectionButtons()
        {
            foreach (var pair in _sectionButtons)
            {
                var active = pair.Key == _currentSection;
                pair.Value.Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromRgb(246, 244, 240));
                pair.Value.FontSize = active ? 21 : 18;
            }
        }

        private void RefreshHistoryToolbar()
        {
            foreach (var pair in _rangeButtons)
                pair.Value.Background = pair.Key == _currentRange ? new SolidColorBrush(Color.FromRgb(196, 189, 177)) : new SolidColorBrush(Color.FromRgb(208, 202, 193));

            _dateNavigator.Visibility = _currentRange == HistoryRange.Season ? Visibility.Collapsed : Visibility.Visible;
            if (_currentRange == HistoryRange.Day)
                _dateLabel.Content = _anchorDate.ToString("yyyy-MM-dd");
            else
            {
                var weekStart = GetWeekStart(_anchorDate);
                _dateLabel.Content = weekStart.ToString("yyyy-MM-dd") + " ~ " + weekStart.AddDays(6).ToString("yyyy-MM-dd");
            }

            _prevDateButton.IsEnabled = HasAdjacentAnchor(-1);
            _nextDateButton.IsEnabled = HasAdjacentAnchor(1);
        }
        private void ShiftAnchor(int direction)
        {
            var nextAnchor = GetAdjacentAnchor(direction);
            if (!nextAnchor.HasValue)
                return;

            _anchorDate = nextAnchor.Value;
            RefreshHistoryToolbar();
            RenderHistoryView();
        }

        private void RefreshVersionButton()
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "版本信息",
                Foreground = new SolidColorBrush(Color.FromRgb(247, 245, 241)),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold
            });
            stack.Children.Add(new TextBlock
            {
                Text = _store.CurrentArchive != null ? _store.CurrentArchive.DisplayName : "season12 patch35.0",
                Foreground = Brushes.White,
                FontSize = 16,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            _versionButton.Content = stack;
        }

        private void OpenVersionMenu()
        {
            var menu = new ContextMenu();
            var archives = _store.GetRecordedArchives();
            if (archives.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = "暂无更多版本数据", IsEnabled = false });
            }
            else
            {
                foreach (var archive in archives)
                {
                    var item = new MenuItem
                    {
                        Header = archive.DisplayName,
                        IsCheckable = true,
                        IsChecked = string.Equals(archive.Key, _store.CurrentArchive?.Key, StringComparison.OrdinalIgnoreCase)
                    };
                    item.Click += delegate
                    {
                        _selectedMatchId = null;
                        _store.SetArchiveByKey(archive.Key);
                        RefreshVersionButton();
                        RefreshHistoryToolbar();
                        RenderHistoryView();
                    };
                    menu.Items.Add(item);
                }
            }

            _versionButton.ContextMenu = menu;
            menu.PlacementTarget = _versionButton;
            menu.IsOpen = true;
        }

        private bool HasAdjacentAnchor(int direction)
        {
            return GetAdjacentAnchor(direction).HasValue;
        }

        private DateTime? GetAdjacentAnchor(int direction)
        {
            if (_currentRange == HistoryRange.Season)
                return null;

            var anchors = GetAvailableAnchors();
            if (anchors.Count == 0)
                return null;

            var currentAnchor = _currentRange == HistoryRange.Day ? _anchorDate.Date : GetWeekStart(_anchorDate);
            if (direction < 0)
                return anchors.Where(x => x < currentAnchor).OrderByDescending(x => x).Cast<DateTime?>().FirstOrDefault();
            return anchors.Where(x => x > currentAnchor).OrderBy(x => x).Cast<DateTime?>().FirstOrDefault();
        }

        private List<DateTime> GetAvailableAnchors()
        {
            var rows = _store.LoadMatchRows();
            if (_currentRange == HistoryRange.Day)
                return rows.Select(x => x.TimestampLocal.Date).Distinct().OrderBy(x => x).ToList();
            return rows.Select(x => GetWeekStart(x.TimestampLocal)).Distinct().OrderBy(x => x).ToList();
        }

        private void OpenRowDetails(BgMatchRow row)
        {
            _selectedMatchId = row.MatchId;
            OpenMatchDetailRequested?.Invoke(row.MatchId);
            ShowMatchDetails(row.MatchId, true);
        }

        private void ShowMatchDetails(string matchId, bool scrollToTop)
        {
            var snapshot = _store.LoadSnapshot(matchId);
            if (snapshot == null)
            {
                _selectedMatchId = null;
                RenderHistoryView();
                return;
            }

            _currentSection = SidebarSection.History;
            RefreshSectionButtons();
            _sectionTitle.Text = "对局详情";
            _historyToolbar.Visibility = Visibility.Collapsed;

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Height = 520,
                Content = BuildDetailContent(snapshot)
            };
            _contentHost.Child = scrollViewer;
            if (scrollToTop)
                scrollViewer.ScrollToTop();
        }

        private UIElement BuildDetailContent(BgSnapshot snapshot)
        {
            var root = new StackPanel();
            root.Children.Add(BuildDetailHeader(snapshot));
            root.Children.Add(BuildHeroSection(snapshot));
            root.Children.Add(BuildFinalBoardSection(snapshot));
            root.Children.Add(BuildTavernSection(snapshot));
            return root;
        }

        private UIElement BuildDetailHeader(BgSnapshot snapshot)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });

            var backButton = new Button
            {
                Content = "返回",
                Width = 92,
                Height = 42,
                Background = new SolidColorBrush(Color.FromRgb(196, 189, 177)),
                Foreground = Brushes.White,
                BorderBrush = Brushes.Transparent,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
            backButton.Click += delegate
            {
                var selected = _selectedMatchId;
                _selectedMatchId = null;
                RenderHistoryView();
                _selectedMatchId = selected;
                RestoreSelectedHistoryCard();
            };
            grid.Children.Add(backButton);

            var infoPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(infoPanel, 2);
            infoPanel.Children.Add(CreateHeaderBadge("对局时间: " + FormatTimestamp(snapshot.Timestamp)));
            infoPanel.Children.Add(CreateHeaderBadge(string.Format("排名: {0}   分数: {1}   分数变化: {2}", snapshot.Placement, snapshot.RatingAfter, snapshot.RatingDelta > 0 ? "+" + snapshot.RatingDelta : snapshot.RatingDelta.ToString()), new Thickness(0, 10, 0, 0)));
            grid.Children.Add(infoPanel);
            return grid;
        }

        private Border CreateHeaderBadge(string text, Thickness? margin = null)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(196, 189, 177)),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = margin ?? new Thickness(0),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = Brushes.White,
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold
                }
            };
        }

        private UIElement BuildHeroSection(BgSnapshot snapshot)
        {
            var section = CreateSectionContainer();
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            section.Child = grid;

            grid.Children.Add(CreateInfoText("选用英雄：" + StatsStore.GetCardName(snapshot.HeroCardId)));
            if (!string.IsNullOrWhiteSpace(snapshot.HeroPowerCardId) && !string.Equals(snapshot.HeroPowerCardId, snapshot.InitialHeroPowerCardId, StringComparison.OrdinalIgnoreCase))
            {
                var finalPower = CreateInfoText("最终英雄技能：" + StatsStore.GetCardName(snapshot.HeroPowerCardId));
                Grid.SetColumn(finalPower, 1);
                grid.Children.Add(finalPower);
            }
            return section;
        }

        private TextBlock CreateInfoText(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
        }

        private UIElement BuildFinalBoardSection(BgSnapshot snapshot)
        {
            var section = CreateSectionContainer();
            var stack = new StackPanel();
            section.Child = stack;

            var topGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topGrid.Children.Add(new TextBlock
            {
                Text = "终局阵容：",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });
            var tagsPanel = BuildTagEditorPanel(snapshot, _store.GetDisplayTags(snapshot).ToList());
            Grid.SetColumn(tagsPanel, 1);
            topGrid.Children.Add(tagsPanel);
            stack.Children.Add(topGrid);

            var boardGrid = new UniformGrid { Columns = 7 };
            foreach (var minion in (snapshot.FinalBoard ?? new List<BgBoardMinionSnapshot>()).OrderBy(x => x.Position).Take(7))
                boardGrid.Children.Add(BuildMinionCard(minion));
            stack.Children.Add(boardGrid);
            return section;
        }

        private Border BuildMinionCard(BgBoardMinionSnapshot minion)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(126, 163, 209)),
                Margin = new Thickness(8, 0, 8, 0),
                Padding = new Thickness(10),
                MinHeight = 116
            };
            border.ToolTip = BuildMinionToolTip(minion);
            var stack = new StackPanel();
            border.Child = stack;
            stack.Children.Add(new TextBlock
            {
                Text = (minion.IsGolden ? GetGoldPrefix() : string.Empty) + SafeText(minion.Name, StatsStore.GetCardName(minion.CardId)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13
            });
            stack.Children.Add(new TextBlock
            {
                Text = "攻击",
                Foreground = Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 10, 0, 0)
            });
            stack.Children.Add(new TextBlock
            {
                Text = minion.Attack.ToString(CultureInfo.InvariantCulture),
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 0)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "生命",
                Foreground = Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 0)
            });
            stack.Children.Add(new TextBlock
            {
                Text = minion.Health.ToString(CultureInfo.InvariantCulture),
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 0)
            });
            return border;
        }

        private object BuildMinionToolTip(BgBoardMinionSnapshot minion)
        {
            var keywords = minion.Keywords?.ToDisplayTokens() ?? Array.Empty<string>();
            return string.Join(Environment.NewLine, new[]
            {
                "名称: " + SafeText(minion.Name, StatsStore.GetCardName(minion.CardId)),
                "ID: " + SafeText(minion.CardId, "-"),
                "种族: " + SafeText(minion.Race, "-"),
                "是否金卡: " + (minion.IsGolden ? "是" : "否"),
                "站位: " + minion.Position.ToString(CultureInfo.InvariantCulture),
                "攻击力: " + minion.Attack.ToString(CultureInfo.InvariantCulture),
                "生命值: " + minion.Health.ToString(CultureInfo.InvariantCulture),
                "关键词: " + (keywords.Count == 0 ? "无" : string.Join(", ", keywords))
            });
        }
        private UIElement BuildTagEditorPanel(BgSnapshot snapshot, List<string> combinedTags)
        {
            var host = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var addButton = new Button
            {
                Content = "+",
                Width = 34,
                Height = 34,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(126, 163, 209)),
                Foreground = Brushes.White,
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                IsEnabled = combinedTags.Count < 5
            };
            addButton.Click += delegate
            {
                var menu = BuildAddTagMenu(snapshot, combinedTags);
                addButton.ContextMenu = menu;
                menu.PlacementTarget = addButton;
                menu.IsOpen = true;
            };
            host.Children.Add(addButton);

            foreach (var tag in combinedTags)
            {
                var removable = (snapshot.ManualTags ?? new List<string>()).Any(x => string.Equals(x, tag, StringComparison.OrdinalIgnoreCase));
                host.Children.Add(BuildEditableTag(tag, removable, () => RemoveManualTag(snapshot, tag)));
            }
            return host;
        }

        private ContextMenu BuildAddTagMenu(BgSnapshot snapshot, List<string> combinedTags)
        {
            var menu = new ContextMenu();
            if (combinedTags.Count >= 5)
            {
                menu.Items.Add(new MenuItem { Header = "最多支持 5 个 TAG", IsEnabled = false });
                return menu;
            }

            var current = new HashSet<string>(combinedTags, StringComparer.OrdinalIgnoreCase);
            var available = _store.GetAvailableTags().Where(tag => !current.Contains(tag)).ToList();
            if (available.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = "没有可添加的 TAG", IsEnabled = false });
                return menu;
            }

            foreach (var tag in available)
            {
                var item = new MenuItem { Header = tag };
                item.Click += delegate { AddManualTag(snapshot, tag); };
                menu.Items.Add(item);
            }
            return menu;
        }

        private Border BuildEditableTag(string tag, bool removable, Action onRemove)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(126, 163, 209)),
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(10, 6, 10, 6)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (removable)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            border.Child = grid;

            grid.Children.Add(new TextBlock
            {
                Text = tag,
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            if (removable)
            {
                var removeButton = new Button
                {
                    Content = "×",
                    Width = 18,
                    Height = 18,
                    Margin = new Thickness(6, -4, 0, 0),
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Transparent,
                    Foreground = Brushes.White,
                    Visibility = Visibility.Collapsed,
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Top
                };
                removeButton.Click += delegate { onRemove?.Invoke(); };
                Grid.SetColumn(removeButton, 1);
                grid.Children.Add(removeButton);
                border.MouseEnter += delegate { removeButton.Visibility = Visibility.Visible; };
                border.MouseLeave += delegate { removeButton.Visibility = Visibility.Collapsed; };
            }
            return border;
        }

        private WrapPanel BuildTagWrapPanel(IEnumerable<string> tags, bool includeEmptyPlaceholder)
        {
            var panel = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            var list = (tags ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Take(5).ToList();
            if (list.Count == 0 && includeEmptyPlaceholder)
                panel.Children.Add(CreateTagChip("无 TAG"));
            else
                foreach (var tag in list)
                    panel.Children.Add(CreateTagChip(tag));
            return panel;
        }

        private Border CreateTagChip(string text)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(126, 163, 209)),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(6, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                }
            };
        }

        private void AddManualTag(BgSnapshot snapshot, string tag)
        {
            var manual = (snapshot.ManualTags ?? new List<string>()).ToList();
            if (manual.Contains(tag, StringComparer.OrdinalIgnoreCase))
                return;

            if (_store.GetDisplayTags(snapshot).Count >= 5)
                return;

            manual.Add(tag);
            snapshot.ManualTags = manual;
            _store.UpdateManualTags(snapshot.MatchId, manual);
            ShowMatchDetails(snapshot.MatchId, false);
        }

        private void RemoveManualTag(BgSnapshot snapshot, string tag)
        {
            var manual = (snapshot.ManualTags ?? new List<string>()).Where(x => !string.Equals(x, tag, StringComparison.OrdinalIgnoreCase)).ToList();
            snapshot.ManualTags = manual;
            _store.UpdateManualTags(snapshot.MatchId, manual);
            ShowMatchDetails(snapshot.MatchId, false);
        }

        private UIElement BuildTavernSection(BgSnapshot snapshot)
        {
            var section = CreateSectionContainer();
            var stack = new StackPanel();
            section.Child = stack;
            stack.Children.Add(new TextBlock
            {
                Text = "升本信息",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 12)
            });
            stack.Children.Add(new TextBlock
            {
                Text = BuildUpgradeText(snapshot),
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 12),
                TextWrapping = TextWrapping.Wrap
            });
            stack.Children.Add(BuildUpgradeChart(snapshot));
            return section;
        }

        private string BuildUpgradeText(BgSnapshot snapshot)
        {
            var upgrades = (snapshot.TavernUpgradeTimeline ?? new List<BgTavernUpgradePoint>()).OrderBy(x => x.Turn).ToList();
            if (upgrades.Count == 0)
                return "回合-酒馆等级: 暂无数据";
            return "回合-酒馆等级: " + string.Join(" / ", upgrades.Select(x => x.Turn + "-" + x.TavernTier));
        }

        private UIElement BuildUpgradeChart(BgSnapshot snapshot)
        {
            var upgrades = (snapshot.TavernUpgradeTimeline ?? new List<BgTavernUpgradePoint>()).OrderBy(x => x.Turn).ToList();
            var host = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(126, 163, 209)),
                Height = 180,
                Padding = new Thickness(16)
            };

            if (upgrades.Count == 0)
            {
                host.Child = new TextBlock
                {
                    Text = "暂无升本数据",
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontSize = 14
                };
                return host;
            }

            var canvas = new Canvas { Width = 720, Height = 148 };
            host.Child = canvas;
            var minTurn = upgrades.Min(x => x.Turn);
            var maxTurn = upgrades.Max(x => x.Turn);
            var maxTier = Math.Max(6, upgrades.Max(x => x.TavernTier));
            const double plotLeft = 28;
            const double plotTop = 10;
            const double plotWidth = 660;
            const double plotHeight = 108;

            canvas.Children.Add(new Line { X1 = plotLeft, Y1 = plotTop + plotHeight, X2 = plotLeft + plotWidth, Y2 = plotTop + plotHeight, Stroke = Brushes.White, StrokeThickness = 1.2 });
            canvas.Children.Add(new Line { X1 = plotLeft, Y1 = plotTop, X2 = plotLeft, Y2 = plotTop + plotHeight, Stroke = Brushes.White, StrokeThickness = 1.2 });

            for (var tier = 1; tier <= maxTier; tier++)
            {
                var y = plotTop + plotHeight - ((tier - 1) / Math.Max(1.0, maxTier - 1)) * plotHeight;
                var label = new TextBlock { Text = tier.ToString(CultureInfo.InvariantCulture), Foreground = Brushes.White, FontSize = 11 };
                Canvas.SetLeft(label, 0);
                Canvas.SetTop(label, y - 8);
                canvas.Children.Add(label);
            }

            var polyline = new Polyline { Stroke = Brushes.White, StrokeThickness = 2.2 };
            foreach (var point in upgrades)
            {
                var x = plotLeft + ((point.Turn - minTurn) / Math.Max(1.0, maxTurn - minTurn)) * plotWidth;
                var y = plotTop + plotHeight - ((point.TavernTier - 1) / Math.Max(1.0, maxTier - 1)) * plotHeight;
                polyline.Points.Add(new Point(x, y));

                var dot = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = Brushes.White,
                    ToolTip = $"回合数: {point.Turn}\n酒馆等级: {point.TavernTier}"
                };
                Canvas.SetLeft(dot, x - 5);
                Canvas.SetTop(dot, y - 5);
                canvas.Children.Add(dot);

                var xlabel = new TextBlock { Text = point.Turn.ToString(CultureInfo.InvariantCulture), Foreground = Brushes.White, FontSize = 11 };
                Canvas.SetLeft(xlabel, x - 6);
                Canvas.SetTop(xlabel, plotTop + plotHeight + 6);
                canvas.Children.Add(xlabel);
            }
            canvas.Children.Add(polyline);
            return host;
        }
        private Border CreateSectionContainer()
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(196, 189, 177)),
                Margin = new Thickness(0, 0, 0, 20),
                Padding = new Thickness(16)
            };
        }

        private void RestoreSelectedHistoryCard()
        {
            if (string.IsNullOrWhiteSpace(_selectedMatchId) || _historyScrollViewer == null)
                return;
            if (!_historyCards.TryGetValue(_selectedMatchId, out var target))
                return;

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(target.BringIntoView));
        }

        private static string FormatTimestamp(string timestamp)
        {
            return DateTimeOffset.TryParse(timestamp, out var value) ? value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "-";
        }

        private static string SafeText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string GetGoldPrefix()
        {
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "(金)" : "(gold) ";
        }

        private static DateTime GetWeekStart(DateTime date)
        {
            var diff = ((int)date.DayOfWeek + 6) % 7;
            return date.Date.AddDays(-diff);
        }
    }
}
