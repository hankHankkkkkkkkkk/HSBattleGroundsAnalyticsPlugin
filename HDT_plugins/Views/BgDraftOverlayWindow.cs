using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HDTplugins.Localization;
using HDTplugins.Models;

namespace HDTplugins.Views
{
    internal sealed class BgDraftOverlayWindow : Window
    {
        private readonly Canvas _canvas;
        private readonly SolidColorBrush _panelBrush = new SolidColorBrush(Color.FromArgb(226, 22, 26, 31));
        private readonly SolidColorBrush _borderBrush = new SolidColorBrush(Color.FromArgb(255, 71, 83, 92));
        private readonly SolidColorBrush _labelBrush = new SolidColorBrush(Color.FromArgb(255, 173, 184, 192));
        private readonly SolidColorBrush _valueBrush = new SolidColorBrush(Colors.White);

        public BgDraftOverlayWindow()
        {
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowActivated = false;
            ShowInTaskbar = false;
            Topmost = true;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            IsHitTestVisible = false;
            Focusable = false;
            _canvas = new Canvas
            {
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            };
            Content = _canvas;
        }

        public void ApplyBounds(Rect bounds)
        {
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
            _canvas.Width = bounds.Width;
            _canvas.Height = bounds.Height;
        }

        public void Render(IReadOnlyList<BgDraftOverlayRenderItem> items)
        {
            _canvas.Children.Clear();
            foreach (var item in items ?? new List<BgDraftOverlayRenderItem>())
                _canvas.Children.Add(CreateCard(item));
        }

        private FrameworkElement CreateCard(BgDraftOverlayRenderItem item)
        {
            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            if (item.HasData)
            {
                foreach (var pair in item.Metrics ?? new List<KeyValuePair<string, string>>())
                    stack.Children.Add(CreateMetricRow(pair.Key, pair.Value));
            }
            else
            {
                stack.Children.Add(new TextBlock
                {
                    Text = Loc.S("Common_NoData"),
                    Foreground = _valueBrush,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                });
            }

            var border = new Border
            {
                Width = item.Width,
                Height = item.Height,
                Background = _panelBrush,
                BorderBrush = _borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = item.HasData ? new Thickness(10, 8, 10, 8) : new Thickness(10, 10, 10, 10),
                Child = stack
            };

            Canvas.SetLeft(border, item.Left);
            Canvas.SetTop(border, item.Top);
            return border;
        }

        private FrameworkElement CreateMetricRow(string label, string value)
        {
            var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var labelBlock = new TextBlock
            {
                Text = label,
                Foreground = _labelBrush,
                FontSize = 12,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(labelBlock, 0);
            grid.Children.Add(labelBlock);

            var valueBlock = new TextBlock
            {
                Text = value,
                Foreground = _valueBrush,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Right
            };
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(valueBlock);

            return grid;
        }
    }
}
