using HDTplugins.Models;
using HDTplugins.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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

        private SidebarSection _currentSection = SidebarSection.History;
        private HistoryRange _currentRange = HistoryRange.Season;
        private DateTime _anchorDate = DateTime.Today;

        public event Action<string> OpenMatchDetailRequested;

        public Func<BgMatchRow, string> ResolveAnomalyDisplay { get; set; }
        public Func<BgMatchRow, string> ResolveFinalBoardDisplay { get; set; }

        public BgStatsWindow(StatsStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            ResolveAnomalyDisplay = delegate(BgMatchRow row) { return string.IsNullOrWhiteSpace(row == null ? null : row.AnomalyDisplay) ? "待开发" : row.AnomalyDisplay; };
            ResolveFinalBoardDisplay = delegate(BgMatchRow row) { return string.IsNullOrWhiteSpace(row == null ? null : row.FinalBoardDisplay) ? "待开发" : row.FinalBoardDisplay; };

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

            RefreshHistoryToolbar();

            var rows = FilterRows(_store.LoadMatchRows());
            foreach (var row in rows)
            {
                row.AnomalyDisplay = ResolveAnomalyDisplay(row) ?? row.AnomalyDisplay;
                row.FinalBoardDisplay = ResolveFinalBoardDisplay(row) ?? row.FinalBoardDisplay;
            }

            RenderHistoryRows(rows);
            UpdateSummary(rows);
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
            _dateLabel.HorizontalAlignment = HorizontalAlignment.Center;
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
                RefreshHistoryToolbar();
                Reload();
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
                _sectionTitle.Text = "历史战绩";
                _historyToolbar.Visibility = Visibility.Visible;
                RefreshHistoryToolbar();

                var viewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = _historyList
                };
                _contentHost.Child = viewer;
                Reload();
                return;
            }

            _historyToolbar.Visibility = Visibility.Collapsed;
            if (_currentSection == SidebarSection.Races)
                _sectionTitle.Text = "种族数据";
            else if (_currentSection == SidebarSection.Heroes)
                _sectionTitle.Text = "英雄数据";
            else
                _sectionTitle.Text = "对局数据";
            _contentHost.Child = BuildPlaceholder();
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
                _historyList.Children.Add(BuildHistoryCard(row));
        }

        private UIElement BuildHistoryCard(BgMatchRow row)
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

            stack.Children.Add(new TextBlock
            {
                Text = row.HeroName,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });

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
            Reload();
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
                menu.Items.Add(new MenuItem
                {
                    Header = "暂无更多版本数据",
                    IsEnabled = false
                });
            }
            else
            {
                foreach (var archive in archives)
                {
                    var item = new MenuItem
                    {
                        Header = archive.DisplayName,
                        IsCheckable = true,
                        IsChecked = string.Equals(archive.Key, _store.CurrentArchive == null ? null : _store.CurrentArchive.Key, StringComparison.OrdinalIgnoreCase)
                    };
                    item.Click += delegate
                    {
                        _store.SetArchiveByKey(archive.Key);
                        RefreshVersionButton();
                        RefreshHistoryToolbar();
                        Reload();
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
            {
                return rows
                    .Select(x => x.TimestampLocal.Date)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();
            }

            return rows
                .Select(x => GetWeekStart(x.TimestampLocal))
                .Distinct()
                .OrderBy(x => x)
                .ToList();
        }

        private void OpenRowDetails(BgMatchRow row)
        {
            OpenMatchDetailRequested?.Invoke(row.MatchId);
            MessageBox.Show("对局详情页待开发，接口已预留。\nmatchId=" + row.MatchId, "酒馆数据分析", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static DateTime GetWeekStart(DateTime date)
        {
            var diff = ((int)date.DayOfWeek + 6) % 7;
            return date.Date.AddDays(-diff);
        }
    }
}
