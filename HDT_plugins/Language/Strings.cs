using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace HDT_plugins.Language
{
    internal static class Strings
    {
        private const string DefaultCultureName = "en-US";
        private const string ChineseCultureName = "zh-CN";
        private const string NeutralResourceName = "HDT_plugins.Language.Strings.resources";
        private const string EnglishResourceName = "HDT_plugins.Language.Strings.en-US.resources";
        private const string ChineseResourceName = "HDT_plugins.Language.Strings.zh-CN.resources";

        private static readonly Lazy<IReadOnlyDictionary<string, string>> NeutralResources =
            new Lazy<IReadOnlyDictionary<string, string>>(() => LoadResources(NeutralResourceName));

        private static readonly Lazy<IReadOnlyDictionary<string, string>> EnglishResources =
            new Lazy<IReadOnlyDictionary<string, string>>(() => LoadResources(EnglishResourceName));

        private static readonly Lazy<IReadOnlyDictionary<string, string>> ChineseResources =
            new Lazy<IReadOnlyDictionary<string, string>>(() => LoadResources(ChineseResourceName));

        internal static CultureInfo Culture { get; set; }

        internal static string GetString(string key, CultureInfo culture)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            var normalizedCultureName = NormalizeCultureName(culture);
            var localizedValue = TryGetString(GetResources(normalizedCultureName), key);
            if (!string.IsNullOrWhiteSpace(localizedValue))
                return localizedValue;

            localizedValue = TryGetString(NeutralResources.Value, key);
            return string.IsNullOrWhiteSpace(localizedValue) ? key : localizedValue;
        }

        private static IReadOnlyDictionary<string, string> GetResources(string cultureName)
        {
            if (string.Equals(cultureName, ChineseCultureName, StringComparison.OrdinalIgnoreCase))
                return ChineseResources.Value;

            if (string.Equals(cultureName, DefaultCultureName, StringComparison.OrdinalIgnoreCase))
                return EnglishResources.Value;

            return NeutralResources.Value;
        }

        private static string NormalizeCultureName(CultureInfo culture)
        {
            var cultureName = culture?.Name;
            if (!string.IsNullOrWhiteSpace(cultureName))
            {
                if (cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                    return ChineseCultureName;

                if (cultureName.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                    return DefaultCultureName;
            }

            return DefaultCultureName;
        }

        private static string TryGetString(IReadOnlyDictionary<string, string> resources, string key)
        {
            string value;
            return resources.TryGetValue(key, out value) ? value : null;
        }

        private static IReadOnlyDictionary<string, string> LoadResources(string resourceName)
        {
            var assembly = typeof(Strings).Assembly;
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    return new Dictionary<string, string>(StringComparer.Ordinal);

                using (var reader = new ResourceReader(stream))
                {
                    var values = new Dictionary<string, string>(StringComparer.Ordinal);
                    var enumerator = reader.GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        var entry = (DictionaryEntry)enumerator.Current;
                        var key = entry.Key as string;
                        if (string.IsNullOrWhiteSpace(key))
                            continue;

                        values[key] = entry.Value as string ?? string.Empty;
                    }

                    return values;
                }
            }
        }
    }
}
