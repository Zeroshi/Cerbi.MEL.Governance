# Cerbi.MEL.Governance

**Real-time logging governance enforcement for Microsoft.Extensions.Logging (MEL) using the Cerbi validation engine.**

> 🚧 **Note:** In this release (v1.0.35), the plugin always emits your original log line, and only emits a secondary JSON payload when governance violations occur. A dedicated `Relax()` helper method has not yet been added.

Cerbi.MEL.Governance is part of the [Cerbi](https://cerbi.io) suite. It enables runtime validation of log fields based on structured governance profiles. Built for ASP.NET Core, Worker Services, Azure Functions, or any .NET app that uses Microsoft.Extensions.Logging.

---

## 📂 Demo & Examples

See the sample usage in our [Demo & Examples repository](https://github.com/Zeroshi/Cerbi.MEL.Governance).

---

## 🚀 Features (Current Scope)

* ✅ Enforce required and forbidden fields
* ✅ **Only emit a secondary JSON payload when violations occur** (original log always appears)
* ✅ Supports structured logging and `BeginScope`
* ✅ Supports `[CerbiTopic("…")]` profile routing (injects a `CerbiTopic` field at runtime)
* ✅ Compatible with any MEL-compatible sink (Console, File, Seq, etc.)

> ⚠️ **Note on Relaxed mode**
> You can toggle `"AllowRelax": true` in your JSON config. If you include `{Relax}` as a Boolean field in your `LogInformation` call, the second JSON line will mark `GovernanceRelaxed: true`. A fluent `Relax()` helper is not provided in this release but may appear in a future version.

---

## 📆 Installation

```bash
dotnet add package Cerbi.MEL.Governance --version 1.0.35
```

---

## 🛠 Setup

### 1. Add a governance config file

Create a file named `cerbi_governance.json` in your project root (or point ConfigPath somewhere else). Example:

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

### 2. Configure MEL to use Cerbi governance

```csharp
using Microsoft.Extensions.Logging;
using Cerbi;   // ← AddCerbiGovernance lives in the Cerbi namespace

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddSimpleConsole(options =>
        {
            options.IncludeScopes = true;
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });

        logging.AddCerbiGovernance(options =>
        {
            options.Profile    = "Orders";                   // default fallback profile name
            options.ConfigPath = "cerbi_governance.json";    // path to your JSON profile
            options.Enabled    = true;                         // enable or disable governance at runtime
        });
    })
    .ConfigureServices(services =>
    {
        services.AddTransient<OrderService>();
    });
```

If you’re using `WebApplication.CreateBuilder(args)`, just call
`builder.Logging.AddCerbiGovernance(...)` in the same way.

---

## 🔹 Optional: `[CerbiTopic("…")]` to route logs

```csharp
using Cerbi;  // for CerbiTopicAttribute

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

> ✅ Any log calls from a class tagged with `[CerbiTopic("Orders")]` will be validated against the "Orders" profile.

---

## ✍️ Example logging

```csharp
// Valid log (has both userId and email)
_logger.LogInformation("User info: {userId} {email}", "abc123", "test@example.com");

// Missing userId → governance violation under "Orders" profile
_logger.LogInformation("Only email provided: {email}", "test@example.com");

// Forbidden field (“password”) → governance violation under "Orders"
_logger.LogInformation(
    "Password in log: {userId} {email} {password}",
    "abc123",
    "test@example.com",
    "secret"
);

// Relaxed example (AllowRelax = true in JSON config; passing {Relax} = true):
_logger.LogInformation(
    "Email‐only (relaxed): {email} {Relax}",
    "user@example.com",
    true
);
```

---

## 🧐 Governance output

When governance enforcement is enabled, Cerbi.MEL.Governance writes your original log line first, then—**only if there’s a violation**—writes a second JSON payload. Example JSON outputs:

1. **Missing required field (`userId`)**

   ```json
   {
     "GovernanceProfileUsed": "Orders",
     "GovernanceViolations": ["MissingField:userId"],
     "GovernanceRelaxed": false
   }
   ```

2. **Forbidden field (`password`)**

   ```json
   {
     "GovernanceProfileUsed": "Orders",
     "GovernanceViolations": ["ForbiddenField:password"],
     "GovernanceRelaxed": false
   }
   ```

3. **Relaxed example (`AllowRelax = true`, `Relax = true`)**

   ```json
   {
     "email": "user@example.com",
     "CerbiTopic": "Orders",
     "GovernanceRelaxed": true,
     "GovernanceProfileUsed": "Orders"
   }
   ```

> **Important:** We never drop your original line. We always print it as you wrote it, then add a JSON object on a second line only if there’s something to flag.

---

## SBOM & Compliance

Cerbi.MEL.Governance is MIT-licensed and safe for secure pipelines.
No outbound calls—everything runs in‐process against your JSON file.

---

## 🔗 Related Projects

* 🌐 [CerbiStream](https://github.com/Zeroshi/Cerbi-CerbiStream) — Core logging library
* ⚙️ [Cerbi.Serilog.Governance](https://www.nuget.org/packages/Cerbi.Serilog.Governance)
* 🔧 [Cerbi.Governance.Runtime](https://www.nuget.org/packages/Cerbi.Governance.Runtime) — shared runtime logic
* 📘 [Cerbi Docs](https://cerbi.io/docs)

---

### Summary of fixes

1. **Changed namespace** for `AddCerbiGovernance` to `using Cerbi;`
2. **Adjusted features** to say “only emit a secondary JSON payload”
3. **Clarified Relaxed mode** instructions (no built‐in `Relax()` yet)
4. **Removed outdated “2,000 downloads” line**

With these edits, your README on NuGet (v1.0.35) will be accurate, clear, and free of compile errors.
