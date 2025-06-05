// File: CerbiGovernanceLoggerTests.cs
using Cerbi.Governance;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using Xunit;

namespace Cerbi.Tests
{
    public class CerbiGovernanceLoggerTests
    {
        [Fact]
        public void LogsOnlyOriginalMessage_WhenDefaultTopicIsEmpty()
        {
            // Arrange: mock inner ILogger so we can verify calls
            var innerLoggerMock = new Mock<ILogger>();

            // Create a dummy RuntimeGovernanceValidator (it won't be invoked because defaultTopic is blank)
            var dummyValidator = new Mock<RuntimeGovernanceValidator>(
                new Func<bool>(() => true),
                "unusedProfile",
                new FileGovernanceSource("nonexistent.json")
            )
            { CallBase = true }.Object;

            var wrapper = new CerbiGovernanceLogger(
                inner: innerLoggerMock.Object,
                validator: dummyValidator,
                defaultTopic: "" // no topic → bypass enrichment
            );

            // Act
            wrapper.Log(
                logLevel: LogLevel.Information,
                eventId: new EventId(1),
                state: "Hello there",
                exception: null,
                formatter: (state, ex) => state
            );

            // Assert: only one call to inner.Log<string> with exactly "Hello there"
            innerLoggerMock.Verify(
                x => x.Log<string>(
                    LogLevel.Information,
                    It.Is<EventId>(eid => eid.Id == 1),
                    It.Is<string>(msg => msg == "Hello there"),
                    null,
                    It.IsAny<Func<string, Exception, string>>()),
                Times.Once
            );
        }

        // If you later want to test the violation‐path, you would need to:
        // 1) Make RuntimeGovernanceValidator.Validate virtual, OR
        // 2) Spin up a real RuntimeGovernanceValidator against a temporary JSON file that triggers a violation.
        // Because Validate(...) is not virtual today, we skip that path here.
    }
}
