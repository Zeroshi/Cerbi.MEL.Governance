using System;
using System.Security.Cryptography;
using System.Text;
using Cerbi.Contracts.Contracts;

namespace Cerbi.Serilog.Governance
{
    internal static class ScoringEnvelopeFactory
    {
        private const string MessageType = "scoring-event";
        private static readonly string ProducerVersion = typeof(ScoringEnvelopeFactory).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        private static readonly string RuntimeSignature = $".NET {Environment.Version}";

        public static ScoringQueueEnvelopeDto Create(ScoringEventDto scoreEvent)
        {
            if (scoreEvent is null) throw new ArgumentNullException(nameof(scoreEvent));

            var normalized = new ScoringEventDto
            {
                SchemaVersion = scoreEvent.SchemaVersion == 0 ? ContractVersions.ScoringEventSchemaVersion : scoreEvent.SchemaVersion,
                TenantId = scoreEvent.TenantId,
                AppName = scoreEvent.AppName,
                Environment = scoreEvent.Environment,
                Runtime = scoreEvent.Runtime ?? RuntimeSignature,
                TimestampUtc = scoreEvent.TimestampUtc == default ? DateTime.UtcNow : scoreEvent.TimestampUtc,
                LogId = scoreEvent.LogId,
                CorrelationId = scoreEvent.CorrelationId,
                GovernanceProfile = scoreEvent.GovernanceProfile,
                GovernanceMode = scoreEvent.GovernanceMode,
                LogLevel = scoreEvent.LogLevel,
                Score = scoreEvent.Score,
                Violations = scoreEvent.Violations,
                GovernanceFlags = scoreEvent.GovernanceFlags
            };

            var idempotency = ComputeDeterministicKey(normalized);

            return new ScoringQueueEnvelopeDto
            {
                EnvelopeVersion = ContractVersions.ScoringEnvelopeVersion,
                MessageType = MessageType,
                Producer = normalized.AppName,
                ProducerVersion = ProducerVersion,
                IdempotencyKey = idempotency,
                EnqueuedUtc = DateTime.UtcNow,
                Payload = normalized
            };
        }

        private static string ComputeDeterministicKey(ScoringEventDto ev)
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(ev.TenantId)) builder.Append(ev.TenantId);
            builder.Append('|');
            if (!string.IsNullOrWhiteSpace(ev.AppName)) builder.Append(ev.AppName);
            builder.Append('|');
            if (!string.IsNullOrWhiteSpace(ev.LogId)) builder.Append(ev.LogId);
            builder.Append('|');
            builder.Append(ev.TimestampUtc.ToUniversalTime().Ticks);

            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(builder.ToString());
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }
    }
}
