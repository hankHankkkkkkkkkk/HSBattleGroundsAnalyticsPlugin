using System.Collections.Generic;

namespace HDTplugins.Models
{
    public class ArchiveDisplayGroup
    {
        public string DisplayName { get; set; }
        public ArchiveVersionInfo RepresentativeArchive { get; set; }
        public List<ArchiveVersionInfo> Archives { get; set; } = new List<ArchiveVersionInfo>();
    }
}
