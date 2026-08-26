using System.Collections.Concurrent;

namespace Jundiai.Api;

public static class TelemedicineEndpoints
{
    public static IEndpointRouteBuilder MapTelemedicineEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/telemedicine/sessions", (TelemedicineStore store) => Results.Ok(store.Sessions()));
        endpoints.MapGet("/api/telemedicine/sessions/{id:guid}", (Guid id, TelemedicineStore store) =>
            store.Get(id) is { } session ? Results.Ok(session) : Results.NotFound());

        endpoints.MapPost("/api/telemedicine/sessions", (CreateTelemedicineSessionRequest request, TelemedicineStore store, DemoStore demo) =>
        {
            var session = store.Create(request);
            demo.AuditExternal("telemedicine", "session.create", $"telemedicine:{session.Id}", $"citizen={session.CitizenId}");
            return Results.Created($"/api/telemedicine/sessions/{session.Id}", session);
        });

        endpoints.MapPost("/api/telemedicine/sessions/{id:guid}/preflight", (Guid id, TelemedicinePreflightRequest request, TelemedicineStore store) =>
            Results.Ok(store.Preflight(id, request)));
        endpoints.MapPost("/api/telemedicine/sessions/{id:guid}/consent", (Guid id, TelemedicineConsentRequest request, TelemedicineStore store) =>
            Results.Ok(store.Consent(id, request)));
        endpoints.MapPost("/api/telemedicine/sessions/{id:guid}/transition", (Guid id, TelemedicineTransitionRequest request, TelemedicineStore store) =>
            Results.Ok(store.Transition(id, request)));
        endpoints.MapPost("/api/telemedicine/sessions/{id:guid}/participant", (Guid id, TelemedicineParticipantRequest request, TelemedicineStore store) =>
            Results.Ok(store.AddParticipant(id, request)));
        endpoints.MapPost("/api/telemedicine/sessions/{id:guid}/clinical-summary", (Guid id, TelemedicineClinicalSummaryRequest request, TelemedicineStore store, DemoStore demo) =>
        {
            var session = store.AttachClinicalSummary(id, request);
            demo.AuditExternal(request.Professional, "telemedicine.clinical-summary", $"telemedicine:{id}", request.Assessment);
            return Results.Ok(session);
        });

        endpoints.MapGet("/api/telemedicine/readiness", () => Results.Ok(new
        {
            implemented = new[] { "waiting-room", "device-preflight", "consent", "participants", "session-state-machine", "clinical-summary", "audit-hooks" },
            videoTransport = "provider-agnostic demo room; no production provider claimed",
            productionBlockers = new[] { "video provider credentials", "SLA monitoring", "production TURN/network policy", "LGPD retention configuration" }
        }));

        return endpoints;
    }
}

public sealed class TelemedicineStore
{
    private readonly ConcurrentDictionary<Guid, TelemedicineSession> _sessions = new();

    public IReadOnlyList<TelemedicineSession> Sessions() => _sessions.Values.OrderByDescending(x => x.CreatedAt).ToList();
    public TelemedicineSession? Get(Guid id) => _sessions.TryGetValue(id, out var session) ? session : null;

    public TelemedicineSession Create(CreateTelemedicineSessionRequest request)
    {
        if (request.CitizenId == Guid.Empty || string.IsNullOrWhiteSpace(request.CitizenName)) throw new ArgumentException("Cidadão obrigatório.");
        var roomKey = $"jdi-{Guid.NewGuid():N}";
        var session = new TelemedicineSession(
            Guid.NewGuid(), request.CitizenId, request.CitizenName.Trim(), request.AppointmentId, request.HealthUnit.Trim(),
            request.Specialty?.Trim() ?? "Clínica Geral", "waiting_room", roomKey,
            null, null, [], null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _sessions[session.Id] = session;
        return session;
    }

    public TelemedicineSession Preflight(Guid id, TelemedicinePreflightRequest request)
    {
        var session = Required(id);
        var preflight = new TelemedicinePreflight(request.CameraAvailable, request.MicrophoneAvailable, request.SpeakerAvailable, request.NetworkQuality?.Trim() ?? "unknown", request.Browser?.Trim(), DateTimeOffset.UtcNow);
        var updated = session with { Preflight = preflight, UpdatedAt = DateTimeOffset.UtcNow };
        _sessions[id] = updated;
        return updated;
    }

    public TelemedicineSession Consent(Guid id, TelemedicineConsentRequest request)
    {
        var session = Required(id);
        if (!request.Accepted) throw new InvalidOperationException("Consentimento informado é obrigatório para iniciar a teleconsulta.");
        var consent = new TelemedicineConsent(true, request.Version?.Trim() ?? "telemed-v1", request.AcceptedBy?.Trim() ?? session.CitizenName, DateTimeOffset.UtcNow);
        var updated = session with { Consent = consent, UpdatedAt = DateTimeOffset.UtcNow };
        _sessions[id] = updated;
        return updated;
    }

    public TelemedicineSession Transition(Guid id, TelemedicineTransitionRequest request)
    {
        var current = Required(id);
        var target = (request.Status ?? string.Empty).Trim().ToLowerInvariant();
        var allowed = current.Status switch
        {
            "waiting_room" => new[] { "ready", "cancelled" },
            "ready" => new[] { "in_progress", "cancelled" },
            "in_progress" => new[] { "completed", "interrupted", "cancelled" },
            "interrupted" => new[] { "ready", "cancelled" },
            _ => Array.Empty<string>()
        };
        if (!allowed.Contains(target)) throw new InvalidOperationException($"Transição inválida: {current.Status} → {target}.");
        if (target is "ready" or "in_progress")
        {
            if (current.Preflight is null || !current.Preflight.CameraAvailable || !current.Preflight.MicrophoneAvailable)
                throw new InvalidOperationException("Preflight de câmera e microfone precisa estar aprovado.");
            if (current.Consent?.Accepted != true) throw new InvalidOperationException("Consentimento informado ausente.");
        }
        var updated = current with { Status = target, UpdatedAt = DateTimeOffset.UtcNow };
        _sessions[id] = updated;
        return updated;
    }

    public TelemedicineSession AddParticipant(Guid id, TelemedicineParticipantRequest request)
    {
        var current = Required(id);
        var participant = new TelemedicineParticipant(Guid.NewGuid(), request.Name.Trim(), request.Role.Trim(), request.ProfessionalCouncil?.Trim(), DateTimeOffset.UtcNow);
        var participants = current.Participants.Concat([participant]).ToList();
        var updated = current with { Participants = participants, UpdatedAt = DateTimeOffset.UtcNow };
        _sessions[id] = updated;
        return updated;
    }

    public TelemedicineSession AttachClinicalSummary(Guid id, TelemedicineClinicalSummaryRequest request)
    {
        var current = Required(id);
        if (current.Status != "completed" && current.Status != "in_progress") throw new InvalidOperationException("Resumo clínico só pode ser registrado durante ou após atendimento.");
        if (string.IsNullOrWhiteSpace(request.Assessment) || string.IsNullOrWhiteSpace(request.Plan)) throw new ArgumentException("Avaliação e plano são obrigatórios.");
        var summary = new TelemedicineClinicalSummary(request.Professional.Trim(), request.Assessment.Trim(), request.Plan.Trim(), request.Cid?.Trim(), request.Documents?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray() ?? [], DateTimeOffset.UtcNow);
        var updated = current with { ClinicalSummary = summary, UpdatedAt = DateTimeOffset.UtcNow };
        _sessions[id] = updated;
        return updated;
    }

    private TelemedicineSession Required(Guid id) => _sessions.TryGetValue(id, out var session) ? session : throw new KeyNotFoundException();
}

public sealed record TelemedicineSession(Guid Id, Guid CitizenId, string CitizenName, Guid? AppointmentId, string HealthUnit, string Specialty, string Status, string RoomKey, TelemedicinePreflight? Preflight, TelemedicineConsent? Consent, IReadOnlyList<TelemedicineParticipant> Participants, TelemedicineClinicalSummary? ClinicalSummary, string? CancellationReason, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record TelemedicinePreflight(bool CameraAvailable, bool MicrophoneAvailable, bool SpeakerAvailable, string NetworkQuality, string? Browser, DateTimeOffset CheckedAt);
public sealed record TelemedicineConsent(bool Accepted, string Version, string AcceptedBy, DateTimeOffset AcceptedAt);
public sealed record TelemedicineParticipant(Guid Id, string Name, string Role, string? ProfessionalCouncil, DateTimeOffset JoinedAt);
public sealed record TelemedicineClinicalSummary(string Professional, string Assessment, string Plan, string? Cid, IReadOnlyList<string> Documents, DateTimeOffset RecordedAt);
public sealed record CreateTelemedicineSessionRequest(Guid CitizenId, string CitizenName, Guid? AppointmentId, string HealthUnit, string? Specialty);
public sealed record TelemedicinePreflightRequest(bool CameraAvailable, bool MicrophoneAvailable, bool SpeakerAvailable, string? NetworkQuality, string? Browser);
public sealed record TelemedicineConsentRequest(bool Accepted, string? Version, string? AcceptedBy);
public sealed record TelemedicineTransitionRequest(string Status, string? Reason);
public sealed record TelemedicineParticipantRequest(string Name, string Role, string? ProfessionalCouncil);
public sealed record TelemedicineClinicalSummaryRequest(string Professional, string Assessment, string Plan, string? Cid, IReadOnlyList<string>? Documents);
