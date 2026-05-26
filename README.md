# SOASAP .NET SDK

Lightweight, production-ready feature flags SDK for .NET.

- **Lock-free hot path** (`O(1)` reads)
- **Zero network calls** on application startup
- **Real-time updates** via SSE (Server-Sent Events)
- **Persistent disk cache** for instant cold starts
- **Graceful offline behavior** & bulletproof resiliency
- **Minimal dependencies** (only core Microsoft extensions)
- **Multi-platform support:** ASP.NET Core, Worker Services, Console Apps, Blazor

# Why SOASAP?

Most feature flag platforms are built for enterprises.

SOASAP focuses on:

- low-latency local evaluation
- minimal dependencies
- zero startup impact
- resilient offline behavior
- simple integration with .NET applications

It intentionally avoids:

- heavyweight analytics
- experimentation platforms
- complex enterprise workflows

# Installation

```bash
dotnet add package Soasap.Sdk
```

# Quick Start

## 1. Register SOASAP

Add the SDK to your dependency injection container.

```csharp
using Soasap.Sdk;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSoasap("your-api-key")
    .PreloadFlags();

var app = builder.Build();
app.Run();
```

## 2. Use feature flags

```csharp
app.MapGet("/", (ISOASAPClient flags) =>
{
    if (flags.GetBool("new-checkout"))
    {
        return "New checkout enabled";
    }

    return "Old checkout";
});
```

# Startup & Synchronization Behavior

SOASAP uses a non-blocking background architecture to ensure feature flagging **never impacts your application startup time**.

## Immediate Synchronization (Recommended)

By appending `.PreloadFlags()`, the SDK starts the SSE background worker automatically during application boot via `IHostedService`.

- **Cold Start**: It instantly loads the last known snapshot from the local disk cache while the network connection is being established.
- **Non-blocking**: It does NOT block the host application startup flow, meaning your web server keeps booting up seamlessly even if the SOASAP API is temporarily unavailable.

## Lazy Synchronization (Optional)

If you omit `.PreloadFlags()`, the SDK operates in a lazy mode:

```csharp
builder.Services.AddSoasap("your-api-key");
```

⚠️ **Warning**: In lazy mode, no background workers are initialized at startup. The network connection will only be established upon the very first flag evaluation. This means the first execution will instantly fall back to default values (or local disk cache if available) while the SSE stream connects in the background.

# Typed Flag Access

## Boolean

```csharp
var enabled = flags.GetBool("feature-x");
```

## Number

```csharp
var limit = flags.GetNumber("rate-limit", 100);
```

## String

```csharp
var theme = flags.GetString("ui-theme", "light");
```

## JSON / Remote Config

```csharp
var config = flags.GetJson<CheckoutConfig>("checkout-config");
```

Example model:

```csharp
public sealed class CheckoutConfig
{
    public bool EnableUpsells { get; set; }
    public int MaxItems { get; set; }
}
```

💡 **Cross-Platform Friendly**: The JSON deserializer is fully case-insensitive by default. It seamlessly maps `camelCase` fields coming from JavaScript dashboards directly into idiomatic C# `PascalCase` properties without requiring verbose `[JsonPropertyName]` attributes.

# Production Safety & Guardrails

SOASAP treats your host application's stability as the highest priority. It is built as a secondary infrastructure component.

- **No Race Conditions**: State evaluation uses an atomic lock-free immutable snapshot replacement pattern. Threads actively reading flags will never suffer from partial mutation state or `ObjectDisposedException`.
- **Memory Cap Protection (Anti-DoS)**: The internal SSE parser enforces a strict 5 MB payload cap. If a corrupted stream or giant payload overflows this limit, the SDK drops the connection and resets, preventing memory spikes.
- **Payload Validation**: Before replacing the active snapshot, the SDK validates that the root element is strictly a JSON object {}. Invalid payloads are safely ignored.
- **IO Coalescing (Disk Debounce)**: To prevent disk thrashing during rapid configuration adjustments on the server, disk updates are managed via an internal bounded channel. Writes are coalesced and written to the disk at most once every 2–3 seconds.

# Offline & Failure Resiliency

SOASAP is designed to be resilient.


| Scenario                    | Behavior                         |
| --------------------------- | -------------------------------- |
| API unavailable             | Uses stale cached flags          |
| SSE disconnected            | Keeps last known snapshot        |
| First startup without cache | Returns default values           |
| Invalid payload             | Payload ignored                  |
| Disk cache failure          | In-memory mode continues         |
| Persistent network issues   | Automatic reconnect with backoff |


**The SDK never throws from flag getters.**

# Disk Cache Locations

SOASAP automatically persists the latest snapshot to disk for instant cold starts.

Default locations:


| Platform      | Location                      |
| ------------- | ----------------------------- |
| Windows       | `%LocalAppData%\soasap\cache` |
| Linux / macOS | `~/.local/share/soasap/cache` |


You can override the cache directory:

```csharp
builder.Services.AddSoasap("your-api-key")
    .WithCacheDirectory("/custom/path");
```

# Error Handling & Observability

You can tap into internal background SDK diagnostics without risking hot path performance:

```csharp
builder.Services.AddSoasap("your-api-key")
    .OnError(ctx =>
    {
        Console.WriteLine($"[{ctx.Source}] (Transient: {ctx.IsTransient}) -> {ctx.Exception.Message}");
    });
```

## Error Sources

```csharp
public enum SoasapErrorSource
{
    Network,
    Disk,
    Parser
}
```

## Failure Philosophy

SOASAP treats feature flags as secondary infrastructure.

The SDK is designed to:

- never block application startup
- never terminate the host application
- continue operating with stale snapshots during outages
- degrade gracefully during network failures

# Architecture Blueprint

SOASAP SDK is designed for low overhead and production safety.

```
[Hot Path]             ISOASAPClient.GetBool() -> Volatile.Read(_currentSnapshot) -> O(1) Memory Lookup
                                                                ^
                                                                | (Atomic Interlocked.Exchange)
[Background Sync]      SSE Stream -> SseEventParser (5MB Cap) --+--> DiskWriteCoalescer (Channel Debounce) -> Local Disk
```

## Performance Goals


| Operation    | Expected Cost                                                            |
| ------------ | ------------------------------------------------------------------------ |
| `GetBool`    | ~O(1), zero heap allocations                                             |
| `GetNumber`  | ~O(1), zero heap allocations                                             |
| `GetString`  | ~O(1), retrieves a pre-allocated text reference from the active snapshot |
| `GetJson<T>` | Deserialization cost, optimized via element-level streaming              |


## Thread Safety

`ISOASAPClient` is fully thread-safe and intended to be registered as a singleton.

# Integration Examples

## ASP.NET Core Middleware

```csharp
app.Use(async (ctx, next) =>
{
    var flags = ctx.RequestServices.GetRequiredService<ISOASAPClient>();

    if (flags.GetBool("maintenance-mode"))
    {
        ctx.Response.StatusCode = 503;
        await ctx.Response.WriteAsync("Maintenance");
        return;
    }

    await next();
});
```

## Background Worker Service

```csharp
public sealed class EmailWorker : BackgroundService
{
    private readonly ISOASAPClient _flags;

    public EmailWorker(ISOASAPClient flags)
    {
        _flags = flags;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_flags.GetBool("email-worker-enabled"))
            {
                // process jobs
            }

            await Task.Delay(1000, stoppingToken);
        }
    }
}
```

## Supported Frameworks

- .NET 8 / .NET 9
- .NET 6
- .NET Standard 2.0 (Compatible with Legacy .NET Framework apps)

# Design Principles

SOASAP SDK follows several core principles:

- Never block application startup
- Never crash the host application
- Prefer stale data over failure
- Keep the hot path extremely fast
- Make network failures invisible to users
- Keep the API minimal and predictable

# Roadmap

Planned features:

- Polling fallback mode
- Source generators for strongly-typed flags
- ASP.NET Core middleware integrations
- Metrics hooks
- SDK diagnostics API
- AI Guardian auto-rollback support

# License

MIT

# Website

[https://soasap.com](https://soasap.com)