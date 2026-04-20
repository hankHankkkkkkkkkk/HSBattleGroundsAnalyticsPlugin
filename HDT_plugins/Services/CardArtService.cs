using HDTplugins.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HDTplugins.Services
{
    public static class CardArtService
    {
        private const string BaseUrl = "https://art.hearthstonejson.com/v1";

        public static IReadOnlyList<string> GetHeroPreviewUrls(params string[] cardIds)
        {
            var language = GetArtLanguageSegment();
            var ids = NormalizeCardIds(cardIds);
            var urls = new List<string>();
            urls.AddRange(ids.Select(id => $"{BaseUrl}/heroes/latest/256x/{id}.png"));
            urls.AddRange(ids.Select(id => $"{BaseUrl}/render/latest/{language}/256x/{id}.png"));
            urls.AddRange(ids.Select(id => $"{BaseUrl}/256x/{id}.jpg"));
            urls.AddRange(ids.Select(id => $"{BaseUrl}/tiles/{id}.jpg"));
            return urls;
        }

        public static IReadOnlyList<string> GetCardPreviewUrls(params string[] cardIds)
        {
            var language = GetArtLanguageSegment();
            var ids = NormalizeCardIds(cardIds);
            var urls = new List<string>();
            urls.AddRange(ids.Select(id => $"{BaseUrl}/render/latest/{language}/256x/{id}.png"));
            urls.AddRange(ids.Select(id => $"{BaseUrl}/bgs/latest/{language}/256x/{id}.png"));
            urls.AddRange(ids.Select(id => $"{BaseUrl}/256x/{id}.jpg"));
            urls.AddRange(ids.Select(id => $"{BaseUrl}/tiles/{id}.jpg"));
            return urls;
        }

        public static IReadOnlyList<string> GetMinionPreviewUrls(string cardId, bool isGolden)
        {
            var language = GetArtLanguageSegment();
            var ids = NormalizeCardIds(cardId);
            var urls = new List<string>();
            if (isGolden)
                urls.AddRange(ids.Select(id => $"{BaseUrl}/bgs/latest/{language}/256x/{id}_triple.png"));
            urls.AddRange(ids.Select(id => $"{BaseUrl}/bgs/latest/{language}/256x/{id}.png"));
            urls.AddRange(ids.Select(id => $"{BaseUrl}/render/latest/{language}/256x/{id}.png"));
            urls.AddRange(ids.Select(id => $"{BaseUrl}/256x/{id}.jpg"));
            urls.AddRange(ids.Select(id => $"{BaseUrl}/tiles/{id}.jpg"));
            return urls;
        }

        public static IReadOnlyList<string> GetRenderUrls(params string[] cardIds)
        {
            var language = GetArtLanguageSegment();
            return BuildUrlList(cardIds, id => $"{BaseUrl}/render/latest/{language}/256x/{id}.png");
        }

        public static IReadOnlyList<string> GetBattlegroundsThenRenderUrls(params string[] cardIds)
        {
            var language = GetArtLanguageSegment();
            var ids = (cardIds ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var urls = new List<string>();
            urls.AddRange(ids.Select(id => $"{BaseUrl}/bgs/latest/{language}/256x/{id}.png"));
            urls.AddRange(ids.Select(id => $"{BaseUrl}/render/latest/{language}/256x/{id}.png"));
            return urls;
        }

        private static IReadOnlyList<string> NormalizeCardIds(params string[] cardIds)
        {
            return (cardIds ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IReadOnlyList<string> BuildUrlList(IEnumerable<string> cardIds, Func<string, string> createUrl)
        {
            return NormalizeCardIds((cardIds ?? Array.Empty<string>()).ToArray())
                .Select(createUrl)
                .ToList();
        }

        private static string GetArtLanguageSegment()
        {
            var culture = LocalizationService.CurrentCulture ?? CultureInfo.GetCultureInfo("en-US");
            var name = culture.Name;
            if (string.Equals(name, "zh-CN", StringComparison.OrdinalIgnoreCase))
                return "zhCN";
            if (string.Equals(name, "zh-TW", StringComparison.OrdinalIgnoreCase))
                return "zhTW";
            if (string.Equals(name, "de-DE", StringComparison.OrdinalIgnoreCase))
                return "deDE";
            if (string.Equals(name, "es-ES", StringComparison.OrdinalIgnoreCase))
                return "esES";
            if (string.Equals(name, "es-MX", StringComparison.OrdinalIgnoreCase))
                return "esMX";
            if (string.Equals(name, "fr-FR", StringComparison.OrdinalIgnoreCase))
                return "frFR";
            if (string.Equals(name, "it-IT", StringComparison.OrdinalIgnoreCase))
                return "itIT";
            if (string.Equals(name, "ja-JP", StringComparison.OrdinalIgnoreCase))
                return "jaJP";
            if (string.Equals(name, "ko-KR", StringComparison.OrdinalIgnoreCase))
                return "koKR";
            if (string.Equals(name, "pl-PL", StringComparison.OrdinalIgnoreCase))
                return "plPL";
            if (string.Equals(name, "pt-BR", StringComparison.OrdinalIgnoreCase))
                return "ptBR";
            if (string.Equals(name, "ru-RU", StringComparison.OrdinalIgnoreCase))
                return "ruRU";
            if (string.Equals(name, "th-TH", StringComparison.OrdinalIgnoreCase))
                return "thTH";
            return "enUS";
        }
    }
}
