using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using HDTplugins.Localization;
using HDTplugins.Models;
using HDTplugins.Views;

namespace HDTplugins.Services
{
    internal sealed class BgDraftOverlayService
    {
        private readonly BgDraftOverlayStatsService _statsService;
        private BgDraftOverlayWindow _window;
        private BgDraftOverlayKind _lastKind = BgDraftOverlayKind.None;
        private string _lastSignature;

        public BgDraftOverlayService(StatsStore store, PluginSettingsService settingsService)
        {
            _statsService = new BgDraftOverlayStatsService(store, settingsService);
        }

        public void Refresh(BgGameProbe probe)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;

            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Refresh(probe)));
                return;
            }

            if (probe == null || !probe.IsBattlegrounds || !TryGetHearthstoneBounds(out var bounds))
            {
                Hide();
                return;
            }

            var state = ResolveState(probe);
            if (state.Kind == BgDraftOverlayKind.None || state.CardIds.Count == 0)
            {
                Hide();
                return;
            }

            EnsureWindow();
            _window.ApplyBounds(bounds);

            var signature = state.Kind + ":" + string.Join("|", state.CardIds);
            _window.Render(BuildRenderItems(bounds, state));

            _lastKind = state.Kind;
            _lastSignature = signature;

            if (!_window.IsVisible)
                _window.Show();
        }

        public void Hide()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;

            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Hide));
                return;
            }

            _lastKind = BgDraftOverlayKind.None;
            _lastSignature = null;
            _window?.Hide();
        }

        public void InvalidateStats()
        {
            _statsService.Invalidate();
        }

        public void Dispose()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;

            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Dispose));
                return;
            }

            _window?.Close();
            _window = null;
        }

        private void EnsureWindow()
        {
            if (_window != null)
                return;

            _window = new BgDraftOverlayWindow();
        }

        private IReadOnlyList<BgDraftOverlayRenderItem> BuildRenderItems(Rect bounds, BgDraftOverlayState state)
        {
            var statsRows = state.Kind == BgDraftOverlayKind.HeroPick
                ? _statsService.GetHeroStats(state.CardIds)
                : _statsService.GetTrinketStats(state.CardIds, state.TrinketFilter);

            var gameRect = GetPrimaryGameRect(bounds);
            var layoutSlots = GetSlots(gameRect, state.Kind, state.CardIds.Count);
            var cardWidth = GetCardWidth(gameRect, state.Kind, state.CardIds.Count);
            var cardHeight = 72d;
            var noDataHeight = 42d;
            var result = new List<BgDraftOverlayRenderItem>();

            for (var i = 0; i < statsRows.Count && i < layoutSlots.Count; i++)
            {
                var row = statsRows[i];
                var slot = layoutSlots[i];
                var height = row.HasData ? cardHeight : noDataHeight;
                var left = Clamp(slot.X - cardWidth / 2, 8, Math.Max(8, bounds.Width - cardWidth - 8));
                var top = Clamp(slot.Y, 8, Math.Max(8, bounds.Height - height - 8));
                result.Add(new BgDraftOverlayRenderItem
                {
                    Left = left,
                    Top = top,
                    Width = cardWidth,
                    Height = height,
                    HasData = row.HasData,
                    Metrics = row.HasData
                        ? new[]
                        {
                            new KeyValuePair<string, string>(Loc.S("OverlayDraft_PickRateLabel"), row.PickRateText),
                            new KeyValuePair<string, string>(Loc.S("Common_AvgPlacementLabel"), row.AveragePlacementText),
                            new KeyValuePair<string, string>(Loc.S("Common_FirstRateLabel"), row.FirstRateText)
                        }
                        : Array.Empty<KeyValuePair<string, string>>()
                });
            }

            return result;
        }

        private static double GetCardWidth(Rect gameRect, BgDraftOverlayKind kind, int count)
        {
            var ratio = kind == BgDraftOverlayKind.HeroPick ? 0.132 : 0.13;
            var width = gameRect.Width * ratio;
            return Math.Max(138, Math.Min(198, width));
        }

        private static List<Point> GetSlots(Rect gameRect, BgDraftOverlayKind kind, int count)
        {
            var anchors = GetAnchors(kind, count);
            var topRatio = kind == BgDraftOverlayKind.HeroPick ? 0.212 : 0.252;
            var safeCount = Math.Min(Math.Max(0, count), anchors.Length);
            var points = new List<Point>(safeCount);
            for (var i = 0; i < safeCount; i++)
            {
                points.Add(new Point(
                    gameRect.Left + gameRect.Width * anchors[i],
                    gameRect.Top + gameRect.Height * topRatio));
            }

            return points;
        }

        private static double[] GetAnchors(BgDraftOverlayKind kind, int count)
        {
            if (kind == BgDraftOverlayKind.HeroPick)
                return new[] { 0.142, 0.355, 0.552, 0.724 };

            if (count >= 4)
                return new[] { 0.192, 0.358, 0.522, 0.668 };

            if (count == 3)
                return new[] { 0.29, 0.5, 0.71 };

            if (count == 2)
                return new[] { 0.39, 0.61 };

            return new[] { 0.5 };
        }

        private static BgDraftOverlayState ResolveState(BgGameProbe probe)
        {
            if (!probe.HasResolvedHero && probe.OfferedHeroCardIds?.Length > 0)
            {
                return new BgDraftOverlayState
                {
                    Kind = BgDraftOverlayKind.HeroPick,
                    CardIds = NormalizeCardIds(probe.OfferedHeroCardIds)
                };
            }

            if (string.IsNullOrWhiteSpace(probe.LesserTrinketCardId) && probe.LesserTrinketOptionCardIds?.Length > 0)
            {
                return new BgDraftOverlayState
                {
                    Kind = BgDraftOverlayKind.LesserTrinketPick,
                    TrinketFilter = TrinketFilter.Lesser,
                    CardIds = NormalizeCardIds(probe.LesserTrinketOptionCardIds)
                };
            }

            if (string.IsNullOrWhiteSpace(probe.GreaterTrinketCardId) && probe.GreaterTrinketOptionCardIds?.Length > 0)
            {
                return new BgDraftOverlayState
                {
                    Kind = BgDraftOverlayKind.GreaterTrinketPick,
                    TrinketFilter = TrinketFilter.Greater,
                    CardIds = NormalizeCardIds(probe.GreaterTrinketOptionCardIds)
                };
            }

            return new BgDraftOverlayState();
        }

        private static List<string> NormalizeCardIds(IEnumerable<string> cardIds)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cardId in cardIds ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(cardId))
                    continue;
                if (seen.Add(cardId))
                    result.Add(cardId);
            }

            return result;
        }

        private static Rect GetPrimaryGameRect(Rect bounds)
        {
            if (bounds.IsEmpty)
                return bounds;

            var targetWidth = Math.Min(bounds.Width, bounds.Height * (4.0 / 3.0));
            var targetHeight = targetWidth * 0.75;
            var left = bounds.Left + (bounds.Width - targetWidth) / 2;
            var top = bounds.Top + (bounds.Height - targetHeight) / 2;
            return new Rect(left, top, targetWidth, targetHeight);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private static bool TryGetHearthstoneBounds(out Rect bounds)
        {
            bounds = Rect.Empty;
            var handle = FindWindow("UnityWndClass", "Hearthstone");
            if (handle == IntPtr.Zero)
                handle = FindWindow(null, "Hearthstone");
            if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect))
                return false;
            if (rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                return false;

            bounds = new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            return true;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private sealed class BgDraftOverlayState
        {
            public BgDraftOverlayKind Kind { get; set; }
            public TrinketFilter TrinketFilter { get; set; }
            public List<string> CardIds { get; set; } = new List<string>();
        }
    }
}
