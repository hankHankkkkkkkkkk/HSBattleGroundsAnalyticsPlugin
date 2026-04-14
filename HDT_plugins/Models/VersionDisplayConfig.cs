using System.Collections.Generic;

namespace HDTplugins.Models
{
    public class VersionDisplayConfig
    {
        public List<VersionDisplayMapping> Versions { get; set; } = new List<VersionDisplayMapping>();
    }

    public class VersionDisplayMapping
    {
        public string RawVersion { get; set; }
        public string DisplayName { get; set; }
        public bool IsHidden { get; set; }
    }

    public class VersionRangeConfig
    {
        public List<VersionRangeMapping> Versions { get; set; } = new List<VersionRangeMapping>();
    }

    public class VersionRangeMapping
    {
        public string DisplayName { get; set; }
        public List<string> VersionRange { get; set; } = new List<string>();
        public bool IsHidden { get; set; }
    }

    public class VersionMenuItem
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public bool IsRange { get; set; }
        public List<string> VersionRange { get; set; } = new List<string>();
        public string PatchVersion { get; set; }
    }
}
