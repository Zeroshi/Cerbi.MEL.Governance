# Cerbi.MEL.Governance

**Real-time logging governance enforcement for Microsoft.Extensions.Logging (MEL)** using the Cerbi validation engine.

Cerbi.MEL.Governance is part of the [Cerbi](https://cerbi.io) suite. It enables runtime validation of log fields based on structured governance profiles. Built for ASP.NET Core, Worker Services, Azure Functions, and any .NET app using Microsoft.Extensions.Logging.

---

## 🚀 Features

* ✅ Enforce required and forbidden fields
* ✅ Drop or tag logs with governance violations
* ✅ Allow relaxed logs (`Relax()` mode)
* ✅ Supports structured logging and `BeginScope`
* ✅ Supports `[CerbiTopic("...")]` profile routing via caller class detection (using injected `CerbiTopic` field)
* ✅ Compatible with any MEL-compatible sink (Console, File, Seq, etc.)

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

// Forbidden field
logger.LogInformation("Password in log: {userId} {email} {password}", "abc123", "test@example.com", "secret");
```

---

## 🧐 Governance Output

If governance enrichment is enabled, the logger can tag entries like:

```json
{
  "GovernanceProfileUsed": "Orders",
  "GovernanceViolations": ["MissingField:userId"],
  "GovernanceRelaxed": false
}
```

---

## 📊 CerbiShield Scoring Integration (v1.1)

The MEL governance SDK ships **scoring identity metadata** with every governance event, enabling end-to-end traceability in CerbiShield dashboards.

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
