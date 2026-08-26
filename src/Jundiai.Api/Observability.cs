using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Jundiai.Api;

public static class JundiaiObservabilityExtensions
{
    public static IServiceCollection AddJundiaiOperationalTelemetry(this IServiceCollection services)
    {
        services.AddSingleton<OperationalTelemetryStore>();
        return services;
    }

    public static IApplicationBuilder UseJundiaiOperationalTelemetry(this IApplicationBuilder app) =>
        app.UseMiddleware<OperationalTelemetryMiddleware>();

    public static IEndpointRouteBuilder MapJundiaiOperationalHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/health/live", () => Results.Ok(new
        {
            status = "live",
            service = "Jundiai HealthOS POC",
            at = DateTimeOffset.UtcNow
        }));

        endpoints.MapGet("/api/health/ready", async (PersistenceRuntimeState persistence, IServiceProvider services, CancellationToken ct) =>
        {
            var databaseReady = true;
            var databaseMode = persistence.Mode;
            if (persistence.Configured)
            {
                try
                {
                    var factory = services.GetRequiredService<IDbContextFactory<JundiaiDbContext>>();
                    await using var db = await factory.CreateDbContextAsync(ct);
                    databaseReady = await db.Database.CanConnectAsync(ct);
                    databaseMode = databaseReady ? "durable-postgresql" : "configured-unreachable";
                }
                catch
                {
                    databaseReady = false;
                    databaseMode = "configured-error";
                }
            }

            var ready = !persistence.Configured || databaseReady;
            return ready
                ? Results.Ok(new { status = "ready", database = databaseMode, pocFallbackAllowed = !persistence.Configured, at = DateTimeOffset.UtcNow })
                : Results.Json(new { status = "not_ready", database = databaseMode, at = DateTimeOffset.UtcNow }, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        endpoints.MapGet("/api/operations/telemetry", (OperationalTelemetryStore store) => Results.Ok(store.Snapshot()));
        return endpoints;
    }
}

public sealed class OperationalTelemetryMiddleware(RequestDelegate next, OperationalTelemetryStore telemetry, ILogger<OperationalTelemetryMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);
        context.TraceIdentifier = correlationId;
        context.Response.Headers["X-Correlation-Id"] = correlationId;
        var started = Stopwatch.GetTimestamp();
        var status = StatusCodes.Status500InternalServerError;

        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["RequestPath"] = context.Request.Path.Value ?? string.Empty
        }))
        {
            try
            {
                await next(context);
                status = context.Response.StatusCode;
            }
            catch
            {
                status = context.Response.StatusCode >= 400 ? context.Response.StatusCode : StatusCodes.Status500InternalServerError;
                throw;
            }
            finally
            {
                var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                telemetry.Record(context.Request.Method, Group(context.Request.Path), status, elapsedMs);
            }
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        var supplied = context.Request.Headers["X-Correlation-Id"].FirstOrDefault()?.Trim();
        if (!string.IsNullOrWhiteSpace(supplied) && supplied.Length <= 80 && supplied.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.'))
            return supplied;
        return Guid.NewGuid().ToString("N");
    }

    private static string Group(PathString path)
    {
        var value = path.Value ?? "/";
        if (!value.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) return "web";
        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 1 ? segments[1].ToLowerInvariant() : "api";
    }
}

public sealed class OperationalTelemetryStore
{
    private long _total;
    private long _errors;
    private readonly ConcurrentDictionary<string, RouteTelemetryCounter> _groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public void Record(string method, string group, int statusCode, double elapsedMs)
    {
        Interlocked.Increment(ref _total);
        if (statusCode >= 500) Interlocked.Increment(ref _errors);
        _groups.GetOrAdd($"{method.ToUpperInvariant()}:{group}", _ => new RouteTelemetryCounter()).Record(statusCode, elapsedMs);
    }

    public object Snapshot()
    {
        var total = Interlocked.Read(ref _total);
        var errors = Interlocked.Read(ref _errors);
        return new
        {
            startedAt = _startedAt,
            generatedAt = DateTimeOffset.UtcNow,
            totalRequests = total,
            serverErrors = errors,
            errorRate = total == 0 ? 0 : Math.Round((double)errors / total * 100, 3),
            groups = _groups.OrderBy(x => x.Key).Select(x => new
            {
                key = x.Key,
                x.Value.Count,
                x.Value.Errors,
                averageMs = x.Value.Count == 0 ? 0 : Math.Round(x.Value.TotalMilliseconds / x.Value.Count, 2),
                maxMs = Math.Round(x.Value.MaxMilliseconds, 2)
            }).ToArray(),
            note = "Telemetria POC em memória; produção deve exportar métricas, logs e traces para backend observável central."
        };
    }
}

public sealed class RouteTelemetryCounter
{
    private long _count;
    private long _errors;
    private long _totalMicros;
    private long _maxMicros;

    public long Count => Interlocked.Read(ref _count);
    public long Errors => Interlocked.Read(ref _errors);
    public double TotalMilliseconds => Interlocked.Read(ref _totalMicros) / 1000d;
    public double MaxMilliseconds => Interlocked.Read(ref _maxMicros) / 1000d;

    public void Record(int statusCode, double elapsedMs)
    {
        Interlocked.Increment(ref _count);
        if (statusCode >= 500) Interlocked.Increment(ref _errors);
        var micros = Math.Max(0, (long)Math.Round(elapsedMs * 1000));
        Interlocked.Add(ref _totalMicros, micros);
        while (true)
        {
            var current = Interlocked.Read(ref _maxMicros);
            if (micros <= current || Interlocked.CompareExchange(ref _maxMicros, micros, current) == current) break;
        }
    }
}
