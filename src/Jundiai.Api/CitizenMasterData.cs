using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Jundiai.Api;

public static class CitizenMasterDataEndpoints
{
    public static IEndpointRouteBuilder MapCitizenMasterDataEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/citizens/master", (string? q, CitizenMasterDataStore store) => Results.Ok(store.Search(q)));
        endpoints.MapGet("/api/citizens/master/{citizenId:guid}", (Guid citizenId, CitizenMasterDataStore store) =>
            store.Get(citizenId) is { } profile ? Results.Ok(profile) : Results.NotFound());
        endpoints.MapGet("/api/citizens/master/duplicates/candidates", (CitizenMasterDataStore store) => Results.Ok(store.DuplicateCandidates()));
        endpoints.MapGet("/api/citizens/master/readiness", (CitizenMasterDataStore store) => Results.Ok(store.Readiness()));

        endpoints.MapPut("/api/citizens/master/{citizenId:guid}", (
            Guid citizenId,
            UpdateCitizenMasterRequest request,
            HttpContext context,
            CitizenMasterDataStore store,
            DemoStore demo,
            EvidenceLedgerStore evidence) =>
        {
            RequireRegistryManager(context);
            var updated = store.Update(citizenId, request, demo);
            demo.AuditExternal(request.Actor, "citizen.master.update", $"citizen:{citizenId}", request.Reason);
            evidence.Append(new CreateEvidenceEventRequest(request.Actor, "citizen.master.update", $"citizen:{citizenId}", "POC-B02", request.Reason, "master-data"));
            return Results.Ok(updated);
        });

        endpoints.MapPost("/api/citizens/master/merge", (
            MergeCitizenMasterRequest request,
            HttpContext context,
            CitizenMasterDataStore store,
            DemoStore demo,
            EvidenceLedgerStore evidence) =>
        {
            RequireRegistryManager(context);
            var result = store.Merge(request, demo);
            demo.AuditExternal(request.Actor, "citizen.master.merge", $"citizen:{result.SurvivorCitizenId}", $"merged={result.MergedCitizenId};reason={request.Reason}");
            evidence.Append(new CreateEvidenceEventRequest(request.Actor, "citizen.master.merge", $"merge:{result.Id}", "POC-B02", $"survivor={result.SurvivorCitizenId};merged={result.MergedCitizenId};reason={request.Reason}", "identity-reconciliation"));
            return Results.Ok(result);
        });

        endpoints.MapGet("/api/citizens/master/merges/history", (CitizenMasterDataStore store) => Results.Ok(store.MergeHistory()));
        return endpoints;
    }

    private static void RequireRegistryManager(HttpContext context)
    {
        var role = context.Items["jundiai.auth.role"] as string
            ?? context.Request.Headers["X-Demo-Role"].FirstOrDefault()
            ?? JundiaiRoles.PocAdmin;
        if (role is not (JundiaiRoles.PocAdmin or JundiaiRoles.MunicipalManager))
            throw new UnauthorizedAccessException("Somente administração POC ou gestão municipal pode reconciliar o cadastro mestre.");
    }
}

public sealed class CitizenMasterDataStore
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<Guid, CitizenMasterProfile> _profiles = new();
    private readonly ConcurrentDictionary<Guid, CitizenMergeRecord> _merges = new();
    private readonly ConcurrentDictionary<Guid, Guid> _redirects = new();

    public CitizenMasterDataStore(DemoStore demo)
    {
        var index = 0;
        foreach (var citizen in demo.Citizens())
        {
            index++;
            _profiles[citizen.Id] = new CitizenMasterProfile(
                citizen.Id, citizen.Cns, citizen.Cpf, citizen.Name, null, citizen.BirthDate,
                index % 2 == 0 ? "F" : "M", index % 3 == 0 ? "Parda" : "Não informado",
                $"Responsável demo {index}", $"1190000{index:0000}", $"cidadao{index}@example.invalid",
                new CitizenAddress($"Rua Demonstrativa {index}", index.ToString(CultureInfo.InvariantCulture), null, "Jundiaí", "SP", "13200-000"),
                citizen.HealthUnit, citizen.Area, citizen.MicroArea, "active", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        }
    }

    public IReadOnlyList<CitizenMasterProfile> Search(string? q)
    {
        lock (_gate)
        {
            IEnumerable<CitizenMasterProfile> query = _profiles.Values.Where(x => x.Status == "active");
            if (!string.IsNullOrWhiteSpace(q))
            {
                var raw = q.Trim();
                var normalized = Normalize(raw);
                var digits = Digits(raw);
                query = query.Where(x =>
                    Normalize(x.Name).Contains(normalized, StringComparison.Ordinal) ||
                    Normalize(x.SocialName).Contains(normalized, StringComparison.Ordinal) ||
                    (!string.IsNullOrWhiteSpace(digits) && (Digits(x.Cpf) == digits || Digits(x.Cns) == digits)));
            }
            return query.OrderBy(x => x.Name).ToList();
        }
    }

    public CitizenMasterProfile? Get(Guid citizenId)
    {
        var canonical = ResolveCanonical(citizenId);
        return _profiles.TryGetValue(canonical, out var profile) ? profile : null;
    }

    public CitizenMasterProfile Update(Guid citizenId, UpdateCitizenMasterRequest request, DemoStore demo)
    {
        lock (_gate)
        {
            var canonical = ResolveCanonical(citizenId);
            if (!_profiles.TryGetValue(canonical, out var current)) throw new KeyNotFoundException();
            if (demo.Citizen(canonical) is null) throw new InvalidOperationException("Cidadão fora da base clínica demonstrativa.");
            if (string.IsNullOrWhiteSpace(request.Actor) || string.IsNullOrWhiteSpace(request.Reason))
                throw new ArgumentException("Ator e justificativa são obrigatórios para alteração cadastral.");

            var phone = string.IsNullOrWhiteSpace(request.Phone) ? current.Phone : request.Phone.Trim();
            var email = string.IsNullOrWhiteSpace(request.Email) ? current.Email : request.Email.Trim();
            var address = request.Address ?? current.Address;
            var updated = current with
            {
                SocialName = Clean(request.SocialName),
                RaceColor = string.IsNullOrWhiteSpace(request.RaceColor) ? current.RaceColor : request.RaceColor.Trim(),
                ResponsibleName = string.IsNullOrWhiteSpace(request.ResponsibleName) ? current.ResponsibleName : request.ResponsibleName.Trim(),
                Phone = phone,
                Email = email,
                Address = address,
                HealthUnit = string.IsNullOrWhiteSpace(request.HealthUnit) ? current.HealthUnit : request.HealthUnit.Trim(),
                Area = string.IsNullOrWhiteSpace(request.Area) ? current.Area : request.Area.Trim(),
                MicroArea = string.IsNullOrWhiteSpace(request.MicroArea) ? current.MicroArea : request.MicroArea.Trim(),
                LastChangeReason = request.Reason.Trim(),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _profiles[canonical] = updated;
            return updated;
        }
    }

    public IReadOnlyList<CitizenDuplicateCandidate> DuplicateCandidates()
    {
        lock (_gate)
        {
            var active = _profiles.Values.Where(x => x.Status == "active").OrderBy(x => x.CitizenId).ToArray();
            var candidates = new List<CitizenDuplicateCandidate>();
            for (var i = 0; i < active.Length; i++)
            for (var j = i + 1; j < active.Length; j++)
            {
                var a = active[i]; var b = active[j];
                var score = DuplicateScore(a, b, out var reasons);
                if (score >= 60) candidates.Add(new CitizenDuplicateCandidate(a.CitizenId, a.Name, b.CitizenId, b.Name, score, reasons));
            }
            return candidates.OrderByDescending(x => x.Score).ThenBy(x => x.NameA).ToList();
        }
    }

    public CitizenMergeRecord Merge(MergeCitizenMasterRequest request, DemoStore demo)
    {
        lock (_gate)
        {
            if (request.SurvivorCitizenId == request.MergedCitizenId) throw new ArgumentException("Cadastro sobrevivente e incorporado devem ser diferentes.");
            if (string.IsNullOrWhiteSpace(request.Actor) || string.IsNullOrWhiteSpace(request.Reason)) throw new ArgumentException("Ator e motivo são obrigatórios.");
            var survivorId = ResolveCanonical(request.SurvivorCitizenId);
            var mergedId = ResolveCanonical(request.MergedCitizenId);
            if (!_profiles.TryGetValue(survivorId, out var survivor) || !_profiles.TryGetValue(mergedId, out var merged)) throw new KeyNotFoundException();
            if (survivor.Status != "active" || merged.Status != "active") throw new InvalidOperationException("Apenas cadastros ativos podem ser reconciliados.");
            if (demo.Citizen(survivorId) is null || demo.Citizen(mergedId) is null) throw new InvalidOperationException("Merge POC restrito a cidadãos existentes na base clínica.");

            var score = DuplicateScore(survivor, merged, out var reasons);
            if (score < 60 && !request.Force) throw new InvalidOperationException("Candidatos não atingem score mínimo de duplicidade; use force apenas após validação humana explícita.");
            if (!string.IsNullOrWhiteSpace(survivor.Cpf) && !string.IsNullOrWhiteSpace(merged.Cpf) && Digits(survivor.Cpf) != Digits(merged.Cpf) && !request.Force)
                throw new InvalidOperationException("CPFs divergentes exigem validação humana explícita antes do merge.");

            var now = DateTimeOffset.UtcNow;
            var mergedProfile = merged with { Status = "merged", RedirectToCitizenId = survivorId, LastChangeReason = request.Reason.Trim(), UpdatedAt = now };
            _profiles[mergedId] = mergedProfile;
            _redirects[mergedId] = survivorId;
            var record = new CitizenMergeRecord(Guid.NewGuid(), survivorId, mergedId, score, reasons, request.Force, request.Actor.Trim(), request.Reason.Trim(), now);
            _merges[record.Id] = record;
            return record;
        }
    }

    public IReadOnlyList<CitizenMergeRecord> MergeHistory() => _merges.Values.OrderByDescending(x => x.MergedAt).ToList();

    public object Readiness()
    {
        var active = _profiles.Values.Count(x => x.Status == "active");
        return new
        {
            activeProfiles = active,
            mergedProfiles = _profiles.Values.Count(x => x.Status == "merged"),
            duplicateCandidates = DuplicateCandidates().Count,
            mergeHistory = _merges.Count,
            capabilities = new[]
            {
                "municipal master patient index", "CPF/CNS lookup", "normalized name search", "demographics",
                "address/contact", "territory/reference unit", "duplicate scoring", "human-reviewed merge", "canonical redirect", "audit/evidence hooks"
            },
            externalBoundary = "CadSUS/CNS official validation remains dependent on authorized external integration."
        };
    }

    private Guid ResolveCanonical(Guid id)
    {
        var seen = new HashSet<Guid>();
        while (_redirects.TryGetValue(id, out var next) && seen.Add(id)) id = next;
        return id;
    }

    private static int DuplicateScore(CitizenMasterProfile a, CitizenMasterProfile b, out IReadOnlyList<string> reasons)
    {
        var list = new List<string>(); var score = 0;
        if (!string.IsNullOrWhiteSpace(a.Cpf) && Digits(a.Cpf) == Digits(b.Cpf)) { score += 65; list.Add("CPF idêntico"); }
        if (!string.IsNullOrWhiteSpace(a.Cns) && Digits(a.Cns) == Digits(b.Cns)) { score += 65; list.Add("CNS idêntico"); }
        if (Normalize(a.Name) == Normalize(b.Name)) { score += 35; list.Add("nome idêntico normalizado"); }
        if (a.BirthDate == b.BirthDate) { score += 25; list.Add("data de nascimento idêntica"); }
        if (!string.IsNullOrWhiteSpace(a.ResponsibleName) && Normalize(a.ResponsibleName) == Normalize(b.ResponsibleName)) { score += 10; list.Add("responsável idêntico"); }
        reasons = list;
        return Math.Min(score, 100);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }
}

public sealed record CitizenAddress(string Street, string Number, string? Complement, string City, string State, string PostalCode);
public sealed record CitizenMasterProfile(
    Guid CitizenId, string Cns, string Cpf, string Name, string? SocialName, DateOnly BirthDate,
    string Sex, string RaceColor, string ResponsibleName, string Phone, string Email, CitizenAddress Address,
    string HealthUnit, string Area, string MicroArea, string Status, Guid? RedirectToCitizenId,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? LastChangeReason = null);
public sealed record UpdateCitizenMasterRequest(
    string? SocialName, string? RaceColor, string? ResponsibleName, string? Phone, string? Email, CitizenAddress? Address,
    string? HealthUnit, string? Area, string? MicroArea, string Actor, string Reason);
public sealed record CitizenDuplicateCandidate(Guid CitizenIdA, string NameA, Guid CitizenIdB, string NameB, int Score, IReadOnlyList<string> Reasons);
public sealed record MergeCitizenMasterRequest(Guid SurvivorCitizenId, Guid MergedCitizenId, bool Force, string Actor, string Reason);
public sealed record CitizenMergeRecord(Guid Id, Guid SurvivorCitizenId, Guid MergedCitizenId, int DuplicateScore, IReadOnlyList<string> Reasons, bool Forced, string Actor, string Reason, DateTimeOffset MergedAt);