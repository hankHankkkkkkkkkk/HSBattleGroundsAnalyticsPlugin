using System;

namespace HDTplugins.Models
{
    public class PluginSettings
    {
        public bool AutoOpenOnStartup { get; set; } = true;
        public string Language { get; set; }
        public double ScoreLine { get; set; } = 4.5;
        public string HeroStatsDefaultSort { get; set; } = "Picks";
        public string SelectedAccountKey { get; set; }

        public double GetNormalizedScoreLine()
        {
            if (Math.Abs(ScoreLine - 2.5) < 0.01)
                return 2.5;
            if (Math.Abs(ScoreLine - 3.5) < 0.01)
                return 3.5;
            return 4.5;
        }
    }
}
