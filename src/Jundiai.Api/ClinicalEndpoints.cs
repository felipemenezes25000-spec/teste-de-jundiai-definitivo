using System.Collections.Concurrent;

namespace Jundiai.Api;

public static class ClinicalEndpoints
{
    private static readonly ConcurrentDictionary<Guid, List<ClinicalEncounter>> Encounters = new();
    private static readonly ConcurrentDictionary<Guid, ClinicalProfile> Profiles = new();

    public static IEndpointRouteBuilder MapClinicalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/clinical/workspaces", () => Results.Ok(ProfessionalWorkspaceCatalog.All));

        endpoints.MapGet("/api/clinical/patients/{citizenId:guid}/summary", (Guid citizenId, DemoStore store) =>
        {
            var citizen = store.Citizen(citizenId);
            if (citizen is null) return Results.NotFound();
            var profile = Profiles.GetOrAdd(citizenId, _ => SeedProfile(citizen));
            var timeline = Encounters.GetOrAdd(citizenId, _ => SeedEncounters(citizen));
            List<ClinicalEncounter> snapshot;
            lock (timeline) snapshot = timeline.OrderByDescending(x => x.OccurredAt).ToList();
            return Results.Ok(new Patient360Summary(citizen, profile, snapshot));
        });

        endpoints.MapPost("/api/clinical/patients/{citizenId:guid}/encounters", (Guid citizenId, CreateClinicalEncounterRequest request, DemoStore store) =>
        {
            var citizen = store.Citizen(citizenId);
            if (citizen is null) return Results.NotFound();
            if (!ProfessionalWorkspaceCatalog.TryResolve(request.Profession, out var workspace))
                return Results.Problem(statusCode: 400, title: "Profissão não suportada", detail: request.Profession);
            if (string.IsNullOrWhiteSpace(request.Assessment) || string.IsNullOrWhiteSpace(request.Plan))
                return Results.Problem(statusCode: 400, title: "Registro clínico incompleto", detail: "Avaliação e plano são obrigatórios.");

            var encounter = new ClinicalEncounter(
                Guid.NewGuid(), citizenId, request.Profession, workspace.Label, request.Professional,
                request.Subjective?.Trim(), request.Objective?.Trim(), request.Assessment.Trim(), request.Plan.Trim(),
                request.Diagnoses?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToArray() ?? [],
                request.Documents?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToArray() ?? [],
                DateTimeOffset.UtcNow);
            var list = Encounters.GetOrAdd(citizenId, _ => []);
            lock (list) list.Add(encounter);
            store.AuditExternal(request.Professional, "clinical.encounter.write", $"citizen:{citizenId}", $"profession={request.Profession}");
            return Results.Created($"/api/clinical/patients/{citizenId}/summary", encounter);
        });

        return endpoints;
    }

    private static ClinicalProfile SeedProfile(Citizen citizen) => citizen.Name.StartsWith("Maria", StringComparison.OrdinalIgnoreCase)
        ? new(citizen.Id,
            ["Hipertensão arterial sistêmica", "Dislipidemia"],
            ["Dipirona — reação cutânea referida"],
            ["Losartana 50 mg 2x/dia", "Sinvastatina 20 mg à noite"],
            new VitalSigns("148/92", 78, 36.4m, 97, 74.2m, 1.61m),
            ["PA acima da meta em registros recentes", "Alergia medicamentosa registrada"])
        : new(citizen.Id,
            ["Sem condição crônica ativa documentada nesta base de demonstração"],
            [],
            [],
            new VitalSigns("122/78", 72, 36.5m, 98, 76m, 1.72m),
            []);

    private static List<ClinicalEncounter> SeedEncounters(Citizen citizen) =>
    [
        new(Guid.NewGuid(), citizen.Id, "nurse", "Enfermagem", "Enf. Juliana Ramos",
            "Retorno para acompanhamento na APS.", "PA aferida e reconciliação de medicamentos realizada.",
            "Necessidade de revisão médica por pressão acima da meta.", "Manter acompanhamento e encaminhar para consulta médica.",
            ["Z01.3"], ["nursing_report"], DateTimeOffset.UtcNow.AddDays(-12)),
        new(Guid.NewGuid(), citizen.Id, "physician", "Medicina", "Dr. Eduardo Martins",
            "Nega dor torácica e dispneia no momento.", "Bom estado geral. Sem sinais de alarme.",
            "Hipertensão em acompanhamento, sem critério de urgência.", "Ajustar seguimento na APS, manter monitorização e retorno programado.",
            ["I10"], ["medical_document"], DateTimeOffset.UtcNow.AddDays(-35))
    ];
}

public sealed record Patient360Summary(Citizen Citizen, ClinicalProfile Profile, IReadOnlyList<ClinicalEncounter> Timeline);
public sealed record ClinicalProfile(Guid CitizenId, IReadOnlyList<string> Conditions, IReadOnlyList<string> Allergies, IReadOnlyList<string> Medications, VitalSigns LastVitals, IReadOnlyList<string> Alerts);
public sealed record VitalSigns(string BloodPressure, int HeartRate, decimal TemperatureC, int Spo2, decimal WeightKg, decimal HeightM);
public sealed record ClinicalEncounter(Guid Id, Guid CitizenId, string Profession, string ProfessionLabel, string Professional, string? Subjective, string? Objective, string Assessment, string Plan, IReadOnlyList<string> Diagnoses, IReadOnlyList<string> Documents, DateTimeOffset OccurredAt);
public sealed record CreateClinicalEncounterRequest(string Profession, string Professional, string? Subjective, string? Objective, string Assessment, string Plan, IReadOnlyList<string>? Diagnoses, IReadOnlyList<string>? Documents);

public sealed record ProfessionalWorkspace(string Code, string Label, string Council, string ClinicalFocus, IReadOnlyList<string> Sections, IReadOnlyList<string> Documents);

public static class ProfessionalWorkspaceCatalog
{
    private static readonly IReadOnlyDictionary<string, ProfessionalWorkspace> Items =
        new Dictionary<string, ProfessionalWorkspace>(StringComparer.OrdinalIgnoreCase)
        {
            ["physician"] = new("physician", "Medicina", "CRM", "Diagnóstico, conduta, prescrição e continuidade do cuidado.", ["História", "Exame", "Problemas", "Exames", "Linha do tempo"], ["medical_document", "prescription", "exam_order", "referral"]),
            ["nurse"] = new("nurse", "Enfermagem", "COREN", "Acolhimento, sinais vitais, necessidades e plano de cuidados.", ["Acolhimento", "Sinais vitais", "Riscos", "Plano de cuidados"], ["nursing_report", "referral"]),
            ["dentist"] = new("dentist", "Odontologia", "CRO", "Odontograma, condição bucal, procedimentos e plano terapêutico.", ["História odontológica", "Odontograma", "Tecidos", "Periodonto", "Imagens"], ["dental_report", "dental_prescription", "dental_certificate", "dental_referral"]),
            ["psychologist"] = new("psychologist", "Psicologia", "CRP", "Acolhimento psicológico com compartimentalização de conteúdo sensível.", ["Demanda", "Contexto psicossocial", "Rede de apoio", "Evolução"], ["psychological_report", "referral"]),
            ["physiotherapist"] = new("physiotherapist", "Fisioterapia", "CREFITO", "Avaliação funcional, dor, mobilidade e plano fisioterapêutico.", ["Dor", "Função", "Mobilidade", "Metas"], ["professional_report", "referral"]),
            ["nutritionist"] = new("nutritionist", "Nutrição", "CRN", "Antropometria, hábitos, avaliação e plano nutricional.", ["Antropometria", "Hábitos", "Exames", "Metas"], ["nutrition_plan", "professional_report", "referral"])
        };

    public static IReadOnlyList<ProfessionalWorkspace> All => Items.Values.OrderBy(x => x.Label).ToList();

    public static bool TryResolve(string? code, out ProfessionalWorkspace workspace)
    {
        if (!string.IsNullOrWhiteSpace(code) && Items.TryGetValue(code, out var found))
        {
            workspace = found;
            return true;
        }

        workspace = null!;
        return false;
    }
}
