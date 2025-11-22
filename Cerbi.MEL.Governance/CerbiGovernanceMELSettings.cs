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

        // New: application identity for scoring events
        public string AppName { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        // New: score shipping options
        public ScoreShippingOptions ScoreShipping { get; set; } = new();
    }

    public class ScoreShippingOptions
    {
        public bool Enabled { get; set; } = false; // feature toggle
        public bool LicenseAllowsScoring { get; set; } = false; // license gate
        public int BatchSize { get; set; } = 50; // max events per batch
        public int MaxQueueSize { get; set; } = 10_000; // safety bound
        public int FlushIntervalSeconds { get; set; } = 5; // background flush cadence
        public int MaxRetries { get; set; } = 3; // retry attempts per batch
        public int RetryDelayMilliseconds { get; set; } = 500; // base delay
        public string Endpoint { get; set; } = ""; // scoring endpoint URL
        public string ApiKey { get; set; } = ""; // optional auth header secret
    }
}
