using HDTplugins.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace HDTplugins.Views
{
    public class BgStatsWindow : Window
    {
        private readonly string _finalFilePath;
        private readonly ObservableCollection<BgMatchRow> _rows = new ObservableCollection<BgMatchRow>();
        private readonly DataGrid _grid;

        public event Action<string> OpenMatchDetailRequested;

        /// <summary>
        /// 预留：畸变展示扩展（待开发）
        /// </summary>
        public Func<BgMatchRow, string> ResolveAnomalyDisplay { get; set; }

        /// <summary>
        /// 预留：终局阵容展示扩展（待开发）
        /// </summary>
        public Func<BgMatchRow, string> ResolveFinalBoardDisplay { get; set; }

        public BgStatsWindow(string finalFilePath)
        {
            _finalFilePath = finalFilePath;
            Title = "酒馆数据分析";
            Width = 980;
            Height = 620;
            MinWidth = 900;
            MinHeight = 420;
            Background = Brushes.White;

            ResolveAnomalyDisplay = _ => "待开发";
            ResolveFinalBoardDisplay = _ => "待开发";

            var root = new DockPanel();
            Content = root;

            var topBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(10)
            };
            DockPanel.SetDock(topBar, Dock.Top);
            root.Children.Add(topBar);

            var title = new TextBlock
            {
                Text = "战绩",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            topBar.Children.Add(title);

            var refreshButton = new Button
            {
                Content = "刷新",
                Margin = new Thickness(12, 0, 0, 0),
                Padding = new Thickness(10, 4, 10, 4)
            };
            refreshButton.Click += (_, __) => Reload();
            topBar.Children.Add(refreshButton);

            _grid = BuildGrid();
            _grid.ItemsSource = _rows;
            root.Children.Add(_grid);

            Loaded += (_, __) => Reload();
        }

        public void Reload()
        {
            _rows.Clear();

            if (string.IsNullOrEmpty(_finalFilePath) || !File.Exists(_finalFilePath))
                return;

            var lines = File.ReadAllLines(_finalFilePath, Encoding.UTF8);
            foreach (var line in lines.Reverse())
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var row = ParseLine(line);
                if (row == null)
                    continue;

                row.AnomalyDisplay = ResolveAnomalyDisplay?.Invoke(row) ?? "待开发";
                row.FinalBoardDisplay = ResolveFinalBoardDisplay?.Invoke(row) ?? "待开发";
                _rows.Add(row);
            }
        }

        private DataGrid BuildGrid()
        {
            var grid = new DataGrid
            {
                Margin = new Thickness(10, 0, 10, 10),
                AutoGenerateColumns = false,
                IsReadOnly = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Single,
                CanUserAddRows = false,
                CanUserDeleteRows = false
            };

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "最终排名",
                Binding = new Binding("Placement"),
                Width = 90
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "结束分数",
                Binding = new Binding("RatingAfter"),
                Width = 100
            });

            var deltaCol = new DataGridTextColumn
            {
                Header = "分数变动",
                Binding = new Binding("RatingDeltaText"),
                Width = 110
            };

            var deltaStyle = new Style(typeof(TextBlock));
            deltaStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, new Binding("RatingDeltaBrush")));
            deltaStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            deltaCol.ElementStyle = deltaStyle;
            grid.Columns.Add(deltaCol);

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "当局英雄",
                Binding = new Binding("HeroName"),
                Width = new DataGridLength(2, DataGridLengthUnitType.Star)
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "当局畸变",
                Binding = new Binding("AnomalyDisplay"),
                Width = new DataGridLength(2, DataGridLengthUnitType.Star)
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "终局阵容",
                Binding = new Binding("FinalBoardDisplay"),
                Width = new DataGridLength(2, DataGridLengthUnitType.Star)
            });

            grid.MouseDoubleClick += OnGridMouseDoubleClick;
            grid.KeyDown += OnGridKeyDown;

            return grid;
        }

        private void OnGridMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenSelectedRowDetails();
        }

        private void OnGridKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OpenSelectedRowDetails();
                e.Handled = true;
            }
        }

        private void OpenSelectedRowDetails()
        {
            if (!(_grid.SelectedItem is BgMatchRow row))
                return;

            OpenMatchDetailRequested?.Invoke(row.MatchId);

            MessageBox.Show(
                "对局详情页待开发，接口已预留。\nmatchId=" + row.MatchId,
                "酒馆数据分析",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private static BgMatchRow ParseLine(string line)
        {
            var placement = GetInt(line, "placement");
            var ratingAfter = GetInt(line, "ratingAfter");
            var ratingDelta = GetInt(line, "ratingDelta");
            var heroName = GetString(line, "heroName");
            var matchId = GetString(line, "matchId");

            if (placement <= 0)
                return null;

            return new BgMatchRow
            {
                MatchId = matchId,
                Placement = placement,
                RatingAfter = ratingAfter,
                RatingDelta = ratingDelta,
                HeroName = string.IsNullOrEmpty(heroName) ? "未知英雄" : heroName
            };
        }

        private static int GetInt(string line, string key)
        {
            var m = Regex.Match(line, "\\\"" + Regex.Escape(key) + "\\\":(-?\\d+)");
            if (!m.Success)
                return 0;

            if (int.TryParse(m.Groups[1].Value, out var value))
                return value;
            return 0;
        }

        private static string GetString(string line, string key)
        {
            var m = Regex.Match(line, "\\\"" + Regex.Escape(key) + "\\\":\\\"(.*?)\\\"");
            if (!m.Success)
                return string.Empty;

            return UnescapeJson(m.Groups[1].Value);
        }

        private static string UnescapeJson(string value)
        {
            return value
                .Replace("\\\\", "\\")
                .Replace("\\\"", "\"")
                .Replace("\\r", "\r")
                .Replace("\\n", "\n")
                .Replace("\\t", "\t");
        }
    }
}
