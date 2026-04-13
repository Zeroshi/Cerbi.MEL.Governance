using System;
using System.Collections.Generic;
using Cerbi;
using CerbiShield.Contracts.Scoring;
using Cerbi.Governance;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Cerbi.Tests
{
    public class CerbiEnforcementControlsTests
    {
        private sealed class NoopScoreShipper : IScoreShipper
        {
            public void Dispose() { }
            public void Enqueue(ScoringEventDto ev) { }
        }

        private static CerbiGovernanceLogger CreateLogger(CerbiGovernanceMELSettings settings, Mock<ILogger>? innerMock = null)
        {
            var inner = (innerMock ?? new Mock<ILogger>()).Object;
            var validator = new Mock<RuntimeGovernanceValidator>(new Func<bool>(() => true), settings.Profile, new FileGovernanceSource("x.json"), Array.Empty<IRuntimeGovernancePlugin>()) { CallBase = true }.Object;
            var shipper = new NoopScoreShipper();
            return new CerbiGovernanceLogger(inner, validator, settings.Profile, "Cat", () => settings.Enabled, shipper, settings);
        }

        [Fact]
        public void Mode_Off_Skips_Validation()
        {
            var settings = new CerbiGovernanceMELSettings { Profile = "p", EnforcementMode = GovernanceEnforcementMode.Off };
            var innerMock = new Mock<ILogger>();
            var logger = CreateLogger(settings, innerMock);
            var state = new List<KeyValuePair<string, object>> { new("A", 1) };
            logger.Log(LogLevel.Information, new EventId(1), state, null, (s, e) => "msg");

            innerMock.Verify(x => x.Log(
                LogLevel.Information,
                It.Is<EventId>(e => e.Id == 1),
                state,
                null,
                It.IsAny<Func<List<KeyValuePair<string, object>>, Exception, string>>()), Times.Once);
        }

        [Fact]
        public void MinValidationLevel_Gates_Validation()
        {
            var settings = new CerbiGovernanceMELSettings { Profile = "p", EnforcementMode = GovernanceEnforcementMode.Strict, MinValidationLevel = LogLevel.Warning };
            var innerMock = new Mock<ILogger>();
            var logger = CreateLogger(settings, innerMock);
            var state = new List<KeyValuePair<string, object>> { new("A", 1) };

            // Below gate -> only one original log
            logger.Log(LogLevel.Information, new EventId(2), state, null, (s, e) => "msg");
            innerMock.Verify(x => x.Log(
                LogLevel.Information,
                It.Is<EventId>(e => e.Id == 2),
                state,
                null,
                It.IsAny<Func<List<KeyValuePair<string, object>>, Exception, string>>()), Times.Once);
        }

        [Fact]
        public void SamplingRate_Skips_Some_Logs()
        {
            var settings = new CerbiGovernanceMELSettings { Profile = "p", EnforcementMode = GovernanceEnforcementMode.Strict, SamplingRate = 0.0 };
            var innerMock = new Mock<ILogger>();
            var logger = CreateLogger(settings, innerMock);
            var state = new List<KeyValuePair<string, object>> { new("A", 1) };
            logger.Log(LogLevel.Warning, new EventId(3), state, null, (s, e) => "msg");

            innerMock.Verify(x => x.Log(
                LogLevel.Warning,
                It.Is<EventId>(e => e.Id == 3),
                state,
                null,
                It.IsAny<Func<List<KeyValuePair<string, object>>, Exception, string>>()), Times.Once);
        }
    }
}
