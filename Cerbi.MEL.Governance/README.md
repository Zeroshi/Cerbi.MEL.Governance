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
- Queue-first scoring ingestion (Azure Service Bus + HTTP fallback)
- Non-blocking governance score shipping (batch, retry, license-gated)
- Hot-path optimizations (caching, low allocation field extraction)

## Installation
```bash
dotnet add package Cerbi.MEL.Governance --version 1.0.36
```

## Enforcement Modes and Controls
- `EnforcementMode`: `Strict` (default), `Audit`, or `Off`.
- `MinValidationLevel`: only validate at/above this MEL `LogLevel`.
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

builder.Logging.AddCerbiGovernance(o =>
{
    o.Profile = "Orders";
    o.ConfigPath = "cerbi_governance.json";
    o.Enabled = true;
    o.EnforcementMode = GovernanceEnforcementMode.Strict;
    o.MinValidationLevel = LogLevel.Information;
    o.SamplingRate = 1.0;
    o.AppName = "MyService";
    o.Environment = "prod";
    o.ScoreShipping = new ScoreShippingOptions
    {
        Enabled = true,
        LicenseAllowsScoring = true,
        Endpoint = "https://scores.cerbi.local/api/ship",
        ApiKey = "secret-key"
    };
    o.ScoringIngestion = new ScoringIngestionOptions
    {
        Mode = ScoringIngestionMode.QueueFirst,
        AzureServiceBus = new AzureServiceBusOptions
        {
            ConnectionString = "Endpoint=sb://...;SharedAccessKeyName=...;SharedAccessKey=...;",
            QueueName = "cerbi-scoring"
        }
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
      },
      "ScoringIngestion": {
        "Mode": "QueueFirst",
        "AzureServiceBus": {
          "ConnectionString": "Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKeyName=...;SharedAccessKey=...;",
          "QueueName": "cerbi-scoring"
        }
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

## Relaxed Mode
```csharp
_logger.LogInformation("Email-only (relaxed): {email} {Relax}", "user@example.com", true);
```
Produces `GovernanceRelaxed: true` when profile allows relax.

## Example Violations
Missing required field:
```json
{"GovernanceProfileUsed":"Orders","GovernanceViolations":["MissingField:userId"],"GovernanceRelaxed":false}
```
Forbidden field:
```json
{"GovernanceProfileUsed":"Orders","GovernanceViolations":["ForbiddenField:password"],"GovernanceRelaxed":false}
```

## Governance Score Shipping
- `ScoreShipping` controls batching/retries for HTTP fallback.
- `ScoringIngestion.Mode` chooses transport:
  - `QueueFirst` (default): send to Azure Service Bus when configured, then HTTP fallback.
  - `QueueOnly`: send only to Service Bus.
  - `HttpOnly`: skip queue entirely.
- Service Bus config keys (adapter only):
  - `ScoringIngestion:AzureServiceBus:ConnectionString`
  - `ScoringIngestion:AzureServiceBus:QueueName`
- Optional: `ScoringIngestion:Mode`.

Payload contract:
```json
Cerbi.Contracts.ScoringQueueEnvelopeDto
{
  "idempotencyKey": "...",
  "correlationId": "...",
  "tenantId": "...",
  "appName": "...",
  "environment": "...",
  "payload": {
    "topic": "Orders",
    "category": "MyType",
    "logId": "abc123",
    "eventId": 42,
    "scoreImpact": 2.5,
    "governanceRelaxed": false,
    "timestamp": "2024-05-10T18:25:43.511Z",
    "violations": ["MissingField:userId"],
    "fields": { "userId": "abc123", "GovernanceScoreImpact": 2.5 }
  }
}
```
- `IdempotencyKey` defaults to a deterministic SHA256 of `TenantId|AppName|LogId` when not provided.
- `MessageId` on Service Bus uses the IdempotencyKey; `CorrelationId` flows when supplied.

## Performance
Optimizations:
- Category + type attribute caching (minimal StackTrace usage)
- Manual dictionary construction (avoid LINQ / boxing)
- Single validator instance per provider
- Queue-first scoring ingestion keeps logging path non-blocking

## Interoperability
- Flows MEL scopes (ILogger.BeginScope) through provider and logger
- Coexists with other MEL providers (Serilog, NLog, OTel, Console)

## FAQ
Q: Does it replace my logger?  A: No, it wraps MEL and preserves existing providers.
Q: Can logs be dropped?       A: Original line is always emitted; violations add a second JSON line.
Q: How to relax one log?       A: Include `{Relax}` true and have `AllowRelax: true` in profile.
Q: Scoring without impact?     A: No envelope is enqueued unless `GovernanceScoreImpact` is present and numeric.
Q: Queue + HTTP?               A: Queue-first will use Service Bus when configured, with HTTP fallback unless `QueueOnly` is chosen.

## Contributing
Issues and PRs with tests welcome. MIT licensed.

## License
MIT
