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
    public const string RegulationRead = "regulation.read";
    public const string RegulationManage = "regulation.manage";
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
    public const string AuditRead = "audit.read";
    public const string AuditWrite = "audit.write";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        CitizenRead, RegulationRead, RegulationManage, BillingRead, BillingManage,
        ImmunizationRead, ImmunizationWrite, InventoryRead, InventoryManage,
        PsfRead, PsfWrite, DentalRead, DentalWrite, DiagnosticsRead, DiagnosticsWrite,
        AuditRead, AuditWrite
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
                JundiaiPermissions.RegulationRead,
                JundiaiPermissions.RegulationManage,
                JundiaiPermissions.BillingRead,
                JundiaiPermissions.BillingManage,
                JundiaiPermissions.ImmunizationRead,
                JundiaiPermissions.InventoryRead,
                JundiaiPermissions.PsfRead,
                JundiaiPermissions.DentalRead,
                JundiaiPermissions.DiagnosticsRead,
                JundiaiPermissions.AuditRead),
            [JundiaiRoles.Regulator] = Set(
                JundiaiPermissions.CitizenRead,
                JundiaiPermissions.RegulationRead,
                JundiaiPermissions.RegulationManage,
                JundiaiPermissions.DiagnosticsRead),
            [JundiaiRoles.Auditor] = Set(
                JundiaiPermissions.CitizenRead,
                JundiaiPermissions.RegulationRead,
                JundiaiPermissions.BillingRead,
                JundiaiPermissions.ImmunizationRead,
                JundiaiPermissions.InventoryRead,
                JundiaiPermissions.PsfRead,
                JundiaiPermissions.DentalRead,
                JundiaiPermissions.DiagnosticsRead,
                JundiaiPermissions.AuditRead),
            [JundiaiRoles.Clinician] = Set(
                JundiaiPermissions.CitizenRead,
                JundiaiPermissions.RegulationRead,
                JundiaiPermissions.DiagnosticsRead,
                JundiaiPermissions.DiagnosticsWrite),
            [JundiaiRoles.Nurse] = Set(
                JundiaiPermissions.CitizenRead,
                JundiaiPermissions.ImmunizationRead,
                JundiaiPermissions.ImmunizationWrite,
                JundiaiPermissions.PsfRead,
                JundiaiPermissions.DiagnosticsRead),
            [JundiaiRoles.Pharmacist] = Set(
                JundiaiPermissions.CitizenRead,
                JundiaiPermissions.InventoryRead,
                JundiaiPermissions.InventoryManage),
            [JundiaiRoles.Dentist] = Set(
                JundiaiPermissions.CitizenRead,
                JundiaiPermissions.DentalRead,
                JundiaiPermissions.DentalWrite,
                JundiaiPermissions.DiagnosticsRead,
                JundiaiPermissions.DiagnosticsWrite),
            [JundiaiRoles.Acs] = Set(
                JundiaiPermissions.CitizenRead,
                JundiaiPermissions.PsfRead,
                JundiaiPermissions.PsfWrite),
            [JundiaiRoles.Support] = Set(
                JundiaiPermissions.RegulationRead,
                JundiaiPermissions.DiagnosticsRead,
                JundiaiPermissions.AuditRead)
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
        if (!context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/api/health"))
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
                permissions = JundiaiPermissionCatalog.Snapshot().TryGetValue(roleContext, out var permissions)
                    ? permissions.OrderBy(x => x).ToArray()
                    : Array.Empty<string>()
            }, Json));
            return;
        }

        var required = ResolveRequiredPermission(context.Request);
        if (required is null)
        {
            await next(context);
            return;
        }

        var role = ResolveRole(context);
        if (!JundiaiPermissionCatalog.RoleGrants(role, required))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                type = "https://jundiai-healthos.local/problems/forbidden",
                title = "Acesso negado",
                status = 403,
                detail = $"O papel '{role}' não possui a permissão '{required}'.",
                requiredPermission = required,
                role
            }, Json));
            return;
        }

        context.Items["jundiai.role"] = role;
        context.Items["jundiai.permission"] = required;
        await next(context);
    }

    private static string ResolveRole(HttpContext context)
    {
        var role = context.Request.Headers["X-Demo-Role"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(role) ? JundiaiRoles.PocAdmin : role.Trim();
    }

    private static string? ResolveRequiredPermission(HttpRequest request)
    {
        var write = !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method);
        var path = request.Path;

        if (path.StartsWithSegments("/api/dashboard") || path.StartsWithSegments("/api/citizens")) return JundiaiPermissions.CitizenRead;
        if (path.StartsWithSegments("/api/regulation")) return write ? JundiaiPermissions.RegulationManage : JundiaiPermissions.RegulationRead;
        if (path.StartsWithSegments("/api/sus")) return write ? JundiaiPermissions.BillingManage : JundiaiPermissions.BillingRead;
        if (path.StartsWithSegments("/api/immunization")) return write ? JundiaiPermissions.ImmunizationWrite : JundiaiPermissions.ImmunizationRead;
        if (path.StartsWithSegments("/api/pharmacy")) return write ? JundiaiPermissions.InventoryManage : JundiaiPermissions.InventoryRead;
        if (path.StartsWithSegments("/api/psf")) return write ? JundiaiPermissions.PsfWrite : JundiaiPermissions.PsfRead;
        if (path.StartsWithSegments("/api/dental")) return write ? JundiaiPermissions.DentalWrite : JundiaiPermissions.DentalRead;
        if (path.StartsWithSegments("/api/diagnostics")) return write ? JundiaiPermissions.DiagnosticsWrite : JundiaiPermissions.DiagnosticsRead;
        if (path.StartsWithSegments("/api/audit")) return write ? JundiaiPermissions.AuditWrite : JundiaiPermissions.AuditRead;
        return null;
    }
}
