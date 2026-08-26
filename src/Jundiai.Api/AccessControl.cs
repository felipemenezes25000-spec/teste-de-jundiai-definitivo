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
}

public static class JundiaiPermissions
{
    public const string CitizenRead = "citizen.read";
    public const string ClinicalRead = "clinical.read";
    public const string ClinicalWrite = "clinical.write";
    public const string RegulationRead = "regulation.read";
    public const string RegulationManage = "regulation.manage";
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

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        CitizenRead, ClinicalRead, ClinicalWrite,
        RegulationRead, RegulationManage, SchedulingRead, SchedulingManage,
        BillingRead, BillingManage, ImmunizationRead, ImmunizationWrite, InventoryRead, InventoryManage,
        PsfRead, PsfWrite, DentalRead, DentalWrite, DiagnosticsRead, DiagnosticsWrite,
        TelemedicineRead, TelemedicineWrite, DocumentsRead, DocumentsWrite,
        AuditRead, AuditWrite, EvidenceRead, EvidenceWrite, ContractRead, SecurityRead
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
                JundiaiPermissions.SchedulingRead, JundiaiPermissions.SchedulingManage,
                JundiaiPermissions.BillingRead, JundiaiPermissions.BillingManage,
                JundiaiPermissions.ImmunizationRead,
                JundiaiPermissions.InventoryRead, JundiaiPermissions.InventoryManage,
                JundiaiPermissions.PsfRead,
                JundiaiPermissions.DentalRead,
                JundiaiPermissions.DiagnosticsRead,
                JundiaiPermissions.TelemedicineRead,
                JundiaiPermissions.DocumentsRead,
                JundiaiPermissions.AuditRead,
                JundiaiPermissions.EvidenceRead,
                JundiaiPermissions.ContractRead,
                JundiaiPermissions.SecurityRead),
            [JundiaiRoles.Regulator] = Set(
                JundiaiPermissions.CitizenRead,
                JundiaiPermissions.RegulationRead, JundiaiPermissions.RegulationManage,
                JundiaiPermissions.SchedulingRead, JundiaiPermissions.SchedulingManage,
                JundiaiPermissions.ClinicalRead,
                JundiaiPermissions.DiagnosticsRead,
                JundiaiPermissions.TelemedicineRead,
                JundiaiPermissions.TelemedicineWrite),
            [JundiaiRoles.Auditor] = Set(
                JundiaiPermissions.CitizenRead,
                JundiaiPermissions.ClinicalRead,
                JundiaiPermissions.RegulationRead,
                JundiaiPermissions.SchedulingRead,
                JundiaiPermissions.BillingRead,
                JundiaiPermissions.ImmunizationRead,
                JundiaiPermissions.InventoryRead,
                JundiaiPermissions.PsfRead,
                JundiaiPermissions.DentalRead,
                JundiaiPermissions.DiagnosticsRead,
                JundiaiPermissions.TelemedicineRead,
                JundiaiPermissions.DocumentsRead,
                JundiaiPermissions.AuditRead,
                JundiaiPermissions.EvidenceRead,
                JundiaiPermissions.ContractRead,
                JundiaiPermissions.SecurityRead),
            [JundiaiRoles.Clinician] = Set(
                JundiaiPermissions.CitizenRead,
                JundiaiPermissions.ClinicalRead, JundiaiPermissions.ClinicalWrite,
                JundiaiPermissions.RegulationRead,
                JundiaiPermissions.SchedulingRead,
                JundiaiPermissions.DiagnosticsRead, JundiaiPermissions.DiagnosticsWrite,
                JundiaiPermissions.TelemedicineRead, JundiaiPermissions.TelemedicineWrite,
                JundiaiPermissions.DocumentsRead, JundiaiPermissions.DocumentsWrite),
            [JundiaiRoles.Nurse] = Set(
                JundiaiPermissions.CitizenRead,
                JundiaiPermissions.ClinicalRead, JundiaiPermissions.ClinicalWrite,
                JundiaiPermissions.SchedulingRead,
                JundiaiPermissions.ImmunizationRead, JundiaiPermissions.ImmunizationWrite,
                JundiaiPermissions.PsfRead,
                JundiaiPermissions.DiagnosticsRead,
                JundiaiPermissions.DocumentsRead, JundiaiPermissions.DocumentsWrite),
            [JundiaiRoles.Pharmacist] = Set(
                JundiaiPermissions.CitizenRead,
                JundiaiPermissions.InventoryRead, JundiaiPermissions.InventoryManage,
                JundiaiPermissions.DocumentsRead),
            [JundiaiRoles.Dentist] = Set(
                JundiaiPermissions.CitizenRead,
                JundiaiPermissions.ClinicalRead, JundiaiPermissions.ClinicalWrite,
                JundiaiPermissions.SchedulingRead,
                JundiaiPermissions.DentalRead, JundiaiPermissions.DentalWrite,
                JundiaiPermissions.DiagnosticsRead, JundiaiPermissions.DiagnosticsWrite,
                JundiaiPermissions.TelemedicineRead, JundiaiPermissions.TelemedicineWrite,
                JundiaiPermissions.DocumentsRead, JundiaiPermissions.DocumentsWrite),
            [JundiaiRoles.Acs] = Set(
                JundiaiPermissions.CitizenRead,
                JundiaiPermissions.PsfRead, JundiaiPermissions.PsfWrite,
                JundiaiPermissions.SchedulingRead),
            [JundiaiRoles.Support] = Set(
                JundiaiPermissions.RegulationRead,
                JundiaiPermissions.SchedulingRead,
                JundiaiPermissions.DiagnosticsRead,
                JundiaiPermissions.TelemedicineRead,
                JundiaiPermissions.AuditRead,
                JundiaiPermissions.ContractRead)
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

        if (context.Request.Path.StartsWithSegments("/api/access/context"))
        {
            var roleContext = ResolveRole(context);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                role = roleContext,
                authenticated = DemoAuthenticationMiddleware.GetIdentity(context) is not null,
                identity = DemoAuthenticationMiddleware.GetIdentity(context)?.UserName,
                permissions = JundiaiPermissionCatalog.Snapshot().TryGetValue(roleContext, out var permissions)
                    ? permissions.OrderBy(x => x).ToArray()
                    : Array.Empty<string>()
            }, Json));
            return;
        }

        var required = ResolveRequiredPermission(context.Request);
        if (required is null)
        {
            await Deny(context, "Endpoint sem política de acesso", $"A rota '{context.Request.Path}' não possui política explícita e foi bloqueada por padrão.", null, ResolveRole(context));
            return;
        }

        var role = ResolveRole(context);
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
        var role = context.Request.Headers["X-Demo-Role"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(role) ? JundiaiRoles.PocAdmin : role.Trim();
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
        if (path.StartsWithSegments("/api/contract")) return JundiaiPermissions.ContractRead;
        if (path.StartsWithSegments("/api/evidence")) return write ? JundiaiPermissions.EvidenceWrite : JundiaiPermissions.EvidenceRead;
        if (path.StartsWithSegments("/api/dashboard") || path.StartsWithSegments("/api/citizens") || path.StartsWithSegments("/api/units")) return JundiaiPermissions.CitizenRead;
        if (path.StartsWithSegments("/api/documents")) return write ? JundiaiPermissions.DocumentsWrite : JundiaiPermissions.DocumentsRead;
        if (path.StartsWithSegments("/api/clinical") || path.StartsWithSegments("/api/ubs") || path.StartsWithSegments("/api/records")) return write ? JundiaiPermissions.ClinicalWrite : JundiaiPermissions.ClinicalRead;
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
