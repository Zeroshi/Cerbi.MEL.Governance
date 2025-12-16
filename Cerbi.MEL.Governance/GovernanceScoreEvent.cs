using System;
using System.Collections.Generic;

namespace Cerbi
{
    [Obsolete("Use Cerbi.Contracts.ScoringQueueEnvelopeDto/ScoringEventDto instead.")]
    public class GovernanceScoreEvent
    {
        public string? TenantId { get; set; }
        public string AppName { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        public string? Topic { get; set; }
        public string? Category { get; set; }
        public string? LogId { get; set; }
        public string? IdempotencyKey { get; set; }
        public string? CorrelationId { get; set; }
        public int EventId { get; set; }
        public string? EventName { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public double ScoreImpact { get; set; }
        public bool GovernanceRelaxed { get; set; }
        public GovernanceViolationSummary[] Violations { get; set; } = Array.Empty<GovernanceViolationSummary>();
        public IDictionary<string, object?> Fields { get; set; } = new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    public class GovernanceViolationSummary
    {
        public string Code { get; set; } = string.Empty; // e.g., MissingField:userId
        public string Field { get; set; } = string.Empty; // extracted field name if applicable
        public string Rule { get; set; } = string.Empty; // raw rule string
    }
}
