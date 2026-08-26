using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Jundiai.Api;

public static class IntegrationMessagingPersistenceEndpoints
{
    public static IEndpointRouteBuilder MapIntegrationMessagingPersistenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/audit/persistence/inbox", async (
            AcceptIntegrationInboxRequest request,
            HttpContext context,
            IntegrationMessagingPersistenceService service,
            EvidenceLedgerStore evidence,
            CancellationToken ct) =>
        {
            var scope = TenantContextMiddleware.GetScope(context);
            var result = await service.AcceptInboxAsync(request, scope, ct);
            evidence.Append(new CreateEvidenceEventRequest(
                request.Actor ?? "integration.inbox",
                result.Duplicate ? "integration.inbox.duplicate" : "integration.inbox.accept",
                $"inbox:{request.Type}:{request.MessageId}",
                "POC-B14",
                $"duplicate={result.Duplicate};hash={result.PayloadSha256}",
                "integration-inbox"));
            if (result.Duplicate) return Results.Ok(result);
            return Results.Accepted($"/api/audit/persistence/inbox/{result.ReceiptId}", result);
        });

        endpoints.MapGet("/api/audit/persistence/inbox", async (
            int? limit,
            HttpContext context,
            IntegrationMessagingPersistenceService service,
            CancellationToken ct) =>
            Results.Ok(await service.InboxReceiptsAsync(TenantContextMiddleware.GetScope(context), limit ?? 100, ct)));

        endpoints.MapGet("/api/audit/persistence/outbox/pending", async (
            int? limit,
            HttpContext context,
            IntegrationMessagingPersistenceService service,
            CancellationToken ct) =>
            Results.Ok(await service.PendingOutboxAsync(TenantContextMiddleware.GetScope(context), limit ?? 100, ct)));

        endpoints.MapPost("/api/audit/persistence/outbox/{id:guid}/failure", async (
            Guid id,
            OutboxFailureRequest request,
            HttpContext context,
            IntegrationMessagingPersistenceService service,
            EvidenceLedgerStore evidence,
            CancellationToken ct) =>
        {
            var scope = TenantContextMiddleware.GetScope(context);
            var result = await service.RecordOutboxFailureAsync(id, request, scope, ct);
            evidence.Append(new CreateEvidenceEventRequest(
                request.Actor ?? "integration.outbox",
                "integration.outbox.failure",
                $"outbox:{id}",
                "POC-B14",
                $"status={result.Status};attempts={result.Attempts};errorCode={request.ErrorCode}",
                "integration-delivery"));
            return Results.Ok(result);
        });

        endpoints.MapPost("/api/audit/persistence/outbox/{id:guid}/requeue", async (
            Guid id,
            RequeueOutboxRequest request,
            HttpContext context,
            IntegrationMessagingPersistenceService service,
            EvidenceLedgerStore evidence,
            CancellationToken ct) =>
        {
            var scope = TenantContextMiddleware.GetScope(context);
            var result = await service.RequeueAsync(id, request, scope, ct);
            evidence.Append(new CreateEvidenceEventRequest(
                request.Actor,
                "integration.outbox.requeue",
                $"outbox:{id}",
                "POC-B14",
                request.Reason,
                "integration-delivery"));
            return Results.Ok(result);
        });

        endpoints.MapGet("/api/audit/persistence/messaging/readiness", async (
            HttpContext context,
            IntegrationMessagingPersistenceService service,
            CancellationToken ct) =>
            Results.Ok(await service.ReadinessAsync(TenantContextMiddleware.GetScope(context), ct)));

        return endpoints;
    }
}

public sealed class IntegrationMessagingPersistenceService(IServiceProvider services, PersistenceRuntimeState runtime)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<InboxReceiptResult> AcceptInboxAsync(AcceptIntegrationInboxRequest request, TenantScope tenant, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Type) || string.IsNullOrWhiteSpace(request.MessageId))
            throw new ArgumentException("Type e MessageId são obrigatórios.");
        var type = request.Type.Trim();
        var messageId = request.MessageId.Trim();
        var scope = $"integration-inbox:{type}";
        var factory = RequiredFactory();
        await using var db = await factory.CreateDbContextAsync(ct);

        var existing = await db.IdempotencyKeys.AsNoTracking().FirstOrDefaultAsync(x =>
            x.InstitutionId == tenant.InstitutionId && x.Scope == scope && x.Key == messageId, ct);
        if (existing is not null)
            return new InboxReceiptResult(existing.Id, tenant.InstitutionId, type, messageId, true, existing.ResponseHash ?? string.Empty, existing.CreatedAt);

        var payloadJson = JsonSerializer.Serialize(request.Payload ?? new { }, JsonOptions);
        var hash = DurableJson.Sha256Canonical(payloadJson);
        var now = DateTimeOffset.UtcNow;
        var receiptId = Guid.NewGuid();
        var retentionDays = Math.Clamp(request.IdempotencyRetentionDays ?? 30, 1, 3650);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.IdempotencyKeys.Add(new PersistedIdempotencyKey
        {
            Id = receiptId,
            InstitutionId = tenant.InstitutionId,
            Scope = scope,
            Key = messageId,
            ResponseHash = hash,
            CreatedAt = now,
            ExpiresAt = now.AddDays(retentionDays)
        });
        db.DurableEnvelopes.Add(new DurableEnvelope
        {
            Id = Guid.NewGuid(),
            CheckpointId = receiptId,
            InstitutionId = tenant.InstitutionId,
            HealthUnitId = tenant.HealthUnitId,
            Kind = "integration-inbox",
            ResourceId = $"{type}:{messageId}",
            PayloadJson = payloadJson,
            ContentHash = hash,
            OccurredAt = now,
            Label = request.Actor?.Trim() ?? "integration.inbox"
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new InboxReceiptResult(receiptId, tenant.InstitutionId, type, messageId, false, hash, now);
    }

    public async Task<IReadOnlyList<object>> InboxReceiptsAsync(TenantScope tenant, int limit, CancellationToken ct)
    {
        var factory = RequiredFactory();
        await using var db = await factory.CreateDbContextAsync(ct);
        var take = Math.Clamp(limit, 1, 500);
        return await db.DurableEnvelopes.AsNoTracking()
            .Where(x => x.InstitutionId == tenant.InstitutionId && x.Kind == "integration-inbox")
            .OrderByDescending(x => x.OccurredAt)
            .Take(take)
            .Select(x => (object)new { receiptId = x.CheckpointId, x.ResourceId, payloadSha256 = x.ContentHash, x.OccurredAt, actor = x.Label })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<object>> PendingOutboxAsync(TenantScope tenant, int limit, CancellationToken ct)
    {
        var factory = RequiredFactory();
        await using var db = await factory.CreateDbContextAsync(ct);
        var take = Math.Clamp(limit, 1, 500);
        return await db.IntegrationOutbox.AsNoTracking()
            .Where(x => x.InstitutionId == tenant.InstitutionId && (x.Status == "pending" || x.Status == "retry"))
            .OrderBy(x => x.CreatedAt)
            .Take(take)
            .Select(x => (object)new { x.Id, x.Type, x.IdempotencyKey, x.Status, x.Attempts, x.CreatedAt })
            .ToListAsync(ct);
    }

    public async Task<OutboxDeliveryState> RecordOutboxFailureAsync(Guid id, OutboxFailureRequest request, TenantScope tenant, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ErrorCode)) throw new ArgumentException("ErrorCode é obrigatório.");
        var factory = RequiredFactory();
        await using var db = await factory.CreateDbContextAsync(ct);
        var message = await db.IntegrationOutbox.FirstOrDefaultAsync(x => x.Id == id && x.InstitutionId == tenant.InstitutionId, ct)
            ?? throw new KeyNotFoundException();
        if (message.Status == "processed") throw new InvalidOperationException("Mensagem processada não pode registrar falha de entrega.");
        if (message.Status == "dead_letter") throw new InvalidOperationException("Mensagem em dead-letter exige requeue manual antes de nova tentativa.");

        var maxAttempts = Math.Clamp(request.MaxAttempts ?? 5, 1, 20);
        message.Attempts++;
        message.Status = message.Attempts >= maxAttempts ? "dead_letter" : "retry";
        var detail = JsonSerializer.Serialize(new
        {
            request.ErrorCode,
            errorClass = request.ErrorClass?.Trim(),
            request.Actor,
            message.Attempts,
            maxAttempts,
            status = message.Status,
            occurredAt = DateTimeOffset.UtcNow
        }, JsonOptions);
        db.DurableEnvelopes.Add(new DurableEnvelope
        {
            Id = Guid.NewGuid(),
            CheckpointId = message.Id,
            InstitutionId = tenant.InstitutionId,
            HealthUnitId = tenant.HealthUnitId,
            Kind = "integration-outbox-delivery",
            ResourceId = message.Id.ToString("N"),
            PayloadJson = detail,
            ContentHash = DurableJson.Sha256Canonical(detail),
            OccurredAt = DateTimeOffset.UtcNow,
            Label = message.Status
        });
        await db.SaveChangesAsync(ct);
        return new OutboxDeliveryState(message.Id, message.Status, message.Attempts, maxAttempts, request.ErrorCode.Trim(), DateTimeOffset.UtcNow);
    }

    public async Task<OutboxDeliveryState> RequeueAsync(Guid id, RequeueOutboxRequest request, TenantScope tenant, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Actor) || string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("Actor e Reason são obrigatórios para requeue.");
        var factory = RequiredFactory();
        await using var db = await factory.CreateDbContextAsync(ct);
        var message = await db.IntegrationOutbox.FirstOrDefaultAsync(x => x.Id == id && x.InstitutionId == tenant.InstitutionId, ct)
            ?? throw new KeyNotFoundException();
        if (message.Status != "dead_letter") throw new InvalidOperationException("Somente mensagem em dead-letter pode ser reenfileirada manualmente.");
        message.Status = "pending";
        var detail = JsonSerializer.Serialize(new { request.Actor, request.Reason, previousAttempts = message.Attempts, occurredAt = DateTimeOffset.UtcNow }, JsonOptions);
        db.DurableEnvelopes.Add(new DurableEnvelope
        {
            Id = Guid.NewGuid(), CheckpointId = message.Id, InstitutionId = tenant.InstitutionId, HealthUnitId = tenant.HealthUnitId,
            Kind = "integration-outbox-requeue", ResourceId = message.Id.ToString("N"), PayloadJson = detail,
            ContentHash = DurableJson.Sha256Canonical(detail), OccurredAt = DateTimeOffset.UtcNow, Label = "manual-requeue"
        });
        await db.SaveChangesAsync(ct);
        return new OutboxDeliveryState(message.Id, message.Status, message.Attempts, Math.Max(message.Attempts + 1, 5), "manual-requeue", DateTimeOffset.UtcNow);
    }

    public async Task<object> ReadinessAsync(TenantScope tenant, CancellationToken ct)
    {
        if (!runtime.Configured)
            return new { configured = false, inbox = false, outbox = false, idempotency = false, mode = runtime.Mode };
        var factory = RequiredFactory();
        await using var db = await factory.CreateDbContextAsync(ct);
        var inbox = await db.DurableEnvelopes.CountAsync(x => x.InstitutionId == tenant.InstitutionId && x.Kind == "integration-inbox", ct);
        var pending = await db.IntegrationOutbox.CountAsync(x => x.InstitutionId == tenant.InstitutionId && (x.Status == "pending" || x.Status == "retry"), ct);
        var deadLetter = await db.IntegrationOutbox.CountAsync(x => x.InstitutionId == tenant.InstitutionId && x.Status == "dead_letter", ct);
        return new
        {
            configured = true,
            inboxReceipts = inbox,
            pendingOutbox = pending,
            deadLetter,
            capabilities = new[] { "persisted inbox receipt", "inbox idempotency", "canonical JSONB SHA-256", "outbox retry state", "dead-letter", "manual requeue with reason", "tenant isolation" },
            productionGap = "Workers, backoff/jitter, broker/queue gerenciada, métricas de entrega e adapters externos reais devem ser definidos na implantação."
        };
    }

    private IDbContextFactory<JundiaiDbContext> RequiredFactory()
    {
        if (!runtime.Configured) throw new InvalidOperationException("Persistência PostgreSQL não está configurada nesta instância da POC.");
        return services.GetRequiredService<IDbContextFactory<JundiaiDbContext>>();
    }
}

public sealed record AcceptIntegrationInboxRequest(string Type, string MessageId, object? Payload, string? Actor, int? IdempotencyRetentionDays);
public sealed record InboxReceiptResult(Guid ReceiptId, string InstitutionId, string Type, string MessageId, bool Duplicate, string PayloadSha256, DateTimeOffset AcceptedAt);
public sealed record OutboxFailureRequest(string ErrorCode, string? ErrorClass, int? MaxAttempts, string? Actor);
public sealed record RequeueOutboxRequest(string Actor, string Reason);
public sealed record OutboxDeliveryState(Guid Id, string Status, int Attempts, int MaxAttempts, string LastCode, DateTimeOffset UpdatedAt);
