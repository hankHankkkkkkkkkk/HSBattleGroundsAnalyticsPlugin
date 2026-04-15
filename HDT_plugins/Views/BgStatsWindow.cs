
using HDTplugins.Localization;
using HDTplugins.Models;
using HDTplugins.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
            Matches,
            Settings
        }

        private enum HistoryRange
        {
            Season,
            Week,
            Day
        }

        private enum RaceSortColumn
        {
            AveragePlacement,
            Race,
            PickRate,
            FirstRate,
            ScoreRate
        }

        private enum HeroSortColumn
        {
            Picks,
            AveragePlacement,
            Contribution,
            PickRate
        }

        private enum MatchStatsPage
        {
            TavernTempo,
            Trinkets,
            Timewarp,
            Quests
        }

        private enum SettingsPage
        {
            Main,
            TagManagement
        }

        private readonly StatsStore _store;
        private readonly PluginSettingsService _settingsService;
        private readonly Dictionary<SidebarSection, Button> _sectionButtons = new Dictionary<SidebarSection, Button>();
        private readonly Dictionary<HistoryRange, Button> _rangeButtons = new Dictionary<HistoryRange, Button>();
        private readonly Dictionary<string, FrameworkElement> _historyCards = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase);
        private readonly Button _versionButton;
        private readonly Button _accountButton;
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
        private RaceSortColumn _raceSortColumn = RaceSortColumn.AveragePlacement;
        private bool _raceSortDescending;
        private string _expandedRaceCode;
        private HeroSortColumn _heroSortColumn = HeroSortColumn.Picks;
        private bool _heroSortDescending = true;
        private string _expandedHeroCardId;
        private MatchStatsPage _currentMatchStatsPage = MatchStatsPage.TavernTempo;
        private SettingsPage _currentSettingsPage = SettingsPage.Main;
        private TrinketFilter _currentTrinketFilter = TrinketFilter.All;
        private TimewarpFilter _currentTimewarpFilter = TimewarpFilter.All;
        private DateTime _anchorDate = DateTime.Today;
        private static readonly Brush WindowBackgroundBrush = CreateBrush(32, 34, 38);
        private static readonly Brush SidebarBackgroundBrush = CreateBrush(43, 46, 52);
        private static readonly Brush SidebarBorderBrush = CreateBrush(68, 72, 79);
        private static readonly Brush MainPanelBackgroundBrush = CreateBrush(38, 41, 47);
        private static readonly Brush SurfaceBrush = CreateBrush(49, 53, 60);
        private static readonly Brush SurfaceAltBrush = CreateBrush(58, 63, 71);
        private static readonly Brush SurfaceHoverBrush = CreateBrush(66, 71, 80);
        private static readonly Brush AccentBrush = CreateBrush(94, 146, 214);
        private static readonly Brush AccentHoverBrush = CreateBrush(117, 164, 224);
        private static readonly Brush BorderSubtleBrush = CreateBrush(84, 89, 99);
        private static readonly Brush BorderStrongBrush = CreateBrush(106, 112, 123);
        private static readonly Brush PrimaryTextBrush = CreateBrush(243, 245, 248);
        private static readonly Brush SecondaryTextBrush = CreateBrush(203, 209, 216);
        private static readonly Brush MutedTextBrush = CreateBrush(148, 155, 166);
        private static readonly Brush PositiveValueBrush = CreateBrush(110, 188, 124);
        private static readonly Brush NegativeValueBrush = CreateBrush(224, 110, 104);
        private static readonly Brush NeutralValueBrush = CreateBrush(158, 165, 174);
        private static readonly Brush LightForegroundBrush = PrimaryTextBrush;
        private static readonly Brush DarkForegroundBrush = SecondaryTextBrush;
        private static readonly CornerRadius PanelCornerRadius = new CornerRadius(20);
        private static readonly CornerRadius CardCornerRadius = new CornerRadius(14);
        private static readonly CornerRadius ChipCornerRadius = new CornerRadius(10);
        private static readonly CornerRadius SmallCornerRadius = new CornerRadius(8);
        private static readonly Thickness DefaultBorderThickness = new Thickness(1);
        private static readonly Effect PanelShadowEffect = CreateShadowEffect(18, 0.20, 0, 6);
        private static readonly Effect CardShadowEffect = CreateShadowEffect(12, 0.16, 0, 4);

        public event Action<string> OpenMatchDetailRequested;

        public Func<BgMatchRow, string> ResolveAnomalyDisplay { get; set; }
        public Func<BgMatchRow, string> ResolveFinalBoardDisplay { get; set; }

        public BgStatsWindow(StatsStore store, PluginSettingsService settingsService)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            ResolveAnomalyDisplay = row => string.IsNullOrWhiteSpace(row?.AnomalyDisplay) ? Loc.S("Common_Todo") : row.AnomalyDisplay;
            ResolveFinalBoardDisplay = row => string.IsNullOrWhiteSpace(row?.FinalBoardDisplay) ? Loc.S("Common_Todo") : row.FinalBoardDisplay;
            Width = 1120;
            Height = 700;
            MinWidth = 980;
            MinHeight = 560;
            Background = WindowBackgroundBrush;
            Foreground = PrimaryTextBrush;
            FontFamily = new FontFamily("Segoe UI");
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ConfigureWindowResources();

            _versionButton = new Button();
            _accountButton = new Button();
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
                ApplyLocalization(false);
            };
            Closed += delegate { LocalizationService.LanguageChanged -= OnLanguageChanged; };
            LocalizationService.LanguageChanged += OnLanguageChanged;
            ApplyLocalization(false);
        }

        private void ConfigureWindowResources()
        {
            Resources[typeof(ComboBox)] = CreateComboBoxStyle();
            Resources[typeof(ComboBoxItem)] = CreateComboBoxItemStyle();
            Resources[typeof(CheckBox)] = CreateCheckBoxStyle();
            Resources[typeof(ContextMenu)] = CreateContextMenuStyle();
            Resources[typeof(MenuItem)] = CreateMenuItemStyle();
            Resources[typeof(ToolTip)] = CreateToolTipStyle();
            Resources[typeof(ScrollBar)] = CreateScrollBarStyle();
        }

        private static Style CreateComboBoxStyle()
        {
            var style = new Style(typeof(ComboBox));
            style.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceAltBrush));
            style.Setters.Add(new Setter(Control.ForegroundProperty, PrimaryTextBrush));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, BorderSubtleBrush));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, DefaultBorderThickness));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 8, 14, 8)));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 14.0));
            style.Setters.Add(new Setter(Control.MinHeightProperty, 38.0));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateComboBoxTemplate()));
            return style;
        }

        private static Style CreateComboBoxItemStyle()
        {
            var style = new Style(typeof(ComboBoxItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceBrush));
            style.Setters.Add(new Setter(Control.ForegroundProperty, PrimaryTextBrush));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 9, 14, 9)));
            style.Setters.Add(new Setter(Control.TemplateProperty, CreateComboBoxItemTemplate()));
            style.Triggers.Add(new Trigger
            {
                Property = UIElement.IsMouseOverProperty,
                Value = true,
                Setters =
                {
                    new Setter(Control.BackgroundProperty, SurfaceHoverBrush),
                    new Setter(Control.ForegroundProperty, PrimaryTextBrush)
                }
            });
            style.Triggers.Add(new Trigger
            {
                Property = ListBoxItem.IsSelectedProperty,
                Value = true,
                Setters =
                {
                    new Setter(Control.BackgroundProperty, AccentBrush),
                    new Setter(Control.ForegroundProperty, PrimaryTextBrush)
                }
            });
            return style;
        }

        private static ControlTemplate CreateComboBoxTemplate()
        {
            var template = new ControlTemplate(typeof(ComboBox));

            var root = new FrameworkElementFactory(typeof(Grid));

            var toggleButton = new FrameworkElementFactory(typeof(ToggleButton));
            toggleButton.Name = "ToggleButton";
            toggleButton.SetValue(ToggleButton.FocusableProperty, false);
            toggleButton.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("IsDropDownOpen")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent),
                Mode = System.Windows.Data.BindingMode.TwoWay
            });
            toggleButton.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            toggleButton.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            toggleButton.SetValue(Control.CursorProperty, Cursors.Hand);
            toggleButton.SetValue(Control.FocusVisualStyleProperty, null);

            var toggleBorder = new FrameworkElementFactory(typeof(Border));
            toggleBorder.Name = "OuterBorder";
            toggleBorder.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            toggleBorder.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            toggleBorder.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            toggleBorder.SetValue(Border.CornerRadiusProperty, SmallCornerRadius);
            toggleBorder.SetValue(FrameworkElement.MinHeightProperty, 38.0);

            var layoutPanel = new FrameworkElementFactory(typeof(DockPanel));

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.Name = "ContentSite";
            contentPresenter.SetValue(FrameworkElement.MarginProperty, new Thickness(14, 0, 10, 0));
            contentPresenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            contentPresenter.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding("SelectionBoxItem")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            contentPresenter.SetBinding(ContentPresenter.ContentTemplateProperty, new System.Windows.Data.Binding("SelectionBoxItemTemplate")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            contentPresenter.SetValue(TextElement.ForegroundProperty, PrimaryTextBrush);
            layoutPanel.AppendChild(contentPresenter);

            var arrow = new FrameworkElementFactory(typeof(Path));
            arrow.Name = "Arrow";
            arrow.SetValue(DockPanel.DockProperty, Dock.Right);
            arrow.SetValue(Path.DataProperty, Geometry.Parse("M 0 0 L 4 4 L 8 0"));
            arrow.SetValue(Shape.StrokeProperty, SecondaryTextBrush);
            arrow.SetValue(Shape.StrokeThicknessProperty, 1.8);
            arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 14, 0));
            arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
            layoutPanel.AppendChild(arrow);

            toggleBorder.AppendChild(layoutPanel);
            toggleButton.AppendChild(toggleBorder);
            root.AppendChild(toggleButton);

            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.Name = "Popup";
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.FocusableProperty, false);
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetBinding(Popup.IsOpenProperty, new System.Windows.Data.Binding("IsDropDownOpen")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent),
                Mode = System.Windows.Data.BindingMode.TwoWay
            });

            var popupBorder = new FrameworkElementFactory(typeof(Border));
            popupBorder.SetValue(Border.BackgroundProperty, SurfaceBrush);
            popupBorder.SetValue(Border.BorderBrushProperty, BorderStrongBrush);
            popupBorder.SetValue(Border.BorderThicknessProperty, DefaultBorderThickness);
            popupBorder.SetValue(Border.CornerRadiusProperty, SmallCornerRadius);
            popupBorder.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 6, 0, 0));
            popupBorder.SetValue(FrameworkElement.MinWidthProperty, 160.0);
            popupBorder.SetBinding(FrameworkElement.WidthProperty, new System.Windows.Data.Binding("ActualWidth")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });

            var scrollViewer = new FrameworkElementFactory(typeof(ScrollViewer));
            scrollViewer.SetValue(FrameworkElement.MaxHeightProperty, 280.0);
            scrollViewer.SetValue(ScrollViewer.CanContentScrollProperty, true);

            var itemsPresenter = new FrameworkElementFactory(typeof(StackPanel));
            itemsPresenter.SetValue(Panel.IsItemsHostProperty, true);
            scrollViewer.AppendChild(itemsPresenter);
            popupBorder.AppendChild(scrollViewer);
            popup.AppendChild(popupBorder);
            root.AppendChild(popup);

            template.VisualTree = root;

            var trigger = new Trigger { Property = ComboBox.IsDropDownOpenProperty, Value = true };
            trigger.Setters.Add(new Setter(Border.BorderBrushProperty, AccentHoverBrush, "OuterBorder"));
            trigger.Setters.Add(new Setter(UIElement.RenderTransformProperty, new RotateTransform(180), "Arrow"));
            trigger.Setters.Add(new Setter(Shape.StrokeProperty, PrimaryTextBrush, "Arrow"));
            template.Triggers.Add(trigger);

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, SurfaceHoverBrush, "OuterBorder"));
            hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, BorderStrongBrush, "OuterBorder"));
            template.Triggers.Add(hoverTrigger);

            return template;
        }

        private static ControlTemplate CreateComboBoxItemTemplate()
        {
            var template = new ControlTemplate(typeof(ComboBoxItem));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "ItemBorder";
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 2, 6, 2));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding("Content")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            presenter.SetBinding(TextElement.ForegroundProperty, new System.Windows.Data.Binding("Foreground")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            border.AppendChild(presenter);

            template.VisualTree = border;
            return template;
        }

        private static Style CreateCheckBoxStyle()
        {
            var style = new Style(typeof(CheckBox));
            style.Setters.Add(new Setter(Control.ForegroundProperty, PrimaryTextBrush));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 15.0));
            return style;
        }

        private static Style CreateContextMenuStyle()
        {
            var style = new Style(typeof(ContextMenu));
            style.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceBrush));
            style.Setters.Add(new Setter(Control.ForegroundProperty, PrimaryTextBrush));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, BorderSubtleBrush));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, DefaultBorderThickness));
            return style;
        }

        private static Style CreateMenuItemStyle()
        {
            var style = new Style(typeof(MenuItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceBrush));
            style.Setters.Add(new Setter(Control.ForegroundProperty, PrimaryTextBrush));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
            return style;
        }

        private static Style CreateToolTipStyle()
        {
            var style = new Style(typeof(ToolTip));
            style.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceBrush));
            style.Setters.Add(new Setter(Control.ForegroundProperty, PrimaryTextBrush));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, BorderSubtleBrush));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, DefaultBorderThickness));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
            return style;
        }

        private static Style CreateScrollBarStyle()
        {
            var style = new Style(typeof(ScrollBar));
            style.Setters.Add(new Setter(Control.BackgroundProperty, SurfaceBrush));
            style.Setters.Add(new Setter(Control.WidthProperty, 10.0));
            return style;
        }

        private static void ApplyNavigationButtonChrome(Button button)
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            button.Foreground = SecondaryTextBrush;
            button.Cursor = Cursors.Hand;
        }

        private static void ApplyChipButtonChrome(Button button, bool isActive)
        {
            button.Background = isActive ? AccentBrush : SurfaceAltBrush;
            button.Foreground = PrimaryTextBrush;
            button.BorderBrush = isActive ? AccentHoverBrush : BorderSubtleBrush;
            button.BorderThickness = DefaultBorderThickness;
            button.Cursor = Cursors.Hand;
            button.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
        }

        private static Border CreatePanelBorder(Brush background, CornerRadius cornerRadius, Thickness padding, Thickness? margin = null)
        {
            return new Border
            {
                Background = background,
                BorderBrush = BorderSubtleBrush,
                BorderThickness = DefaultBorderThickness,
                CornerRadius = cornerRadius,
                Padding = padding,
                Margin = margin ?? new Thickness(0),
                Effect = PanelShadowEffect
            };
        }

        private static Border CreateCardBorder(Brush background, Thickness padding, Thickness? margin = null)
        {
            return new Border
            {
                Background = background,
                BorderBrush = BorderSubtleBrush,
                BorderThickness = DefaultBorderThickness,
                CornerRadius = CardCornerRadius,
                Padding = padding,
                Margin = margin ?? new Thickness(0),
                Effect = CardShadowEffect
            };
        }

        private static void StyleSummaryLabel(Label label)
        {
            label.Background = SurfaceAltBrush;
            label.Foreground = PrimaryTextBrush;
            label.BorderBrush = BorderSubtleBrush;
            label.BorderThickness = DefaultBorderThickness;
            label.Padding = new Thickness(10, 4, 10, 4);
            label.FontSize = 14;
            label.FontWeight = FontWeights.SemiBold;
        }

        private static DropShadowEffect CreateShadowEffect(double blurRadius, double opacity, double direction, double shadowDepth)
        {
            return new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = blurRadius,
                Opacity = opacity,
                Direction = direction,
                ShadowDepth = shadowDepth
            };
        }

        public void Reload()
        {
            RefreshVersionButton();
            RefreshAccountButton();
            if (_currentSection == SidebarSection.Settings || _currentSection == SidebarSection.Races || _currentSection == SidebarSection.Heroes)
            {
                RebuildContent();
                return;
            }

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
            else
                _store.RefreshLatestRecordedArchiveForDisplay();

            _currentMatchStatsPage = _store.ShouldDefaultToTrinketStatsPage()
                ? MatchStatsPage.Trinkets
                : MatchStatsPage.TavernTempo;

            RefreshVersionButton();
            RefreshHistoryToolbar();
            Reload();
        }

        public void ShowSettings()
        {
            _settingsService.Reload();
            _selectedMatchId = null;
            _currentSettingsPage = SettingsPage.Main;
            _currentSection = SidebarSection.Settings;
            RefreshSectionButtons();
            RebuildContent();
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            ApplyLocalization(true);
        }

        private void ApplyLocalization(bool rebuildContent)
        {
            Title = Loc.S("Plugin_Name");
            RefreshSectionButtonTexts();
            RefreshRangeButtonTexts();
            RefreshSectionButtons();
            RefreshVersionButton();
            RefreshAccountButton();
            RefreshHistoryToolbar();
            _sectionTitle.Text = GetSectionTitle();

            if (rebuildContent)
                RebuildContent();
        }

        private void RefreshSectionButtonTexts()
        {
            SetButtonContent(_sectionButtons, SidebarSection.History, "Sidebar_History");
            SetButtonContent(_sectionButtons, SidebarSection.Races, "Sidebar_Races");
            SetButtonContent(_sectionButtons, SidebarSection.Heroes, "Sidebar_Heroes");
            SetButtonContent(_sectionButtons, SidebarSection.Matches, "Sidebar_Matches");
            SetButtonContent(_sectionButtons, SidebarSection.Settings, "Sidebar_Settings");
        }

        private void RefreshRangeButtonTexts()
        {
            SetButtonContent(_rangeButtons, HistoryRange.Season, "HistoryMatches_SeasonRange");
            SetButtonContent(_rangeButtons, HistoryRange.Week, "HistoryMatches_WeekRange");
            SetButtonContent(_rangeButtons, HistoryRange.Day, "HistoryMatches_DayRange");
        }

        private static void SetButtonContent<T>(IDictionary<T, Button> buttons, T key, string resourceKey)
        {
            if (buttons.TryGetValue(key, out var button))
                button.Content = Loc.S(resourceKey);
        }

        private string GetSectionTitle()
        {
            if (_currentSection == SidebarSection.History)
                return string.IsNullOrWhiteSpace(_selectedMatchId) ? Loc.S("HistoryMatches_Title") : Loc.S("MatchDetail_Title");

            if (_currentSection == SidebarSection.Races)
                return Loc.S("Sidebar_Races");

            if (_currentSection == SidebarSection.Heroes)
                return Loc.S("Sidebar_Heroes");

            if (_currentSection == SidebarSection.Settings)
                return Loc.S("Settings_Header");

            return Loc.S("Sidebar_Matches");
        }

        private Border BuildSidebar()
        {
            var border = CreatePanelBorder(SidebarBackgroundBrush, PanelCornerRadius, new Thickness(16, 18, 16, 18));
            border.BorderBrush = SidebarBorderBrush;

            var dock = new DockPanel();
            border.Child = dock;

            _accountButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            _accountButton.Padding = new Thickness(10, 12, 10, 12);
            _accountButton.Margin = new Thickness(0, 22, 0, 0);
            ApplyNavigationButtonChrome(_accountButton);
            _accountButton.Click += delegate { OpenAccountMenu(); };
            DockPanel.SetDock(_accountButton, Dock.Bottom);
            dock.Children.Add(_accountButton);

            var sidebarGrid = new Grid();
            sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(118) });
            sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            DockPanel.SetDock(sidebarGrid, Dock.Top);
            dock.Children.Add(sidebarGrid);

            _versionButton.HorizontalContentAlignment = HorizontalAlignment.Left;
            _versionButton.Padding = new Thickness(10, 12, 10, 12);
            _versionButton.Margin = new Thickness(0, 0, 0, 0);
            ApplyNavigationButtonChrome(_versionButton);
            _versionButton.VerticalAlignment = VerticalAlignment.Stretch;
            _versionButton.Click += delegate { OpenVersionMenu(); };
            Grid.SetRow(_versionButton, 0);
            sidebarGrid.Children.Add(_versionButton);

            var navigationStack = new StackPanel { Margin = new Thickness(0, 34, 0, 0) };
            navigationStack.Children.Add(CreateSectionButton(SidebarSection.History, Loc.S("Sidebar_History")));
            navigationStack.Children.Add(CreateSectionButton(SidebarSection.Races, Loc.S("Sidebar_Races")));
            navigationStack.Children.Add(CreateSectionButton(SidebarSection.Heroes, Loc.S("Sidebar_Heroes")));
            navigationStack.Children.Add(CreateSectionButton(SidebarSection.Matches, Loc.S("Sidebar_Matches")));
            navigationStack.Children.Add(CreateSectionButton(SidebarSection.Settings, Loc.S("Sidebar_Settings")));
            Grid.SetRow(navigationStack, 1);
            sidebarGrid.Children.Add(navigationStack);
            return border;
        }

        private Border BuildMainPanel()
        {
            var border = CreatePanelBorder(MainPanelBackgroundBrush, PanelCornerRadius, new Thickness(24, 22, 24, 22));

            var dock = new DockPanel();
            border.Child = dock;

            var header = new StackPanel { Orientation = Orientation.Vertical };
            DockPanel.SetDock(header, Dock.Top);
            dock.Children.Add(header);

            _sectionTitle.Text = Loc.S("HistoryMatches_Title");
            _sectionTitle.FontSize = 22;
            _sectionTitle.FontWeight = FontWeights.SemiBold;
            _sectionTitle.Foreground = PrimaryTextBrush;
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
            rangePanel.Children.Add(CreateRangeButton(HistoryRange.Season, Loc.S("HistoryMatches_SeasonRange")));
            rangePanel.Children.Add(CreateRangeButton(HistoryRange.Week, Loc.S("HistoryMatches_WeekRange")));
            rangePanel.Children.Add(CreateRangeButton(HistoryRange.Day, Loc.S("HistoryMatches_DayRange")));
            Grid.SetRow(rangePanel, 0);
            Grid.SetColumn(rangePanel, 0);
            _historyToolbar.Children.Add(rangePanel);

            _dateNavigator.Orientation = Orientation.Horizontal;
            _dateNavigator.VerticalAlignment = VerticalAlignment.Center;
            _prevDateButton.Click += delegate { ShiftAnchor(-1); };
            _dateNavigator.Children.Add(_prevDateButton);

            _dateLabel.MinWidth = 220;
            _dateLabel.Margin = new Thickness(10, 0, 10, 0);
            _dateLabel.HorizontalContentAlignment = HorizontalAlignment.Center;
            StyleSummaryLabel(_dateLabel);
            _dateNavigator.Children.Add(_dateLabel);

            _nextDateButton.Click += delegate { ShiftAnchor(1); };
            _dateNavigator.Children.Add(_nextDateButton);

            Grid.SetRow(_dateNavigator, 0);
            Grid.SetColumn(_dateNavigator, 2);
            _historyToolbar.Children.Add(_dateNavigator);

            _summaryText.Margin = new Thickness(0, 10, 0, 0);
            StyleSummaryLabel(_summaryText);
            _summaryText.Padding = new Thickness(12, 4, 12, 4);
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
                Foreground = SecondaryTextBrush
            };
            ApplyNavigationButtonChrome(button);
            button.Click += delegate
            {
                _currentSection = section;
                if (section != SidebarSection.History)
                    _selectedMatchId = null;
                if (section == SidebarSection.Races)
                {
                    _raceSortColumn = RaceSortColumn.AveragePlacement;
                    _raceSortDescending = false;
                    _expandedRaceCode = null;
                }
                if (section == SidebarSection.Heroes)
                {
                    LoadHeroSortPreference();
                    _expandedHeroCardId = null;
                }
                if (section == SidebarSection.Matches)
                    _currentMatchStatsPage = _store.ShouldDefaultToTrinketStatsPage() ? MatchStatsPage.Trinkets : MatchStatsPage.TavernTempo;
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
                Foreground = PrimaryTextBrush
            };
            ApplyChipButtonChrome(button, _currentRange == range);
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
            var button = new Button
            {
                Content = text,
                Width = 26,
                Height = 26,
                Foreground = PrimaryTextBrush,
                FontWeight = FontWeights.Bold
            };
            ApplyChipButtonChrome(button, false);
            return button;
        }

        private void RebuildContent()
        {
            if (_currentSection == SidebarSection.History)
            {
                _sectionTitle.Text = GetSectionTitle();
                _historyToolbar.Visibility = string.IsNullOrWhiteSpace(_selectedMatchId) ? Visibility.Visible : Visibility.Collapsed;
                if (string.IsNullOrWhiteSpace(_selectedMatchId))
                    RenderHistoryView();
                else
                    ShowMatchDetails(_selectedMatchId, false);
                return;
            }

            if (_currentSection == SidebarSection.Settings)
            {
                _historyToolbar.Visibility = Visibility.Collapsed;
                _sectionTitle.Text = GetSectionTitle();
                _contentHost.Child = BuildSettingsView();
                return;
            }

            if (_currentSection == SidebarSection.Races)
            {
                _historyToolbar.Visibility = Visibility.Collapsed;
                _sectionTitle.Text = GetSectionTitle();
                _contentHost.Child = BuildRaceStatsView();
                return;
            }

            if (_currentSection == SidebarSection.Heroes)
            {
                _historyToolbar.Visibility = Visibility.Collapsed;
                _sectionTitle.Text = GetSectionTitle();
                _contentHost.Child = BuildHeroStatsView();
                return;
            }

            if (_currentSection == SidebarSection.Matches)
            {
                _historyToolbar.Visibility = Visibility.Collapsed;
                _sectionTitle.Text = GetSectionTitle();
                _contentHost.Child = BuildMatchStatsView();
                return;
            }

            _historyToolbar.Visibility = Visibility.Collapsed;
            _sectionTitle.Text = GetSectionTitle();
            _contentHost.Child = BuildPlaceholder();
        }

        private void RenderHistoryView()
        {
            _sectionTitle.Text = Loc.S("HistoryMatches_Title");
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

        private UIElement BuildSettingsView()
        {
            _settingsService.Reload();
            if (_currentSettingsPage == SettingsPage.TagManagement)
                return BuildTagManagementView();

            var currentLanguage = string.IsNullOrWhiteSpace(_settingsService.Settings.Language)
                ? LocalizationService.CurrentCulture.Name
                : LocalizationService.NormalizeCulture(_settingsService.Settings.Language).Name;
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var root = new Grid();
            scrollViewer.Content = root;

            var contentGrid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(contentGrid);

            var leftColumn = new StackPanel();
            var rightColumn = new StackPanel();
            Grid.SetColumn(rightColumn, 2);
            contentGrid.Children.Add(leftColumn);
            contentGrid.Children.Add(rightColumn);

            leftColumn.Children.Add(CreateSettingsLabel(Loc.S("Settings_LanguageLabel")));

            var languageComboBox = new ComboBox
            {
                Margin = new Thickness(0, 0, 0, 20),
                Width = 300,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var zhCnItem = new ComboBoxItem { Content = Loc.S("Settings_LanguageZhCn"), Tag = LocalizationService.ChineseCultureName };
            var enUsItem = new ComboBoxItem { Content = Loc.S("Settings_LanguageEnUs"), Tag = LocalizationService.DefaultCultureName };
            languageComboBox.Items.Add(zhCnItem);
            languageComboBox.Items.Add(enUsItem);
            languageComboBox.SelectedItem = string.Equals(currentLanguage, LocalizationService.ChineseCultureName, StringComparison.OrdinalIgnoreCase)
                ? (object)zhCnItem
                : enUsItem;
            languageComboBox.SelectionChanged += delegate
            {
                if (!(languageComboBox.SelectedItem is ComboBoxItem selectedItem) || !(selectedItem.Tag is string selectedLanguage))
                    return;

                SaveSettings(language: selectedLanguage, applyLanguage: true);
            };
            leftColumn.Children.Add(languageComboBox);

            rightColumn.Children.Add(CreateSettingsLabel(Loc.S("Settings_ScoreLineLabel")));

            var scoreLineComboBox = new ComboBox
            {
                Margin = new Thickness(0, 0, 0, 20),
                Width = 300,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var normalizedScoreLine = _settingsService.Settings.GetNormalizedScoreLine();
            var scoreLineTop4 = new ComboBoxItem { Content = Loc.S("Settings_ScoreLineTop4"), Tag = 4.5 };
            var scoreLineTop3 = new ComboBoxItem { Content = Loc.S("Settings_ScoreLineTop3"), Tag = 3.5 };
            var scoreLineTop2 = new ComboBoxItem { Content = Loc.S("Settings_ScoreLineTop2"), Tag = 2.5 };
            scoreLineComboBox.Items.Add(scoreLineTop4);
            scoreLineComboBox.Items.Add(scoreLineTop3);
            scoreLineComboBox.Items.Add(scoreLineTop2);
            scoreLineComboBox.SelectedItem = Math.Abs(normalizedScoreLine - 2.5) < 0.01
                ? (object)scoreLineTop2
                : Math.Abs(normalizedScoreLine - 3.5) < 0.01
                    ? scoreLineTop3
                    : scoreLineTop4;
            scoreLineComboBox.SelectionChanged += delegate
            {
                if (!(scoreLineComboBox.SelectedItem is ComboBoxItem selectedItem) || !(selectedItem.Tag is double selectedScoreLine))
                    return;

                SaveSettings(scoreLine: selectedScoreLine, refreshCurrentView: true);
            };
            rightColumn.Children.Add(scoreLineComboBox);

            leftColumn.Children.Add(CreateSettingsLabel(Loc.S("Settings_HeroSortLabel")));

            var heroSortComboBox = new ComboBox
            {
                Margin = new Thickness(0, 0, 0, 24),
                Width = 300,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var heroSortPicks = new ComboBoxItem { Content = Loc.S("HeroStats_HeaderPicks"), Tag = HeroSortColumn.Picks };
            var heroSortAveragePlacement = new ComboBoxItem { Content = Loc.S("HeroStats_HeaderAvgPlacement"), Tag = HeroSortColumn.AveragePlacement };
            var heroSortContribution = new ComboBoxItem { Content = Loc.S("HeroStats_HeaderContribution"), Tag = HeroSortColumn.Contribution };
            var heroSortPickRate = new ComboBoxItem { Content = Loc.S("HeroStats_HeaderPickRate"), Tag = HeroSortColumn.PickRate };
            heroSortComboBox.Items.Add(heroSortPicks);
            heroSortComboBox.Items.Add(heroSortAveragePlacement);
            heroSortComboBox.Items.Add(heroSortContribution);
            heroSortComboBox.Items.Add(heroSortPickRate);
            heroSortComboBox.SelectedItem = GetHeroSortComboItem(_settingsService.Settings.HeroStatsDefaultSort, heroSortPicks, heroSortAveragePlacement, heroSortContribution, heroSortPickRate);
            heroSortComboBox.SelectionChanged += delegate
            {
                if (!(heroSortComboBox.SelectedItem is ComboBoxItem selectedItem) || !(selectedItem.Tag is HeroSortColumn selectedHeroSort))
                    return;

                SaveSettings(heroSortColumn: selectedHeroSort, refreshCurrentView: _currentSection == SidebarSection.Heroes);
            };
            leftColumn.Children.Add(heroSortComboBox);

            var autoOpenCheckBox = new CheckBox
            {
                Content = Loc.S("Settings_AutoOpenOnStartup"),
                IsChecked = _settingsService.Settings.AutoOpenOnStartup,
                Foreground = PrimaryTextBrush,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 8)
            };
            autoOpenCheckBox.Checked += delegate { SaveSettings(autoOpenOnStartup: true); };
            autoOpenCheckBox.Unchecked += delegate { SaveSettings(autoOpenOnStartup: false); };
            rightColumn.Children.Add(autoOpenCheckBox);

            rightColumn.Children.Add(new TextBlock
            {
                Text = Loc.S("Settings_AutoOpenHint"),
                Foreground = MutedTextBrush,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 24)
            });

            var manageTagsButton = new Button
            {
                Content = Loc.S("Settings_ManageTags"),
                Height = 38,
                MinWidth = 160,
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = SurfaceAltBrush,
                Foreground = PrimaryTextBrush,
                BorderBrush = BorderSubtleBrush,
                BorderThickness = DefaultBorderThickness,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(18, 0, 18, 0)
            };
            manageTagsButton.Click += delegate
            {
                _currentSettingsPage = SettingsPage.TagManagement;
                RebuildContent();
            };
            leftColumn.Children.Add(manageTagsButton);

            return scrollViewer;
        }

        private TextBlock CreateSettingsLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = SecondaryTextBrush,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        private void SaveSettings(string language = null, double? scoreLine = null, HeroSortColumn? heroSortColumn = null, bool? autoOpenOnStartup = null, bool applyLanguage = false, bool refreshCurrentView = false)
        {
            var normalizedLanguage = LocalizationService.NormalizeCulture(language ?? _settingsService.Settings.Language).Name;
            _settingsService.Settings.Language = normalizedLanguage;
            _settingsService.Settings.ScoreLine = scoreLine ?? _settingsService.Settings.GetNormalizedScoreLine();
            _settingsService.Settings.HeroStatsDefaultSort = (heroSortColumn ?? ParseHeroSortOrDefault(_settingsService.Settings.HeroStatsDefaultSort)).ToString();

            if (autoOpenOnStartup.HasValue)
                _settingsService.Settings.AutoOpenOnStartup = autoOpenOnStartup.Value;

            _settingsService.Save();
            LoadHeroSortPreference();

            if (applyLanguage)
            {
                LocalizationService.SetLanguage(_settingsService.Settings.Language);
                return;
            }

            if (refreshCurrentView)
                RebuildContent();
        }

        private HeroSortColumn ParseHeroSortOrDefault(string sortValue)
        {
            if (!Enum.TryParse(sortValue, true, out HeroSortColumn sortColumn))
                return HeroSortColumn.Picks;

            return sortColumn;
        }

        private UIElement BuildTagManagementView()
        {
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var panel = new StackPanel();
            scrollViewer.Content = panel;

            var backButton = new Button
            {
                Content = Loc.S("Common_Back"),
                Height = 32,
                MinWidth = 88,
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = SurfaceAltBrush,
                Foreground = PrimaryTextBrush,
                BorderBrush = BorderSubtleBrush,
                BorderThickness = DefaultBorderThickness,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12)
            };
            backButton.Click += delegate
            {
                _currentSettingsPage = SettingsPage.Main;
                RebuildContent();
            };
            panel.Children.Add(backButton);

            panel.Children.Add(new TextBlock
            {
                Text = Loc.S("TagManager_Title"),
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = PrimaryTextBrush,
                Margin = new Thickness(0, 0, 0, 12)
            });

            panel.Children.Add(new TextBlock
            {
                Text = Loc.S("TagManager_Description"),
                Foreground = MutedTextBrush,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 18)
            });

            var addGrid = new Grid { Margin = new Thickness(0, 0, 0, 22) };
            addGrid.ColumnDefinitions.Add(new ColumnDefinition());
            addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panel.Children.Add(addGrid);

            var tagInput = new TextBox
            {
                Height = 38,
                Padding = new Thickness(12, 7, 12, 7),
                Background = SurfaceAltBrush,
                Foreground = PrimaryTextBrush,
                BorderBrush = BorderSubtleBrush,
                BorderThickness = DefaultBorderThickness,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetColumn(tagInput, 0);
            addGrid.Children.Add(tagInput);

            var addButton = new Button
            {
                Content = Loc.S("TagManager_Add"),
                Width = 96,
                Height = 38,
                Margin = new Thickness(12, 0, 0, 0),
                Background = AccentBrush,
                Foreground = PrimaryTextBrush,
                BorderBrush = AccentHoverBrush,
                BorderThickness = DefaultBorderThickness,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            };
            addButton.Click += delegate
            {
                if (_store.AddCustomTag(tagInput.Text))
                {
                    RebuildContent();
                    return;
                }

                MessageBox.Show(this, Loc.S("TagManager_AddFailed"), Loc.S("TagManager_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            };
            Grid.SetColumn(addButton, 1);
            addGrid.Children.Add(addButton);

            var tagDefinitions = _store.GetAvailableTagDefinitions();
            if (tagDefinitions.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = Loc.S("TagManager_Empty"),
                    Foreground = MutedTextBrush,
                    FontSize = 14
                });
                return scrollViewer;
            }

            var tagWrapPanel = new WrapPanel
            {
                Margin = new Thickness(0, 0, 0, 4)
            };
            panel.Children.Add(tagWrapPanel);

            foreach (var tagDefinition in tagDefinitions)
            {
                tagWrapPanel.Children.Add(BuildTagManagementRow(tagDefinition));
            }

            return scrollViewer;
        }

        private UIElement BuildTagManagementRow(LineupTagDefinition tagDefinition)
        {
            var border = CreateCardBorder(SurfaceAltBrush, new Thickness(14, 10, 14, 10), new Thickness(0, 0, 8, 8));
            border.HorizontalAlignment = HorizontalAlignment.Left;
            border.MinWidth = 160;

            var root = new Grid();
            border.Child = root;

            var contentPanel = new StackPanel();
            root.Children.Add(contentPanel);

            contentPanel.Children.Add(new TextBlock
            {
                Text = tagDefinition?.Name ?? string.Empty,
                Foreground = PrimaryTextBrush,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            });

            contentPanel.Children.Add(new TextBlock
            {
                Text = tagDefinition != null && tagDefinition.IsEditable ? Loc.S("TagManager_CustomTag") : Loc.S("TagManager_BuiltInTag"),
                Foreground = MutedTextBrush,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0)
            });

            if (tagDefinition != null && tagDefinition.IsEditable)
            {
                var deleteButton = new Button
                {
                    Content = "×",
                    Width = 22,
                    Height = 22,
                    Padding = new Thickness(0),
                    Background = SurfaceBrush,
                    Foreground = PrimaryTextBrush,
                    BorderBrush = BorderSubtleBrush,
                    BorderThickness = DefaultBorderThickness,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Visibility = Visibility.Collapsed,
                    Cursor = Cursors.Hand
                };
                deleteButton.Click += delegate
                {
                    if (_store.RemoveCustomTag(tagDefinition.Name))
                    {
                        RebuildContent();
                        return;
                    }

                    MessageBox.Show(this, Loc.S("TagManager_DeleteFailed"), Loc.S("TagManager_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
                };
                root.Children.Add(deleteButton);

                border.MouseEnter += delegate { deleteButton.Visibility = Visibility.Visible; };
                border.MouseLeave += delegate { deleteButton.Visibility = Visibility.Collapsed; };
            }

            return border;
        }

        private UIElement BuildRaceStatsView()
        {
            _settingsService.Reload();
            var rows = SortRaceRows(_store.LoadRaceStats(_settingsService.Settings.GetNormalizedScoreLine()));

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var root = new StackPanel();
            scrollViewer.Content = root;

            root.Children.Add(BuildRaceHeaderRow());

            if (rows.Count == 0)
            {
                root.Children.Add(new TextBlock
                {
                    Text = Loc.S("Common_NoData"),
                    Foreground = MutedTextBrush,
                    FontSize = 15,
                    Margin = new Thickness(0, 20, 0, 0)
                });
                return scrollViewer;
            }

            foreach (var row in rows)
                root.Children.Add(BuildRaceRow(row));

            return scrollViewer;
        }

        private IReadOnlyList<RaceStatsRow> SortRaceRows(IReadOnlyList<RaceStatsRow> rows)
        {
            var ordered = rows ?? Array.Empty<RaceStatsRow>();
            IOrderedEnumerable<RaceStatsRow> query;

            switch (_raceSortColumn)
            {
                case RaceSortColumn.Race:
                    query = _raceSortDescending
                        ? ordered.OrderBy(x => x.HasData ? 0 : 1).ThenByDescending(x => x.RaceName, StringComparer.CurrentCultureIgnoreCase)
                        : ordered.OrderBy(x => x.HasData ? 0 : 1).ThenBy(x => x.RaceName, StringComparer.CurrentCultureIgnoreCase);
                    break;
                case RaceSortColumn.FirstRate:
                    query = _raceSortDescending
                        ? ordered.OrderBy(x => x.HasData ? 0 : 1).ThenByDescending(x => x.FirstRate)
                        : ordered.OrderBy(x => x.HasData ? 0 : 1).ThenBy(x => x.FirstRate);
                    query = query.ThenBy(x => x.RaceName, StringComparer.CurrentCultureIgnoreCase);
                    break;
                case RaceSortColumn.PickRate:
                    query = _raceSortDescending
                        ? ordered.OrderBy(x => x.HasData ? 0 : 1).ThenByDescending(x => x.PickRate)
                        : ordered.OrderBy(x => x.HasData ? 0 : 1).ThenBy(x => x.PickRate);
                    query = query.ThenBy(x => x.RaceName, StringComparer.CurrentCultureIgnoreCase);
                    break;
                case RaceSortColumn.ScoreRate:
                    query = _raceSortDescending
                        ? ordered.OrderBy(x => x.HasData ? 0 : 1).ThenByDescending(x => x.ScoreRate)
                        : ordered.OrderBy(x => x.HasData ? 0 : 1).ThenBy(x => x.ScoreRate);
                    query = query.ThenBy(x => x.RaceName, StringComparer.CurrentCultureIgnoreCase);
                    break;
                default:
                    query = _raceSortDescending
                        ? ordered.OrderBy(x => x.HasData ? 0 : 1).ThenByDescending(x => x.AveragePlacement ?? double.MinValue)
                        : ordered.OrderBy(x => x.HasData ? 0 : 1).ThenBy(x => x.AveragePlacement ?? double.MaxValue);
                    query = query.ThenBy(x => x.RaceName, StringComparer.CurrentCultureIgnoreCase);
                    break;
            }

            return query.ToList();
        }

        private UIElement BuildRaceHeaderRow()
        {
            var border = CreateCardBorder(SurfaceAltBrush, new Thickness(14, 10, 14, 10), new Thickness(0, 0, 0, 8));

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            border.Child = grid;

            grid.Children.Add(CreateRaceHeaderButton("RaceStats_HeaderRace", RaceSortColumn.Race, 0));
            grid.Children.Add(CreateRaceHeaderButton("RaceStats_HeaderAvgPlacement", RaceSortColumn.AveragePlacement, 1));
            grid.Children.Add(CreateRaceHeaderButton("RaceStats_HeaderPickRate", RaceSortColumn.PickRate, 2));
            grid.Children.Add(CreateRaceHeaderButton("RaceStats_HeaderFirstRate", RaceSortColumn.FirstRate, 3));
            grid.Children.Add(CreateRaceHeaderButton("RaceStats_HeaderScoreRate", RaceSortColumn.ScoreRate, 4));

            return border;
        }

        private UIElement CreateRaceHeaderButton(string resourceKey, RaceSortColumn column, int columnIndex)
        {
            var button = new Button
            {
                Content = Loc.S(resourceKey),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                HorizontalContentAlignment = columnIndex == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Center
            };
            ApplyNavigationButtonChrome(button);
            button.Foreground = PrimaryTextBrush;
            button.Click += delegate
            {
                if (_raceSortColumn == column)
                    _raceSortDescending = !_raceSortDescending;
                else
                {
                    _raceSortColumn = column;
                    _raceSortDescending = column != RaceSortColumn.AveragePlacement;
                }

                _contentHost.Child = BuildRaceStatsView();
            };
            Grid.SetColumn(button, columnIndex);
            return button;
        }

        private UIElement BuildRaceRow(RaceStatsRow row)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

            var summaryBorder = CreateCardBorder(SurfaceBrush, new Thickness(14, 12, 14, 12));
            summaryBorder.BorderBrush = BorderStrongBrush;
            summaryBorder.Cursor = Cursors.Hand;

            var summaryGrid = new Grid();
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            summaryBorder.Child = summaryGrid;

            summaryGrid.Children.Add(CreateRaceSummaryText(row.RaceName, 0, HorizontalAlignment.Left, FontWeights.SemiBold));
            summaryGrid.Children.Add(CreateRaceSummaryText(
                row.HasData ? row.AveragePlacement.Value.ToString("F2", CultureInfo.CurrentCulture) : Loc.S("RaceStats_NoData"),
                1,
                HorizontalAlignment.Center,
                FontWeights.Normal,
                row.HasData ? GetPlacementBrush(row.AveragePlacement.Value) : NeutralValueBrush));
            summaryGrid.Children.Add(CreateRaceSummaryText(FormatRate(row.PickRate, row.HasData), 2, HorizontalAlignment.Center, FontWeights.Normal));
            summaryGrid.Children.Add(CreateRaceSummaryText(FormatRate(row.FirstRate, row.HasData), 3, HorizontalAlignment.Center, FontWeights.Normal));
            summaryGrid.Children.Add(CreateRaceSummaryText(FormatRate(row.ScoreRate, row.HasData), 4, HorizontalAlignment.Center, FontWeights.Normal));

            summaryBorder.MouseLeftButtonUp += delegate
            {
                _expandedRaceCode = string.Equals(_expandedRaceCode, row.RaceCode, StringComparison.OrdinalIgnoreCase) ? null : row.RaceCode;
                _contentHost.Child = BuildRaceStatsView();
            };

            container.Children.Add(summaryBorder);

            if (string.Equals(_expandedRaceCode, row.RaceCode, StringComparison.OrdinalIgnoreCase))
                container.Children.Add(BuildRaceDetail(row));

            return container;
        }

        private UIElement CreateRaceSummaryText(string text, int columnIndex, HorizontalAlignment alignment, FontWeight fontWeight, Brush foreground = null)
        {
            var block = new TextBlock
            {
                Text = text,
                Foreground = foreground ?? DarkForegroundBrush,
                FontSize = 14,
                FontWeight = fontWeight,
                HorizontalAlignment = alignment,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(block, columnIndex);
            return block;
        }

        private UIElement BuildRaceDetail(RaceStatsRow row)
        {
            var border = CreateCardBorder(SurfaceAltBrush, new Thickness(16), new Thickness(0, 2, 0, 0));

            var root = new StackPanel();
            border.Child = root;

            root.Children.Add(new TextBlock
            {
                Text = Loc.F("RaceStats_DetailMatches", row.MatchCount),
                Foreground = LightForegroundBrush,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var detailGrid = new Grid();
            detailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            detailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(detailGrid);

            var leftColumn = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
            leftColumn.Children.Add(BuildRaceDetailSection(Loc.S("RaceStats_BestSynergy"), BuildRaceSynergyList(row.BestSynergies)));
            leftColumn.Children.Add(BuildRaceDetailSection(Loc.S("RaceStats_WorstSynergy"), BuildRaceSynergyList(row.WorstSynergies), false));
            Grid.SetColumn(leftColumn, 0);
            detailGrid.Children.Add(leftColumn);

            var rightColumn = new StackPanel();
            rightColumn.Children.Add(BuildRaceDetailSection(Loc.S("RaceStats_TopCards"), BuildRaceCardUsageList(row.TopCards)));
            rightColumn.Children.Add(BuildRaceDetailSection(Loc.S("RaceStats_TopLineups"), BuildRaceTextValue(FormatTagList(row.TopLineups)), false));
            Grid.SetColumn(rightColumn, 1);
            detailGrid.Children.Add(rightColumn);

            return border;
        }

        private UIElement BuildRaceDetailSection(string title, UIElement content, bool includeBottomMargin = true)
        {
            var stack = new StackPanel { Margin = includeBottomMargin ? new Thickness(0, 0, 0, 10) : new Thickness(0) };
            stack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = LightForegroundBrush,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            });
            if (content is FrameworkElement element)
                element.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(content);
            return stack;
        }

        private string FormatRate(double value, bool hasData)
        {
            return hasData ? value.ToString("P1", CultureInfo.CurrentCulture) : Loc.S("RaceStats_NoData");
        }

        private UIElement BuildRaceSynergyList(IReadOnlyList<RaceSynergyStat> synergies)
        {
            if (synergies == null || synergies.Count == 0)
                return BuildRaceTextValue(Loc.S("RaceStats_NoSynergyData"));

            var stack = new StackPanel();
            foreach (var synergy in synergies)
            {
                var block = new TextBlock
                {
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap
                };
                block.Inlines.Add(CreateRun(synergy.RaceName, LightForegroundBrush));
                block.Inlines.Add(CreateRun(" (", LightForegroundBrush));
                block.Inlines.Add(CreateRun(Loc.S("Common_AvgPlacementLabel"), LightForegroundBrush));
                block.Inlines.Add(CreateRun(" ", LightForegroundBrush));
                block.Inlines.Add(CreateRun(synergy.AveragePlacement.ToString("F2", CultureInfo.CurrentCulture), GetPlacementBrush(synergy.AveragePlacement)));
                block.Inlines.Add(CreateRun(", ", LightForegroundBrush));
                block.Inlines.Add(CreateRun(Loc.S("Common_PlacementDeltaLabel"), LightForegroundBrush));
                block.Inlines.Add(CreateRun(" ", LightForegroundBrush));
                block.Inlines.Add(CreateRun(synergy.PlacementDelta.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture), GetDeltaBrush(-synergy.PlacementDelta)));
                block.Inlines.Add(CreateRun(")", LightForegroundBrush));
                stack.Children.Add(block);
            }

            return stack;
        }

        private UIElement BuildRaceCardUsageList(IReadOnlyList<RaceCardUsage> cards)
        {
            if (cards == null || cards.Count == 0)
                return BuildRaceTextValue(Loc.S("Common_NoData"));

            var grid = new UniformGrid { Columns = 2 };
            foreach (var card in cards.Take(3))
            {
                grid.Children.Add(new TextBlock
                {
                    Text = card.CardName + " - " + card.Rate.ToString("P0", CultureInfo.CurrentCulture),
                    Foreground = LightForegroundBrush,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 12, 6)
                });
            }

            return grid;
        }

        private string FormatTagList(IReadOnlyList<RaceTagUsage> tags)
        {
            if (tags == null || tags.Count == 0)
                return Loc.S("Common_NoData");

            return string.Join(" / ", tags.Take(3).Select(x => Loc.F("RaceStats_TagItemFormat", x.Tag, x.Rate.ToString("P0", CultureInfo.CurrentCulture))));
        }

        private string BuildScoreLineText(double scoreLine)
        {
            if (Math.Abs(scoreLine - 2.5) < 0.01)
                return Loc.S("Settings_ScoreLineTop2");
            if (Math.Abs(scoreLine - 3.5) < 0.01)
                return Loc.S("Settings_ScoreLineTop3");
            return Loc.S("Settings_ScoreLineTop4");
        }

        private object GetHeroSortComboItem(string sortValue, params ComboBoxItem[] items)
        {
            if (!Enum.TryParse(sortValue, true, out HeroSortColumn sortColumn))
                sortColumn = HeroSortColumn.Picks;

            foreach (var item in items)
            {
                if (item?.Tag is HeroSortColumn column && column == sortColumn)
                    return item;
            }

            return items.FirstOrDefault();
        }

        private void LoadHeroSortPreference()
        {
            _settingsService.Reload();
            if (!Enum.TryParse(_settingsService.Settings.HeroStatsDefaultSort, true, out _heroSortColumn))
                _heroSortColumn = HeroSortColumn.Picks;

            _heroSortDescending = _heroSortColumn != HeroSortColumn.AveragePlacement;
        }

        private UIElement BuildHeroStatsView()
        {
            _settingsService.Reload();
            var summary = _store.LoadHeroStats(_settingsService.Settings.GetNormalizedScoreLine());
            var rows = SortHeroRows(summary.Heroes);
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var root = new StackPanel();
            scrollViewer.Content = root;
            root.Children.Add(BuildHeroOverviewCard(summary));
            root.Children.Add(BuildHeroHeaderRow());

            if (rows.Count == 0)
            {
                root.Children.Add(new TextBlock
                {
                    Text = Loc.S("Common_NoData"),
                    Foreground = MutedTextBrush,
                    FontSize = 15,
                    Margin = new Thickness(0, 20, 0, 0)
                });
                return scrollViewer;
            }

            foreach (var row in rows)
                root.Children.Add(BuildHeroRow(row));

            return scrollViewer;
        }

        private IReadOnlyList<HeroStatsRow> SortHeroRows(IReadOnlyList<HeroStatsRow> rows)
        {
            var ordered = rows ?? Array.Empty<HeroStatsRow>();
            switch (_heroSortColumn)
            {
                case HeroSortColumn.AveragePlacement:
                    return (_heroSortDescending
                        ? ordered.OrderByDescending(x => x.AveragePlacement)
                        : ordered.OrderBy(x => x.AveragePlacement))
                        .ThenByDescending(x => x.Picks)
                        .ThenByDescending(x => x.ContributionValue)
                        .ThenBy(x => x.HeroName, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                case HeroSortColumn.Contribution:
                    return (_heroSortDescending
                        ? ordered.OrderByDescending(x => x.ContributionValue)
                        : ordered.OrderBy(x => x.ContributionValue))
                        .ThenByDescending(x => x.Picks)
                        .ThenBy(x => x.AveragePlacement)
                        .ThenBy(x => x.HeroName, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                case HeroSortColumn.PickRate:
                    return (_heroSortDescending
                        ? ordered.OrderByDescending(x => x.PickRate)
                        : ordered.OrderBy(x => x.PickRate))
                        .ThenByDescending(x => x.Picks)
                        .ThenByDescending(x => x.ContributionValue)
                        .ThenBy(x => x.HeroName, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                default:
                    return (_heroSortDescending
                        ? ordered.OrderByDescending(x => x.Picks)
                        : ordered.OrderBy(x => x.Picks))
                        .ThenByDescending(x => x.ContributionValue)
                        .ThenBy(x => x.AveragePlacement)
                        .ThenBy(x => x.HeroName, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
            }
        }

        private UIElement BuildHeroOverviewCard(HeroStatsSummary summary)
        {
            var border = CreateCardBorder(SurfaceAltBrush, new Thickness(14, 10, 14, 10), new Thickness(0, 0, 0, 10));

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            border.Child = grid;

            var summaryBlock = new TextBlock
            {
                FontSize = 14,
                TextAlignment = TextAlignment.Right
            };
            AppendLabelValue(summaryBlock.Inlines, Loc.S("Common_MatchesLabel"), summary.TotalMatches.ToString(CultureInfo.CurrentCulture), LightForegroundBrush);
            AppendSpacer(summaryBlock.Inlines, "  ");
            AppendLabelValue(summaryBlock.Inlines, Loc.S("Common_AvgPlacementLabel"), summary.TotalMatches > 0 ? summary.OverallAveragePlacement.ToString("F2", CultureInfo.CurrentCulture) : "-", summary.TotalMatches > 0 ? GetPlacementBrush(summary.OverallAveragePlacement) : LightForegroundBrush);
            summaryBlock.HorizontalAlignment = HorizontalAlignment.Right;
            grid.Children.Add(summaryBlock);

            return border;
        }

        private UIElement BuildHeroHeaderRow()
        {
            var border = CreateCardBorder(SurfaceAltBrush, new Thickness(14, 10, 14, 10), new Thickness(0, 0, 0, 8));

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.4, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            border.Child = grid;

            grid.Children.Add(CreateHeroHeaderLabel("HeroStats_HeaderHero", 0, false));
            grid.Children.Add(CreateHeroHeaderButton("HeroStats_HeaderPicks", HeroSortColumn.Picks, 1));
            grid.Children.Add(CreateHeroHeaderButton("HeroStats_HeaderAvgPlacement", HeroSortColumn.AveragePlacement, 2));
            grid.Children.Add(CreateHeroHeaderButton("HeroStats_HeaderPickRate", HeroSortColumn.PickRate, 3));
            grid.Children.Add(CreateHeroHeaderButton("HeroStats_HeaderContribution", HeroSortColumn.Contribution, 4));

            return border;
        }

        private UIElement CreateHeroHeaderLabel(string resourceKey, int columnIndex, bool centered)
        {
            var block = new TextBlock
            {
                Text = Loc.S(resourceKey),
                Foreground = PrimaryTextBrush,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = centered ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(block, columnIndex);
            return block;
        }

        private UIElement CreateHeroHeaderButton(string resourceKey, HeroSortColumn column, int columnIndex)
        {
            var button = new Button
            {
                Content = Loc.S(resourceKey),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            ApplyNavigationButtonChrome(button);
            button.Foreground = PrimaryTextBrush;
            button.Click += delegate
            {
                if (_heroSortColumn == column)
                    _heroSortDescending = !_heroSortDescending;
                else
                {
                    _heroSortColumn = column;
                    _heroSortDescending = column != HeroSortColumn.AveragePlacement;
                }

                _contentHost.Child = BuildHeroStatsView();
            };
            Grid.SetColumn(button, columnIndex);
            return button;
        }

        private UIElement BuildHeroRow(HeroStatsRow row)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var summaryBorder = CreateCardBorder(SurfaceBrush, new Thickness(14, 12, 14, 12));
            summaryBorder.BorderBrush = BorderStrongBrush;
            summaryBorder.Cursor = Cursors.Hand;

            var summaryGrid = new Grid();
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.4, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            summaryBorder.Child = summaryGrid;

            summaryGrid.Children.Add(CreateHeroSummaryText(row.HeroName, 0, HorizontalAlignment.Left, FontWeights.SemiBold, DarkForegroundBrush));
            summaryGrid.Children.Add(CreateHeroSummaryText(row.Picks.ToString(CultureInfo.CurrentCulture), 1, HorizontalAlignment.Center, FontWeights.Normal, DarkForegroundBrush));
            summaryGrid.Children.Add(CreateHeroSummaryText(row.AveragePlacement.ToString("F2", CultureInfo.CurrentCulture), 2, HorizontalAlignment.Center, FontWeights.Normal, GetPlacementBrush(row.AveragePlacement)));
            summaryGrid.Children.Add(CreateHeroSummaryText(FormatRate(row.PickRate, row.HasPickRateData), 3, HorizontalAlignment.Center, FontWeights.Normal, DarkForegroundBrush));
            summaryGrid.Children.Add(CreateHeroSummaryText(FormatSignedDouble(row.ContributionValue), 4, HorizontalAlignment.Center, FontWeights.Normal, GetDeltaBrush(row.ContributionValue)));

            summaryBorder.MouseLeftButtonUp += delegate
            {
                _expandedHeroCardId = string.Equals(_expandedHeroCardId, row.HeroCardId, StringComparison.OrdinalIgnoreCase) ? null : row.HeroCardId;
                _contentHost.Child = BuildHeroStatsView();
            };

            container.Children.Add(summaryBorder);
            if (string.Equals(_expandedHeroCardId, row.HeroCardId, StringComparison.OrdinalIgnoreCase))
                container.Children.Add(BuildHeroDetail(row));

            return container;
        }

        private UIElement CreateHeroSummaryText(string text, int columnIndex, HorizontalAlignment alignment, FontWeight fontWeight, Brush foreground)
        {
            var block = new TextBlock
            {
                Text = text,
                Foreground = foreground,
                FontSize = 14,
                FontWeight = fontWeight,
                HorizontalAlignment = alignment,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(block, columnIndex);
            return block;
        }

        private UIElement BuildHeroDetail(HeroStatsRow row)
        {
            var border = CreateCardBorder(SurfaceAltBrush, new Thickness(16), new Thickness(0, 2, 0, 0));

            var root = new StackPanel();
            border.Child = root;

            root.Children.Add(new TextBlock
            {
                Text = Loc.F("HeroStats_DetailSummary", row.OfferedCount, row.Picks, row.AveragePlacement.ToString("F2", CultureInfo.CurrentCulture)),
                Foreground = LightForegroundBrush,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var detailGrid = new Grid();
            detailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            detailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(detailGrid);

            var leftColumn = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
            leftColumn.Children.Add(BuildRaceDetailSection(Loc.S("HeroStats_BestRaces"), BuildHeroRaceList(row.BestRaces)));
            leftColumn.Children.Add(BuildRaceDetailSection(Loc.S("HeroStats_WorstRaces"), BuildHeroRaceList(row.WorstRaces), false));
            Grid.SetColumn(leftColumn, 0);
            detailGrid.Children.Add(leftColumn);

            var rightColumn = new StackPanel();
            rightColumn.Children.Add(BuildRaceDetailSection(Loc.S("HeroStats_Performance"), BuildHeroMetricGrid(row), false));
            Grid.SetColumn(rightColumn, 1);
            detailGrid.Children.Add(rightColumn);

            return border;
        }

        private UIElement BuildHeroRaceList(IReadOnlyList<HeroRaceAffinityStat> races)
        {
            if (races == null || races.Count == 0)
                return BuildRaceTextValue(Loc.S("Common_NoData"));

            var stack = new StackPanel();
            foreach (var race in races)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = Loc.F("HeroStats_RaceItemFormat", race.RaceName, race.MatchCount, race.AveragePlacement.ToString("F2", CultureInfo.CurrentCulture), FormatSignedDouble(race.PlacementDelta)),
                    Foreground = GetPlacementDeltaBrush(race.PlacementDelta),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4)
                });
            }

            return stack;
        }

        private UIElement BuildHeroMetricGrid(HeroStatsRow row)
        {
            var stack = new StackPanel();
            stack.Children.Add(BuildHeroMetricText("HeroStats_FirstRate", FormatRate(row.FirstRate, row.Picks > 0), DarkForegroundBrush));
            stack.Children.Add(BuildHeroMetricText("HeroStats_LastRate", FormatRate(row.LastRate, row.Picks > 0), DarkForegroundBrush));
            stack.Children.Add(BuildHeroMetricText("HeroStats_ScoreRate", FormatRate(row.ScoreRate, row.Picks > 0), DarkForegroundBrush));
            stack.Children.Add(BuildHeroMetricText("HeroStats_ContributionPerGame", FormatSignedDouble(row.ContributionPerGame), GetDeltaBrush(row.ContributionPerGame)));
            return stack;
        }

        private UIElement BuildHeroMetricText(string labelKey, string value, Brush valueBrush)
        {
            var block = new TextBlock
            {
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4)
            };
            AppendLabelValue(block.Inlines, Loc.S(labelKey), value, valueBrush);
            return block;
        }

        private Brush GetPlacementDeltaBrush(double delta)
        {
            if (delta < -0.001)
                return PositiveValueBrush;
            if (delta > 0.001)
                return NegativeValueBrush;
            return LightForegroundBrush;
        }

        private string FormatSignedDouble(double value)
        {
            var formatted = value.ToString("F2", CultureInfo.CurrentCulture);
            return value > 0.001 ? "+" + formatted : formatted;
        }

        private UIElement BuildMatchStatsView()
        {
            _settingsService.Reload();
            var tavernSummary = _store.LoadTavernTempoSummary();
            var trinketSummary = _store.LoadTrinketStats(_settingsService.Settings.GetNormalizedScoreLine(), _currentTrinketFilter);
            var timewarpSummary = _store.LoadTimewarpStats(_settingsService.Settings.GetNormalizedScoreLine(), _currentTimewarpFilter);

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var root = new StackPanel();
            scrollViewer.Content = root;
            root.Children.Add(BuildMatchStatsPageBar(_currentMatchStatsPage == MatchStatsPage.Trinkets ? new Thickness(0, 0, 0, 18) : new Thickness(0, 0, 0, 18)));

            if (_currentMatchStatsPage == MatchStatsPage.TavernTempo)
                root.Children.Add(BuildTavernTempoView(tavernSummary));
            else if (_currentMatchStatsPage == MatchStatsPage.Trinkets)
                root.Children.Add(BuildTrinketStatsView(trinketSummary));
            else if (_currentMatchStatsPage == MatchStatsPage.Timewarp)
                root.Children.Add(BuildTimewarpStatsView(timewarpSummary));
            else if (_currentMatchStatsPage == MatchStatsPage.Quests)
                root.Children.Add(BuildQuestStatsPlaceholderView());

            return scrollViewer;
        }

        private UIElement BuildMatchStatsPageBar(Thickness margin)
        {
            var border = CreateCardBorder(SurfaceAltBrush, new Thickness(10), margin);

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            border.Child = panel;
            panel.Children.Add(CreateMatchStatsPageButton(MatchStatsPage.TavernTempo, "TavernTempo_PageTitle"));
            panel.Children.Add(CreateMatchStatsPageButton(MatchStatsPage.Trinkets, "TrinketStats_PageTitle"));
            panel.Children.Add(CreateMatchStatsPageButton(MatchStatsPage.Timewarp, "TimewarpStats_PageTitle"));
            panel.Children.Add(CreateMatchStatsPageButton(MatchStatsPage.Quests, "QuestStats_PageTitle"));
            return border;
        }

        private Button CreateMatchStatsPageButton(MatchStatsPage page, string resourceKey)
        {
            var isActive = _currentMatchStatsPage == page;
            var button = new Button
            {
                Content = Loc.S(resourceKey),
                Padding = new Thickness(14, 7, 14, 7),
                Margin = new Thickness(0, 0, 10, 0),
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal
            };
            ApplyChipButtonChrome(button, isActive);
            button.Click += delegate
            {
                if (_currentMatchStatsPage == page)
                    return;

                _currentMatchStatsPage = page;
                RebuildContent();
            };
            return button;
        }

        private UIElement BuildTavernTempoView(TavernTempoSummary summary)
        {
            var root = new StackPanel();
            root.Children.Add(BuildTavernTempoSummaryCard(summary));

            if (summary == null || summary.TotalMatches == 0)
            {
                root.Children.Add(new TextBlock
                {
                    Text = Loc.S("Common_NoData"),
                    Foreground = MutedTextBrush,
                    FontSize = 15,
                    Margin = new Thickness(0, 10, 0, 0)
                });
                return root;
            }

            foreach (var section in summary.Sections)
                root.Children.Add(BuildTavernTempoSection(section));

            return root;
        }

        private UIElement BuildTrinketStatsView(TrinketStatsSummary summary)
        {
            var root = new StackPanel();
            root.Children.Add(BuildTrinketFilterBar());

            if (summary == null || summary.Rows.Count == 0)
            {
                root.Children.Add(new TextBlock
                {
                    Text = Loc.S("Common_NoData"),
                    Foreground = MutedTextBrush,
                    FontSize = 15,
                    Margin = new Thickness(0, 10, 0, 0)
                });
                return root;
            }

            root.Children.Add(BuildTrinketHeaderRow());
            foreach (var row in summary.Rows)
                root.Children.Add(BuildTrinketRow(row));

            return root;
        }

        private UIElement BuildTrinketFilterBar()
        {
            var border = CreateCardBorder(SurfaceAltBrush, new Thickness(10), new Thickness(0, 0, 0, 18));

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            border.Child = panel;
            panel.Children.Add(CreateTrinketFilterButton(TrinketFilter.All, "TrinketStats_FilterAll"));
            panel.Children.Add(CreateTrinketFilterButton(TrinketFilter.Lesser, "TrinketStats_FilterLesser"));
            panel.Children.Add(CreateTrinketFilterButton(TrinketFilter.Greater, "TrinketStats_FilterGreater"));
            return border;
        }

        private Button CreateTrinketFilterButton(TrinketFilter filter, string resourceKey)
        {
            var isActive = _currentTrinketFilter == filter;
            var button = new Button
            {
                Content = Loc.S(resourceKey),
                Padding = new Thickness(14, 7, 14, 7),
                Margin = new Thickness(0, 0, 10, 0),
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal
            };
            ApplyChipButtonChrome(button, isActive);
            button.Click += delegate
            {
                if (_currentTrinketFilter == filter)
                    return;

                _currentTrinketFilter = filter;
                RebuildContent();
            };
            return button;
        }

        private UIElement BuildTrinketHeaderRow()
        {
            var border = CreateCardBorder(SurfaceAltBrush, new Thickness(14, 10, 14, 10), new Thickness(0, 0, 0, 8));

            var grid = CreateTrinketGrid();
            border.Child = grid;
            grid.Children.Add(CreateTavernTempoCell(Loc.S("TrinketStats_HeaderName"), 0, FontWeights.SemiBold, Brushes.White));
            grid.Children.Add(CreateTavernTempoCell(Loc.S("Common_MatchesLabel"), 1, FontWeights.SemiBold, Brushes.White, TextAlignment.Right));
            grid.Children.Add(CreateTavernTempoCell(Loc.S("TrinketStats_HeaderPickRate"), 2, FontWeights.SemiBold, Brushes.White, TextAlignment.Right));
            grid.Children.Add(CreateTavernTempoCell(Loc.S("Common_FirstRateLabel"), 3, FontWeights.SemiBold, Brushes.White, TextAlignment.Right));
            grid.Children.Add(CreateTavernTempoCell(Loc.S("Common_ScoreRateLabel"), 4, FontWeights.SemiBold, Brushes.White, TextAlignment.Right));
            return border;
        }

        private UIElement BuildTrinketRow(TrinketStatsRow row)
        {
            var border = CreateCardBorder(SurfaceBrush, new Thickness(14, 12, 14, 12), new Thickness(0, 0, 0, 6));

            var grid = CreateTrinketGrid();
            border.Child = grid;
            grid.Children.Add(CreateTavernTempoCell(row.CardName, 0, FontWeights.SemiBold, LightForegroundBrush));
            grid.Children.Add(CreateTavernTempoCell(row.MatchCount.ToString(CultureInfo.CurrentCulture), 1, FontWeights.Normal, LightForegroundBrush, TextAlignment.Right));
            grid.Children.Add(CreateTavernTempoCell(FormatRate(row.PickRate, row.MatchCount > 0), 2, FontWeights.Normal, LightForegroundBrush, TextAlignment.Right));
            grid.Children.Add(CreateTavernTempoCell(FormatRate(row.FirstRate, row.MatchCount > 0), 3, FontWeights.Normal, LightForegroundBrush, TextAlignment.Right));
            grid.Children.Add(CreateTavernTempoCell(FormatRate(row.ScoreRate, row.MatchCount > 0), 4, FontWeights.Normal, LightForegroundBrush, TextAlignment.Right));
            return border;
        }

        private Grid CreateTrinketGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.4, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            return grid;
        }

        private UIElement BuildTimewarpStatsView(TimewarpStatsSummary summary)
        {
            var root = new StackPanel();
            root.Children.Add(BuildTimewarpFilterBar());

            if (summary == null || summary.Rows.Count == 0)
            {
                root.Children.Add(new TextBlock
                {
                    Text = Loc.S("Common_NoData"),
                    Foreground = MutedTextBrush,
                    FontSize = 15,
                    Margin = new Thickness(0, 10, 0, 0)
                });
                return root;
            }

            root.Children.Add(BuildTimewarpHeaderRow());
            foreach (var row in summary.Rows)
                root.Children.Add(BuildTimewarpRow(row));

            return root;
        }

        private UIElement BuildTimewarpFilterBar()
        {
            var border = CreateCardBorder(SurfaceAltBrush, new Thickness(10), new Thickness(0, 0, 0, 18));

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            border.Child = panel;
            panel.Children.Add(CreateTimewarpFilterButton(TimewarpFilter.All, "TimewarpStats_FilterAll"));
            panel.Children.Add(CreateTimewarpFilterButton(TimewarpFilter.Major, "TimewarpStats_FilterMajor"));
            panel.Children.Add(CreateTimewarpFilterButton(TimewarpFilter.Minor, "TimewarpStats_FilterMinor"));
            return border;
        }

        private Button CreateTimewarpFilterButton(TimewarpFilter filter, string resourceKey)
        {
            var isActive = _currentTimewarpFilter == filter;
            var button = new Button
            {
                Content = Loc.S(resourceKey),
                Padding = new Thickness(14, 7, 14, 7),
                Margin = new Thickness(0, 0, 10, 0),
                FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal
            };
            ApplyChipButtonChrome(button, isActive);
            button.Click += delegate
            {
                if (_currentTimewarpFilter == filter)
                    return;

                _currentTimewarpFilter = filter;
                RebuildContent();
            };
            return button;
        }

        private UIElement BuildTimewarpHeaderRow()
        {
            var border = CreateCardBorder(SurfaceAltBrush, new Thickness(14, 10, 14, 10), new Thickness(0, 0, 0, 8));

            var grid = CreateTimewarpGrid();
            border.Child = grid;
            grid.Children.Add(CreateTavernTempoCell(Loc.S("TimewarpStats_HeaderName"), 0, FontWeights.SemiBold, Brushes.White));
            grid.Children.Add(CreateTavernTempoCell(Loc.S("TimewarpStats_HeaderAppearanceRate"), 1, FontWeights.SemiBold, Brushes.White, TextAlignment.Right));
            grid.Children.Add(CreateTavernTempoCell(Loc.S("TimewarpStats_HeaderPickRate"), 2, FontWeights.SemiBold, Brushes.White, TextAlignment.Right));
            grid.Children.Add(CreateTavernTempoCell(Loc.S("Common_FirstRateLabel"), 3, FontWeights.SemiBold, Brushes.White, TextAlignment.Right));
            grid.Children.Add(CreateTavernTempoCell(Loc.S("Common_ScoreRateLabel"), 4, FontWeights.SemiBold, Brushes.White, TextAlignment.Right));
            return border;
        }

        private UIElement BuildTimewarpRow(TimewarpStatsRow row)
        {
            var border = CreateCardBorder(SurfaceBrush, new Thickness(14, 12, 14, 12), new Thickness(0, 0, 0, 6));

            var grid = CreateTimewarpGrid();
            border.Child = grid;
            grid.Children.Add(CreateTavernTempoCell(row.CardName, 0, FontWeights.SemiBold, LightForegroundBrush));
            grid.Children.Add(CreateTavernTempoCell(FormatRate(row.AppearanceRate, row.AppearanceCount > 0), 1, FontWeights.Normal, LightForegroundBrush, TextAlignment.Right));
            grid.Children.Add(CreateTavernTempoCell(FormatRate(row.PickRate, row.AppearanceCount > 0), 2, FontWeights.Normal, LightForegroundBrush, TextAlignment.Right));
            grid.Children.Add(CreateTavernTempoCell(FormatRate(row.FirstRate, row.PickCount > 0), 3, FontWeights.Normal, LightForegroundBrush, TextAlignment.Right));
            grid.Children.Add(CreateTavernTempoCell(FormatRate(row.ScoreRate, row.PickCount > 0), 4, FontWeights.Normal, LightForegroundBrush, TextAlignment.Right));
            return border;
        }

        private Grid CreateTimewarpGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.4, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            return grid;
        }

        private UIElement BuildQuestStatsPlaceholderView()
        {
            return new TextBlock
            {
                Text = Loc.S("QuestStats_Empty"),
                Foreground = MutedTextBrush,
                FontSize = 15,
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
        }

        private UIElement BuildTavernTempoSummaryCard(TavernTempoSummary summary)
        {
            var border = CreateCardBorder(SurfaceAltBrush, new Thickness(16), new Thickness(0, 0, 0, 18));

            var stack = new StackPanel();
            border.Child = stack;
            stack.Children.Add(new TextBlock
            {
                Text = Loc.S("TavernTempo_PageTitle"),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });
            stack.Children.Add(new TextBlock
            {
                Text = summary == null || summary.TotalMatches == 0
                    ? Loc.S("TavernTempo_SummaryEmpty")
                    : Loc.F(
                        "TavernTempo_SummaryFormat",
                        summary.TotalMatches,
                        summary.OverallAveragePlacement.ToString("F2", CultureInfo.CurrentCulture),
                        BuildScoreLineText(_settingsService.Settings.GetNormalizedScoreLine())),
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            return border;
        }

        private UIElement BuildTavernTempoSection(TavernTempoTierSection section)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
            container.Children.Add(new TextBlock
            {
                Text = Loc.F("TavernTempo_TierHeaderFormat", section.TavernTier),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = PrimaryTextBrush,
                Margin = new Thickness(0, 0, 0, 10)
            });
            container.Children.Add(BuildTavernTempoHeaderRow());

            foreach (var row in section.Buckets)
                container.Children.Add(BuildTavernTempoRow(row));

            return container;
        }

        private UIElement BuildTavernTempoHeaderRow()
        {
            var border = new Border
            {
                Background = SurfaceAltBrush,
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var grid = CreateTavernTempoGrid();
            border.Child = grid;
            grid.Children.Add(CreateTavernTempoCell(Loc.S("TavernTempo_HeaderBucket"), 0, FontWeights.SemiBold, Brushes.White));
            grid.Children.Add(CreateTavernTempoCell(Loc.S("Common_MatchesLabel"), 1, FontWeights.SemiBold, Brushes.White, TextAlignment.Right));
            grid.Children.Add(CreateTavernTempoCell(Loc.S("TavernTempo_HeaderRate"), 2, FontWeights.SemiBold, Brushes.White, TextAlignment.Right));
            grid.Children.Add(CreateTavernTempoCell(Loc.S("Common_AvgPlacementLabel"), 3, FontWeights.SemiBold, Brushes.White, TextAlignment.Right));
            grid.Children.Add(CreateTavernTempoCell(Loc.S("TavernTempo_HeaderVsOverall"), 4, FontWeights.SemiBold, Brushes.White, TextAlignment.Right));
            return border;
        }

        private UIElement BuildTavernTempoRow(TavernTempoBucketRow row)
        {
            var border = new Border
            {
                Background = SurfaceAltBrush,
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var grid = CreateTavernTempoGrid();
            border.Child = grid;
            grid.Children.Add(CreateTavernTempoCell(Loc.S(row.BucketKey), 0, FontWeights.SemiBold, LightForegroundBrush));
            grid.Children.Add(CreateTavernTempoCell(row.MatchCount.ToString(CultureInfo.CurrentCulture), 1, FontWeights.Normal, LightForegroundBrush, TextAlignment.Right));
            grid.Children.Add(CreateTavernTempoCell(FormatRate(row.MatchRate, row.MatchCount > 0), 2, FontWeights.Normal, LightForegroundBrush, TextAlignment.Right));
            grid.Children.Add(CreateTavernTempoCell(
                row.AveragePlacement.HasValue ? row.AveragePlacement.Value.ToString("F2", CultureInfo.CurrentCulture) : "-",
                3,
                FontWeights.Normal,
                row.AveragePlacement.HasValue ? GetPlacementBrush(row.AveragePlacement.Value) : NeutralValueBrush,
                TextAlignment.Right));
            grid.Children.Add(CreateTavernTempoCell(
                row.PlacementDelta.HasValue ? FormatSignedDouble(row.PlacementDelta.Value) : "-",
                4,
                FontWeights.Normal,
                row.PlacementDelta.HasValue ? GetPlacementDeltaBrush(row.PlacementDelta.Value) : NeutralValueBrush,
                TextAlignment.Right));
            return border;
        }

        private Grid CreateTavernTempoGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            return grid;
        }

        private UIElement CreateTavernTempoCell(string text, int columnIndex, FontWeight fontWeight, Brush foreground, TextAlignment textAlignment = TextAlignment.Left)
        {
            var block = new TextBlock
            {
                Text = text,
                Foreground = foreground,
                FontSize = 14,
                FontWeight = fontWeight,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = textAlignment,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(block, columnIndex);
            return block;
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
                Foreground = PrimaryTextBrush,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            });
            stack.Children.Add(new TextBlock
            {
                Text = Loc.S("Common_PlaceholderDeveloping"),
                FontSize = 15,
                Foreground = MutedTextBrush,
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
                    Text = Loc.S("HistoryMatches_Empty"),
                    Foreground = MutedTextBrush,
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
            var border = CreateCardBorder(SurfaceBrush, new Thickness(16, 12, 16, 12), new Thickness(0, 0, 0, 18));
            border.Cursor = Cursors.Hand;

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
                Foreground = LightForegroundBrush,
                VerticalAlignment = VerticalAlignment.Center
            });
            var tagsPanel = BuildTagWrapPanel(row.Tags, false);
            Grid.SetColumn(tagsPanel, 1);
            topGrid.Children.Add(tagsPanel);
            stack.Children.Add(topGrid);

            stack.Children.Add(BuildHistoryStatsText(row));
            stack.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = LightForegroundBrush,
                FontSize = 13,
                Text = Loc.F("HistoryMatches_AnomalyFormat", row.AnomalyDisplay)
            });
            stack.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = LightForegroundBrush,
                FontSize = 13,
                Text = Loc.F("HistoryMatches_FinalBoardFormat", row.FinalBoardDisplay),
                TextWrapping = TextWrapping.Wrap
            });

            border.MouseLeftButtonUp += delegate { OpenRowDetails(row); };
            return border;
        }

        private void UpdateSummary(IReadOnlyList<BgMatchRow> rows)
        {
            if (rows.Count == 0)
            {
                _summaryText.Content = Loc.S("HistoryMatches_SummaryEmpty");
                return;
            }

            var averagePlacement = rows.Average(x => x.Placement);
            var totalDelta = rows.Sum(x => x.RatingDelta);
            _summaryText.Content = BuildHistorySummaryText(rows.Count, averagePlacement, totalDelta);
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
                pair.Value.Foreground = active ? PrimaryTextBrush : SecondaryTextBrush;
                pair.Value.FontSize = active ? 21 : 18;
                pair.Value.FontWeight = active ? FontWeights.Bold : FontWeights.SemiBold;
            }
        }

        private void RefreshHistoryToolbar()
        {
            foreach (var pair in _rangeButtons)
                ApplyChipButtonChrome(pair.Value, pair.Key == _currentRange);

            _dateNavigator.Visibility = _currentRange == HistoryRange.Season ? Visibility.Collapsed : Visibility.Visible;
            if (_currentRange == HistoryRange.Day)
                _dateLabel.Content = _anchorDate.ToString("yyyy-MM-dd");
            else
            {
                var weekStart = GetWeekStart(_anchorDate);
                _dateLabel.Content = Loc.F("Common_WeekRangeFormat", weekStart, weekStart.AddDays(6));
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
            var archive = _store.GetSelectedArchiveForDisplay() ?? _store.GetLatestRecordedArchiveForDisplay();
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = Loc.S("VersionInfo_Title"),
                Foreground = SecondaryTextBrush,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold
            });
            stack.Children.Add(new TextBlock
            {
                Text = archive != null ? _store.GetArchiveDisplayName(archive) : Loc.S("VersionInfo_NoMoreData"),
                Foreground = PrimaryTextBrush,
                FontSize = 16,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 52
            });
            _versionButton.Content = stack;
            _versionButton.ToolTip = archive != null ? _store.GetArchiveDisplayName(archive) : Loc.S("VersionInfo_NoMoreData");
        }

        private void RefreshAccountButton()
        {
            var account = _store.CurrentAccount ?? _store.UnknownAccount;
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "账号",
                Foreground = SecondaryTextBrush,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold
            });
            stack.Children.Add(new TextBlock
            {
                Text = account.BattleTagName,
                Foreground = PrimaryTextBrush,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrWhiteSpace(account.BattleTagCode))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = account.BattleTagCode,
                    Foreground = SecondaryTextBrush,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 1, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            stack.Children.Add(new TextBlock
            {
                Text = account.Subtitle,
                Foreground = MutedTextBrush,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            _accountButton.Content = stack;
        }

        private void OpenVersionMenu()
        {
            var menu = new ContextMenu();
            var archives = _store.GetRecordedArchives();
            var currentArchive = _store.GetSelectedArchiveForDisplay();
            var currentKey = currentArchive?.Key;
            if (archives.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = Loc.S("VersionInfo_NoMoreData"), IsEnabled = false });
            }
            else
            {
                foreach (var archive in archives)
                {
                    var displayName = _store.GetArchiveDisplayName(archive);
                    var item = new MenuItem
                    {
                        Header = displayName,
                        IsCheckable = true,
                        IsChecked = string.Equals(archive.Key, currentKey, StringComparison.OrdinalIgnoreCase)
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

        private void OpenAccountMenu()
        {
            var menu = new ContextMenu();
            var accounts = _store.GetAvailableAccounts();
            if (accounts.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = "暂无账号数据", IsEnabled = false });
            }
            else
            {
                foreach (var account in accounts)
                {
                    var item = new MenuItem
                    {
                        Header = account.MenuDisplay,
                        IsCheckable = true,
                        IsChecked = string.Equals(account.Key, _store.CurrentAccountKey, StringComparison.OrdinalIgnoreCase)
                    };
                    item.Click += delegate
                    {
                        _store.SetCurrentAccountByKey(account.Key);
                        _store.RefreshLatestRecordedArchiveForDisplay();
                        _settingsService.Settings.SelectedAccountKey = _store.CurrentAccountKey;
                        _settingsService.Save();
                        _selectedMatchId = null;
                        RefreshAccountButton();
                        RefreshVersionButton();
                        RefreshHistoryToolbar();
                        RebuildContent();
                    };
                    menu.Items.Add(item);
                }
            }

            _accountButton.ContextMenu = menu;
            menu.PlacementTarget = _accountButton;
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
            _sectionTitle.Text = Loc.S("MatchDetail_Title");
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
            if (_store.ShouldShowTrinketDetails(snapshot))
                root.Children.Add(BuildTrinketDetailSection(snapshot));
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
                Content = Loc.S("Common_Back"),
                Width = 92,
                Height = 42,
                Background = SurfaceAltBrush,
                Foreground = PrimaryTextBrush,
                BorderBrush = BorderSubtleBrush,
                BorderThickness = DefaultBorderThickness,
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
            infoPanel.Children.Add(CreateHeaderBadge(Loc.F("MatchDetail_TimeFormat", FormatTimestamp(snapshot.Timestamp))));
            infoPanel.Children.Add(CreateHeaderBadge(BuildMatchDetailStatsText(snapshot), new Thickness(0, 10, 0, 0)));
            grid.Children.Add(infoPanel);
            return grid;
        }

        private Border CreateHeaderBadge(string text, Thickness? margin = null)
        {
            var border = CreateCardBorder(SurfaceAltBrush, new Thickness(14, 10, 14, 10), margin ?? new Thickness(0));
            border.Child = new TextBlock
            {
                Text = text,
                Foreground = LightForegroundBrush,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold
            };
            return border;
        }

        private Border CreateHeaderBadge(UIElement content, Thickness? margin = null)
        {
            var border = CreateCardBorder(SurfaceAltBrush, new Thickness(14, 10, 14, 10), margin ?? new Thickness(0));
            border.Child = content;
            return border;
        }

        private UIElement BuildHeroSection(BgSnapshot snapshot)
        {
            var section = CreateSectionContainer();
            var row = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            section.Child = row;

            row.Children.Add(CreateInfoText(Loc.F("MatchDetail_SelectedHeroFormat", GameTextService.GetCardName(snapshot.HeroCardId, snapshot.HeroName))));
            var combatHeroPowers = (snapshot.HeroPowerCardIds ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            if (combatHeroPowers.Length > 0)
            {
                row.Children.Add(new TextBlock
                {
                    Text = Loc.S("MatchDetail_FinalHeroPowerLabel"),
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 8, 0)
                });

                foreach (var heroPowerCardId in combatHeroPowers)
                    row.Children.Add(CreateHeroPowerBadge(GameTextService.GetCardName(heroPowerCardId, heroPowerCardId)));
            }
            return section;
        }

        private Border CreateHeroPowerBadge(string text)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(124, 154, 201)),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(10, 4, 10, 4),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        private UIElement BuildTrinketDetailSection(BgSnapshot snapshot)
        {
            var section = CreateSectionContainer();
            var stack = new StackPanel();
            section.Child = stack;
            stack.Children.Add(new TextBlock
            {
                Text = Loc.S("MatchDetail_TrinketSection"),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = PrimaryTextBrush,
                Margin = new Thickness(0, 0, 0, 12)
            });
            stack.Children.Add(BuildDetailInfoRow(Loc.S("MatchDetail_LesserTrinketLabel"), ResolveTrinketName(snapshot.LesserTrinketCardId)));
            stack.Children.Add(BuildDetailInfoRow(Loc.S("MatchDetail_GreaterTrinketLabel"), ResolveTrinketName(snapshot.GreaterTrinketCardId)));
            if (!string.IsNullOrWhiteSpace(snapshot.HeroPowerTrinketCardId))
                stack.Children.Add(BuildDetailInfoRow(Loc.S("MatchDetail_HeroPowerTrinketLabel"), ResolveTrinketName(snapshot.HeroPowerTrinketCardId)));
            return section;
        }

        private UIElement BuildDetailInfoRow(string label, string value)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });
            var valueBlock = new TextBlock
            {
                Text = value,
                FontSize = 14,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(valueBlock);
            return grid;
        }

        private string ResolveTrinketName(string cardId)
        {
            return string.IsNullOrWhiteSpace(cardId)
                ? Loc.S("Common_None")
                : GameTextService.GetCardName(cardId, cardId);
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
                Text = Loc.S("MatchDetail_FinalBoard"),
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
            var border = CreateCardBorder(SurfaceAltBrush, new Thickness(10), new Thickness(8, 0, 8, 0));
            border.MinHeight = 116;
            border.ToolTip = BuildMinionToolTip(minion);
            var stack = new StackPanel();
            border.Child = stack;
            var minionName = ResolveMinionName(minion);
            stack.Children.Add(new TextBlock
            {
                Text = (minion.IsGolden ? GetGoldPrefix() : string.Empty) + minionName,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13
            });
            stack.Children.Add(new TextBlock
            {
                Text = Loc.S("Minion_Attack"),
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
                Text = Loc.S("Minion_Health"),
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
            var minionName = ResolveMinionName(minion);
            var raceName = ResolveMinionRace(minion);
            return string.Join(Environment.NewLine, new[]
            {
                Loc.F("MinionTooltip_NameFormat", minionName),
                Loc.F("MinionTooltip_IdFormat", SafeText(minion.CardId, "-")),
                Loc.F("MinionTooltip_RaceFormat", SafeText(raceName, "-")),
                Loc.F("MinionTooltip_GoldenFormat", minion.IsGolden ? Loc.S("MinionTooltip_GoldenYes") : Loc.S("MinionTooltip_GoldenNo")),
                Loc.F("MinionTooltip_PositionFormat", minion.Position.ToString(CultureInfo.InvariantCulture)),
                Loc.F("MinionTooltip_AttackFormat", minion.Attack.ToString(CultureInfo.InvariantCulture)),
                Loc.F("MinionTooltip_HealthFormat", minion.Health.ToString(CultureInfo.InvariantCulture)),
                Loc.F("MinionTooltip_KeywordsFormat", keywords.Count == 0 ? Loc.S("Common_None") : string.Join(", ", keywords))
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
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                IsEnabled = combinedTags.Count < 5
            };
            ApplyChipButtonChrome(addButton, true);
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
                menu.Items.Add(new MenuItem { Header = Loc.S("Tags_MaxReached"), IsEnabled = false });
                return menu;
            }

            var current = new HashSet<string>(combinedTags, StringComparer.OrdinalIgnoreCase);
            var available = _store.GetAvailableTags(snapshot).Where(tag => !current.Contains(tag)).ToList();
            if (available.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = Loc.S("Tags_NoAvailable"), IsEnabled = false });
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
            var border = CreateCardBorder(AccentBrush, new Thickness(10, 6, 10, 6), new Thickness(0, 0, 8, 0));
            border.BorderBrush = AccentHoverBrush;
            border.CornerRadius = ChipCornerRadius;
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
                    Foreground = PrimaryTextBrush,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
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
                panel.Children.Add(CreateTagChip(Loc.S("Tags_Empty")));
            else
                foreach (var tag in list)
                    panel.Children.Add(CreateTagChip(tag));
            return panel;
        }

        private Border CreateTagChip(string text)
        {
            var border = CreateCardBorder(AccentBrush, new Thickness(8, 4, 8, 4), new Thickness(6, 0, 0, 0));
            border.BorderBrush = AccentHoverBrush;
            border.CornerRadius = ChipCornerRadius;
            border.Child = new TextBlock
            {
                Text = text,
                Foreground = PrimaryTextBrush,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            };
            return border;
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
                Text = Loc.S("MatchDetail_TavernSection"),
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
                return Loc.S("MatchDetail_TavernUpgradeEmpty");
            return Loc.F("MatchDetail_TavernUpgradeFormat", string.Join(" / ", upgrades.Select(x => x.Turn + "-" + x.TavernTier)));
        }

        private UIElement BuildUpgradeChart(BgSnapshot snapshot)
        {
            var upgrades = (snapshot.TavernUpgradeTimeline ?? new List<BgTavernUpgradePoint>()).OrderBy(x => x.Turn).ToList();
            var host = CreateCardBorder(SurfaceAltBrush, new Thickness(16));
            host.Height = 180;

            if (upgrades.Count == 0)
            {
                host.Child = new TextBlock
                {
                    Text = Loc.S("MatchDetail_TavernChartEmpty"),
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
                    ToolTip = Loc.F("MatchDetail_TavernTooltipFormat", point.Turn, Environment.NewLine, point.TavernTier)
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
            return CreateCardBorder(SurfaceBrush, new Thickness(16), new Thickness(0, 0, 0, 20));
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

        private static string ResolveMinionName(BgBoardMinionSnapshot minion)
        {
            if (minion == null)
                return string.Empty;

            return GameTextService.GetCardName(minion.CardId, SafeText(minion.Name, minion.CardId));
        }

        private static string ResolveMinionRace(BgBoardMinionSnapshot minion)
        {
            if (minion == null)
                return string.Empty;

            return GameTextService.GetRaceNameFromCardId(minion.CardId, minion.Race);
        }

        private TextBlock BuildHistoryStatsText(BgMatchRow row)
        {
            var block = new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            };
            AppendLabelValue(block.Inlines, Loc.S("Common_TimeLabel"), row.TimestampText, LightForegroundBrush);
            AppendSpacer(block.Inlines);
            AppendLabelValue(block.Inlines, Loc.S("Common_PlacementLabel"), row.Placement.ToString(CultureInfo.CurrentCulture), GetPlacementBrush(row.Placement));
            AppendSpacer(block.Inlines);
            AppendLabelValue(block.Inlines, Loc.S("Common_RatingLabel"), row.RatingAfter.ToString(CultureInfo.CurrentCulture), LightForegroundBrush);
            AppendSpacer(block.Inlines);
            AppendLabelValue(block.Inlines, Loc.S("Common_RatingDeltaLabel"), FormatRatingDelta(row.RatingDelta), GetDeltaBrush(row.RatingDelta));
            return block;
        }

        private TextBlock BuildHistorySummaryText(int matchCount, double averagePlacement, int totalDelta)
        {
            var block = new TextBlock
            {
                FontSize = 14,
                TextAlignment = TextAlignment.Right
            };
            AppendLabelValue(block.Inlines, Loc.S("Common_MatchesLabel"), matchCount.ToString(CultureInfo.CurrentCulture), LightForegroundBrush);
            AppendSpacer(block.Inlines, "  ");
            AppendLabelValue(block.Inlines, Loc.S("Common_AvgPlacementLabel"), averagePlacement.ToString("F2", CultureInfo.CurrentCulture), GetPlacementBrush(averagePlacement));
            AppendSpacer(block.Inlines, "  ");
            AppendLabelValue(block.Inlines, Loc.S("Common_RatingDeltaLabel"), FormatRatingDelta(totalDelta), GetDeltaBrush(totalDelta));
            return block;
        }

        private TextBlock BuildMatchDetailStatsText(BgSnapshot snapshot)
        {
            var block = new TextBlock
            {
                FontSize = 15,
                FontWeight = FontWeights.SemiBold
            };
            AppendLabelValue(block.Inlines, Loc.S("Common_PlacementLabel"), snapshot.Placement.ToString(CultureInfo.CurrentCulture), GetPlacementBrush(snapshot.Placement));
            AppendSpacer(block.Inlines, "   ");
            AppendLabelValue(block.Inlines, Loc.S("Common_RatingLabel"), snapshot.RatingAfter.ToString(CultureInfo.CurrentCulture), LightForegroundBrush);
            AppendSpacer(block.Inlines, "   ");
            AppendLabelValue(block.Inlines, Loc.S("Common_RatingDeltaLabel"), FormatRatingDelta(snapshot.RatingDelta), GetDeltaBrush(snapshot.RatingDelta));
            return block;
        }

        private static void AppendLabelValue(ICollection<Inline> inlines, string label, string value, Brush valueBrush)
        {
            inlines.Add(CreateRun(label + ": ", LightForegroundBrush));
            inlines.Add(CreateRun(value, valueBrush));
        }

        private static void AppendSpacer(ICollection<Inline> inlines, string spacing = "    ")
        {
            inlines.Add(CreateRun(spacing, LightForegroundBrush));
        }

        private static Run CreateRun(string text, Brush brush)
        {
            return new Run(text) { Foreground = brush };
        }

        private TextBlock BuildRaceTextValue(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = LightForegroundBrush,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            };
        }

        private Brush GetPlacementBrush(double placement)
        {
            return placement < _settingsService.Settings.GetNormalizedScoreLine() ? PositiveValueBrush : NegativeValueBrush;
        }

        private static Brush GetDeltaBrush(double delta)
        {
            if (delta > 0.001)
                return PositiveValueBrush;
            if (delta < -0.001)
                return NegativeValueBrush;
            return NeutralValueBrush;
        }

        private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
        {
            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        private static string FormatRatingDelta(int ratingDelta)
        {
            return ratingDelta > 0
                ? Loc.F("Common_RatingDeltaPositive", ratingDelta)
                : ratingDelta.ToString(CultureInfo.CurrentCulture);
        }

        private static string GetGoldPrefix()
        {
            return Loc.S("Common_GoldPrefix");
        }

        private static DateTime GetWeekStart(DateTime date)
        {
            var diff = ((int)date.DayOfWeek + 6) % 7;
            return date.Date.AddDays(-diff);
        }
    }
}

