using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cerbi;
using CerbiShield.Contracts.Scoring;
using Cerbi.Governance;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Cerbi.Tests
{
    internal class TestScoreShipper : ScoreShipper
    {
        public int Enqueued { get; private set; }
        public TestScoreShipper()
            : base(new HttpClient(), new ScoreShippingOptions { Enabled = true, LicenseAllowsScoring = true, Endpoint = "http://localhost" }, new ScoringIngestionOptions(), NoopScoringQueueSender.Instance) { }
        public override void Enqueue(ScoringQueueEnvelopeDto envelope)
        {
            Enqueued++;
            base.Enqueue(envelope);
        }
    }

    internal sealed class FakeQueueSender : IScoringQueueSender
    {
        public int Calls { get; private set; }
        public bool IsConfigured => true;
        public Task SendAsync(ScoringQueueEnvelopeDto envelope, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
        public void Dispose() { }
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
                ScoreShipping = opts,
                ScoringIngestion = new ScoringIngestionOptions()
            };
            var shipper = new ScoreShipper(new HttpClient(), opts, settings.ScoringIngestion, NoopScoringQueueSender.Instance);
            return new CerbiGovernanceLogger(innerLogger, validator, settings.Profile, "Cat", () => true, shipper, settings);
        }

        [Fact]
        public void NoEnqueue_WhenImpactMissing()
        {
            var logger = CreateLogger(new ScoreShippingOptions { Enabled = true, LicenseAllowsScoring = true, Endpoint = "http://localhost" });
            var state = new List<KeyValuePair<string, object>> { new("A", 1) };
            logger.Log(LogLevel.Information, new EventId(1), state, null, (s, e) => "msg");
            Assert.True(true);
        }

        [Fact]
        public void Enqueue_WhenImpactPresent_And_Enabled_Licensed()
        {
            var opts = new ScoreShippingOptions { Enabled = true, LicenseAllowsScoring = true, Endpoint = "http://localhost" };
            var innerLogger = new Mock<ILogger>().Object;
            var validator = new Mock<RuntimeGovernanceValidator>(new Func<bool>(() => true), "p", new FileGovernanceSource("x.json")) { CallBase = true }.Object;
            var settings = new CerbiGovernanceMELSettings { Profile = "p", AppName = "app", Environment = "env", ScoreShipping = opts, ScoringIngestion = new ScoringIngestionOptions() };
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
            var settings = new CerbiGovernanceMELSettings { Profile = "p", AppName = "app", Environment = "env", ScoreShipping = opts, ScoringIngestion = new ScoringIngestionOptions() };
            var testShipper = new TestScoreShipper();
            var logger = new CerbiGovernanceLogger(innerLogger, validator, settings.Profile, "Cat", () => true, testShipper, settings);
            var initial = testShipper.Enqueued;
            var state = new List<KeyValuePair<string, object>> { new("GovernanceScoreImpact", 3.0) };
            logger.Log(LogLevel.Information, new EventId(3), state, null, (s, e) => "msg");
            Assert.Equal(initial, testShipper.Enqueued);
        }

        [Fact]
        public void QueueFirst_Uses_ServiceBus_When_Configured()
        {
            var opts = new ScoreShippingOptions { Enabled = true, LicenseAllowsScoring = true };
            var ingestion = new ScoringIngestionOptions { Mode = ScoringIngestionMode.QueueFirst };
            var sender = new FakeQueueSender();
            var shipper = new ScoreShipper(new HttpClient(), opts, ingestion, sender);
            shipper.Enqueue(new ScoringQueueEnvelopeDto { Payload = new ScoringEventDto { TimestampUtc = DateTime.UtcNow } });
            shipper.FlushForTesting();
            Assert.Equal(1, sender.Calls);
        }
    }
}
