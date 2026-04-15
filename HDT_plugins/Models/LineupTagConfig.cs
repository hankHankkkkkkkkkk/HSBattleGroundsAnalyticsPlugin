using System.Collections.Generic;

namespace HDTplugins.Models
{
    public class LineupTagConfig
    {
        public List<LineupTagDefinition> AvailableTags { get; set; } = new List<LineupTagDefinition>();
        public List<LineupTagRule> Rules { get; set; } = new List<LineupTagRule>();
    }

    public class LineupTagDefinition
    {
        public string Name { get; set; }
        public bool IsEditable { get; set; }
    }

    public class LineupTagRule
    {
        public string Tag { get; set; }
        public int Priority { get; set; } = 0;
        public List<string> VersionRange { get; set; } = new List<string>();
        public LineupTagCondition Conditions { get; set; } = new LineupTagCondition();
    }

    public class LineupTagCondition
    {
        public string Op { get; set; }
        public List<LineupTagCondition> Items { get; set; } = new List<LineupTagCondition>();
        public string Type { get; set; }
        public string Race { get; set; }
        public string CardId { get; set; }
        public string[] CardIds { get; set; }
        public string HeroCardId { get; set; }
        public string HeroPowerCardId { get; set; }
        public string Keyword { get; set; }
        public bool? IsGolden { get; set; }
        public int Value { get; set; }
    }
}
