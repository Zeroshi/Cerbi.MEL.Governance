// File: CerbiGovernanceLoggerTests.cs
using Cerbi; // for CerbiTopicAttribute
using Cerbi.Governance;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
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
 new FileGovernanceSource("nonexistent.json", "unusedProfile"),
 Array.Empty<IRuntimeGovernancePlugin>()
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

 var validator = new Mock<RuntimeGovernanceValidator>(
 new Func<bool>(() => true),
 "any",
 new FileGovernanceSource("dummy.json", "any"),
 Array.Empty<IRuntimeGovernancePlugin>())
 { CallBase = true }.Object;

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

 var validator = new Mock<RuntimeGovernanceValidator>(
 new Func<bool>(() => true),
 "any",
 new FileGovernanceSource("dummy.json", "any"),
 Array.Empty<IRuntimeGovernancePlugin>())
 { CallBase = true }.Object;

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
 var validator = new Mock<RuntimeGovernanceValidator>(
 new Func<bool>(() => true),
 "Profile",
 new FileGovernanceSource("dummy.json", "Profile"),
 Array.Empty<IRuntimeGovernancePlugin>()) { CallBase = true }.Object;

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

 [Fact]
 public void DisabledGovernance_WithMissingWrappedProfile_DoesNotThrowDuringCreationOrLogging()
 {
 using var file = TempGovernanceFile.Create(MissingOrdersProfileJson);
 var settings = new CerbiGovernanceMELSettings
 {
 Profile = "Orders",
 ConfigPath = file.Path,
 Enabled = false,
 EnforcementMode = GovernanceEnforcementMode.Strict
 };
 var logger = CreateLoggerWithSettings(settings);
 var state = new List<KeyValuePair<string, object>> { new("rid", "r-1") };

 var exception = Record.Exception(() => logger.Log(LogLevel.Information, new EventId(10), state, null, (s, e) => "msg"));

 Assert.Null(exception);
 }

 [Fact]
 public void OffMode_WithMissingWrappedProfile_DoesNotThrow()
 {
 using var file = TempGovernanceFile.Create(MissingOrdersProfileJson);
 var settings = new CerbiGovernanceMELSettings
 {
 Profile = "Orders",
 ConfigPath = file.Path,
 Enabled = true,
 EnforcementMode = GovernanceEnforcementMode.Off
 };
 var logger = CreateLoggerWithSettings(settings);
 var state = new List<KeyValuePair<string, object>> { new("rid", "r-1") };

 var exception = Record.Exception(() => logger.Log(LogLevel.Information, new EventId(11), state, null, (s, e) => "msg"));

 Assert.Null(exception);
 }

 [Fact]
 public void ReEnabledSettings_LoadAliasesOnNextGovernedEvent()
 {
 using var file = TempGovernanceFile.Create(MissingOrdersProfileJson);
 var settings = new CerbiGovernanceMELSettings
 {
 Profile = "Orders",
 ConfigPath = file.Path,
 Enabled = false,
 EnforcementMode = GovernanceEnforcementMode.Strict
 };
 var logger = CreateLoggerWithSettings(settings);
 var state = new List<KeyValuePair<string, object>> { new("rid", "r-1") };

 logger.Log(LogLevel.Information, new EventId(12), state, null, (s, e) => "msg");
 settings.Enabled = true;

 Assert.Throws<InvalidDataException>(() => logger.Log(LogLevel.Information, new EventId(13), state, null, (s, e) => "msg"));
 }

 [Fact]
 public void StrictMode_WithMissingWrappedProfile_ThrowsWhenGovernanceApplies()
 {
 using var file = TempGovernanceFile.Create(MissingOrdersProfileJson);
 var settings = new CerbiGovernanceMELSettings
 {
 Profile = "Orders",
 ConfigPath = file.Path,
 Enabled = true,
 EnforcementMode = GovernanceEnforcementMode.Strict
 };
 var logger = CreateLoggerWithSettings(settings);
 var state = new List<KeyValuePair<string, object>> { new("rid", "r-1") };

 Assert.Throws<InvalidDataException>(() => logger.Log(LogLevel.Information, new EventId(14), state, null, (s, e) => "msg"));
 }


 [Fact]
 public void LoggerAliasExpansion_UsesSelectedWrappedProfileAliases()
 {
 using var file = TempGovernanceFile.Create("""
 {
   "EnforcementMode": "Strict",
   "LoggingProfiles": {
     "Orders": {
       "name": "Orders",
       "version": "2026.07",
       "requiredFields": ["requestId"],
       "disallowedFields": ["password"],
       "fieldSeverities": {},
       "fieldAliases": { "requestId": ["rid"] }
     }
   }
 }
 """);
 var settings = new CerbiGovernanceMELSettings
 {
 Profile = "Orders",
 ConfigPath = file.Path,
 Enabled = true,
 EnforcementMode = GovernanceEnforcementMode.Strict
 };
 var innerLoggerMock = new Mock<ILogger>();
 var validator = new RuntimeGovernanceValidator(
 new Func<bool>(() => true),
 settings.Profile,
 new FileGovernanceSource(file.Path, settings.Profile));
 var logger = new CerbiGovernanceLogger(innerLoggerMock.Object, validator, settings.Profile, null, () => settings.Enabled, null, settings);
 var state = new List<KeyValuePair<string, object>> { new("rid", "r-1") };

 logger.Log(LogLevel.Information, new EventId(15), state, null, (s, e) => "msg");

 innerLoggerMock.Verify(x => x.Log(
 LogLevel.Information,
 It.Is<EventId>(eid => eid.Id == 15),
 It.Is<List<KeyValuePair<string, object>>>(lst => ReferenceEquals(lst, state)),
 null,
 It.IsAny<Func<List<KeyValuePair<string, object>>, Exception?, string>>()),
 Times.Once);
 }

 private const string MissingOrdersProfileJson = """
 {
   "LoggingProfiles": {
     "Payments": {
       "fieldAliases": { "requestId": ["rid"] }
     }
   }
 }
 """;

 private static CerbiGovernanceLogger CreateLoggerWithSettings(CerbiGovernanceMELSettings settings)
 {
 var innerLoggerMock = new Mock<ILogger>();
 var validator = new Mock<RuntimeGovernanceValidator>(
 new Func<bool>(() => true),
 settings.Profile,
 new FileGovernanceSource("dummy.json", settings.Profile),
 Array.Empty<IRuntimeGovernancePlugin>()) { CallBase = true }.Object;

 return new CerbiGovernanceLogger(innerLoggerMock.Object, validator, settings.Profile, null, () => settings.Enabled, null, settings);
 }

 private sealed class TempGovernanceFile : IDisposable
 {
 public TempGovernanceFile(string path) => Path = path;
 public string Path { get; }
 public static TempGovernanceFile Create(string json)
 {
 var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cerbi-governance-{Guid.NewGuid():N}.json");
 File.WriteAllText(path, json);
 return new TempGovernanceFile(path);
 }
 public void Dispose()
 {
 if (File.Exists(Path)) File.Delete(Path);
 }
 }

 }
 }
