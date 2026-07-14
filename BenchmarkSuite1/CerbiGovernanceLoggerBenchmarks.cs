using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using Cerbi;
using Cerbi.Governance;
using Microsoft.VSDiagnostics;

// Stub for Microsoft.VSDiagnostics CPUUsageDiagnoser attribute so the benchmarks compile if the diagnoser isn't available.
// Do not remove this; the build may replace it with a real diagnoser when present.
namespace Microsoft.VSDiagnostics
{
 [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
 public sealed class CPUUsageDiagnoserAttribute : Attribute { }
}

namespace Cerbi.MEL.Governance.Benchmarks
{
 [MemoryDiagnoser]
 [CPUUsageDiagnoser]
 [CerbiTopic("Payments")]
 public class CerbiGovernanceLogger_AttributeTopic_Benchmark
 {
 private ILogger _inner = default!;
 private CerbiGovernanceLogger _logger = default!;
 private List<KeyValuePair<string, object>> _state = default!;

 [GlobalSetup]
 public void Setup()
 {
 _inner = new NoopLogger();

 // Validator disabled to minimize work in the hot path
 var validator = new RuntimeGovernanceValidator(
 () => false,
 "Payments",
 new FileGovernanceSource("nonexistent.json", "Payments"));

 _logger = new CerbiGovernanceLogger(_inner, validator, "Payments");
 _state = new List<KeyValuePair<string, object>>
 {
 new("A",1),
 new("B",2),
 new("C",3)
 };
 }

 [Benchmark]
 public void Log_WithAttributeTopic_StructuredState()
 {
 _logger.Log(LogLevel.Information, new EventId(42), _state, null, (s, e) => "fmt");
 }
 }

 [MemoryDiagnoser]
 [CPUUsageDiagnoser]
 public class CerbiGovernanceLogger_NoTopic_Benchmark
 {
 private ILogger _inner = default!;
 private CerbiGovernanceLogger _logger = default!;

 [GlobalSetup]
 public void Setup()
 {
 _inner = new NoopLogger();
 var validator = new RuntimeGovernanceValidator(
 () => false,
 "Unused",
 new FileGovernanceSource("nonexistent.json", "Unused"));
 // Default topic empty → early bypass to inner logger
 _logger = new CerbiGovernanceLogger(_inner, validator, "");
 }

 [Benchmark]
 public void Log_NoTopic_Bypass()
 {
 _logger.Log(LogLevel.Information, new EventId(7), "hello", null, (s, e) => s);
 }
 }

 // Shared no-op logger for the benchmarks
 public sealed class NoopLogger : ILogger
 {
 private sealed class NoopScope : IDisposable { public void Dispose() { } }
 public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoopScope();
 public bool IsEnabled(LogLevel logLevel) => true;
 public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
 }
}