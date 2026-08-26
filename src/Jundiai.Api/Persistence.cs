using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Jundiai.Api;

public static class JundiaiPersistenceExtensions
{
    public static IServiceCollection AddJundiaiPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Jundiai");
        var configured = !string.IsNullOrWhiteSpace(connectionString);
        services.AddSingleton(new PersistenceRuntimeState(configured, "PostgreSQL", configured ? "configured" : "poc-memory-fallback"));

        if (configured)
        {
            services.AddDbContextFactory<JundiaiDbContext>(options =>
                options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(JundiaiDbContext).Assembly.FullName)));
        }

        services.AddSingleton<PersistenceService>();
        return services;
    }

    public static IApplicationBuilder UseJundiaiTenantContext(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantContextMiddleware>();

    public static async Task InitializeJundiaiPersistenceAsync(this WebApplication app)
    {
        var runtime = app.Services.GetRequiredService<PersistenceRuntimeState>();
        if (!runtime.Configured) return;
        var migrate = app.Configuration.GetValue("Jundiai:Persistence:AutoMigrate", false);
        if (!migrate) return;

        var factory = app.Services.GetRequiredService<IDbContextFactory<JundiaiDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public static IEndpointRouteBuilder MapJundiaiPersistenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/persistence/readiness", async (PersistenceService service, CancellationToken ct) =>
            Results.Ok(await service.ReadinessAsync(ct)));

        endpoints.MapPost("/api/persistence/checkpoint", async (
            PersistenceCheckpointRequest request,
            HttpContext context,
            PersistenceService service,
            DemoStore demo,
            EvidenceLedgerStore evidence,
            IntegrationRegistryStore integrations,
            PocScenarioStore scenarios,
            SusBillingEngineStore billing,
            CancellationToken ct) =>
        {
            var scope = TenantContextMiddleware.GetScope(context);
            var result = await service.CreateCheckpointAsync(request, scope, demo, evidence, integrations, scenarios, billing, ct);
            return Results.Created($"/api/persistence/checkpoints/{result.CheckpointId}", result);
        });

        endpoints.MapGet("/api/persistence/outbox", async (PersistenceService service, CancellationToken ct) =>
            Results.Ok(await service.OutboxAsync(ct)));

        endpoints.MapPost("/api/persistence/outbox", async (CreateOutboxMessageRequest request, HttpContext context, PersistenceService service, CancellationToken ct) =>
        {
            var scope = TenantContextMiddleware.GetScope(context);
            return Results.Created("/api/persistence/outbox", await service.EnqueueOutboxAsync(request, scope, ct));
        });

        endpoints.MapPost("/api/persistence/outbox/{id:guid}/processed", async (Guid id, PersistenceService service, CancellationToken ct) =>
            Results.Ok(await service.MarkOutboxProcessedAsync(id, ct)));

        return endpoints;
    }
}

public sealed class JundiaiDbContext(DbContextOptions<JundiaiDbContext> options) : DbContext(options)
{
    public DbSet<DurableEnvelope> DurableEnvelopes => Set<DurableEnvelope>();
    public DbSet<IntegrationOutboxMessage> IntegrationOutbox => Set<IntegrationOutboxMessage>();
    public DbSet<PersistedIdempotencyKey> IdempotencyKeys => Set<PersistedIdempotencyKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DurableEnvelope>(entity =>
        {
            entity.ToTable("platform_envelopes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.InstitutionId).HasMaxLength(120).IsRequired();
            entity.Property(x => x.HealthUnitId).HasMaxLength(120);
            entity.Property(x => x.Kind).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ResourceId).HasMaxLength(160).IsRequired();
            entity.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(x => new { x.InstitutionId, x.Kind, x.ResourceId });
            entity.HasIndex(x => x.CheckpointId);
            entity.HasIndex(x => x.OccurredAt);
        });

        modelBuilder.Entity<IntegrationOutboxMessage>(entity =>
        {
            entity.ToTable("integration_outbox");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.InstitutionId).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Type).HasMaxLength(120).IsRequired();
            entity.Property(x => x.IdempotencyKey).HasMaxLength(180).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(32).IsRequired();
            entity.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(x => new { x.InstitutionId, x.IdempotencyKey }).IsUnique();
            entity.HasIndex(x => new { x.Status, x.CreatedAt });
        });

        modelBuilder.Entity<PersistedIdempotencyKey>(entity =>
        {
            entity.ToTable("idempotency_keys");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.InstitutionId).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Scope).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Key).HasMaxLength(180).IsRequired();
            entity.Property(x => x.ResponseHash).HasMaxLength(64);
            entity.HasIndex(x => new { x.InstitutionId, x.Scope, x.Key }).IsUnique();
            entity.HasIndex(x => x.ExpiresAt);
        });
    }
}

public sealed class PersistenceService(IServiceProvider services, PersistenceRuntimeState runtime)
{
    public async Task<object> ReadinessAsync(CancellationToken ct)
    {
        if (!runtime.Configured)
            return new
            {
                provider = runtime.Provider,
                configured = false,
                canConnect = false,
                mode = runtime.Mode,
                pendingMigrations = Array.Empty<string>(),
                productionTarget = "PostgreSQL + migrations + tenant/institution scope + outbox/idempotency",
                note = "A POC continua funcional em memória. Configure ConnectionStrings:Jundiai para ativar persistência durável."
            };

        try
        {
            var factory = services.GetRequiredService<IDbContextFactory<JundiaiDbContext>>();
            await using var db = await factory.CreateDbContextAsync(ct);
            var canConnect = await db.Database.CanConnectAsync(ct);
            var pending = canConnect ? (await db.Database.GetPendingMigrationsAsync(ct)).ToArray() : Array.Empty<string>();
            return new
            {
                provider = runtime.Provider,
                configured = true,
                canConnect,
                mode = canConnect ? "durable-postgresql" : "configured-unreachable",
                pendingMigrations = pending,
                autoMigrate = false,
                schema = new[] { "platform_envelopes", "integration_outbox", "idempotency_keys" }
            };
        }
        catch (Exception ex)
        {
            return new
            {
                provider = runtime.Provider,
                configured = true,
                canConnect = false,
                mode = "configured-error",
                error = ex.GetType().Name,
                note = "Detalhes de conexão não são expostos pela API."
            };
        }
    }

    public async Task<PersistenceCheckpointResult> CreateCheckpointAsync(
        PersistenceCheckpointRequest request,
        TenantScope scope,
        DemoStore demo,
        EvidenceLedgerStore evidence,
        IntegrationRegistryStore integrations,
        PocScenarioStore scenarios,
        SusBillingEngineStore billing,
        CancellationToken ct)
    {
        var factory = RequiredFactory();
        var checkpointId = Guid.NewGuid();
        var label = string.IsNullOrWhiteSpace(request.Label) ? "manual-checkpoint" : request.Label.Trim();
        var snapshots = new (string Kind, string ResourceId, object Payload)[]
        {
            ("citizens", "all", demo.Citizens()),
            ("audit", "recent", demo.Audit()),
            ("evidence-ledger", "all", evidence.Events()),
            ("integrations", "registry", integrations.All()),
            ("poc-scenarios", "all", scenarios.Runs()),
            ("sus-production-v2", "all", billing.Production()),
            ("sus-batches-v2", "all", billing.Batches())
        };

        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var snapshot in snapshots)
        {
            var json = JsonSerializer.Serialize(snapshot.Payload, JsonOptions);
            db.DurableEnvelopes.Add(new DurableEnvelope
            {
                Id = Guid.NewGuid(),
                CheckpointId = checkpointId,
                InstitutionId = scope.InstitutionId,
                HealthUnitId = scope.HealthUnitId,
                Kind = snapshot.Kind,
                ResourceId = snapshot.ResourceId,
                PayloadJson = json,
                ContentHash = Sha256(json),
                OccurredAt = now,
                Label = label
            });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new PersistenceCheckpointResult(checkpointId, scope.InstitutionId, scope.HealthUnitId, snapshots.Length, label, now);
    }

    public async Task<IReadOnlyList<object>> OutboxAsync(CancellationToken ct)
    {
        var factory = RequiredFactory();
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.IntegrationOutbox.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(200)
            .Select(x => (object)new { x.Id, x.InstitutionId, x.Type, x.IdempotencyKey, x.Status, x.Attempts, x.CreatedAt, x.ProcessedAt })
            .ToListAsync(ct);
    }

    public async Task<object> EnqueueOutboxAsync(CreateOutboxMessageRequest request, TenantScope scope, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Type) || string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new ArgumentException("Type e IdempotencyKey são obrigatórios.");
        var factory = RequiredFactory();
        await using var db = await factory.CreateDbContextAsync(ct);
        var existing = await db.IntegrationOutbox.AsNoTracking().FirstOrDefaultAsync(x =>
            x.InstitutionId == scope.InstitutionId && x.IdempotencyKey == request.IdempotencyKey.Trim(), ct);
        if (existing is not null)
            return new { existing.Id, existing.Type, existing.IdempotencyKey, existing.Status, idempotentReplay = true };

        var message = new IntegrationOutboxMessage
        {
            Id = Guid.NewGuid(),
            InstitutionId = scope.InstitutionId,
            Type = request.Type.Trim(),
            IdempotencyKey = request.IdempotencyKey.Trim(),
            PayloadJson = JsonSerializer.Serialize(request.Payload ?? new { }, JsonOptions),
            Status = "pending",
            Attempts = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.IntegrationOutbox.Add(message);
        await db.SaveChangesAsync(ct);
        return new { message.Id, message.Type, message.IdempotencyKey, message.Status, idempotentReplay = false };
    }

    public async Task<object> MarkOutboxProcessedAsync(Guid id, CancellationToken ct)
    {
        var factory = RequiredFactory();
        await using var db = await factory.CreateDbContextAsync(ct);
        var message = await db.IntegrationOutbox.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
        message.Status = "processed";
        message.ProcessedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return new { message.Id, message.Status, message.ProcessedAt };
    }

    private IDbContextFactory<JundiaiDbContext> RequiredFactory()
    {
        if (!runtime.Configured) throw new InvalidOperationException("Persistência PostgreSQL não está configurada nesta instância da POC.");
        return services.GetRequiredService<IDbContextFactory<JundiaiDbContext>>();
    }

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var institution = context.Request.Headers["X-Institution-Id"].FirstOrDefault();
        var unit = context.Request.Headers["X-Health-Unit-Id"].FirstOrDefault();
        context.Items["jundiai.tenant"] = new TenantScope(
            string.IsNullOrWhiteSpace(institution) ? "jundiai-demo" : institution.Trim(),
            string.IsNullOrWhiteSpace(unit) ? null : unit.Trim());
        await next(context);
    }

    public static TenantScope GetScope(HttpContext context) =>
        context.Items["jundiai.tenant"] as TenantScope ?? new TenantScope("jundiai-demo", null);
}

public sealed record TenantScope(string InstitutionId, string? HealthUnitId);
public sealed record PersistenceRuntimeState(bool Configured, string Provider, string Mode);
public sealed record PersistenceCheckpointRequest(string? Label);
public sealed record PersistenceCheckpointResult(Guid CheckpointId, string InstitutionId, string? HealthUnitId, int EnvelopeCount, string Label, DateTimeOffset CreatedAt);
public sealed record CreateOutboxMessageRequest(string Type, string IdempotencyKey, object? Payload);

public sealed class DurableEnvelope
{
    public Guid Id { get; set; }
    public Guid CheckpointId { get; set; }
    public string InstitutionId { get; set; } = string.Empty;
    public string? HealthUnitId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string ContentHash { get; set; } = string.Empty;
    public string? Label { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class IntegrationOutboxMessage
{
    public Guid Id { get; set; }
    public string InstitutionId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

public sealed class PersistedIdempotencyKey
{
    public Guid Id { get; set; }
    public string InstitutionId { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string? ResponseHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
