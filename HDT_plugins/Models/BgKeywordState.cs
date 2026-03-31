using System.Collections.Generic;
using System.Linq;

namespace HDTplugins.Models
{
    public class BgKeywordState
    {
        public bool Taunt { get; set; }
        public bool DivineShield { get; set; }
        public bool Poisonous { get; set; }
        public bool Reborn { get; set; }
        public bool Windfury { get; set; }
        public bool MegaWindfury { get; set; }
        public bool Stealth { get; set; }
        public bool Cleave { get; set; }

        public IReadOnlyList<string> ToDisplayTokens()
        {
            var tokens = new List<string>();
            if (Taunt) tokens.Add("Taunt");
            if (DivineShield) tokens.Add("Divine Shield");
            if (Poisonous) tokens.Add("Poisonous");
            if (Reborn) tokens.Add("Reborn");
            if (MegaWindfury) tokens.Add("Mega Windfury");
            else if (Windfury) tokens.Add("Windfury");
            if (Stealth) tokens.Add("Stealth");
            if (Cleave) tokens.Add("Cleave");
            return tokens;
        }

        public bool HasAny => ToDisplayTokens().Any();
    }
}
