using System;
using System.Collections.Generic;
using System.Reflection;
using Cerbi;
using Cerbi.Governance;
using Cerbi.Serilog.Governance;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Cerbi.Tests
{
    public class RelaxAndViolationTests
    {
        [Fact]
        public void Relax_sets_flag_and_zeroes_score_impact()
        {
            var settings = new CerbiGovernanceMELSettings
            {
                Profile = "relaxed-profile",
                ScoreShipping = new ScoreShippingOptions { Enabled = true, LicenseAllowsScoring = true },
                ScoringIngestion = new ScoringIngestionOptions()
            };

            var validator = new RuntimeGovernanceValidator(new Func<bool>(() => true), settings.Profile, new FileGovernanceSource(settings.ConfigPath));
            var shipper = new CapturingShipper();
            var logger = new CerbiGovernanceLogger(new Mock<ILogger>().Object, validator, settings.Profile, null, () => true, shipper, settings);

            var state = new List<KeyValuePair<string, object>>
            {
                new("Relax", true),
                new("GovernanceScoreImpact", 5.0),
                new("TenantId", "tenant-xyz"),
                new("LogId", "log-abc"),
                new("CorrelationId", "corr-123")
            };

            logger.Log(LogLevel.Warning, new EventId(10, "relax"), state, null, (s, e) => "msg");

            var ev = Assert.Single(shipper.Events);
            Assert.True(ev.GovernanceFlags?.GovernanceRelaxed ?? false, System.Text.Json.JsonSerializer.Serialize(ev));
            Assert.Equal(0, ev.Score?.Overall);
            Assert.Equal(0, ev.Score?.Governance);
        }

        [Fact]
        public void Violation_objects_convert_to_contract_shape()
        {
            var settings = new CerbiGovernanceMELSettings
            {
                Profile = "contracts",
                ScoreShipping = new ScoreShippingOptions { Enabled = true, LicenseAllowsScoring = true },
                ScoringIngestion = new ScoringIngestionOptions()
            };

            var validator = new RuntimeGovernanceValidator(new Func<bool>(() => true), settings.Profile, new FileGovernanceSource(settings.ConfigPath));
            var shipper = new CapturingShipper();
            var logger = new CerbiGovernanceLogger(new Mock<ILogger>().Object, validator, settings.Profile, null, () => true, shipper, settings);

            var fields = new Dictionary<string, object>
            {
                ["GovernanceScoreImpact"] = 2.0,
                ["GovernanceViolations"] = new[] { new { RuleId = "ForbiddenField:password", Message = "password forbidden" } },
                ["TenantId"] = "tenant-1",
                ["LogId"] = "log-1",
                ["CorrelationId"] = "corr-1",
                ["GovernanceRelaxed"] = false
            };

            var tryShip = typeof(CerbiGovernanceLogger).GetMethod("TryShipScore", BindingFlags.Instance | BindingFlags.NonPublic);
            tryShip!.Invoke(logger, new object[] { fields, settings.Profile, new EventId(22, "vio"), LogLevel.Error });

            var ev = Assert.Single(shipper.Events);
            var violation = Assert.Single(ev.Violations);
            Assert.Equal("ForbiddenField:password", violation.RuleId);
            Assert.Equal("password forbidden", violation.Message);
        }

        private sealed class CapturingShipper : IScoreShipper
        {
            public List<Cerbi.Contracts.Contracts.ScoringEventDto> Events { get; } = new();
            public void Enqueue(Cerbi.Contracts.Contracts.ScoringEventDto ev) => Events.Add(ev);
            public void Dispose() { }
        }
    }
}
