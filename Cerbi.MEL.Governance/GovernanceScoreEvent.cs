using System;
using System.Collections.Generic;

namespace Cerbi
{
    [Obsolete("Use Cerbi.Contracts.ScoringQueueEnvelopeDto/ScoringEventDto instead.")]
    public sealed class GovernanceScoreEvent
    {
        private IReadOnlyDictionary<string, object?> _fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        public string? TenantId { get; init; }
        public string? AppName { get; init; }
        public string? Environment { get; init; }
        public string? Topic { get; init; }
        public string? Category { get; init; }
        public string? Profile { get; init; }
        public string? ConfigPath { get; init; }
        public string? LogId { get; init; }
        public string? CorrelationId { get; init; }
        public string? IdempotencyKey { get; init; }
        public DateTimeOffset Timestamp { get; init; }
        public double ScoreImpact { get; init; }
        public bool GovernanceRelaxed { get; init; }
        public IReadOnlyList<GovernanceViolationSummary> Violations { get; init; } = Array.Empty<GovernanceViolationSummary>();

        public IReadOnlyDictionary<string, object?> Fields
        {
            get => _fields;
            init => _fields = value ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        [Obsolete("Use Fields instead.")]
        public IReadOnlyDictionary<string, object?> Metadata
        {
            get => _fields;
            init => _fields = value ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public sealed class GovernanceViolationSummary
    {
        public string Code { get; set; } = string.Empty; // e.g., MissingField:userId
        public string Field { get; set; } = string.Empty; // extracted field name if applicable
        public string Rule { get; set; } = string.Empty; // raw rule string
    }
}
