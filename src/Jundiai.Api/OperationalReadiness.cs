using System.Collections.Concurrent;

namespace Jundiai.Api;

public static class OperationalReadinessEndpoints
{
    public static IEndpointRouteBuilder MapOperationalReadinessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/operations/support/tickets", (OperationalReadinessStore store) => Results.Ok(store.Tickets()));
        endpoints.MapPost("/api/operations/support/tickets", (CreateSupportTicketRequest request, OperationalReadinessStore store, DemoStore demo) =>
        {
            var ticket = store.CreateTicket(request);
            demo.AuditExternal(request.OpenedBy, "support.ticket.create", $"support-ticket:{ticket.Id}", $"severity={ticket.Severity}");
            return Results.Created($"/api/operations/support/tickets/{ticket.Id}", ticket);
        });
        endpoints.MapPost("/api/operations/support/tickets/{id:guid}/transition", (Guid id, SupportTicketTransitionRequest request, OperationalReadinessStore store, DemoStore demo) =>
        {
            var ticket = store.TransitionTicket(id, request);
            demo.AuditExternal(request.Actor, "support.ticket.transition", $"support-ticket:{id}", ticket.Status);
            return Results.Ok(ticket);
        });
        endpoints.MapGet("/api/operations/support/sla", (OperationalReadinessStore store) => Results.Ok(store.SlaDashboard()));
        endpoints.MapGet("/api/operations/training", (OperationalReadinessStore store) => Results.Ok(store.Trainings()));
        endpoints.MapPost("/api/operations/training", (CreateTrainingSessionRequest request, OperationalReadinessStore store) => Results.Created("/api/operations/training", store.CreateTraining(request)));
        endpoints.MapPost("/api/operations/training/{id:guid}/attendance", (Guid id, TrainingAttendanceRequest request, OperationalReadinessStore store) => Results.Ok(store.AddAttendance(id, request)));
        endpoints.MapGet("/api/operations/readiness", (OperationalReadinessStore store) => Results.Ok(new
        {
            serviceDesk = store.SlaDashboard(),
            trainingSessions = store.Trainings().Count,
            capabilities = new[] { "ticket por severidade", "SLA alvo", "histórico de transição", "painel de violação", "treinamento por perfil", "lista de presença", "evidência de capacitação" },
            disclaimer = "A POC demonstra o mecanismo. Cobertura 24x7, equipe mínima, presença local e tempos contratuais precisam ser garantidos pela operação real e dimensionamento formal."
        }));
        return endpoints;
    }
}

public sealed class OperationalReadinessStore
{
    private readonly ConcurrentDictionary<Guid, SupportTicket> _tickets = new();
    private readonly ConcurrentDictionary<Guid, TrainingSession> _trainings = new();

    public OperationalReadinessStore()
    {
        var ticket = CreateTicket(new CreateSupportTicketRequest("POC-001", "Falha demonstrativa de integração PACS", "high", "Ambulatório Central", "support.seed", "Exemplo para demonstrar SLA sem incidente real."));
        TransitionTicket(ticket.Id, new SupportTicketTransitionRequest("in_progress", "support.seed", "Triagem iniciada."));
        CreateTraining(new CreateTrainingSessionRequest("Treinamento Regulação e Agenda", "regulator", DateTimeOffset.UtcNow.AddDays(5), 120, "Sala de treinamento municipal", "Equipe de implantação", 25));
        CreateTraining(new CreateTrainingSessionRequest("Treinamento UBS: recepção e Patient 360", "ubs-team", DateTimeOffset.UtcNow.AddDays(6), 180, "Auditório municipal", "Equipe de implantação", 50));
        CreateTraining(new CreateTrainingSessionRequest("Treinamento ACS offline", "acs", DateTimeOffset.UtcNow.AddDays(7), 120, "USF demonstrativa", "Equipe de implantação", 35));
    }

    public IReadOnlyList<SupportTicket> Tickets() => _tickets.Values.OrderByDescending(x => SeverityRank(x.Severity)).ThenBy(x => x.OpenedAt).ToList();
    public IReadOnlyList<TrainingSession> Trainings() => _trainings.Values.OrderBy(x => x.StartsAt).ToList();

    public SupportTicket CreateTicket(CreateSupportTicketRequest request)
    {
        var severity = NormalizeSeverity(request.Severity);
        var target = TargetMinutes(severity);
        var now = DateTimeOffset.UtcNow;
        var ticket = new SupportTicket(Guid.NewGuid(), request.ExternalReference?.Trim(), request.Title.Trim(), severity, request.Unit?.Trim(), "open", request.OpenedBy.Trim(), request.Description?.Trim(), now, now.AddMinutes(target), null, null, [new SupportTicketEvent(Guid.NewGuid(), "created", request.OpenedBy.Trim(), request.Description?.Trim(), now)], now);
        _tickets[ticket.Id] = ticket;
        return ticket;
    }

    public SupportTicket TransitionTicket(Guid id, SupportTicketTransitionRequest request)
    {
        if (!_tickets.TryGetValue(id, out var current)) throw new KeyNotFoundException();
        var target = (request.Status ?? string.Empty).Trim().ToLowerInvariant();
        var allowed = current.Status switch
        {
            "open" => new[] { "in_progress", "resolved", "cancelled" },
            "in_progress" => new[] { "waiting_external", "resolved", "cancelled" },
            "waiting_external" => new[] { "in_progress", "resolved", "cancelled" },
            "resolved" => new[] { "reopened" },
            "reopened" => new[] { "in_progress", "resolved" },
            _ => Array.Empty<string>()
        };
        if (!allowed.Contains(target)) throw new InvalidOperationException($"Transição de suporte inválida: {current.Status} → {target}.");
        var at = DateTimeOffset.UtcNow;
        var events = current.Events.Concat([new SupportTicketEvent(Guid.NewGuid(), target, request.Actor.Trim(), request.Note?.Trim(), at)]).ToList();
        var updated = current with
        {
            Status = target == "reopened" ? "open" : target,
            ResolvedAt = target == "resolved" ? at : current.ResolvedAt,
            ResolvedBy = target == "resolved" ? request.Actor.Trim() : current.ResolvedBy,
            Events = events,
            UpdatedAt = at
        };
        _tickets[id] = updated;
        return updated;
    }

    public object SlaDashboard()
    {
        var now = DateTimeOffset.UtcNow;
        var tickets = Tickets();
        var active = tickets.Where(x => x.Status is not ("resolved" or "cancelled")).ToList();
        var breached = active.Where(x => now > x.TargetAt).ToList();
        return new
        {
            total = tickets.Count,
            active = active.Count,
            resolved = tickets.Count(x => x.Status == "resolved"),
            breached = breached.Count,
            targets = new Dictionary<string,int> { ["critical"] = 15, ["high"] = 60, ["medium"] = 240, ["low"] = 480 },
            activeTickets = active.Select(x => new { x.Id, x.Title, x.Severity, x.Status, x.TargetAt, breached = now > x.TargetAt }).ToArray()
        };
    }

    public TrainingSession CreateTraining(CreateTrainingSessionRequest request)
    {
        if (request.DurationMinutes <= 0 || request.Capacity <= 0) throw new ArgumentException("Duração e capacidade devem ser positivas.");
        var session = new TrainingSession(Guid.NewGuid(), request.Title.Trim(), request.Audience.Trim(), request.StartsAt, request.DurationMinutes, request.Location.Trim(), request.Instructor.Trim(), request.Capacity, [], "planned", DateTimeOffset.UtcNow);
        _trainings[session.Id] = session;
        return session;
    }

    public TrainingSession AddAttendance(Guid id, TrainingAttendanceRequest request)
    {
        if (!_trainings.TryGetValue(id, out var current)) throw new KeyNotFoundException();
        if (current.Attendance.Count >= current.Capacity) throw new InvalidOperationException("Capacidade da turma atingida.");
        var attendance = current.Attendance.ToList();
        if (!attendance.Any(x => x.Identifier.Equals(request.Identifier.Trim(), StringComparison.OrdinalIgnoreCase)))
            attendance.Add(new TrainingAttendee(Guid.NewGuid(), request.Name.Trim(), request.Identifier.Trim(), request.Role.Trim(), request.Unit?.Trim(), request.Present, request.AssessmentScore, DateTimeOffset.UtcNow));
        var updated = current with { Attendance = attendance, Status = current.StartsAt < DateTimeOffset.UtcNow ? "in_progress_or_completed" : current.Status };
        _trainings[id] = updated;
        return updated;
    }

    private static string NormalizeSeverity(string value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "critical" or "p1" => "critical",
        "high" or "p2" => "high",
        "medium" or "p3" => "medium",
        _ => "low"
    };
    private static int TargetMinutes(string severity) => severity switch { "critical" => 15, "high" => 60, "medium" => 240, _ => 480 };
    private static int SeverityRank(string severity) => severity switch { "critical" => 4, "high" => 3, "medium" => 2, _ => 1 };
}

public sealed record SupportTicket(Guid Id, string? ExternalReference, string Title, string Severity, string? Unit, string Status, string OpenedBy, string? Description, DateTimeOffset OpenedAt, DateTimeOffset TargetAt, DateTimeOffset? ResolvedAt, string? ResolvedBy, IReadOnlyList<SupportTicketEvent> Events, DateTimeOffset UpdatedAt);
public sealed record SupportTicketEvent(Guid Id, string Action, string Actor, string? Note, DateTimeOffset OccurredAt);
public sealed record CreateSupportTicketRequest(string? ExternalReference, string Title, string Severity, string? Unit, string OpenedBy, string? Description);
public sealed record SupportTicketTransitionRequest(string Status, string Actor, string? Note);
public sealed record TrainingSession(Guid Id, string Title, string Audience, DateTimeOffset StartsAt, int DurationMinutes, string Location, string Instructor, int Capacity, IReadOnlyList<TrainingAttendee> Attendance, string Status, DateTimeOffset CreatedAt);
public sealed record TrainingAttendee(Guid Id, string Name, string Identifier, string Role, string? Unit, bool Present, decimal? AssessmentScore, DateTimeOffset RegisteredAt);
public sealed record CreateTrainingSessionRequest(string Title, string Audience, DateTimeOffset StartsAt, int DurationMinutes, string Location, string Instructor, int Capacity);
public sealed record TrainingAttendanceRequest(string Name, string Identifier, string Role, string? Unit, bool Present, decimal? AssessmentScore);
