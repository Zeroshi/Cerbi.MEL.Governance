# Cerbi.MEL.Governance

**Real-time logging governance enforcement for Microsoft.Extensions.Logging (MEL) using the Cerbi validation engine.**

> 🚧 **Note:** In this release (v1.0.36), the plugin always emits your original log line, and only emits a secondary JSON payload when governance violations occur. A dedicated `Relax()` helper method has not yet been added.

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
dotnet add package Cerbi.MEL.Governance --version 1.0.36
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

## Performance and benchmarks

A small benchmark project is included to measure logging hot-paths.

Run locally (from repo root):

* `dotnet restore`
* `dotnet build -c Release BenchmarkSuite1/BenchmarkSuite1.csproj`
* `dotnet run -c Release --project BenchmarkSuite1 -- --list tree`
* Run specific benchmark:
  * `dotnet run -c Release --project BenchmarkSuite1 -- --filter "*AttributeTopic*"`

Notes:
* The `CPUUsageDiagnoser` attribute is stubbed; it’s safe to run with or without the real diagnoser installed.
* PowerShell users should keep the filter quoted.

---

## What’s new (performance)

We optimized the hot path to improve throughput and reduce allocations:

* Attribute-topic caching
  * Cache topic by logger category name and by declaring type.
  * Resolve via category (no StackTrace) when available; otherwise do a single lazy StackTrace scan per logger instance.
* Lower-allocation field extraction
  * Replace `ToDictionary` with a manual copy into a pre-sized `Dictionary<string, object>` (ordinal comparer) for structured state.

Result: fewer CPU cycles and fewer allocations per log in happy-path scenarios.

---

## Expected throughput (single-thread, .NET8, in-process, no I/O)

Rules of thumb you can expect when measuring against a no-op sink:

* Default-topic empty (bypass):    3–10 million logs/sec is strong.
* Attribute-topic path with caching (structured state of a few fields):  2–6 million logs/sec is strong.
* If a StackTrace is performed on every log (pre-optimization), sub-0.5 million logs/sec is common.

With real console/file sinks (I/O-bound), 50–200k logs/sec is normal. Hot-path gains are masked by I/O there, but still reduce CPU spikes.

---

## Benchmarks

A small BenchmarkDotNet project is included.

Run locally (from repo root):

* `dotnet restore`
* `dotnet build -c Release BenchmarkSuite1/BenchmarkSuite1.csproj`
* `dotnet run -c Release --project BenchmarkSuite1 -- --list tree`
* Run a specific benchmark:
  * `dotnet run -c Release --project BenchmarkSuite1 -- --filter "*AttributeTopic*"`

Tips for consistent results:

* Close background apps; use AC power; keep filters quoted in PowerShell.
* The `CPUUsageDiagnoser` attribute is a stub and won’t block runs.

---

## Optimized output (runtime behavior)

* Original line is always preserved.
* Second JSON payload appears only when there’s a violation.

Examples:

**Missing required field `userId`:**
```
{"GovernanceProfileUsed":"Orders","GovernanceViolations":["MissingField:userId"],"GovernanceRelaxed":false}
```

**Forbidden field `password`:**
```
{"GovernanceProfileUsed":"Orders","GovernanceViolations":["ForbiddenField:password"],"GovernanceRelaxed":false}
```

**Relaxed example (`AllowRelax = true`, `Relax = true`):**
```
{"email":"user@example.com","CerbiTopic":"Orders","GovernanceRelaxed":true,"GovernanceProfileUsed":"Orders"}
```

---

## Further tuning (advanced)

* Ensure logger categories map to concrete types to maximize attribute-topic cache hits.
* Keep structured state small and typed; avoid unnecessary boxing.
* Real sinks (console/file) are I/O-bound—batching or async sinks improve overall throughput.

---

## 9) Performance

- Designed for in-process validation with predictable overhead.
- Works on structured state and message templates to minimize allocations.
- See the `BenchmarkSuite1` project in this repository for scenarios and measurements.

Tip: Place Cerbi at the start of the logging pipeline to avoid downstream cost on non-compliant events.

---

## 10) FAQ

- Does this replace Serilog/NLog/OTEL?
  - No. It complements them by adding governance. Keep your sinks/exporters; Cerbi enforces policy upstream.
- What happens on violation?
  - Relaxed: event is emitted with violation tags and redacted values.
  - Strict: event may be blocked or downgraded based on policy.
- Can I define required/forbidden fields?
  - Yes, via governance profile configuration and settings.
- Can I load profiles dynamically?
  - Yes; load from JSON at startup, environment-specific configuration, or integrate with CerbiShield / Governance.Runtime for updates.
- Will message templates be updated?
  - Redaction targets structured properties and, when possible, the rendered message.
- How do I integrate with OTEL Collector?
  - Keep Cerbi in the MEL pipeline and add OTLP exporters. Events arrive governed at the Collector.
- Can I tag events with domain context?
  - Use `CerbiTopicAttribute` or include a `Topic` property in structured state.

---

## 11) Why not just use Serilog/NLog/OTEL alone?

They handle transport, storage, and query. They do not enforce enterprise governance (required/forbidden fields, PII/PHI redaction, policy validation, compile-time plus runtime consistency). Cerbi adds that governance layer so your logs are safe, compliant, and ML-ready—before they reach Seq, ELK/OpenSearch, Loki, Graylog, VictoriaLogs/VictoriaMetrics, TelemetryHarbor, Dozzle, or the OTEL Collector.

---

## 12) Contributing and Links

- Website: https://cerbi.io
- Repo: https://github.com/Zeroshi/Cerbi.MEL.Governance
- Related projects: CerbiStream, GovernanceAnalyzer, Governance.Runtime, CerbiShield, CerbIQ, CerbiSense (see Cerbi site/org)

If this helps govern production logs, please star the repo and open issues/PRs with tests.
