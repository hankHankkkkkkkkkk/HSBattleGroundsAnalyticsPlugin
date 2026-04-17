using System.Collections.Generic;

namespace HDTplugins.Models
{
    internal enum BgDraftOverlayKind
    {
        None,
        HeroPick,
        LesserTrinketPick,
        GreaterTrinketPick
    }

    internal sealed class BgDraftOverlayStatsRow
    {
        public string CardId { get; set; }
        public bool HasData { get; set; }
        public string PickRateText { get; set; }
        public string AveragePlacementText { get; set; }
        public string FirstRateText { get; set; }
    }

    internal sealed class BgDraftOverlayRenderItem
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool HasData { get; set; }
        public IReadOnlyList<KeyValuePair<string, string>> Metrics { get; set; }
    }
}
