using System;
using System.Globalization;
using System.Threading;

namespace HDTplugins.Localization
{
    public static class LocalizationService
    {
        public const string DefaultCultureName = "en-US";
        public const string ChineseCultureName = "zh-CN";

        public static event EventHandler LanguageChanged;

        public static CultureInfo CurrentCulture => CultureInfo.CurrentUICulture;

        public static void InitializeFromHostCulture()
        {
            SetLanguage(CultureInfo.CurrentUICulture?.Name);
        }

        public static void Initialize(string preferredCultureName)
        {
            if (!string.IsNullOrWhiteSpace(preferredCultureName))
            {
                SetLanguage(preferredCultureName);
                return;
            }

            InitializeFromHostCulture();
        }

        public static void SetLanguage(string cultureName)
        {
            var culture = NormalizeCulture(cultureName);
            if (string.Equals(CultureInfo.CurrentUICulture.Name, culture.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(CultureInfo.CurrentCulture.Name, culture.Name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Thread.CurrentThread.CurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;

            Loc.Instance.NotifyLanguageChanged();
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        public static CultureInfo NormalizeCulture(string cultureName)
        {
            if (!string.IsNullOrWhiteSpace(cultureName))
            {
                try
                {
                    var requested = CultureInfo.GetCultureInfo(cultureName);
                    if (requested.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                        return CultureInfo.GetCultureInfo(ChineseCultureName);

                    if (requested.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                        return CultureInfo.GetCultureInfo(DefaultCultureName);
                }
                catch (CultureNotFoundException)
                {
                }
            }

            return CultureInfo.GetCultureInfo(DefaultCultureName);
        }
    }
}
