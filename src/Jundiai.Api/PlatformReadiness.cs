namespace Jundiai.Api;

public static class PlatformReadinessEndpoints
{
    public static IEndpointRouteBuilder MapPlatformReadinessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/contract/platform/readiness", () => Results.Ok(PlatformReadinessModel.Build()));
        endpoints.MapGet("/api/platform/readiness", () => Results.Ok(PlatformReadinessModel.Build()));
        endpoints.MapGet("/api/contract/platform/architecture", () => Results.Ok(new
        {
            application = ".NET 8 ASP.NET Core modular monolith POC",
            targetPersistence = "PostgreSQL EF Core with migrations, transactions and tenant/institution scoping",
            targetCaching = "Redis for ephemeral coordination/cache only; never source of truth for clinical records",
            integrationPattern = "versioned adapters + transactional outbox/inbox + idempotency keys",
            audit = "immutable-oriented event/evidence retention with production-grade storage policy",
            deployment = "containerized, health probes, rolling/blue-green compatible",
            security = "external IdP/MFA, least privilege, secret manager, TLS, certificate lifecycle",
            resilience = "backup, point-in-time recovery, restore drills, DR runbook, dependency circuit breaking",
            observability = "structured logs, traces, metrics, SLO/SLA dashboards, security/audit alerts"
        }));
        return endpoints;
    }
}

public static class PlatformReadinessModel
{
    public static object Build() => new
    {
        currentPoc = new
        {
            persistence = "in-memory domain runtime with validated PostgreSQL EF Core foundation, migrations, full-domain checkpoints, manifests, outbox/idempotency and tenant scope",
            authentication = "in-memory demo identity/session + MFA demonstration",
            secrets = "no production secrets required or committed",
            deployment = "Docker-ready with liveness/readiness probes",
            evidence = "SHA-256 evidence chain plus durable PostgreSQL checkpoint/recovery bridge when configured",
            recovery = "full-domain checkpoint + manifest verification + restore preview/recovery drill; not production PITR/failover",
            observability = "correlation ID, endpoint-group telemetry, 5xx count and latency aggregation",
            privacy = "purpose/minimization policy, break-glass, revocation and subject export demonstration",
            status = "POC"
        },
        productionGates = new[]
        {
            Gate("PERSIST-01", "PostgreSQL foundation", "foundation_implemented", "DbContext, tenant scope, migration, durable full-domain checkpoint and recovery bridge implemented; production must migrate domain stores fully."),
            Gate("PERSIST-02", "Migrations and data retention", "migration_foundation", "Versioned migration exists; archival, retention, partitioning and full domain relational schema remain production work."),
            Gate("SEC-01", "Production IdP/MFA", "required", "Integrate institutional identity provider and production-grade second factor/session lifecycle."),
            Gate("SEC-02", "Secrets and certificates", "required", "Store credentials/certificates exclusively in managed secret storage; never source control."),
            Gate("OBS-01", "Observability", "foundation_implemented", "Correlation ID and operational telemetry are implemented; production still requires centralized logs/traces/metrics, alert routing, SLOs and SOC integration."),
            Gate("DR-01", "Backup/PITR/restore", "recovery_foundation", "POC validates full-domain checkpoints, SHA-256 manifests and restore preview. Managed backups, PITR and isolated restoration are still required."),
            Gate("DR-02", "Disaster recovery", "drill_poc", "Recovery drill exists at data-envelope level; contractual RTO/RPO, infrastructure failover, network/dependency recovery and scheduled drills remain production work."),
            Gate("INT-01", "Outbox/idempotency", "foundation_implemented", "Transactional outbox and persisted idempotency schema implemented; workers/inbox for each external adapter remain."),
            Gate("PERF-01", "Load/capacity testing", "required", "Validate concurrency and volumetry against municipal estimates before go-live."),
            Gate("LGPD-01", "Privacy lifecycle", "foundation_implemented", "Purpose/minimization, break-glass, revocation and subject export exist in POC; retention/legal basis governance and incident process remain production work."),
            Gate("DEPLOY-01", "Production deployment", "partial", "Container, health probes and environment boundaries exist; release approvals, managed runtime, rollback evidence and infrastructure hardening remain."),
            Gate("E2E-01", "Browser E2E", "partial", "Large smoke covers API journeys and page availability; real browser interaction automation remains to be added.")
        },
        rule = "Uma fundação implementada reduz risco técnico, mas não transforma automaticamente a POC em produção."
    };

    private static object Gate(string id, string name, string status, string description) => new { id, name, status, description };
}
