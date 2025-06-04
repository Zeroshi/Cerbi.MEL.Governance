namespace Cerbi
{
    public class CerbiGovernanceMELSettings
    {
        /// <summary>
        /// This string is literally the “profile name” (i.e. topic) that will be used as a fallback
        /// if no [CerbiTopic("…")] attribute is found on the call stack.
        /// </summary>
        public string Profile { get; set; } = "default";

        /// <summary>
        /// Path (relative or absolute) to your Cerbi governance JSON file.
        /// </summary>
        public string ConfigPath { get; set; } = "cerbi_governance.json";

        /// <summary>
        /// Set to false to temporarily disable all Cerbi enforcement at runtime.
        /// </summary>
        public bool Enabled { get; set; } = true;
    }
}
