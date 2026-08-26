using System.Text.Json;

namespace Jundiai.Api;

public static class JundiaiRoles
{
    public const string PocAdmin = "poc_admin";
    public const string MunicipalManager = "municipal_manager";
    public const string Regulator = "regulator";
    public const string Auditor = "auditor";
    public const string Clinician = "clinician";
    public const string Nurse = "nurse";
    public const string Pharmacist = "pharmacist";
    public const string Dentist = "dentist";
    public const string Acs = "acs";
    public const string Support = "support";
    public const string Anonymous = "anonymous";
}

public static class JundiaiPermissions
{
    public const string CitizenRead = "citizen.read";
    public const string ClinicalRead = "clinical.read";
    public const string ClinicalWrite = "clinical.write";
    public const string RegulationRead = "regulation.read";
    public const string RegulationManage = "regulation.manage";
    public const string ReferralRead = "referral.read";
    public const string ReferralWrite = "referral.write";
    public const string SchedulingRead = "scheduling.read";
    public const string SchedulingManage = "scheduling.manage";
    public const string BillingRead = "billing.read";
    public const string BillingManage = "billing.manage";
    public const string ImmunizationRead = "immunization.read";
    public const string ImmunizationWrite = "immunization.write";
    public const string InventoryRead = "inventory.read";
    public const string InventoryManage = "inventory.manage";
    public const string PsfRead = "psf.read";
    public const string PsfWrite = "psf.write";
    public const string DentalRead = "dental.read";
    public const string DentalWrite = "dental.write";
    public const string DiagnosticsRead = "diagnostics.read";
    public const string DiagnosticsWrite = "diagnostics.write";
    public const string TelemedicineRead = "telemedicine.read";
    public const string TelemedicineWrite = "telemedicine.write";
    public const string DocumentsRead = "documents.read";
    public const string DocumentsWrite = "documents.write";
    public const string AuditRead = "audit.read";
    public const string AuditWrite = "audit.write";
    public const string EvidenceRead = "evidence.read";
    public const string EvidenceWrite = "evidence.write";
    public const string ContractRead = "contract.read";
    public const string SecurityRead = "security.read";
    public const string AiRead = "ai.read";
    public const string AiManage = "ai.manage";
    public const string MigrationRead = "migration.read";
    public const string MigrationManage = "migration.manage";
    public const string IntegrationRead = "integration.read";
    public const string IntegrationManage = "integration.manage";
    public const string OperationsRead = "operations.read";
    public const string OperationsManage = "operations.manage";
    public const string AnalyticsRead = "analytics.read";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        CitizenRead, ClinicalRead, ClinicalWrite,
        RegulationRead, RegulationManage, ReferralRead, ReferralWrite, SchedulingRead, SchedulingManage,
        BillingRead, BillingManage, ImmunizationRead, ImmunizationWrite, InventoryRead, InventoryManage,
        PsfRead, PsfWrite, DentalRead, DentalWrite, DiagnosticsRead, DiagnosticsWrite,
        TelemedicineRead, TelemedicineWrite, DocumentsRead, DocumentsWrite,
        AuditRead, AuditWrite, EvidenceRead, EvidenceWrite, ContractRead, SecurityRead,
        AiRead, AiManage, MigrationRead, MigrationManage, IntegrationRead, IntegrationManage,
        OperationsRead, OperationsManage, AnalyticsRead
    };
}

public static class JundiaiPermissionCatalog
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> RolePermissions =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [JundiaiRoles.PocAdmin] = new HashSet<string>(JundiaiPermissions.All, StringComparer.OrdinalIgnoreCase),
            [JundiaiRoles.MunicipalManager] = Set(
                JundiaiPermissions.CitizenRead,
                JundiaiPermissions.RegulationRead, JundiaiPermissions.RegulationManage,
                JundiaiPermissions.ReferralRead, JundiaiPermissions.ReferralWrite,
                JundiaiPermissions.SchedulingRead, JundiaiPermissions.SchedulingManage,
                JundiaiPermissions.BillingRead, JundiaiPermissions.BillingManage,
                JundiaiPermissions.ImmunizationRead,
                JundiaiPermissions.InventoryRead, JundiaiPermissions.InventoryManage,
                JundiaiPermissions.PsfRead, JundiaiPermissions.DentalRead,
                JundiaiPermissions.DiagnosticsRead, JundiaiPermissions.TelemedicineRead,
                JundiaiPermissions.DocumentsRead, JundiaiPermissions.AuditRead,
                JundiaiPermissions.EvidenceRead, JundiaiPermissions.ContractRead, JundiaiPermissions.SecurityRead,
                JundiaiPermissions.AiRead,
                JundiaiPermissions.MigrationRead, JundiaiPermissions.MigrationManage,
                JundiaiPermissions.IntegrationRead, JundiaiPermissions.IntegrationManage,
                JundiaiPermissions.OperationsRead, JundiaiPermissions.OperationsManage,
                JundiaiPermissions.AnalyticsRead),
            [JundiaiRoles.Regulator] = Set(
                JundiaiPermissions.CitizenRead, JundiaiPermissions.ClinicalRead,
                JundiaiPermissions.RegulationRead, JundiaiPermissions.RegulationManage,
                JundiaiPermissions.ReferralRead, JundiaiPermissions.ReferralWrite,
                JundiaiPermissions.SchedulingRead, JundiaiPermissions.SchedulingManage,
                JundiaiPermissions.DiagnosticsRead,
                JundiaiPermissions.TelemedicineRead, JundiaiPermissions.TelemedicineWrite,
                JundiaiPermissions.AnalyticsRead),
            [JundiaiRoles.Auditor] = Set(
                JundiaiPermissions.CitizenRead, JundiaiPermissions.ClinicalRead,
                JundiaiPermissions.RegulationRead, JundiaiPermissions.ReferralRead, JundiaiPermissions.SchedulingRead,
                JundiaiPermissions.BillingRead, JundiaiPermissions.ImmunizationRead,
                JundiaiPermissions.InventoryRead, JundiaiPermissions.PsfRead,
                JundiaiPermissions.DentalRead, JundiaiPermissions.DiagnosticsRead,
                JundiaiPermissions.TelemedicineRead, JundiaiPermissions.DocumentsRead,
                JundiaiPermissions.AuditRead, JundiaiPermissions.EvidenceRead,
                JundiaiPermissions.ContractRead, JundiaiPermissions.SecurityRead,
                JundiaiPermissions.AiRead, JundiaiPermissions.MigrationRead,
                JundiaiPermissions.IntegrationRead, JundiaiPermissions.OperationsRead,
                JundiaiPermissions.AnalyticsRead),
            [JundiaiRoles.Clinician] = Set(
                JundiaiPermissions.CitizenRead,
                JundiaiPermissions.ClinicalRead, JundiaiPermissions.ClinicalWrite,
                JundiaiPermissions.RegulationRead, JundiaiPermissions.ReferralRead, JundiaiPermissions.ReferralWrite, JundiaiPermissions.SchedulingRead,
                JundiaiPermissions.DiagnosticsRead, JundiaiPermissions.DiagnosticsWrite,
                JundiaiPermissions.TelemedicineRead, JundiaiPermissions.TelemedicineWrite,
                JundiaiPermissions.DocumentsRead, JundiaiPermissions.DocumentsWrite,
                JundiaiPermissions.AiRead, JundiaiPermissions.AiManage),
            [JundiaiRoles.Nurse] = Set(
                JundiaiPermissions.CitizenRead,
                JundiaiPermissions.ClinicalRead, JundiaiPermissions.ClinicalWrite,
                JundiaiPermissions.ReferralRead, JundiaiPermissions.ReferralWrite,
                JundiaiPermissions.SchedulingRead,
                JundiaiPermissions.ImmunizationRead, JundiaiPermissions.ImmunizationWrite,
                JundiaiPermissions.PsfRead, JundiaiPermissions.DiagnosticsRead,
                JundiaiPermissions.DocumentsRead, JundiaiPermissions.DocumentsWrite,
                JundiaiPermissions.AiRead),
            [JundiaiRoles.Pharmacist] = Set(
                JundiaiPermissions.CitizenRead,
                JundiaiPermissions.InventoryRead, JundiaiPermissions.InventoryManage,
                JundiaiPermissions.DocumentsRead, JundiaiPermissions.OperationsRead),
            [JundiaiRoles.Dentist] = Set(
                JundiaiPermissions.CitizenRead,
                JundiaiPermissions.ClinicalRead, JundiaiPermissions.ClinicalWrite,
                JundiaiPermissions.ReferralRead, JundiaiPermissions.ReferralWrite,
                JundiaiPermissions.SchedulingRead,
                JundiaiPermissions.DentalRead, JundiaiPermissions.DentalWrite,
                JundiaiPermissions.DiagnosticsRead, JundiaiPermissions.DiagnosticsWrite,
                JundiaiPermissions.TelemedicineRead, JundiaiPermissions.TelemedicineWrite,
                JundiaiPermissions.DocumentsRead, JundiaiPermissions.DocumentsWrite,
                JundiaiPermissions.AiRead, JundiaiPermissions.AiManage),
            [JundiaiRoles.Acs] = Set(
                JundiaiPermissions.CitizenRead,
                JundiaiPermissions.PsfRead, JundiaiPermissions.PsfWrite,
                JundiaiPermissions.SchedulingRead),
            [JundiaiRoles.Support] = Set(
                JundiaiPermissions.RegulationRead, JundiaiPermissions.ReferralRead, JundiaiPermissions.SchedulingRead,
                JundiaiPermissions.DiagnosticsRead, JundiaiPermissions.TelemedicineRead,
                JundiaiPermissions.AuditRead, JundiaiPermissions.ContractRead,
                JundiaiPermissions.IntegrationRead, JundiaiPermissions.MigrationRead,
                JundiaiPermissions.OperationsRead, JundiaiPermissions.OperationsManage,
                JundiaiPermissions.AnalyticsRead)
        };

    public static bool RoleGrants(string role, string permission) =>
        RolePermissions.TryGetValue(role, out var permissions) && permissions.Contains(permission);

    public static IReadOnlyDictionary<string, IReadOnlySet<string>> Snapshot() => RolePermissions;

    private static IReadOnlySet<string> Set(params string[] permissions) =>
        new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
}

public static class DemoAccessControlExtensions
{
    public static IApplicationBuilder UseJundiaiDemoAccessControl(this IApplicationBuilder app) =>
        app.UseMiddleware<DemoAccessControlMiddleware>();
}

public sealed class DemoAccessControlMiddleware(RequestDelegate next)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static bool DemoRoleHeaderEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("JUNDIAI_ALLOW_DEMO_ROLE_HEADER"), "true", StringComparison.OrdinalIgnoreCase);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api") ||
            context.Request.Path.StartsWithSegments("/api/health") ||
            context.Request.Path.StartsWithSegments("/api/citizen") ||
            context.Request.Path.StartsWithSegments("/api/auth"))
        {
            await next(context);
            return;
        }

        var role = ResolveRole(context);
        var identity = DemoAuthenticationMiddleware.GetIdentity(context);

        if (string.Equals(role, JundiaiRoles.Anonymous, StringComparison.OrdinalIgnoreCase))
        {
            await Unauthorized(context, "Autenticação necessária", "A rota protegida exige uma sessão POC válida. Cabeçalhos de papel demonstrativo ficam desabilitados por padrão.");
            return;
        }

        if (context.Request.Path.StartsWithSegments("/api/access/context"))
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                role,
                authenticated = identity is not null,
                identity = identity?.UserName,
                demoRoleHeaderEnabled = DemoRoleHeaderEnabled,
                permissions = JundiaiPermissionCatalog.Snapshot().TryGetValue(role, out var permissions)
                    ? permissions.OrderBy(x => x).ToArray()
                    : Array.Empty<string>()
            }, Json));
            return;
        }

        var required = ResolveRequiredPermission(context.Request);
        if (required is null)
        {
            await Deny(context, "Endpoint sem política de acesso", $"A rota '{context.Request.Path}' não possui política explícita e foi bloqueada por padrão.", null, role);
            return;
        }
        if (!JundiaiPermissionCatalog.RoleGrants(role, required))
        {
            await Deny(context, "Acesso negado", $"O papel '{role}' não possui a permissão '{required}'.", required, role);
            return;
        }

        context.Items["jundiai.role"] = role;
        context.Items["jundiai.permission"] = required;
        await next(context);
    }

    private static string ResolveRole(HttpContext context)
    {
        if (context.Items["jundiai.auth.role"] is string authenticatedRole && !string.IsNullOrWhiteSpace(authenticatedRole))
            return authenticatedRole;

        if (DemoRoleHeaderEnabled)
        {
            var headerRole = context.Request.Headers["X-Demo-Role"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerRole)) return headerRole.Trim();
        }

        return JundiaiRoles.Anonymous;
    }

    private static async Task Unauthorized(HttpContext context, string title, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers.WWWAuthenticate = "Bearer realm=\"Jundiai HealthOS POC\"";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = "https://jundiai-healthos.local/problems/unauthorized",
            title,
            status = 401,
            detail,
            role = JundiaiRoles.Anonymous
        }, Json));
    }

    private static async Task Deny(HttpContext context, string title, string detail, string? requiredPermission, string role)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = requiredPermission is null ? "https://jundiai-healthos.local/problems/unmapped-permission" : "https://jundiai-healthos.local/problems/forbidden",
            title,
            status = 403,
            detail,
            requiredPermission,
            role
        }, Json));
    }

    private static string? ResolveRequiredPermission(HttpRequest request)
    {
        var write = !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method);
        var path = request.Path;

        if (path.StartsWithSegments("/api/access/context")) return JundiaiPermissions.CitizenRead;
        if (path.StartsWithSegments("/api/security")) return JundiaiPermissions.SecurityRead;
        if (path.StartsWithSegments("/api/contract") || path.StartsWithSegments("/api/platform") || path.StartsWithSegments("/api/poc")) return JundiaiPermissions.ContractRead;
        if (path.StartsWithSegments("/api/evidence")) return write ? JundiaiPermissions.EvidenceWrite : JundiaiPermissions.EvidenceRead;
        if (path.StartsWithSegments("/api/ai")) return write ? JundiaiPermissions.AiManage : JundiaiPermissions.AiRead;
        if (path.StartsWithSegments("/api/migration")) return write ? JundiaiPermissions.MigrationManage : JundiaiPermissions.MigrationRead;
        if (path.StartsWithSegments("/api/integrations")) return write ? JundiaiPermissions.IntegrationManage : JundiaiPermissions.IntegrationRead;
        if (path.StartsWithSegments("/api/operations")) return write ? JundiaiPermissions.OperationsManage : JundiaiPermissions.OperationsRead;
        if (path.StartsWithSegments("/api/professionals")) return write ? JundiaiPermissions.OperationsManage : JundiaiPermissions.OperationsRead;
        if (path.StartsWithSegments("/api/analytics")) return JundiaiPermissions.AnalyticsRead;
        if (path.StartsWithSegments("/api/dashboard") || path.StartsWithSegments("/api/citizens") || path.StartsWithSegments("/api/units")) return JundiaiPermissions.CitizenRead;
        if (path.StartsWithSegments("/api/care-trace")) return JundiaiPermissions.ClinicalRead;
        if (path.StartsWithSegments("/api/documents")) return write ? JundiaiPermissions.DocumentsWrite : JundiaiPermissions.DocumentsRead;
        if (path.StartsWithSegments("/api/clinical") || path.StartsWithSegments("/api/ubs") || path.StartsWithSegments("/api/records")) return write ? JundiaiPermissions.ClinicalWrite : JundiaiPermissions.ClinicalRead;
        if (path.StartsWithSegments("/api/referrals")) return write ? JundiaiPermissions.ReferralWrite : JundiaiPermissions.ReferralRead;
        if (path.StartsWithSegments("/api/regulation")) return write ? JundiaiPermissions.RegulationManage : JundiaiPermissions.RegulationRead;
        if (path.StartsWithSegments("/api/scheduling")) return write ? JundiaiPermissions.SchedulingManage : JundiaiPermissions.SchedulingRead;
        if (path.StartsWithSegments("/api/telemedicine")) return write ? JundiaiPermissions.TelemedicineWrite : JundiaiPermissions.TelemedicineRead;
        if (path.StartsWithSegments("/api/sus")) return write ? JundiaiPermissions.BillingManage : JundiaiPermissions.BillingRead;
        if (path.StartsWithSegments("/api/immunization")) return write ? JundiaiPermissions.ImmunizationWrite : JundiaiPermissions.ImmunizationRead;
        if (path.StartsWithSegments("/api/pharmacy") || path.StartsWithSegments("/api/warehouse") || path.StartsWithSegments("/api/inventory")) return write ? JundiaiPermissions.InventoryManage : JundiaiPermissions.InventoryRead;
        if (path.StartsWithSegments("/api/psf")) return write ? JundiaiPermissions.PsfWrite : JundiaiPermissions.PsfRead;
        if (path.StartsWithSegments("/api/dental")) return write ? JundiaiPermissions.DentalWrite : JundiaiPermissions.DentalRead;
        if (path.StartsWithSegments("/api/diagnostics")) return write ? JundiaiPermissions.DiagnosticsWrite : JundiaiPermissions.DiagnosticsRead;
        if (path.StartsWithSegments("/api/audit")) return write ? JundiaiPermissions.AuditWrite : JundiaiPermissions.AuditRead;
        return null;
    }
}