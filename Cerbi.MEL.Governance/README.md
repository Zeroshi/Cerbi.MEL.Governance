# Cerbi.MEL.Governance

Real-time logging governance for Microsoft.Extensions.Logging (MEL). Validates structured state against Cerbi governance profiles, preserves the original log line, and emits a secondary JSON payload only on violations. Adds optional non-blocking governance score shipping.

## Why
Standard loggers / collectors (Serilog, NLog, Log4Net, MEL console/file, OpenTelemetry / OTLP Collector, Seq, Loki / Promtail / Alloy, Fluentd / FluentBit, ELK / OpenSearch, Graylog, VictoriaLogs / VictoriaMetrics, journald / syslog) do not enforce enterprise governance (required / forbidden fields, PII/PHI protection, relaxation tagging, scoring metadata). Cerbi adds that governance layer without replacing existing sinks.

## Key Features
- Required / forbidden field enforcement
- Structured logging + scope support
- Topic routing via `[CerbiTopic]`
- Original line always emitted; second JSON line only on violations
- Relaxed mode via `{Relax}` property + profile `AllowRelax`
- Non-blocking governance score shipping (batch, retry, license-gated)
- Hot-path optimizations (caching, low allocation field extraction)

## Installation
```bash
dotnet add package Cerbi.MEL.Governance --version 1.0.36
```

## Configuration JSON (cerbi_governance.json)
```json
{
  "EnforcementMode": "Strict",
  "LoggingProfiles": {
    "Orders": {
      "FieldSeverities": {
        "userId": "Required",
        "email": "Required",
        "password": "Forbidden"
      },
      "AllowRelax": true,
      "RequireTopic": true,
      "AllowedTopics": ["Orders"]
    }
  }
}
```

## Wiring (Host builder)
```csharp
builder.Logging.AddCerbiGovernance(o =>
{
    o.Profile = "Orders";              // fallback profile
    o.ConfigPath = "cerbi_governance.json"; // profile file
    o.Enabled = true;                   // toggle governance
    o.AppName = "MyService";           // for score events
    o.Environment = "prod";            // for score events
    o.ScoreShipping = new ScoreShippingOptions
    {
        Enabled = true,
        LicenseAllowsScoring = true,
        Endpoint = "https://scores.cerbi.local/api/ship",
        ApiKey = "secret-key"
    };
});
```

## Topic Routing
```csharp
[CerbiTopic("Orders")]
public class OrderService
{
    private readonly ILogger<OrderService> _logger;
    public OrderService(ILogger<OrderService> logger) => _logger = logger;
    public void Process(string userId, string email)
        => _logger.LogInformation("Order processed for {userId} {email}", userId, email);
}
```
Logs from `OrderService` use the `Orders` profile automatically.

## Relaxed Mode (v1.0.36)
- No fluent `Relax()` helper exists.
- Set `AllowRelax: true` in profile and include a structured property `{Relax}` (bool true) in the log state.
- Example:
```csharp
_logger.LogInformation("Email-only (relaxed): {email} {Relax}", "user@example.com", true);
```
Produces second JSON line with `GovernanceRelaxed: true`.

## Example Violations
Missing required field:
```json
{"GovernanceProfileUsed":"Orders","GovernanceViolations":["MissingField:userId"],"GovernanceRelaxed":false}
```
Forbidden field:
```json
{"GovernanceProfileUsed":"Orders","GovernanceViolations":["ForbiddenField:password"],"GovernanceRelaxed":false}
```
Relaxed:
```json
{"email":"user@example.com","CerbiTopic":"Orders","GovernanceRelaxed":true,"GovernanceProfileUsed":"Orders"}
```

## Governance Score Shipping
When `ScoreShipping.Enabled` and `LicenseAllowsScoring` are true and the structured state contains `GovernanceScoreImpact` (numeric), a `GovernanceScoreEvent` is enqueued (non-blocking):

Fields extracted:
- `GovernanceScoreImpact` → double
- `GovernanceViolations` → array mapped to summaries
- `GovernanceRelaxed` → bool

Event model:
```csharp
public class GovernanceScoreEvent
{
    public string AppName { get; set; }
    public string Environment { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public double ScoreImpact { get; set; }
    public bool GovernanceRelaxed { get; set; }
    public GovernanceViolationSummary[] Violations { get; set; }
}
```
Shipper behavior:
- Queue size capped (`MaxQueueSize`)
- Batch flush (`BatchSize`) every `FlushIntervalSeconds`
- Retries (`MaxRetries`, `RetryDelayMilliseconds`)
- Drops silently on errors; logging path unaffected

To trigger scoring:
```csharp
_logger.LogInformation("Scored event {userId} {GovernanceScoreImpact}", "abc123", 2.5);
```
(Include governance-related fields per profile.)

## Performance
Optimizations:
- Category + type attribute caching (minimal StackTrace usage)
- Manual dictionary construction (avoid LINQ / boxing)
- Single validator instance per provider
- Score shipping done off-thread; enqueue O(1)

Benchmark guidance (.NET 8, no-op sink):
- Fast path: 3–10M logs/sec
- Topic cached path: 2–6M logs/sec
- With per-log StackTrace (avoid): <0.5M logs/sec
Real sinks are I/O-bound (50–200k logs/sec typical).

## Interoperability
Works alongside existing MEL providers and frameworks:
- Serilog / NLog / Log4Net (via MEL bridge)
- OpenTelemetry Logging + OTLP Collector
- Seq, Loki, ELK / OpenSearch, Fluentd / FluentBit, Graylog, VictoriaLogs / VictoriaMetrics, TelemetryHarbor, journald / syslog

## FAQ
Q: Does it replace my logger?  A: No, it wraps MEL and preserves existing providers.
Q: Can logs be dropped?       A: Original line is always emitted in this version; violations add a second JSON line.
Q: How to relax one log?       A: Include `{Relax}` true and have `AllowRelax: true` in profile.
Q: Scoring without impact?     A: No event shipped if `GovernanceScoreImpact` missing or non-numeric.
Q: License gating?             A: `LicenseAllowsScoring=false` blocks shipping even if enabled.
Q: PII/Forbidden handling?     A: Profile `FieldSeverities` drives enforcement and violation tagging.

## Related Cerbi Components
- CerbiStream (logging pipeline)
- Cerbi.Governance.Core / Runtime (shared models, validation)
- GovernanceAnalyzer (compile-time rules)
- CerbiShield (profile management)
- CerbIQ / CerbiSense (routing / analytics)

## Contributing
Issues and PRs with tests welcome. MIT licensed.

## License
MIT
