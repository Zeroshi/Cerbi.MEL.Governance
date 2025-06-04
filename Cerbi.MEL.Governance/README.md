# Cerbi.MEL.Governance (Draft Version)

## Current work: only emit the secondary log when violations occur.

**Demo & Examples:** [https://github.com/Zeroshi/Cerbi.MEL.Governance](https://github.com/Zeroshi/Cerbi.MEL.Governance)

**Real-time logging governance enforcement for Microsoft.Extensions.Logging (MEL)** using the Cerbi validation engine.

> 🚧 **Note:** In this release, the plugin emits the primary log line always and only emits a secondary JSON payload when there are governance violations. A dedicated `Relax()` helper for relaxed mode has not yet been added.
>
> We’ve been thrilled—and a bit surprised—by nearly 2,000 downloads in just a few days since this was quietly released. Thank you for your patience and feedback as we continue improving this project!

Cerbi.MEL.Governance is part of the [Cerbi](https://cerbi.io) suite. It enables runtime validation of log fields based on structured governance profiles. Built for ASP.NET Core, Worker Services, Azure Functions, and any .NET app using Microsoft.Extensions.Logging.

---

## 📂 Demo

See the sample implementation in our [Demo & Examples Repository](https://github.com/Zeroshi/Cerbi.MEL.Governance).

---

## 🚀 Features (Current Scope)

* ✅ Enforce required and forbidden fields
* ✅ Drop or tag logs with governance violations (only writes a second line when violations occur)
* ✅ Supports structured logging and `BeginScope`
* ✅ Supports `[CerbiTopic("...")]` profile routing via caller class detection (injected `CerbiTopic` field)
* ✅ Compatible with any MEL-compatible sink (Console, File, Seq, etc.)

> ⚠️ **Note:** “Relaxed mode” (AllowRelax) can be toggled via configuration and will mark entries as relaxed when a structured `Relax = true` field is provided. A dedicated `Relax()` helper method is not available in this release but may be introduced in a future version.

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
    options.Profile    = "Orders";                   // default fallback topic
    options.ConfigPath = "cerbi_governance.json";
    options.Enabled    = true;                         // enable or disable governance at runtime
});
```

---

## 🔹 Optional: Use `[CerbiTopic("...")]` to route logs to specific profiles

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

> ✅ The `[CerbiTopic]` attribute automatically injects a `CerbiTopic` field at runtime. Logs emitted from this class will be validated against the "Orders" profile.

---

## ✍️ Example Logging

```csharp
_logger.LogInformation("User info: {userId} {email}", "abc123", "test@example.com");

// Missing userId → governance violation under "Orders"
_logger.LogInformation("Only email provided: {email}", "test@example.com");

// Forbidden field (password) → governance violation under "Orders"
_logger.LogInformation("Password in log: {userId} {email} {password}", "abc123", "test@example.com", "secret");

// Relaxed example (when AllowRelax = true in config):
_logger.LogInformation("Email-only (relaxed): {email} {Relax}", "user@example.com", true);
```

---

## 🧐 Governance Output

When governance enrichment is enabled, the plugin writes a JSON payload only on a second line when violations occur. Examples:

1. **Violation example (missing required field):**

```json
{
  "GovernanceProfileUsed": "Orders",
  "GovernanceViolations": ["MissingField:userId"],
  "GovernanceRelaxed": false
}
```

2. **Forbidden field example:**

```json
{
  "GovernanceProfileUsed": "Orders",
  "GovernanceViolations": ["ForbiddenField:password"],
  "GovernanceRelaxed": false
}
```

3. **Relaxed example (AllowRelax = true, `Relax = true` passed):**

```json
{
  "email": "user@example.com",
  "CerbiTopic": "Orders",
  "GovernanceRelaxed": true,
  "GovernanceProfileUsed": "Orders"
}
```

---

## SBOM & Compliance

Cerbi.MEL.Governance is safe for use in secure logging pipelines. No outbound calls. MIT licensed. All governance logic is internal and validated at runtime.

---

## 🔗 Related Projects

* 🌐 CerbiStream — Core logging library
* ⚙️ Cerbi.Serilog.Governance
* 🔧 Cerbi.Governance.Runtime
* 📘 Cerbi Docs
