using System.Collections.Concurrent;

namespace Jundiai.Api;

public static class IntelligentAccessEndpoints
{
    private static readonly ConcurrentDictionary<Guid, IntelligentAccessAssessment> Assessments = new();

    public static IEndpointRouteBuilder MapIntelligentAccessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/citizen/branding", () => Results.Ok(new
        {
            productName = "Saúde Digital Jundiaí",
            institutionName = "Rede Municipal de Saúde",
            primaryColor = "#176b5b",
            portalTitle = "Porta Digital",
            welcome = "Conte o que está acontecendo para organizarmos o próximo passo na rede.",
            services = new[] { "teleconsulta", "regulação", "UBS", "urgência" }
        }));

        endpoints.MapGet("/api/citizen/demo-profile", (DemoStore store) =>
        {
            var citizen = store.Citizens().First();
            return Results.Ok(new
            {
                citizen.Id,
                citizen.Name,
                citizen.BirthDate,
                citizen.HealthUnit,
                citizen.Area,
                citizen.MicroArea
            });
        });

        endpoints.MapPost("/api/citizen/intelligent-access/evaluate", (IntelligentAccessRequest request, DemoStore store) =>
        {
            var citizen = store.Citizen(request.CitizenId);
            if (citizen is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(request.ChiefComplaint) || request.ChiefComplaint.Trim().Length < 3)
                return Results.Problem(statusCode: 400, title: "Relato insuficiente", detail: "Descreva brevemente o que está sentindo.");

            var result = IntelligentAccessEngine.Evaluate(citizen, request);
            Assessments[result.Id] = result;
            store.AuditExternal("citizen.portal", "intelligent_access.evaluate", $"assessment:{result.Id}", $"risk={result.RiskLevel};route={result.Routing.Channel}");
            return Results.Ok(result);
        });

        endpoints.MapGet("/api/citizen/intelligent-access/{assessmentId:guid}", (Guid assessmentId) =>
            Assessments.TryGetValue(assessmentId, out var result) ? Results.Ok(result) : Results.NotFound());

        endpoints.MapPost("/api/citizen/intelligent-access/{assessmentId:guid}/handoff", (Guid assessmentId, IntelligentAccessHandoffRequest request, DemoStore store) =>
        {
            if (!Assessments.TryGetValue(assessmentId, out var assessment)) return Results.NotFound();
            if (assessment.RiskLevel == "emergency")
                return Results.Problem(statusCode: 409, title: "Handoff bloqueado", detail: "Avaliações classificadas como emergência não podem abrir fluxo eletivo/teleconsulta.");
            if (!request.ConsentAccepted)
                return Results.Problem(statusCode: 409, title: "Consentimento necessário", detail: "Confirme o consentimento da jornada antes de continuar.");

            if (assessment.HandoffId.HasValue)
                return Results.Ok(new { assessmentId, appointmentId = assessment.HandoffId, idempotent = true });

            var destination = assessment.Routing.Channel switch
            {
                "teleconsultation" => "Telemedicina Municipal",
                "regulation" => "Central de Regulação",
                "assisted_care" => assessment.CitizenHealthUnit,
                _ => assessment.CitizenHealthUnit
            };

            var appointment = store.CreateRegulation(new CreateRegulationRequest(
                assessment.CitizenId,
                assessment.CitizenHealthUnit,
                request.Specialty?.Trim() is { Length: > 0 } specialty ? specialty : assessment.Routing.SuggestedSpecialty,
                assessment.Routing.Priority,
                destination,
                $"Porta Digital {assessment.Id:N} · {assessment.StructuredSummary}"));

            var updated = assessment with { HandoffId = appointment.Id };
            Assessments[assessmentId] = updated;
            store.AuditExternal("citizen.portal", "intelligent_access.handoff", $"assessment:{assessmentId}", $"appointment={appointment.Id}");
            return Results.Created("/api/regulation", new { assessmentId, appointmentId = appointment.Id, idempotent = false });
        });

        return endpoints;
    }
}

public sealed record IntelligentAccessRequest(
    Guid CitizenId,
    string ChiefComplaint,
    int? Age,
    bool? Pregnant,
    IReadOnlyDictionary<string, string>? Answers = null);

public sealed record IntelligentAccessHandoffRequest(string? Specialty, bool ConsentAccepted);

public sealed record IntelligentAccessRouting(string Channel, string Priority, string SuggestedSpecialty, string Action);

public sealed record IntelligentAccessAssessment(
    Guid Id,
    Guid CitizenId,
    string CitizenName,
    string CitizenHealthUnit,
    string ChiefComplaint,
    string RiskLevel,
    int Score,
    IReadOnlyList<string> RedFlags,
    IntelligentAccessRouting Routing,
    string StructuredSummary,
    bool HumanReviewRequired,
    string ProtocolVersion,
    string EngineVersion,
    Guid? HandoffId,
    DateTimeOffset CreatedAt);

public static class IntelligentAccessEngine
{
    private const string ProtocolVersion = "jundiai-rce008-intake-v1";
    private const string EngineVersion = "deterministic-safety-v1";

    private static readonly (string Pattern, string Label)[] EmergencyPatterns =
    [
        ("dor no peito", "Dor torácica relatada"),
        ("falta de ar intensa", "Dispneia intensa relatada"),
        ("não consigo respirar", "Dificuldade respiratória grave relatada"),
        ("desmaio", "Perda de consciência/desmaio relatado"),
        ("convuls", "Convulsão relatada"),
        ("sangramento intenso", "Sangramento intenso relatado"),
        ("rosto torto", "Possível déficit neurológico agudo relatado"),
        ("não consigo mexer", "Possível déficit neurológico agudo relatado")
    ];

    public static IntelligentAccessAssessment Evaluate(Citizen citizen, IntelligentAccessRequest request)
    {
        var complaint = request.ChiefComplaint.Trim();
        var normalized = complaint.ToLowerInvariant();
        var redFlags = EmergencyPatterns
            .Where(x => normalized.Contains(x.Pattern, StringComparison.Ordinal))
            .Select(x => x.Label)
            .Distinct()
            .ToList();

        if (request.Pregnant == true && (normalized.Contains("sangramento") || normalized.Contains("contração") || normalized.Contains("contracao")))
            redFlags.Add("Gestação com sinal obstétrico de alerta relatado");

        if (redFlags.Count > 0)
        {
            return Build(citizen, complaint, "emergency", 100, redFlags,
                new IntelligentAccessRouting("emergency", "emergency", "Urgência/Emergência", "Procure atendimento presencial imediato; em risco grave, acione o SAMU 192."));
        }

        var score = 20;
        if (request.Age is >= 65) score += 15;
        if (request.Age is <= 2 and >= 0) score += 15;
        if (request.Pregnant == true) score += 15;
        if (normalized.Contains("febre")) score += 10;
        if (normalized.Contains("dor forte") || normalized.Contains("dor intensa")) score += 20;
        if (normalized.Contains("há dias") || normalized.Contains("ha dias")) score += 5;

        var hasRespiratory = normalized.Contains("tosse") || normalized.Contains("garganta") || normalized.Contains("resfriado");
        var hasChronic = normalized.Contains("pressão") || normalized.Contains("pressao") || normalized.Contains("diabetes") || normalized.Contains("receita");
        var hasSpecialtySignal = normalized.Contains("joelho") || normalized.Contains("ombro") || normalized.Contains("coluna");

        if (request.Pregnant == true)
            return Build(citizen, complaint, "high", Math.Max(score, 60), [], new("assisted_care", "high", "Pré-natal / Obstetrícia", "Direcionar para avaliação da rede materno-infantil com revisão profissional."));

        if (hasSpecialtySignal || score >= 60)
            return Build(citizen, complaint, "high", Math.Max(score, 60), [], new("regulation", "high", hasSpecialtySignal ? "Ortopedia" : "Clínica Médica", "Encaminhar para regulação, sujeito à avaliação profissional e protocolo local."));

        if (hasRespiratory || hasChronic || score <= 45)
            return Build(citizen, complaint, score <= 30 ? "low" : "moderate", score, [], new("teleconsultation", score <= 30 ? "routine" : "high", "Clínica Geral", "Teleconsulta pode ser utilizada como próxima etapa, com decisão clínica humana."));

        return Build(citizen, complaint, "moderate", score, [], new("assisted_care", "routine", "Atenção Primária", "Prosseguir para avaliação assistida na UBS de referência."));
    }

    private static IntelligentAccessAssessment Build(
        Citizen citizen,
        string complaint,
        string risk,
        int score,
        IReadOnlyList<string> redFlags,
        IntelligentAccessRouting routing)
    {
        var summary = $"Queixa principal: {complaint}. Risco operacional: {risk}. Destino sugerido: {routing.Channel}/{routing.SuggestedSpecialty}.";
        return new IntelligentAccessAssessment(
            Guid.NewGuid(), citizen.Id, citizen.Name, citizen.HealthUnit, complaint,
            risk, Math.Clamp(score, 0, 100), redFlags, routing, summary,
            true, ProtocolVersion, EngineVersion, null, DateTimeOffset.UtcNow);
    }
}
