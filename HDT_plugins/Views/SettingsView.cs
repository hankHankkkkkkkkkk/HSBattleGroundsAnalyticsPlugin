using HDTplugins.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HDTplugins.Views
{
    public class SettingsView : Window
    {
        private readonly PluginSettingsService _settingsService;
        private readonly CheckBox _autoOpenCheckBox;

        public SettingsView(PluginSettingsService settingsService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

            Title = "酒馆数据分析 - 插件设置";
            Width = 420;
            Height = 220;
            MinWidth = 380;
            MinHeight = 200;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(241, 238, 233));
            ResizeMode = ResizeMode.NoResize;

            var root = new Grid { Margin = new Thickness(20) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            root.Children.Add(new TextBlock
            {
                Text = "插件设置",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(88, 80, 70)),
                Margin = new Thickness(0, 0, 0, 16)
            });

            var contentBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(196, 189, 177)),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 16)
            };
            Grid.SetRow(contentBorder, 1);
            root.Children.Add(contentBorder);

            var contentStack = new StackPanel();
            contentBorder.Child = contentStack;
            _autoOpenCheckBox = new CheckBox
            {
                Content = "打开 HDT 时自动打开酒馆数据分析插件",
                IsChecked = _settingsService.Settings.AutoOpenOnStartup,
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold
            };
            contentStack.Children.Add(_autoOpenCheckBox);
            contentStack.Children.Add(new TextBlock
            {
                Text = "关闭后，插件仍会加载，但不会在 HDT 启动时自动弹出窗口。",
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = Brushes.White,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetRow(buttonPanel, 2);
            root.Children.Add(buttonPanel);

            var cancelButton = CreateButton("取消", new Thickness(0, 0, 10, 0));
            cancelButton.Click += delegate { Close(); };
            buttonPanel.Children.Add(cancelButton);

            var saveButton = CreateButton("保存", new Thickness(0));
            saveButton.Click += delegate
            {
                _settingsService.Settings.AutoOpenOnStartup = _autoOpenCheckBox.IsChecked != false;
                _settingsService.Save();
                DialogResult = true;
                Close();
            };
            buttonPanel.Children.Add(saveButton);
        }

        private static Button CreateButton(string text, Thickness margin)
        {
            return new Button
            {
                Content = text,
                Width = 80,
                Height = 34,
                Margin = margin,
                Background = new SolidColorBrush(Color.FromRgb(126, 163, 209)),
                Foreground = Brushes.White,
                BorderBrush = Brushes.Transparent,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            };
        }
    }
}
