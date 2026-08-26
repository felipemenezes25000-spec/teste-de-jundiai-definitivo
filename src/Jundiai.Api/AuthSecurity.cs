using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Jundiai.Api;

public static class AuthSecurityEndpoints
{
    public static IEndpointRouteBuilder MapAuthSecurityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/auth/demo-users", (DemoIdentityStore store) => Results.Ok(store.DemoUsers()));

        endpoints.MapPost("/api/auth/login", (LoginRequest request, DemoIdentityStore store) =>
        {
            var result = store.BeginLogin(request);
            return result.Status switch
            {
                "authenticated" => Results.Ok(result),
                "mfa_required" => Results.Accepted("/api/auth/mfa/verify", result),
                _ => Results.Unauthorized()
            };
        });

        endpoints.MapPost("/api/auth/mfa/verify", (MfaVerifyRequest request, DemoIdentityStore store) =>
        {
            var result = store.VerifyMfa(request);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        });

        endpoints.MapGet("/api/auth/me", (HttpContext context) =>
        {
            var identity = DemoAuthenticationMiddleware.GetIdentity(context);
            return identity is null
                ? Results.Unauthorized()
                : Results.Ok(new { identity.UserName, identity.DisplayName, identity.Role, identity.HealthUnit, identity.MfaVerified, identity.ExpiresAt });
        });

        endpoints.MapPost("/api/auth/logout", (HttpContext context, DemoIdentityStore store) =>
        {
            var token = DemoAuthenticationMiddleware.ResolveSessionToken(context);
            if (!string.IsNullOrWhiteSpace(token)) store.Revoke(token);
            return Results.NoContent();
        });

        endpoints.MapGet("/api/security/readiness", (DemoIdentityStore store) => Results.Ok(new
        {
            passwordHash = "PBKDF2-SHA256",
            sessionTokens = "cryptographically-random-memory-session",
            mfa = "demo challenge with fixed-time verification; production identity remains a Production Gate",
            pocMode = store.PocMode,
            mfaDefaultCodeEnabled = store.AllowDefaultDemoMfaCode,
            mfaCodeSource = store.MfaCodeSource,
            lockout = new { attempts = DemoIdentityStore.MaxFailedAttempts, minutes = DemoIdentityStore.LockoutMinutes },
            rbac = "explicit-role-permission/default-deny",
            anonymousProtectedApi = "401-fail-closed",
            demoRoleHeaderEnabled = DemoAccessControlMiddleware.DemoRoleHeaderEnabled,
            demoRoleHeaderPolicy = "disabled-by-default; enable only with JUNDIAI_ALLOW_DEMO_ROLE_HEADER=true",
            responseHeaders = new
            {
                pocMarker = "X-Jundiai-POC: RCE-008-2026",
                contentTypeOptions = "nosniff",
                frameOptions = "SAMEORIGIN",
                referrerPolicy = "same-origin",
                permissionsPolicy = "camera=(self), microphone=(self), geolocation=(self)",
                contentSecurityPolicy = "pending-production-hardening",
                strictTransportSecurity = "edge/TLS-dependent; pending-final-deployment",
                sensitiveApiCacheControl = "pending-production-hardening"
            },
            seededUsers = store.DemoUsers().Count,
            productionNote = "POC identity store only; production must disable demo defaults and use persistent identity provider, secret-backed MFA, audited token lifecycle, CSP/cache policy and final TLS/HSTS controls."
        }));

        return endpoints;
    }
}

public sealed class DemoIdentityStore
{
    public const int MaxFailedAttempts = 5;
    public const int LockoutMinutes = 10;
    private const string DefaultPocMfaCode = "008026";

    private readonly ConcurrentDictionary<string, DemoUserAccount> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DemoSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, PendingMfaChallenge> _challenges = new();

    public DemoIdentityStore(IConfiguration configuration)
    {
        PocMode = configuration.GetValue("Jundiai:PocMode", false);
        AllowDefaultDemoMfaCode = PocMode && configuration.GetValue("Jundiai:DemoMfa:AllowDefaultCode", false);

        Add("admin.jundiai", "Admin POC Jundiaí", JundiaiRoles.PocAdmin, "Gestão Municipal", true, "Jundiai#008");
        Add("gestor.saude", "Gestor Municipal", JundiaiRoles.MunicipalManager, "Secretaria Municipal de Saúde", true, "Gestor#008");
        Add("regulador.central", "Regulador Central", JundiaiRoles.Regulator, "Central de Regulação", true, "Regula#008");
        Add("medico.ubs", "Dr. Eduardo Martins", JundiaiRoles.Clinician, "UBS Vila Hortolândia", false, "Medico#008");
        Add("enfermagem.ubs", "Enf. Juliana Ramos", JundiaiRoles.Nurse, "UBS Vila Hortolândia", false, "Enfermagem#008");
        Add("farmacia.central", "Farm. Camila Souza", JundiaiRoles.Pharmacist, "Farmácia Central", true, "Farmacia#008");
        Add("dentista.ubs", "Dra. Marina Lopes", JundiaiRoles.Dentist, "CEO Jundiaí", false, "Dentista#008");
        Add("acs.micro01", "ACS Ana Paula", JundiaiRoles.Acs, "USF Parque Centenário", false, "Acs#008");
        Add("auditoria.cijun", "Auditoria POC", JundiaiRoles.Auditor, "CIJUN", true, "Audita#008");
    }

    public bool PocMode { get; }
    public bool AllowDefaultDemoMfaCode { get; }
    public string MfaCodeSource => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JUNDIAI_DEMO_MFA_CODE"))
        ? "environment"
        : AllowDefaultDemoMfaCode ? "explicit-poc-default" : "unconfigured-fail-closed";

    public IReadOnlyList<object> DemoUsers() => _users.Values
        .OrderBy(x => x.Role)
        .ThenBy(x => x.UserName)
        .Select(x => (object)new
        {
            x.UserName,
            x.DisplayName,
            x.Role,
            x.HealthUnit,
            x.RequiresMfa,
            passwordHint = "Credenciais exclusivamente demonstrativas; consulte docs/POC_DEMO_CREDENTIALS.md."
        }).ToList();

    public LoginResult BeginLogin(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
            return new("denied", null, null, null, null, false, "Credenciais inválidas.");

        if (!_users.TryGetValue(request.UserName.Trim(), out var user))
            return new("denied", null, null, null, null, false, "Credenciais inválidas.");

        lock (user)
        {
            if (user.LockedUntil is { } locked && locked > DateTimeOffset.UtcNow)
                return new("denied", null, null, null, null, false, $"Conta temporariamente bloqueada até {locked:O}.");

            if (!PasswordHasher.Verify(request.Password, user.PasswordSalt, user.PasswordHash))
            {
                user.FailedAttempts++;
                if (user.FailedAttempts >= MaxFailedAttempts)
                {
                    user.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(LockoutMinutes);
                    user.FailedAttempts = 0;
                }
                return new("denied", null, null, null, null, false, "Credenciais inválidas.");
            }

            user.FailedAttempts = 0;
            user.LockedUntil = null;
        }

        if (user.RequiresMfa)
        {
            var challenge = new PendingMfaChallenge(Guid.NewGuid(), user.UserName, DateTimeOffset.UtcNow.AddMinutes(5), false);
            _challenges[challenge.Id] = challenge;
            return new("mfa_required", null, challenge.Id, user.UserName, user.Role, false,
                AllowDefaultDemoMfaCode
                    ? "Segundo fator obrigatório. A POC permite o código demonstrativo configurado; JUNDIAI_DEMO_MFA_CODE tem precedência quando definido."
                    : "Segundo fator obrigatório. Configure JUNDIAI_DEMO_MFA_CODE; sem código configurado a verificação falha fechada.");
        }

        return CreateSession(user, false);
    }

    public LoginResult? VerifyMfa(MfaVerifyRequest request)
    {
        if (!_challenges.TryGetValue(request.ChallengeId, out var challenge) || challenge.Used || challenge.ExpiresAt <= DateTimeOffset.UtcNow)
            return null;
        if (!_users.TryGetValue(challenge.UserName, out var user)) return null;

        var configured = Environment.GetEnvironmentVariable("JUNDIAI_DEMO_MFA_CODE");
        var expected = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : AllowDefaultDemoMfaCode ? DefaultPocMfaCode : null;
        if (string.IsNullOrWhiteSpace(expected)) return null;

        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(request.Code ?? string.Empty), Encoding.UTF8.GetBytes(expected)))
            return null;

        _challenges[challenge.Id] = challenge with { Used = true };
        return CreateSession(user, true);
    }

    public DemoSession? Resolve(string token)
    {
        if (!_sessions.TryGetValue(token, out var session)) return null;
        if (session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(token, out _);
            return null;
        }
        return session;
    }

    public void Revoke(string token) => _sessions.TryRemove(token, out _);

    private LoginResult CreateSession(DemoUserAccount user, bool mfaVerified)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace("+", "-").Replace("/", "_").TrimEnd('=');
        var session = new DemoSession(token, user.UserName, user.DisplayName, user.Role, user.HealthUnit, mfaVerified, DateTimeOffset.UtcNow.AddHours(8));
        _sessions[token] = session;
        return new("authenticated", token, null, user.UserName, user.Role, mfaVerified, "Sessão POC criada.");
    }

    private void Add(string userName, string displayName, string role, string healthUnit, bool mfa, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = PasswordHasher.Hash(password, salt);
        _users[userName] = new DemoUserAccount(userName, displayName, role, healthUnit, mfa, salt, hash);
    }
}

public static class DemoAuthenticationExtensions
{
    public static IApplicationBuilder UseJundiaiDemoAuthentication(this IApplicationBuilder app) =>
        app.UseMiddleware<DemoAuthenticationMiddleware>();
}

public sealed class DemoAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, DemoIdentityStore identities)
    {
        var token = ResolveSessionToken(context);
        if (!string.IsNullOrWhiteSpace(token) && identities.Resolve(token) is { } identity)
        {
            context.Items["jundiai.auth.identity"] = identity;
            context.Items["jundiai.auth.role"] = identity.Role;
            context.Items["jundiai.auth.user"] = identity.UserName;
        }
        await next(context);
    }

    public static string? ResolveSessionToken(HttpContext context)
    {
        var auth = context.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth[7..].Trim();
        return context.Request.Headers["X-Demo-Session"].FirstOrDefault();
    }

    public static DemoSession? GetIdentity(HttpContext context) => context.Items["jundiai.auth.identity"] as DemoSession;
}

public static class PasswordHasher
{
    public static byte[] Hash(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, 120_000, HashAlgorithmName.SHA256, 32);

    public static bool Verify(string password, byte[] salt, byte[] expected) =>
        CryptographicOperations.FixedTimeEquals(Hash(password, salt), expected);
}

public sealed class DemoUserAccount(string userName, string displayName, string role, string healthUnit, bool requiresMfa, byte[] passwordSalt, byte[] passwordHash)
{
    public string UserName { get; } = userName;
    public string DisplayName { get; } = displayName;
    public string Role { get; } = role;
    public string HealthUnit { get; } = healthUnit;
    public bool RequiresMfa { get; } = requiresMfa;
    public byte[] PasswordSalt { get; } = passwordSalt;
    public byte[] PasswordHash { get; } = passwordHash;
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
}

public sealed record DemoSession(string Token, string UserName, string DisplayName, string Role, string HealthUnit, bool MfaVerified, DateTimeOffset ExpiresAt);
public sealed record PendingMfaChallenge(Guid Id, string UserName, DateTimeOffset ExpiresAt, bool Used);
public sealed record LoginRequest(string UserName, string Password);
public sealed record MfaVerifyRequest(Guid ChallengeId, string Code);
public sealed record LoginResult(string Status, string? SessionToken, Guid? ChallengeId, string? UserName, string? Role, bool MfaVerified, string Message);