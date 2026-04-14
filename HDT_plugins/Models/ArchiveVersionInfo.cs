namespace HDTplugins.Models
{
    public class ArchiveVersionInfo
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public string RawVersion { get; set; }
        public string PatchVersion { get; set; }
        public bool IsDetected { get; set; }
    }
}
