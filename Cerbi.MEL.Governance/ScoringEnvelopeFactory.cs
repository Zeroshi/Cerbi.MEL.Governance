using System;
using System.Security.Cryptography;
using System.Text;
using CerbiShield.Contracts;
using CerbiShield.Contracts.Scoring;

namespace Cerbi
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
                SchemaVersion = ContractVersions.ScoringEventSchemaVersion,
                TenantId = scoreEvent.TenantId,
                AppName = scoreEvent.AppName ?? string.Empty,
                Environment = scoreEvent.Environment ?? string.Empty,
                Runtime = string.IsNullOrWhiteSpace(scoreEvent.Runtime) ? RuntimeSignature : scoreEvent.Runtime,
                TimestampUtc = scoreEvent.TimestampUtc == default ? DateTime.UtcNow : scoreEvent.TimestampUtc,
                LogId = scoreEvent.LogId ?? string.Empty,
                CorrelationId = scoreEvent.CorrelationId ?? string.Empty,
                GovernanceProfile = scoreEvent.GovernanceProfile ?? string.Empty,
                GovernanceMode = scoreEvent.GovernanceMode ?? string.Empty,
                LogLevel = scoreEvent.LogLevel ?? string.Empty,
                Score = scoreEvent.Score,
                Violations = scoreEvent.Violations,
                GovernanceFlags = scoreEvent.GovernanceFlags
            };

            var idempotency = ComputeDeterministicKey(normalized);

            return new ScoringQueueEnvelopeDto
            {
                Version = ContractVersions.ScoringEnvelopeVersion,
                MessageId = Guid.NewGuid().ToString("N"),
                EnqueuedAtUtc = DateTime.UtcNow,
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