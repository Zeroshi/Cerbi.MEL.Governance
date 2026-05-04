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

        /// <summary>
        /// Enforcement behavior for validation.
        /// Strict: validate in-process; emit violation JSON when violations occur.
        /// Audit: validate in-process; only record violations for auditing (no behavior change).
        /// Off: skip validation entirely.
        /// </summary>
        public GovernanceEnforcementMode EnforcementMode { get; set; } = GovernanceEnforcementMode.Strict;

        /// <summary>
        /// Minimum log level to run validation for.
        /// </summary>
        public Microsoft.Extensions.Logging.LogLevel MinValidationLevel { get; set; } = Microsoft.Extensions.Logging.LogLevel.Trace;

        /// <summary>
        /// Fraction of log entries to validate (0.0–1.0). 1.0 = validate all.
        /// </summary>
        public double SamplingRate { get; set; } = 1.0;

        // application identity for scoring events
        public string AppName { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public ScoreShippingOptions ScoreShipping { get; set; } = new();
        public ScoringIngestionOptions ScoringIngestion { get; set; } = new();
    }

    public enum GovernanceEnforcementMode
    {
        Strict = 0,
        Audit = 1,
        Off = 2
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

    public class ScoringIngestionOptions
    {
        public ScoringIngestionMode Mode { get; set; } = ScoringIngestionMode.QueueFirst;
        public AzureServiceBusOptions AzureServiceBus { get; set; } = new();
    }

    public enum ScoringIngestionMode
    {
        QueueFirst = 0,
        HttpOnly = 1,
        QueueOnly = 2
    }

    public class AzureServiceBusOptions
    {
        public string ConnectionString { get; set; } = string.Empty;
        public string QueueName { get; set; } = string.Empty;
    }
}
