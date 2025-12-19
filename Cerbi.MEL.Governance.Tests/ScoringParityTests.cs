using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cerbi;
using Cerbi.Contracts.Contracts;
using Cerbi.Governance;
using Cerbi.Serilog.Governance;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Cerbi.Tests
{
    public class ScoringParityTests
    {
        [Fact]
        public void Mel_score_event_matches_reference_builder_shape()
        {
            var settings = new CerbiGovernanceMELSettings
            {
                AppName = "mel-app",
                Environment = "test",
                ScoreShipping = new ScoreShippingOptions { Enabled = true, LicenseAllowsScoring = true },
                ScoringIngestion = new ScoringIngestionOptions { Mode = ScoringIngestionMode.HttpOnly }
            };

            var topic = "safety";
            var eventId = new EventId(42, "governance");
            var logId = "log-123";
            var correlationId = "corr-456";
            var tenantId = "tenant-abc";
            var impact = 7.3;
            var relaxed = false;

            var state = new Dictionary<string, object>
            {
                ["GovernanceScoreImpact"] = impact,
                ["GovernanceRelaxed"] = relaxed,
                ["GovernanceViolations"] = new[]
                {
                    new { RuleId = "R001", Severity = "high", Field = "content", Message = "violation" }
                },
                ["TenantId"] = tenantId,
                ["LogId"] = logId,
                ["CorrelationId"] = correlationId
            };

            var innerLogger = new Mock<ILogger>();
            var validator = new Mock<RuntimeGovernanceValidator>(new Func<bool>(() => true), topic, new FileGovernanceSource("dummy.json")) { CallBase = true };

            var shipper = new CapturingShipper();
            var logger = new CerbiGovernanceLogger(innerLogger.Object, validator.Object, topic, null, () => true, shipper, settings);

            var method = typeof(CerbiGovernanceLogger).GetMethod("TryShipScore", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            method!.Invoke(logger, new object[] { state, topic, eventId, LogLevel.Warning });

            var actual = Assert.Single(shipper.Events);
            var reference = ReferenceScoreBuilder.Build(settings, topic, impact, relaxed, logId, correlationId, tenantId, actual.TimestampUtc, actual.Violations);

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = null,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var actualJson = JsonSerializer.Serialize(actual, options);
            var referenceJson = JsonSerializer.Serialize(reference, options);

            using var actualDoc = JsonDocument.Parse(actualJson);
            using var referenceDoc = JsonDocument.Parse(referenceJson);

            var root = actualDoc.RootElement;
            Assert.True(root.TryGetProperty("SchemaVersion", out _));
            Assert.True(root.TryGetProperty("AppName", out _));
            Assert.True(root.TryGetProperty("Environment", out _));
            Assert.True(root.TryGetProperty("Runtime", out _));
            Assert.True(root.TryGetProperty("TimestampUtc", out _));
            Assert.True(root.TryGetProperty("LogId", out _));
            Assert.True(root.TryGetProperty("CorrelationId", out _));
            Assert.True(root.TryGetProperty("GovernanceProfile", out _));
            Assert.True(root.TryGetProperty("GovernanceMode", out _));
            Assert.True(root.TryGetProperty("LogLevel", out _));
            Assert.True(root.TryGetProperty("Score", out var scoreElement) && scoreElement.TryGetProperty("Governance", out _));
            Assert.True(root.TryGetProperty("Violations", out var vioElement) && vioElement.GetArrayLength() == 1);
            Assert.True(root.TryGetProperty("GovernanceFlags", out var flagsElement) && flagsElement.TryGetProperty("GovernanceRelaxed", out _));

            Assert.True(actualDoc.RootElement.ToString() == referenceDoc.RootElement.ToString(), "Serialized payloads diverged");
        }

        private sealed class CapturingShipper : IScoreShipper
        {
            public List<ScoringEventDto> Events { get; } = new();
            public void Enqueue(ScoringEventDto ev) => Events.Add(ev);
            public void Dispose() { }
        }

        private static class ReferenceScoreBuilder
        {
            public static ScoringEventDto Build(CerbiGovernanceMELSettings settings, string topic, double impact, bool relaxed, string logId, string correlationId, string tenantId, DateTime timestamp, IReadOnlyList<ViolationDto> violations)
            {
                var bucket = ToScoreBucket(impact);
                return new ScoringEventDto
                {
                    SchemaVersion = ContractVersions.ScoringEventSchemaVersion,
                    TenantId = tenantId,
                    AppName = settings.AppName,
                    Environment = settings.Environment,
                    Runtime = $".NET {Environment.Version}",
                    TimestampUtc = timestamp,
                    LogId = logId,
                    CorrelationId = correlationId,
                    GovernanceProfile = topic,
                    GovernanceMode = settings.EnforcementMode.ToString(),
                    LogLevel = LogLevel.Warning.ToString(),
                    Score = new ScoreBreakdownDto { Overall = bucket, Governance = bucket },
                    GovernanceFlags = new GovernanceFlagsDto { GovernanceRelaxed = relaxed },
                    Violations = violations
                };
            }

            private static int? ToScoreBucket(double impact)
            {
                if (double.IsNaN(impact) || double.IsInfinity(impact)) return null;
                return (int)Math.Round(impact, MidpointRounding.AwayFromZero);
            }
        }
    }
}
