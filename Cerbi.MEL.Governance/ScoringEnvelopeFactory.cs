using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Cerbi.Contracts;
 
 namespace Cerbi.Serilog.Governance
 {
     internal static class ScoringEnvelopeFactory
     {
        private const string MessageType = "scoring-event";
        private const string SchemaVersion = "1.0";
        private static readonly string ProducerVersion = typeof(ScoringEnvelopeFactory).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        private static readonly string RuntimeSignature = $".NET {Environment.Version}";

        public static ScoringQueueEnvelopeDto Create(GovernanceScoreEvent scoreEvent)
         {
            if (scoreEvent is null) throw new ArgumentNullException(nameof(scoreEvent));

            var fields = scoreEvent.Fields ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var payload = new ScoringEventDto
            {
                IdempotencyKey = scoreEvent.IdempotencyKey,
                CorrelationId = scoreEvent.CorrelationId,
                TenantId = scoreEvent.TenantId,
                AppName = scoreEvent.AppName,
                Environment = scoreEvent.Environment,
                Topic = scoreEvent.Topic,
                Category = scoreEvent.Category,
                LogId = scoreEvent.LogId,
                GovernanceProfile = scoreEvent.Profile,
                Score = new ScoreBreakdownDto
                {
                    Overall = ToScoreBucket(scoreEvent.ScoreImpact),
                    Governance = ToScoreBucket(scoreEvent.ScoreImpact)
                },
                GovernanceRelaxed = scoreEvent.GovernanceRelaxed,
                Timestamp = scoreEvent.Timestamp,
                Violations = scoreEvent.Violations ?? Array.Empty<GovernanceViolationSummary>(),
                Fields = new Dictionary<string, object?>(fields, StringComparer.Ordinal)
            };

            var idempotency = scoreEvent.IdempotencyKey ?? ComputeDeterministicKey(scoreEvent);

            return new ScoringQueueEnvelopeDto
            {
                SchemaVersion = SchemaVersion,
                IdempotencyKey = idempotency,
                CorrelationId = scoreEvent.CorrelationId,
                TenantId = scoreEvent.TenantId,
                AppName = scoreEvent.AppName,
                Environment = scoreEvent.Environment,
                Payload = payload
            };
         }
 
         private static int? ToScoreBucket(double impact)
         {
             if (double.IsNaN(impact) || double.IsInfinity(impact)) return null;
             return (int)Math.Round(impact, MidpointRounding.AwayFromZero);
         }
 
         private static string ComputeDeterministicKey(GovernanceScoreEvent ev)
         {
             var builder = new StringBuilder();
             if (!string.IsNullOrWhiteSpace(ev.TenantId)) builder.Append(ev.TenantId);
             builder.Append('|');
             if (!string.IsNullOrWhiteSpace(ev.AppName)) builder.Append(ev.AppName);
             builder.Append('|');
             if (!string.IsNullOrWhiteSpace(ev.LogId)) builder.Append(ev.LogId);
             builder.Append('|');
             builder.Append(ev.Timestamp.UtcDateTime.Ticks);
 
             using var sha = SHA256.Create();
             var bytes = Encoding.UTF8.GetBytes(builder.ToString());
             var hash = sha.ComputeHash(bytes);
             return Convert.ToHexString(hash);
         }
     }
 }
