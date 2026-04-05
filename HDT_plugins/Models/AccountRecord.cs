namespace HDTplugins.Models
{
    public class AccountRecord
    {
        public string Key { get; set; }
        public string AccountHi { get; set; }
        public string AccountLo { get; set; }
        public string BattleTag { get; set; }
        public string ServerInfo { get; set; }
        public string RegionCode { get; set; }
        public string RegionName { get; set; }
        public bool IsUnknown { get; set; }
        public int MatchCount { get; set; }

        public string DisplayName
        {
            get
            {
                if(!string.IsNullOrWhiteSpace(BattleTag))
                    return BattleTag;
                if(!string.IsNullOrWhiteSpace(AccountHi) || !string.IsNullOrWhiteSpace(AccountLo))
                    return $"ID {AccountHi ?? "?"}/{AccountLo ?? "?"}";
                return "无账号数据";
            }
        }

        public string BattleTagName
        {
            get
            {
                if(string.IsNullOrWhiteSpace(BattleTag))
                    return DisplayName;

                var index = BattleTag.IndexOf('#');
                return index > 0 ? BattleTag.Substring(0, index) : BattleTag;
            }
        }

        public string BattleTagCode
        {
            get
            {
                if(string.IsNullOrWhiteSpace(BattleTag))
                    return string.Empty;

                var index = BattleTag.IndexOf('#');
                if(index < 0 || index >= BattleTag.Length - 1)
                    return string.Empty;
                return BattleTag.Substring(index);
            }
        }

        public string RegionDisplay => string.IsNullOrWhiteSpace(RegionName) ? (RegionCode ?? "?") : RegionName;

        public string Subtitle
        {
            get
            {
                if(IsUnknown)
                    return "未识别到账号";
                return RegionDisplay;
            }
        }

        public string MenuDisplay
        {
            get
            {
                if(IsUnknown)
                    return DisplayName;

                var region = RegionDisplay;
                return string.IsNullOrWhiteSpace(region) ? DisplayName : $"{DisplayName} | {region}";
            }
        }

        public string FullDisplay => IsUnknown ? DisplayName : MenuDisplay;
    }
}
