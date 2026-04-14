using Hearthstone_Deck_Tracker;
using System;
using System.Reflection;

namespace HDTplugins.Services
{
    internal static class HeroIdNormalizer
    {
        public static string Normalize(string heroCardId)
        {
            if (string.IsNullOrWhiteSpace(heroCardId))
                return string.Empty;

            heroCardId = heroCardId.Trim();

            try
            {
                var asm = typeof(Core).Assembly;
                var type = asm.GetType("Hearthstone_Deck_Tracker.Hearthstone.BattlegroundsUtils");
                var method = type?.GetMethod("GetOriginalHeroId", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                var resolved = method?.Invoke(null, new object[] { heroCardId }) as string;
                if (!string.IsNullOrWhiteSpace(resolved))
                    return resolved.Trim();
            }
            catch
            {
            }

            var idx = heroCardId.IndexOf("_SKIN_", StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
                return heroCardId.Substring(0, idx);

            idx = heroCardId.IndexOf("_ALT_", StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
                return heroCardId.Substring(0, idx);

            return heroCardId;
        }
    }
}
