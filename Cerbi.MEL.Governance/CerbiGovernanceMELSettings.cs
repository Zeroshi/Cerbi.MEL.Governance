namespace Cerbi
{
    public class CerbiGovernanceMELSettings
    {
        // ← note: the class currently has "Profile", not "DefaultTopic"
        public string Profile { get; set; } = "default";
        public string ConfigPath { get; set; } = "cerbi_governance.json";
        public bool Enabled { get; set; } = true;
    }
}
