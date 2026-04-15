using System.ComponentModel;
using System.Globalization;
using HDT_plugins.Language;

namespace HDTplugins.Localization
{
    public sealed class Loc : INotifyPropertyChanged
    {
        public static Loc Instance { get; } = new Loc();

        public event PropertyChangedEventHandler PropertyChanged;

        public string this[string key] => Get(key);

        public static string S(string key)
        {
            return Instance.Get(key);
        }

        public static string F(string key, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, S(key), args);
        }

        public string Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            var value = Strings.GetString(key, Strings.Culture ?? CultureInfo.CurrentUICulture);
            return string.IsNullOrWhiteSpace(value) ? key : value;
        }

        internal void NotifyLanguageChanged()
        {
            Strings.Culture = CultureInfo.CurrentUICulture;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }
}
