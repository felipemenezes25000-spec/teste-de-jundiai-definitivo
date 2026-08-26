namespace Jundiai.Api;

public static class PlatformReadinessEndpoints
{
    public static IEndpointRouteBuilder MapPlatformReadinessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/platform/readiness", () => Results.Ok(PlatformReadinessModel.Build()));
        endpoints.MapGet("/api/platform/architecture", () => Results.Ok(new
        {
            application = ".NET 8 ASP.NET Core modular monolith POC",
            targetPersistence = "PostgreSQL with migrations, transactions and tenant/institution scoping",
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
            persistence = "in-memory process state",
            authentication = "in-memory demo identity/session + MFA demonstration",
            secrets = "no production secrets required or committed",
            deployment = "Docker-ready",
            evidence = "in-memory SHA-256 chain",
            status = "POC"
        },
        productionGates = new[]
        {
            Gate("PERSIST-01", "PostgreSQL persistence", "required", "Migrate domain state from singleton in-memory stores to transactional repositories."),
            Gate("PERSIST-02", "Migrations and data retention", "required", "Versioned schema migrations, archival and retention policy for clinical/audit records."),
            Gate("SEC-01", "Production IdP/MFA", "required", "Integrate institutional identity provider and production-grade second factor/session lifecycle."),
            Gate("SEC-02", "Secrets and certificates", "required", "Store credentials/certificates exclusively in managed secret storage; never source control."),
            Gate("OBS-01", "Observability", "required", "Central logs, metrics, traces, alerting, correlation IDs and operational dashboards."),
            Gate("DR-01", "Backup/PITR/restore", "required", "Automated backups plus tested restoration and point-in-time recovery."),
            Gate("DR-02", "Disaster recovery", "required", "RTO/RPO contractually defined, runbook, failover and drills."),
            Gate("INT-01", "Outbox/inbox/idempotency", "required", "Reliable integration delivery and deduplication across external systems."),
            Gate("PERF-01", "Load/capacity testing", "required", "Validate concurrency and volumetry against municipal estimates before go-live."),
            Gate("LGPD-01", "Privacy lifecycle", "required", "Access purpose, retention, minimization, data subject workflows and incident response."),
            Gate("DEPLOY-01", "Production deployment", "required", "Environment isolation, release approvals, health probes, rollback and change evidence."),
            Gate("E2E-01", "Browser E2E", "required", "Automated browser journeys for the 14 POC blocks and critical production workflows.")
        },
        rule = "A readiness da POC não muda automaticamente o status de nenhum production gate."
    };

    private static object Gate(string id, string name, string status, string description) => new { id, name, status, description };
}
