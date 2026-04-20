using HearthDb;
using HearthDb.Enums;
using HDTplugins.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

using HdtLog = Hearthstone_Deck_Tracker.Utility.Logging.Log;

namespace HDTplugins.Services
{
    public static class GameTextService
    {
        private static readonly object SyncRoot = new object();
        private static readonly HashSet<Locale> LoadedLocales = new HashSet<Locale>();

        private static Locale _currentLocale = Locale.enUS;
        private static bool _initialized;

        public static void Initialize(bool warmLocaleImmediately = true)
        {
            if (_initialized)
                return;

            _initialized = true;
            LocalizationService.LanguageChanged += OnLanguageChanged;
            if (warmLocaleImmediately)
                ApplyCulture(LocalizationService.CurrentCulture);
            else
                _currentLocale = ToLocale(LocalizationService.CurrentCulture);
        }

        public static void ForceRefreshCurrentLanguage()
        {
            var locale = ToLocale(LocalizationService.CurrentCulture);
            lock (SyncRoot)
            {
                LoadedLocales.Remove(locale);
                EnsureLocaleLoaded(locale);
                TrySetHdtSelectedLanguage(locale);
                _currentLocale = locale;
            }
        }

        public static string GetCardName(string cardId, string fallback = null)
        {
            EnsureCurrentLanguageReady();

            if (string.IsNullOrWhiteSpace(cardId))
                return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback;

            try
            {
                var card = ResolveCard(cardId);
                if (card == null)
                    return FirstNonEmpty(TryGetHdtCardName(cardId), fallback, cardId);

                var hdtName = TryGetHdtCardName(cardId);
                var localized = GetLocalizedCardName(card);
                return FirstNonEmpty(hdtName, localized, card.Name, fallback, cardId);
            }
            catch
            {
                return FirstNonEmpty(TryGetHdtCardName(cardId), fallback, cardId);
            }
        }

        public static string GetRaceNameFromCardId(string cardId, string fallback = null)
        {
            EnsureCurrentLanguageReady();

            if (string.IsNullOrWhiteSpace(cardId))
                return FirstNonEmpty(fallback, string.Empty);

            try
            {
                var hdtRace = TryGetHdtRace(cardId);
                if (!string.IsNullOrWhiteSpace(hdtRace))
                    return FirstNonEmpty(hdtRace, fallback);

                var card = ResolveCard(cardId);
                if (card == null)
                    return FirstNonEmpty(fallback, string.Empty);

                return GetRaceName(card.Race, fallback);
            }
            catch
            {
                return FirstNonEmpty(fallback, string.Empty);
            }
        }

        public static string GetRaceName(string raceValue, string fallback = null)
        {
            EnsureCurrentLanguageReady();

            if (string.IsNullOrWhiteSpace(raceValue))
                return FirstNonEmpty(fallback, string.Empty);

            if (string.Equals(raceValue, "NEUTRAL", StringComparison.OrdinalIgnoreCase))
            {
                var neutral = Loc.S("GameRace_NEUTRAL");
                return FirstNonEmpty(
                    !string.Equals(neutral, "GameRace_NEUTRAL", StringComparison.Ordinal) ? neutral : string.Empty,
                    fallback,
                    "Neutral");
            }

            if (!Enum.TryParse(raceValue, true, out Race race))
                return FirstNonEmpty(fallback, raceValue);

            return GetRaceName(race, fallback);
        }

        public static string GetRaceName(Race race, string fallback = null)
        {
            EnsureCurrentLanguageReady();

            if (race == Race.INVALID || race == Race.BLANK)
                return FirstNonEmpty(fallback, string.Empty);

            var value = Loc.S("GameRace_" + race);
            if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value, "GameRace_" + race, StringComparison.Ordinal))
                return value;

            return FirstNonEmpty(fallback, HumanizeRace(race));
        }

        private static void OnLanguageChanged(object sender, EventArgs e)
        {
            ApplyCulture(LocalizationService.CurrentCulture);
        }

        private static void ApplyCulture(CultureInfo culture)
        {
            var locale = ToLocale(culture);
            lock (SyncRoot)
            {
                if (_currentLocale == locale && LoadedLocales.Contains(locale))
                    return;

                EnsureLocaleLoaded(locale);
                TrySetHdtSelectedLanguage(locale);
                _currentLocale = locale;
            }
        }

        private static void EnsureCurrentLanguageReady()
        {
            ApplyCulture(LocalizationService.CurrentCulture);
        }

        private static void EnsureLocaleLoaded(Locale locale)
        {
            if (LoadedLocales.Contains(locale))
                return;

            try
            {
                var managerType = System.Type.GetType("Hearthstone_Deck_Tracker.Utility.Assets.CardDefsManager, HearthstoneDeckTracker", false);
                var loadLocale = managerType?.GetMethod("LoadLocale", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), typeof(bool) }, null);
                if (loadLocale == null)
                {
                    LoadedLocales.Add(locale);
                    return;
                }

                var task = loadLocale.Invoke(null, new object[] { ToHdtLocaleName(locale), true });
                var getAwaiter = task?.GetType().GetMethod("GetAwaiter", BindingFlags.Public | BindingFlags.Instance);
                var awaiter = getAwaiter?.Invoke(task, null);
                var getResult = awaiter?.GetType().GetMethod("GetResult", BindingFlags.Public | BindingFlags.Instance);
                getResult?.Invoke(awaiter, null);
                LoadedLocales.Add(locale);
            }
            catch (Exception ex)
            {
                HdtLog.Warn("[BGStats] Failed to load localized card defs, falling back to default card text: " + ex.Message);
            }
        }

        private static void TrySetHdtSelectedLanguage(Locale locale)
        {
            try
            {
                var cardType = System.Type.GetType("Hearthstone_Deck_Tracker.Hearthstone.Card, HearthstoneDeckTracker", false);
                var field = cardType?.GetField("_selectedLanguage", BindingFlags.NonPublic | BindingFlags.Static);
                if (field == null)
                    return;

                var nullableLocale = Activator.CreateInstance(field.FieldType, locale);
                field.SetValue(null, nullableLocale);
            }
            catch (Exception ex)
            {
                HdtLog.Warn("[BGStats] Failed to sync HDT selected card language, ignored: " + ex.Message);
            }
        }

        private static Card ResolveCard(string cardId)
        {
            if (Cards.All.TryGetValue(cardId, out var exact))
                return exact;

            return Cards.All.Values.FirstOrDefault(x => string.Equals(x.Id, cardId, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetLocalizedCardName(Card card)
        {
            if (card == null)
                return string.Empty;

            try
            {
                return card.GetLocName(_currentLocale);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string TryGetHdtCardName(string cardId)
        {
            var card = CreateHdtCard(cardId);
            if (card == null)
                return string.Empty;

            return FirstNonEmpty(
                GetPropertyText(card, "LocalizedName"),
                GetPropertyText(card, "Name"));
        }

        private static string TryGetHdtRace(string cardId)
        {
            var card = CreateHdtCard(cardId);
            return card == null ? string.Empty : GetPropertyText(card, "Race");
        }

        private static object CreateHdtCard(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId))
                return null;

            try
            {
                var cardType = System.Type.GetType("Hearthstone_Deck_Tracker.Hearthstone.Card, HearthstoneDeckTracker", false);
                if (cardType == null)
                    return null;

                return Activator.CreateInstance(cardType, new object[] { cardId });
            }
            catch
            {
                return null;
            }
        }

        private static string GetPropertyText(object target, string propertyName)
        {
            try
            {
                var value = target?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target, null);
                return value?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static Locale ToLocale(CultureInfo culture)
        {
            var name = culture?.Name ?? string.Empty;
            if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return Locale.zhCN;

            return Locale.enUS;
        }

        private static string ToHdtLocaleName(Locale locale)
        {
            switch (locale)
            {
                case Locale.zhCN:
                    return "zhCN";
                default:
                    return "enUS";
            }
        }

        private static string HumanizeRace(Race race)
        {
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(race.ToString().ToLowerInvariant());
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
        }
    }
}
