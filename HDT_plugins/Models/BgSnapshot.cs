using System.Collections.Generic;

namespace HDTplugins.Models
{
    public class BgSnapshot
    {
        public string MatchId { get; set; }
        public string Timestamp { get; set; }
        public string GameVersion { get; set; }

        public string HeroCardId { get; set; }
        public string HeroName { get; set; }
        public string HeroSkinCardId { get; set; }
        public string InitialHeroPowerCardId { get; set; }
        public string HeroPowerCardId { get; set; }

        public int RatingBefore { get; set; }
        public int RatingAfter { get; set; }
        public int RatingDelta { get; set; }
        public int Placement { get; set; }

        public string[] AvailableRaces { get; set; }
        public string AnomalyCardId { get; set; }
        public string AnomalyName { get; set; }
        public string[] FinalBoardCardIds { get; set; }

        public List<BgBoardMinionSnapshot> FinalBoard { get; set; } = new List<BgBoardMinionSnapshot>();
        public List<BgTavernUpgradePoint> TavernUpgradeTimeline { get; set; } = new List<BgTavernUpgradePoint>();
        public List<string> AutoTags { get; set; } = new List<string>();
        public List<string> ManualTags { get; set; } = new List<string>();
    }
}
