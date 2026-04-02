using HDTplugins.Localization;
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
        private readonly TextBlock _headerText;
        private readonly TextBlock _hintText;
        private readonly Button _cancelButton;
        private readonly Button _saveButton;

        public SettingsView(PluginSettingsService settingsService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

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

            _headerText = new TextBlock
            {
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(88, 80, 70)),
                Margin = new Thickness(0, 0, 0, 16)
            };
            root.Children.Add(_headerText);

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
                IsChecked = _settingsService.Settings.AutoOpenOnStartup,
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold
            };
            contentStack.Children.Add(_autoOpenCheckBox);

            _hintText = new TextBlock
            {
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = Brushes.White,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            contentStack.Children.Add(_hintText);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetRow(buttonPanel, 2);
            root.Children.Add(buttonPanel);

            _cancelButton = CreateButton(new Thickness(0, 0, 10, 0));
            _cancelButton.Click += delegate { Close(); };
            buttonPanel.Children.Add(_cancelButton);

            _saveButton = CreateButton(new Thickness(0));
            _saveButton.Click += delegate
            {
                _settingsService.Settings.AutoOpenOnStartup = _autoOpenCheckBox.IsChecked != false;
                _settingsService.Save();
                DialogResult = true;
                Close();
            };
            buttonPanel.Children.Add(_saveButton);

            LocalizationService.LanguageChanged += OnLanguageChanged;
            Closed += delegate { LocalizationService.LanguageChanged -= OnLanguageChanged; };
            ApplyLocalization();
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            ApplyLocalization();
        }

        private void ApplyLocalization()
        {
            Title = Loc.S("Settings_Title");
            _headerText.Text = Loc.S("Settings_Header");
            _autoOpenCheckBox.Content = Loc.S("Settings_AutoOpenOnStartup");
            _hintText.Text = Loc.S("Settings_AutoOpenHint");
            _cancelButton.Content = Loc.S("Common_Cancel");
            _saveButton.Content = Loc.S("Common_Save");
        }

        private static Button CreateButton(Thickness margin)
        {
            return new Button
            {
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
