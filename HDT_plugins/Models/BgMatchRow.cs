using System.Windows.Media;

namespace HDTplugins.Models
{
    public class BgMatchRow
    {
        public string MatchId { get; set; }
        public int Placement { get; set; }
        public int RatingAfter { get; set; }
        public int RatingDelta { get; set; }
        public string HeroName { get; set; }

        public string AnomalyDisplay { get; set; }
        public string FinalBoardDisplay { get; set; }

        public string RatingDeltaText
        {
            get
            {
                if (RatingDelta > 0)
                    return "+" + RatingDelta;
                return RatingDelta.ToString();
            }
        }

        public Brush RatingDeltaBrush
        {
            get
            {
                if (RatingDelta > 0)
                    return Brushes.ForestGreen;
                if (RatingDelta < 0)
                    return Brushes.IndianRed;
                return Brushes.Gray;
            }
        }
    }
}
