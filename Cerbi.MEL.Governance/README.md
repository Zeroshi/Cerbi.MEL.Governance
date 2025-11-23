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

## Enforcement Modes and Controls
- `EnforcementMode`: `Strict` (default), `Audit`, or `Off`.
  - Strict: validate and tag success/violations; emit violation JSON lines when violations occur.
  - Audit: validate and tag but do not change behavior beyond emitting violation JSON on violations.
  - Off: skip validation fast-path.
- `MinValidationLevel`: only validate at or above this MEL `LogLevel`.
- `SamplingRate` (0.0–1.0): fraction of events validated.

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
// Program.cs / host builder
builder.Logging.AddCerbiGovernance(builder.Configuration); // binds from "Cerbi:Governance" by default

// or minimal manual configuration
builder.Logging.AddCerbiGovernance(o =>
{
    o.Profile = "Orders";              // fallback profile
    o.ConfigPath = "cerbi_governance.json"; // profile file
    o.Enabled = true;                   // toggle governance
    o.EnforcementMode = GovernanceEnforcementMode.Strict; // Strict | Audit | Off
    o.MinValidationLevel = LogLevel.Information;          // validate at/above this level
    o.SamplingRate = 1.0;                                  // 0..1 sampling
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

### Optional configuration binding
```json
{
  "Cerbi": {
    "Governance": {
      "Profile": "Orders",
      "ConfigPath": "cerbi_governance.json",
      "Enabled": true,
      "EnforcementMode": "Strict",
      "MinValidationLevel": "Information",
      "SamplingRate": 1.0,
      "AppName": "MyService",
      "Environment": "prod",
      "ScoreShipping": {
        "Enabled": true,
        "LicenseAllowsScoring": true,
        "Endpoint": "https://scores.cerbi.local/api/ship",
        "ApiKey": "secret-key"
      }
    }
  }
}
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

Shipper behavior:
- Queue size capped (`MaxQueueSize`)
- Batch flush (`BatchSize`) every `FlushIntervalSeconds`
- Retries (`MaxRetries`, `RetryDelayMilliseconds`)
- Drops silently on errors; logging path unaffected

To trigger scoring:
```csharp
_logger.LogInformation("Scored event {userId} {GovernanceScoreImpact}", "abc123", 2.5);
```

## Performance
Optimizations:
- Category + type attribute caching (minimal StackTrace usage)
- Manual dictionary construction (avoid LINQ / boxing)
- Single validator instance per provider
- Score shipping done off-thread; enqueue O(1)

## Interoperability
- Flows MEL scopes (ILogger.BeginScope) through provider and logger
- Coexists with other MEL providers (Serilog, NLog, OTel, Console)

## FAQ
Q: Does it replace my logger?  A: No, it wraps MEL and preserves existing providers.
Q: Can logs be dropped?       A: Original line is always emitted; violations add a second JSON line.
Q: How to relax one log?       A: Include `{Relax}` true and have `AllowRelax: true` in profile.
Q: Scoring without impact?     A: No event shipped if `GovernanceScoreImpact` missing or non-numeric.
Q: License gating?             A: `LicenseAllowsScoring=false` blocks shipping even if enabled.

## Contributing
Issues and PRs with tests welcome. MIT licensed.

## License
MIT
