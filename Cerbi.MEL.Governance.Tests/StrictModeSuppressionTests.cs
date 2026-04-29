using System;
using System.Collections.Generic;
using Cerbi;
using Cerbi.Governance;
using CerbiShield.Contracts.Scoring;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Cerbi.Tests
{
    /// <summary>
    /// Verifies that CerbiGovernanceLogger in Strict mode replaces the raw message with
    /// a redacted governance JSON payload when violations are present, and that non-Strict
    /// mode still passes the original message through.
    /// </summary>
    public class StrictModeSuppressionTests
    {
        private sealed class CapturingShipper : IScoreShipper
        {
            public void Enqueue(ScoringEventDto ev) { }
            public void Dispose() { }
        }

        private static CerbiGovernanceLogger BuildLogger(
            GovernanceEnforcementMode mode,
            Mock<ILogger> innerMock)
        {
            var settings = new CerbiGovernanceMELSettings
            {
                Profile = "test-profile",
                EnforcementMode = mode,
                ScoreShipping = new ScoreShippingOptions { Enabled = false }
            };
            var validator = new Mock<RuntimeGovernanceValidator>(
                new Func<bool>(() => true),
                settings.Profile,
                new FileGovernanceSource("nonexistent.json"),
                Array.Empty<IRuntimeGovernancePlugin>())
            { CallBase = true }.Object;

            return new CerbiGovernanceLogger(
                innerMock.Object, validator, settings.Profile,
                null, () => true, new CapturingShipper(), settings);
        }

        /// <summary>
        /// State that already carries a GovernanceViolations entry so we do not need a
        /// real governance config file — the logger reads this field directly.
        /// </summary>
        private static List<KeyValuePair<string, object>> ViolatingState() =>
            new()
            {
                new("GovernanceViolations", new[] { "ForbiddenField:password" }),
                new("userId", "abc123"),
                new("password", "secret")
            };

        [Fact]
        public void Strict_WithViolations_DoesNotPassRawStateThroughToSink()
        {
            var inner = new Mock<ILogger>();
            var logger = BuildLogger(GovernanceEnforcementMode.Strict, inner);
            var state = ViolatingState();

            logger.Log(LogLevel.Warning, new EventId(1), state, null, (s, e) => "raw message");

            // The original List<KVP> state must NOT reach the sink
            inner.Verify(x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<List<KeyValuePair<string, object>>>(s => ReferenceEquals(s, state)),
                null,
                It.IsAny<Func<List<KeyValuePair<string, object>>, Exception?, string>>()),
                Times.Never);
        }

        [Fact]
        public void Strict_WithViolations_EmitsRedactedJsonStringToSink()
        {
            var inner = new Mock<ILogger>();
            var logger = BuildLogger(GovernanceEnforcementMode.Strict, inner);
            var state = ViolatingState();

            logger.Log(LogLevel.Warning, new EventId(2), state, null, (s, e) => "raw message");

            // A string payload (the redacted JSON) MUST be emitted instead
            inner.Verify(x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<string>(msg => msg.Contains("GovernanceViolations")),
                null,
                It.IsAny<Func<string, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void Audit_WithViolations_StillPassesRawStateThroughToSink()
        {
            var inner = new Mock<ILogger>();
            var logger = BuildLogger(GovernanceEnforcementMode.Audit, inner);
            var state = ViolatingState();

            logger.Log(LogLevel.Warning, new EventId(3), state, null, (s, e) => "raw message");

            // In Audit mode the original state must still reach the sink
            inner.Verify(x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<List<KeyValuePair<string, object>>>(s => ReferenceEquals(s, state)),
                null,
                It.IsAny<Func<List<KeyValuePair<string, object>>, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void Strict_NoViolations_PassesRawStateThroughToSink()
        {
            var inner = new Mock<ILogger>();
            var logger = BuildLogger(GovernanceEnforcementMode.Strict, inner);
            var state = new List<KeyValuePair<string, object>>
            {
                new("userId", "abc123"),
                new("email", "test@example.com")
            };

            logger.Log(LogLevel.Information, new EventId(4), state, null, (s, e) => "clean message");

            // No violations → original state passes through
            inner.Verify(x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<List<KeyValuePair<string, object>>>(s => ReferenceEquals(s, state)),
                null,
                It.IsAny<Func<List<KeyValuePair<string, object>>, Exception?, string>>()),
                Times.Once);
        }
    }
}
