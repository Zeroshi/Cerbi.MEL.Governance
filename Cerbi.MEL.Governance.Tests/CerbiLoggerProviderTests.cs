using Cerbi;
using Cerbi.Governance;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Cerbi.Tests
{
    public class CerbiLoggerProviderTests
    {
        [Fact]
        public void CreateLogger_Returns_CerbiGovernanceLogger()
        {
            // Arrange: mock IOptionsMonitor<ConsoleLoggerOptions> so we can create ConsoleLoggerProvider
            var optionsMonitorMock = new Mock<IOptionsMonitor<ConsoleLoggerOptions>>();
            optionsMonitorMock
                .Setup(x => x.CurrentValue)
                .Returns(new ConsoleLoggerOptions());

            var consoleProv = new ConsoleLoggerProvider(optionsMonitorMock.Object);

            // Dummy validator (we won't actually call Validate in this test)
            var dummyValidator = new Mock<RuntimeGovernanceValidator>(
                new Func<bool>(() => true),
                "unusedProfile",
                new FileGovernanceSource("dummy.json")
            )
            { CallBase = true }.Object;

            var provider = new CerbiLoggerProvider(
                consoleProvider: consoleProv,
                validator: dummyValidator,
                profileName: "Sales"
            );

            // Act
            var logger = provider.CreateLogger("CategoryX");

            // Assert
            Assert.NotNull(logger);
            Assert.IsType<CerbiGovernanceLogger>(logger);
        }

        [Fact]
        public void Dispose_DoesNotThrow()
        {
            // Arrange
            var optionsMonitorMock = new Mock<IOptionsMonitor<ConsoleLoggerOptions>>();
            optionsMonitorMock
                .Setup(x => x.CurrentValue)
                .Returns(new ConsoleLoggerOptions());

            var consoleProv = new ConsoleLoggerProvider(optionsMonitorMock.Object);

            var dummyValidator = new Mock<RuntimeGovernanceValidator>(
                new Func<bool>(() => true),
                "unusedProfile",
                new FileGovernanceSource("dummy.json")
            )
            { CallBase = true }.Object;

            var provider = new CerbiLoggerProvider(
                consoleProvider: consoleProv,
                validator: dummyValidator,
                profileName: "X"
            );

            // Act & Assert: calling Dispose should not throw any exception
            var exception = Record.Exception(() => provider.Dispose());
            Assert.Null(exception);
        }
    }
}
