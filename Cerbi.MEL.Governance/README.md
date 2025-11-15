Got it, you want the README to be **explicit** about Relax() not existing and how Relax actually works in this repo.

Here’s the **fully updated README** for `Cerbi.MEL.Governance` with your new Relax details integrated cleanly into the docs.

You can drop this directly into `README.md`.

---

# Cerbi.MEL.Governance

**Real-time logging governance enforcement for Microsoft.Extensions.Logging (MEL) using the Cerbi validation engine.**

`Cerbi.MEL.Governance` wires **Cerbi’s runtime governance engine** directly into the MEL pipeline used by ASP.NET Core, Worker Services, Azure Functions, and any .NET app built on `Microsoft.Extensions.Logging`.

It validates structured logs against **Cerbi governance profiles** and emits **a secondary JSON payload only when violations occur**, while always preserving your original log line.

Part of the **[Cerbi Suite](https://cerbi.io)** for governed logging, telemetry, and analytics.

---

## Why This Exists

MEL + sinks like **Console**, **File**, **Serilog**, **NLog**, **Log4Net**, **OpenTelemetry Logging / OTLP**, **Seq**, **Grafana Loki / Promtail / Alloy**, **Fluentd / FluentBit**, **ELK / OpenSearch**, **Graylog**, **VictoriaLogs / VictoriaMetrics**, or even **Journald / syslog** can ship a huge volume of logs.

What they *don’t* do:

* Enforce **Required** or **Forbidden** fields (e.g., `userId`, `SSN`, `password`).
* Enforce **field types** or **enum sets**.
* Tag events as **Relaxed** vs **Strictly governed**.
* Emit explicit **governance violation metadata** for downstream scoring and compliance.

`Cerbi.MEL.Governance` adds that **governance layer** on top of MEL, without replacing your existing sinks or observability stack.

---

## Demo & Examples

Full examples and demo projects live here:

👉 **[Cerbi.MEL.Governance Demo & Examples](https://github.com/Zeroshi/Cerbi.MEL.Governance)**

---

## Features (Current Scope)

* ✅ Enforce **required** and **forbidden** fields via governance profiles
* ✅ **Secondary JSON payload only when violations occur** (original log always appears)
* ✅ Supports **structured logging** and `BeginScope`
* ✅ Supports `[CerbiTopic("…")]` profile routing (injects a `CerbiTopic` field at runtime)
* ✅ Compatible with any MEL-compatible sink (Console, File, Seq, Serilog, NLog, OTEL exporters, etc.)

> ⚠️ **Relaxed mode (v1.0.36 details)**
>
> * In this repo (v1.0.36), **there is no fluent `Relax()` helper**.
> * No `Relax()` method or extension exists in the codebase.
> * `CerbiGovernanceMELSettings` has **no `Relax` flag**.
> * Relaxed behavior can only be signaled via a **structured state property named `Relax` (Boolean)** and enabled by `"AllowRelax": true` in your profile JSON.
> * When both conditions are met, the second JSON line will include `GovernanceRelaxed: true`.

---

## Installation

```bash
dotnet add package Cerbi.MEL.Governance --version 1.0.36
```

---

## Setup

### 1. Add a governance config file

Create `cerbi_governance.json` in your project root (or point `ConfigPath` elsewhere):

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
      "AllowedTopics": [ "Orders" ]
    }
  }
}
```

Governance profiles are defined using the shared schema from `Cerbi.Governance.Core` and can also be managed via CerbiShield.

---

### 2. Configure MEL to use Cerbi governance

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Cerbi; // AddCerbiGovernance lives in the Cerbi namespace

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
            options.ConfigPath = "cerbi_governance.json";    // governance JSON path
            options.Enabled    = true;                       // enable/disable at runtime
        });
    })
    .ConfigureServices(services =>
    {
        services.AddTransient<OrderService>();
    });
```

For minimal APIs / ASP.NET Core:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddCerbiGovernance(options =>
{
    options.Profile    = "Orders";
    options.ConfigPath = "cerbi_governance.json";
    options.Enabled    = true;
});

var app = builder.Build();
```

---

## Optional: `[CerbiTopic("…")]` to route logs

Use `[CerbiTopic]` to scope logs to a specific governance profile:

```csharp
using Cerbi;  // CerbiTopicAttribute

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

✅ Any log calls from a class tagged with `[CerbiTopic("Orders")]` will be validated under the `"Orders"` profile, subject to your JSON config.

---

## Example Logging

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

// Relaxed example (AllowRelax = true in JSON config; {Relax} = true in structured state)
_logger.LogInformation(
    "Email-only (relaxed): {email} {Relax}",
    "user@example.com",
    true
);
```

---

## Governance Output

When governance is enabled:

1. Your **original log line** is written exactly as you called it.
2. If a violation occurs, a **second JSON payload** is emitted describing the violation(s) and governance metadata.

### 1. Missing required field (`userId`)

```json
{
  "GovernanceProfileUsed": "Orders",
  "GovernanceViolations": [ "MissingField:userId" ],
  "GovernanceRelaxed": false
}
```

### 2. Forbidden field (`password`)

```json
{
  "GovernanceProfileUsed": "Orders",
  "GovernanceViolations": [ "ForbiddenField:password" ],
  "GovernanceRelaxed": false
}
```

### 3. Relaxed example (`AllowRelax = true`, `Relax = true`)

```json
{
  "email": "user@example.com",
  "CerbiTopic": "Orders",
  "GovernanceRelaxed": true,
  "GovernanceProfileUsed": "Orders"
}
```

> 🔎 **Guarantee:** The original message is **never dropped**. The second line is additive and only appears when there’s something to flag.

---

## Environment & Context Enrichment

Cerbi.MEL.Governance can add environment metadata for downstream systems (Loki, Seq, Elastic/OpenSearch, Graylog, VictoriaLogs, OTEL Collector, etc.):

* `ApplicationId`

  * From `CERBI_APP_ID`
  * Default: `"MyApp"`

* `InstanceId`

  * Machine name / host

* `Region`

  * From `CLOUD_REGION`
  * Default: `"unknown-region"`

* `CloudProvider`

  * Inferred from environment (e.g., Azure, AWS, GCP, OnPrem) when possible

This makes it easier to slice governance violations by app, instance, region, or cloud provider.

---

## Performance & Benchmarks

A **BenchmarkDotNet** project is included to test hot-path performance.

From the repo root:

```bash
dotnet restore
dotnet build -c Release BenchmarkSuite1/BenchmarkSuite1.csproj
dotnet run -c Release --project BenchmarkSuite1 -- --list tree

# Run a specific benchmark
dotnet run -c Release --project BenchmarkSuite1 -- --filter "*AttributeTopic*"
```

Notes:

* `CPUUsageDiagnoser` is stubbed; benchmarks will run with or without a real diagnoser.
* On real console/file sinks, logging becomes **I/O-bound**; CPU optimizations still help with spikes but raw throughput is dictated by sinks.

### Hot-path Optimizations

Current optimizations include:

* **Attribute-topic caching**

  * Cache topic by logger category name and declaring type.
  * Prefer category-based resolution; only fall back to a one-time `StackTrace` scan per logger instance when needed.

* **Lower-allocation field extraction**

  * Manual copy to a pre-sized `Dictionary<string, object>` (ordinal comparer).
  * Avoids LINQ and unnecessary allocations on the hot path.

### Expected Ballpark Throughput (no-op / in-process scenarios)

These are *guidance*, not hard guarantees, assuming a no-op sink on .NET 8:

* Default-topic fast path: ~3–10M logs/sec is strong.
* Attribute-topic path with caching + small structured state: ~2–6M logs/sec is strong.
* With `StackTrace` on every log (pre-optimization): sub-0.5M logs/sec is common.

With actual console/file sinks, 50–200k logs/sec is normal; the sink dominates.

---

## How It Plays with Other Stacks

`Cerbi.MEL.Governance` runs in the **MEL pipeline** and is compatible with:

* **Serilog / NLog / Log4Net** via MEL integration
* **Seq** (Datalust)
* **Grafana Loki / Promtail / Alloy**
* **Fluentd / FluentBit**
* **Elastic / OpenSearch / Graylog / VictoriaLogs / VictoriaMetrics**
* **OpenTelemetry Logging / OTLP / OTEL Collector**
* **Journald / syslog**

You keep your existing sinks/exporters. Cerbi:

1. Validates structured state against governance profiles.
2. Emits your original log + optional governance JSON line.
3. Lets your sinks or OTEL exporters handle the rest.

---

## SBOM & Compliance

* **License:** MIT
* No outbound network calls.
* All enforcement is **in-process** against your governance JSON file.
* Safe for secure and regulated pipelines (subject to your own review).

---

## FAQ

**Does this replace Serilog/NLog/OTEL?**
No. It sits in the MEL pipeline and **adds governance** before logs flow into Serilog, NLog, OTEL exporters, Loki, Seq, ELK, or any other sink.

**Does it ever drop logs?**
In this release:

* The original log is *never* dropped.
* Violations are surfaced via a second JSON line.

**Is there a `Relax()` helper?**
No. In this repo (v1.0.36):

* There is **no fluent `Relax()` helper**.
* No `Relax()` method or extension exists in the codebase.
* `CerbiGovernanceMELSettings` has **no `Relax` flag**.
* Relaxed behavior can **only** be signaled via a structured state property named `Relax` (Boolean), and only when `"AllowRelax": true` is enabled in your governance profile JSON.

**How do I relax governance for specific logs?**
Given `"AllowRelax": true` in the profile:

```csharp
_logger.LogInformation(
    "Email-only (relaxed): {email} {Relax}",
    "user@example.com",
    true
);
```

The governance JSON line will include `GovernanceRelaxed: true` and `GovernanceProfileUsed`.

**Can I define required/forbidden fields?**
Yes, via `FieldSeverities` in your governance JSON (e.g., `Required`, `Forbidden`, `Info`).

**Can I load profiles dynamically?**
Profiles are read from JSON. You can combine this with config transforms, environment-specific files, or future CerbiShield-based deployment flows.

**How does this work with OpenTelemetry Collector?**
Use Cerbi in the MEL pipeline, then attach OTLP exporters. Logs will reach the OTEL Collector already governed and tagged.

---

## Contributing & Links

* Website: **[https://cerbi.io](https://cerbi.io)**
* Repo: **[https://github.com/Zeroshi/Cerbi.MEL.Governance](https://github.com/Zeroshi/Cerbi.MEL.Governance)**
* Related projects: CerbiStream, Cerbi.Governance.Core, Cerbi.Governance.Runtime, Cerbi.GovernanceAnalyzer, CerbiShield, CerbIQ, CerbiSense

If this helps keep your logs compliant and non-chaotic, **star the repo** and open issues/PRs with tests.

---

## License

MIT — see `LICENSE`.
