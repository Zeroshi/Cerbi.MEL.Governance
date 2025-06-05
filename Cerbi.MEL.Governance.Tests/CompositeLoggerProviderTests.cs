using Cerbi;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using Xunit;

namespace Cerbi.Tests
{
    public class CompositeLoggerProviderTests
    {
        private class DummyProvider : ILoggerProvider
        {
            public readonly Mock<ILogger> MockLogger = new Mock<ILogger>();

            public ILogger CreateLogger(string categoryName)
            {
                return MockLogger.Object;
            }

            public void Dispose() { /* no-op */ }
        }

        [Fact]
        public void CreateLogger_FansOut_LogCalls_ToAllInnerLoggers()
        {
            // Arrange: two dummy providers
            var provA = new DummyProvider();
            var provB = new DummyProvider();
            var compositeProv = new CompositeLoggerProvider(new[] { provA, provB });
            var compositeLogger = compositeProv.CreateLogger("CategoryX");

            // Act: call Log with a string state
            compositeLogger.Log<string>(
                logLevel: LogLevel.Warning,
                eventId: new EventId(123),
                state: "Hello",
                exception: null,
                formatter: (state, ex) => state
            );

            // Assert: both inner mock loggers received Log<string>(...) once
            provA.MockLogger.Verify(x => x.Log<string>(
                    LogLevel.Warning,
                    It.Is<EventId>(eid => eid.Id == 123),
                    "Hello",
                    null,
                    It.IsAny<Func<string, Exception, string>>()),
                Times.Once
            );
            provB.MockLogger.Verify(x => x.Log<string>(
                    LogLevel.Warning,
                    It.Is<EventId>(eid => eid.Id == 123),
                    "Hello",
                    null,
                    It.IsAny<Func<string, Exception, string>>()),
                Times.Once
            );
        }

        [Fact]
        public void IsEnabled_ReturnsTrue_IfAnyInnerLogger_IsEnabled()
        {
            // Arrange: Two mock loggers—one enabled, one disabled
            var mockLogger1 = new Mock<ILogger>();
            mockLogger1.Setup(x => x.IsEnabled(LogLevel.Critical)).Returns(true);

            var mockLogger2 = new Mock<ILogger>();
            mockLogger2.Setup(x => x.IsEnabled(LogLevel.Critical)).Returns(false);

            var prov1 = new Mock<ILoggerProvider>();
            prov1.Setup(p => p.CreateLogger(It.IsAny<string>())).Returns(mockLogger1.Object);

            var prov2 = new Mock<ILoggerProvider>();
            prov2.Setup(p => p.CreateLogger(It.IsAny<string>())).Returns(mockLogger2.Object);

            var compositeProv = new CompositeLoggerProvider(new[] { prov1.Object, prov2.Object });
            var compositeLogger = compositeProv.CreateLogger("Cat");

            // Act
            var result = compositeLogger.IsEnabled(LogLevel.Critical);

            // Assert: should be true because at least one inner logger is enabled
            Assert.True(result);
        }

        [Fact]
        public void BeginScope_ReturnsCompositeScope_And_DisposesAllScopes()
        {
            // Arrange: two mock loggers, each returning a mock scope
            var mockScope1 = new Mock<IDisposable>();
            var mockLogger1 = new Mock<ILogger>();
            mockLogger1.Setup(x => x.BeginScope("scope")).Returns(mockScope1.Object);

            var mockScope2 = new Mock<IDisposable>();
            var mockLogger2 = new Mock<ILogger>();
            mockLogger2.Setup(x => x.BeginScope("scope")).Returns(mockScope2.Object);

            var prov1 = new Mock<ILoggerProvider>();
            prov1.Setup(p => p.CreateLogger(It.IsAny<string>())).Returns(mockLogger1.Object);

            var prov2 = new Mock<ILoggerProvider>();
            prov2.Setup(p => p.CreateLogger(It.IsAny<string>())).Returns(mockLogger2.Object);

            var compositeProv = new CompositeLoggerProvider(new[] { prov1.Object, prov2.Object });
            var compositeLogger = compositeProv.CreateLogger("Cat");

            // Act
            using (var compositeScope = compositeLogger.BeginScope("scope"))
            {
                // no-op
            }

            // Assert: disposing the composite scope calls Dispose() on both inner scopes
            mockScope1.Verify(x => x.Dispose(), Times.Once);
            mockScope2.Verify(x => x.Dispose(), Times.Once);
        }
    }
}
