# Cerbi.MEL.Governance

[![CerbiSuite](https://img.shields.io/badge/CerbiSuite-Compatible-brightgreen?style=for-the-badge)](https://cerbi.io)

> ✅ **Now working with CerbiSuite** — Fully integrated with CerbiShield scoring, governance dashboards, and end-to-end traceability across all Cerbi logging SDKs.

**Real-time logging governance enforcement for Microsoft.Extensions.Logging (MEL)** using the Cerbi validation engine.

Cerbi.MEL.Governance is part of the [Cerbi](https://cerbi.io) suite. It enables runtime validation of log fields based on structured governance profiles. Built for ASP.NET Core, Worker Services, Azure Functions, and any .NET app using Microsoft.Extensions.Logging.

---

## 🚀 Features

* ✅ Enforce required and forbidden fields
* ✅ **Strict mode suppresses raw forbidden values** — violating messages are replaced by a redacted governance-annotated JSON payload that never reaches the sink
* ✅ Drop or tag logs with governance violations
* ✅ Allow relaxed logs (`Relax()` mode)
* ✅ Supports structured logging and `BeginScope`
* ✅ Supports `[CerbiTopic("...")]` profile routing via caller class detection (using injected `CerbiTopic` field)
* ✅ Compatible with any MEL-compatible sink (Console, File, Seq, etc.)
* ✅ Score shipping always fires — fallback scoring (100 − 10 × violations, floor 0) applied when `GovernanceScoreImpact` is absent

---

## 📆 Installation

```bash
dotnet add package Cerbi.MEL.Governance
```

---

## 🛠 Setup

### 1. Add a governance config file

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

Save this as `cerbi_governance.json` in your project root.

### 2. Configure MEL to use Cerbi governance

```csharp
using Microsoft.Extensions.Logging;
using Cerbi.MEL.Governance;

builder.Logging.AddCerbiGovernance(options =>
{
    options.Profile = "Orders"; // default fallback
    options.ConfigPath = "cerbi_governance.json";
});
```

---

## 🔹 Optional: Use \[CerbiTopic("...")] to route logs to specific profiles

```csharp
[CerbiTopic("Orders")]
public class OrderService
{
    private readonly ILogger<OrderService> _logger;

    public OrderService(ILogger<OrderService> logger)
    {
        _logger = logger;
    }

    public void Process()
    {
        _logger.LogInformation("Order processed for {userId}", "abc123");
    }
}
```

> ✅ This works via automatic injection of the topic into the log fields.
> The logger sets the `CerbiTopic` field at runtime if the caller class has the `[CerbiTopic("...")]` attribute.

---

## ✍️ Example Logging

```csharp
logger.LogInformation("User info: {userId} {email}", "abc123", "test@example.com");

// Violates governance (missing userId)
logger.LogInformation("Only email provided: {email}", "test@example.com");

// Forbidden field — in Strict mode the raw message is suppressed;
// a redacted governance JSON payload is emitted instead
logger.LogInformation("Password in log: {userId} {email} {password}", "abc123", "test@example.com", "secret");
```

---

## 🧐 Governance Output

### Non-Strict / no violations
The original log message passes through unchanged, with an optional governance-annotated JSON side-channel attached.

### Strict mode + violations (NEW in v1.1)
The **original message is suppressed**. A redacted JSON payload is emitted to the sink instead, ensuring forbidden field values never leave the application boundary:

```json
{
  "userId": "abc123",
  "email": "test@example.com",
  "GovernanceProfileUsed": "Orders",
  "GovernanceViolations": ["ForbiddenField:password"],
  "GovernanceRelaxed": false,
  "GovernanceMode": "Strict"
}
```

Note: the forbidden field value (`password`) is **absent** from the output — it is stripped during redaction.

---

## 📊 CerbiShield Scoring Integration (v1.1)

The MEL governance SDK ships **scoring identity metadata** with every governance event, enabling end-to-end traceability in CerbiShield dashboards.

### Score shipping guarantees

| Scenario | Behaviour |
|----------|-----------|
| `GovernanceScoreImpact` present in validated fields | Used directly |
| `GovernanceScoreImpact` absent (validator did not compute it) | Computed as `max(0, 100 − 10 × violationCount)` |
| Relaxed log | Score impact forced to `0` |

Score events are always enqueued regardless of enforcement mode, so the portal always receives telemetry even for blocked events.

### Identity Fields

| Field | Source | Purpose |
|-------|--------|---------|
| `ServiceName` | `CerbiGovernanceMELSettings.ServiceName` | Logical service name (e.g., `OrderService`) |
| `AppVersion` | `CerbiGovernanceMELSettings.AppVersion` | Deployed version (e.g., `1.2.3`) |
| `InstanceId` | `CerbiGovernanceMELSettings.InstanceId` | Container/pod instance identifier |
| `DeploymentId` | `CerbiGovernanceMELSettings.DeploymentId` | Release/deployment tracking ID |
| `ProfileName` | Governance profile name (topic) | Stamped onto every `ViolationDto` |
| `AppName` | `CerbiGovernanceMELSettings.AppName` | Stamped onto every `ViolationDto` |

### Configuration

```csharp
builder.Logging.AddCerbiGovernance(options =>
{
    options.Profile = "Orders";
    options.ConfigPath = "cerbi_governance.json";
    options.AppName = "OrderService";
    options.Environment = "Production";
    options.ServiceName = "order-api";
    options.AppVersion = "1.2.3";
    options.InstanceId = Environment.GetEnvironmentVariable("HOSTNAME");
    options.DeploymentId = Environment.GetEnvironmentVariable("DEPLOYMENT_ID");
    options.ScoreShipping = new ScoreShippingOptions
    {
        Enabled = true,
        LicenseAllowsScoring = true
    };
});
```

All identity fields flow through:
1. `CerbiGovernanceMELSettings` → `ScoringEventDto` → `ScoringEnvelopeFactory`
2. Each `ViolationDto` is stamped with `ProfileName` and `AppName` for downstream linkage.

---

## 🔗 Related Projects

* 🌐 [CerbiStream](https://www.nuget.org/packages/CerbiStream) — Core logging library
* ⚙️ [Cerbi.Serilog.Governance](https://www.nuget.org/packages/Cerbi.Serilog.Governance)
* 🔧 [Cerbi.Governance.Runtime](https://www.nuget.org/packages/Cerbi.Governance.Runtime)
* 📘 [Cerbi Docs](https://cerbi.io)
