using System;
using System.Collections.Generic;

namespace Cerbi.Contracts
{
    public class ScoringQueueEnvelopeDto
    {
        public string SchemaVersion { get; set; } = "1.0";
        public string? IdempotencyKey { get; set; }
        public string? CorrelationId { get; set; }
        public string? TenantId { get; set; }
        public string? AppName { get; set; }
        public string? Environment { get; set; }
        public ScoringEventDto Payload { get; set; } = new();
    }

    public class ScoringEventDto
    {
        public string? IdempotencyKey { get; set; }
        public string? CorrelationId { get; set; }
        public string? TenantId { get; set; }
        public string? AppName { get; set; }
        public string? Environment { get; set; }
        public string? Topic { get; set; }
        public string? Category { get; set; }
        public string? LogId { get; set; }
        public string? GovernanceProfile { get; set; }
        public int EventId { get; set; }
        public string? EventName { get; set; }
        public double ScoreImpact { get; set; }
        public bool GovernanceRelaxed { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public Cerbi.GovernanceViolationSummary[] Violations { get; set; } = Array.Empty<Cerbi.GovernanceViolationSummary>();
        public IDictionary<string, object?> Fields { get; set; } = new Dictionary<string, object?>(StringComparer.Ordinal);
    }
}
