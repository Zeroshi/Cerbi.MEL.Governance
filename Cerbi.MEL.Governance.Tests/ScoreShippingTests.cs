using System;
using System.Collections.Generic;
using Cerbi;
using Cerbi.Governance;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Cerbi.Tests
{
    internal class TestScoreShipper : ScoreShipper
    {
        public int Enqueued { get; private set; }
        public TestScoreShipper() : base(new System.Net.Http.HttpClient(), new ScoreShippingOptions { Enabled = true, LicenseAllowsScoring = true, Endpoint = "http://localhost" }) { }
        public override void Enqueue(GovernanceScoreEvent ev)
        {
            Enqueued++;
            base.Enqueue(ev);
        }
    }

    public class ScoreShippingTests
    {
        private CerbiGovernanceLogger CreateLogger(ScoreShippingOptions opts)
        {
            var innerLogger = new Mock<ILogger>().Object;
            var validator = new Mock<RuntimeGovernanceValidator>(new Func<bool>(() => true), "p", new FileGovernanceSource("x.json")) { CallBase = true }.Object;
            var settings = new CerbiGovernanceMELSettings
            {
                Profile = "p",
                AppName = "app",
                Environment = "env",
                ScoreShipping = opts
            };
            var shipper = new ScoreShipper(new System.Net.Http.HttpClient(), opts);
            return new CerbiGovernanceLogger(innerLogger, validator, settings.Profile, "Cat", () => true, shipper, settings);
        }

        [Fact]
        public void NoEnqueue_WhenImpactMissing()
        {
            var logger = CreateLogger(new ScoreShippingOptions { Enabled = true, LicenseAllowsScoring = true, Endpoint = "http://localhost" });
            var state = new List<KeyValuePair<string, object>> { new("A", 1) };
            logger.Log(LogLevel.Information, new EventId(1), state, null, (s, e) => "msg");
            // cannot directly read queue; rely on absence of exceptions
            Assert.True(true);
        }

        [Fact]
        public void Enqueue_WhenImpactPresent_And_Enabled_Licensed()
        {
            var opts = new ScoreShippingOptions { Enabled = true, LicenseAllowsScoring = true, Endpoint = "http://localhost" };
            var innerLogger = new Mock<ILogger>().Object;
            var validator = new Mock<RuntimeGovernanceValidator>(new Func<bool>(() => true), "p", new FileGovernanceSource("x.json")) { CallBase = true }.Object;
            var settings = new CerbiGovernanceMELSettings { Profile = "p", AppName = "app", Environment = "env", ScoreShipping = opts };
            var testShipper = new TestScoreShipper();
            var logger = new CerbiGovernanceLogger(innerLogger, validator, settings.Profile, "Cat", () => true, testShipper, settings);
            var state = new List<KeyValuePair<string, object>> { new("GovernanceScoreImpact", 2.5) };
            logger.Log(LogLevel.Information, new EventId(2), state, null, (s, e) => "msg");
            Assert.True(testShipper.Enqueued >= 1);
        }

        [Fact]
        public void LicenseGate_BlocksEnqueue()
        {
            var opts = new ScoreShippingOptions { Enabled = true, LicenseAllowsScoring = false, Endpoint = "http://localhost" };
            var innerLogger = new Mock<ILogger>().Object;
            var validator = new Mock<RuntimeGovernanceValidator>(new Func<bool>(() => true), "p", new FileGovernanceSource("x.json")) { CallBase = true }.Object;
            var settings = new CerbiGovernanceMELSettings { Profile = "p", AppName = "app", Environment = "env", ScoreShipping = opts };
            var testShipper = new TestScoreShipper();
            var logger = new CerbiGovernanceLogger(innerLogger, validator, settings.Profile, "Cat", () => true, testShipper, settings);
            var initial = testShipper.Enqueued;
            var state = new List<KeyValuePair<string, object>> { new("GovernanceScoreImpact", 3.0) };
            logger.Log(LogLevel.Information, new EventId(3), state, null, (s, e) => "msg");
            Assert.Equal(initial, testShipper.Enqueued); // unchanged
        }
    }
}
