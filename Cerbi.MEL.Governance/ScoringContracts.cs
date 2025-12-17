using System;
using System.Collections.Generic;
using Cerbi.Serilog.Governance;

namespace Cerbi.Contracts
{
    public sealed class ScoringQueueEnvelopeDto
    {
        public string SchemaVersion { get; set; } = "1.0";
        public string? IdempotencyKey { get; set; }
        public string? CorrelationId { get; set; }
        public string? TenantId { get; set; }
        public string? AppName { get; set; }
        public string? Environment { get; set; }
        public ScoringEventDto Payload { get; set; } = new();
    }

    public sealed class ScoringEventDto
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
        public ScoreBreakdownDto Score { get; set; } = new();
        public bool GovernanceRelaxed { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public IReadOnlyList<GovernanceViolationSummary> Violations { get; set; } = Array.Empty<GovernanceViolationSummary>();
        public IDictionary<string, object?> Fields { get; set; } = new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    public sealed class ScoreBreakdownDto
    {
        public int? Overall { get; set; }
        public int? Governance { get; set; }
    }
}
