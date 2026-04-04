// File: CerbiGovernanceLoggerTests.cs
using Cerbi; // for CerbiTopicAttribute
using Cerbi.Governance;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
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
 var dummyValidator = new RuntimeGovernanceValidator(() => true,
 "unusedProfile",
 new FileGovernanceSource("nonexistent.json")
 );

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
 It.Is<EventId>(eid => eid.Id ==1),
 It.Is<string>(msg => msg == "Hello there"),
 null,
 It.IsAny<Func<string, Exception, string>>()),
 Times.Once
 );
 }

 [Fact]
 public void IsEnabled_Delegates_To_InnerLogger()
 {
 var innerLoggerMock = new Mock<ILogger>();
 innerLoggerMock.Setup(x => x.IsEnabled(LogLevel.Debug)).Returns(true);

 var validator = new RuntimeGovernanceValidator(() => true,
 "any",
 new FileGovernanceSource("dummy.json"));

 var logger = new CerbiGovernanceLogger(innerLoggerMock.Object, validator, "Topic");

 Assert.True(logger.IsEnabled(LogLevel.Debug));
 innerLoggerMock.Verify(x => x.IsEnabled(LogLevel.Debug), Times.Once);
 }

 [Fact]
 public void BeginScope_Delegates_And_Disposes()
 {
 var innerLoggerMock = new Mock<ILogger>();
 var mockScope = new Mock<IDisposable>();
 innerLoggerMock.Setup(x => x.BeginScope(It.IsAny<string>()))
 .Returns(mockScope.Object);

 var validator = new RuntimeGovernanceValidator(() => true,
 "any",
 new FileGovernanceSource("dummy.json"));

 var logger = new CerbiGovernanceLogger(innerLoggerMock.Object, validator, "Topic");

 using (logger.BeginScope("scope"))
 {
 // no-op
 }

 innerLoggerMock.Verify(x => x.BeginScope("scope"), Times.Once);
 mockScope.Verify(x => x.Dispose(), Times.Once);
 }

 [Fact]
 public void NonEmptyTopic_Always_LogsOriginal_WithStructuredState_And_Exception()
 {
 var innerLoggerMock = new Mock<ILogger>();
 var validator = new RuntimeGovernanceValidator(() => true,
 "Profile",
 new FileGovernanceSource("dummy.json"));

 var logger = new CerbiGovernanceLogger(innerLoggerMock.Object, validator, "Payments");

 var state = new List<KeyValuePair<string, object>> { new("A",1) };
 var ex = new InvalidOperationException("boom");

 logger.Log(LogLevel.Warning, new EventId(3), state, ex, (s, e) => "fmt");

 // We don't control validator output, but original call must happen at least once
 innerLoggerMock.Verify(x => x.Log(
 LogLevel.Warning,
 It.Is<EventId>(eid => eid.Id ==3),
 It.Is<List<KeyValuePair<string, object>>>(lst => lst == state),
 ex,
 It.IsAny<Func<List<KeyValuePair<string, object>>, Exception, string>>()),
 Times.AtLeastOnce);
 }
 }
}
