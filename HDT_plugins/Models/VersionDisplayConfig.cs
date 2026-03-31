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
    }
}
