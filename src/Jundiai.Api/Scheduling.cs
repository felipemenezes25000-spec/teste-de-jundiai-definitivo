using System.Collections.Concurrent;

namespace Jundiai.Api;

public static class SchedulingEndpoints
{
    public static IEndpointRouteBuilder MapSchedulingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/scheduling/grids", (SchedulingStore store) => Results.Ok(store.Grids()));
        endpoints.MapGet("/api/scheduling/slots", (string? specialty, string? unit, DateOnly? date, SchedulingStore store) =>
            Results.Ok(store.Slots(specialty, unit, date)));
        endpoints.MapGet("/api/scheduling/quotas", (SchedulingStore store) => Results.Ok(store.Quotas()));
        endpoints.MapGet("/api/scheduling/waitlist", (SchedulingStore store) => Results.Ok(store.Waitlist()));
        endpoints.MapGet("/api/scheduling/bookings", (SchedulingStore store) => Results.Ok(store.Bookings()));
        endpoints.MapGet("/api/scheduling/loss-report", (SchedulingStore store) => Results.Ok(store.LossReport()));

        endpoints.MapPost("/api/scheduling/book", (BookSlotRequest request, SchedulingStore store, DemoStore demo) =>
        {
            var booking = store.Book(request);
            demo.AuditExternal("scheduler", "scheduling.book", $"booking:{booking.Id}", $"slot={booking.SlotId};citizen={booking.CitizenId}");
            return Results.Created($"/api/scheduling/bookings/{booking.Id}", booking);
        });

        endpoints.MapPost("/api/scheduling/bookings/{bookingId:guid}/transition", (Guid bookingId, BookingTransitionRequest request, SchedulingStore store, DemoStore demo) =>
        {
            var booking = store.TransitionBooking(bookingId, request);
            demo.AuditExternal(request.Actor ?? "scheduler", "scheduling.booking.transition", $"booking:{bookingId}", $"status={booking.Status};reason={request.Reason}");
            return Results.Ok(booking);
        });

        endpoints.MapPost("/api/scheduling/bookings/{bookingId:guid}/reschedule", (Guid bookingId, RescheduleBookingRequest request, SchedulingStore store, DemoStore demo) =>
        {
            var booking = store.Reschedule(bookingId, request);
            demo.AuditExternal(request.Actor ?? "scheduler", "scheduling.booking.reschedule", $"booking:{bookingId}", $"slot={booking.SlotId};reason={request.Reason}");
            return Results.Ok(booking);
        });

        endpoints.MapPost("/api/scheduling/waitlist", (CreateWaitlistRequest request, SchedulingStore store) =>
            Results.Created("/api/scheduling/waitlist", store.Enqueue(request)));

        endpoints.MapPost("/api/scheduling/slots/{slotId:guid}/block", (Guid slotId, BlockSlotRequest request, SchedulingStore store) =>
            Results.Ok(store.Block(slotId, request)));

        endpoints.MapPost("/api/scheduling/quotas/{quotaId:guid}/adjust", (Guid quotaId, AdjustQuotaRequest request, SchedulingStore store) =>
            Results.Ok(store.AdjustQuota(quotaId, request)));

        endpoints.MapPost("/api/scheduling/waitlist/{entryId:guid}/promote", (Guid entryId, PromoteWaitlistRequest request, SchedulingStore store) =>
            Results.Ok(store.Promote(entryId, request)));

        endpoints.MapGet("/api/scheduling/readiness", (SchedulingStore store) => Results.Ok(new
        {
            gridCount = store.Grids().Count,
            slotCount = store.Slots(null, null, null).Count,
            quotaCount = store.Quotas().Count,
            bookingCount = store.Bookings().Count,
            capabilities = new[]
            {
                "centralized-grids", "unit-specialty-slots", "quotas", "blocked-slots", "overbooking-control",
                "waitlist", "priority-promotion", "booking-lifecycle", "reschedule", "no-show", "cancellation", "loss-and-occupancy-report"
            }
        }));

        return endpoints;
    }
}

public sealed class SchedulingStore
{
    private readonly ConcurrentDictionary<Guid, ScheduleGrid> _grids = new();
    private readonly ConcurrentDictionary<Guid, ScheduleSlot> _slots = new();
    private readonly ConcurrentDictionary<Guid, ScheduleQuota> _quotas = new();
    private readonly ConcurrentDictionary<Guid, ScheduleBooking> _bookings = new();
    private readonly ConcurrentDictionary<Guid, WaitlistEntry> _waitlist = new();

    public SchedulingStore()
    {
        SeedGrid("Clínica Geral", "UBS Vila Hortolândia", DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(17, 0), 20, 1);
        SeedGrid("Cardiologia", "Ambulatório de Especialidades", DayOfWeek.Tuesday, new TimeOnly(8, 0), new TimeOnly(13, 0), 30, 0);
        SeedGrid("Ortopedia", "Ambulatório de Especialidades", DayOfWeek.Wednesday, new TimeOnly(13, 0), new TimeOnly(18, 0), 30, 1);
        SeedGrid("Odontologia", "CEO Jundiaí", DayOfWeek.Thursday, new TimeOnly(8, 0), new TimeOnly(17, 0), 40, 0);
        SeedGrid("Telemedicina - Clínica Geral", "Telemedicina Municipal", DayOfWeek.Friday, new TimeOnly(8, 0), new TimeOnly(20, 0), 15, 2);

        foreach (var grid in _grids.Values)
        {
            var quota = new ScheduleQuota(Guid.NewGuid(), grid.Specialty, grid.Unit, "municipal", 70, 20, 10, DateTimeOffset.UtcNow);
            _quotas[quota.Id] = quota;
            GenerateSlots(grid, DateOnly.FromDateTime(DateTime.Today), 21);
        }
    }

    public IReadOnlyList<ScheduleGrid> Grids() => _grids.Values.OrderBy(x => x.Specialty).ToList();
    public IReadOnlyList<ScheduleQuota> Quotas() => _quotas.Values.OrderBy(x => x.Specialty).ToList();
    public IReadOnlyList<WaitlistEntry> Waitlist() => _waitlist.Values.OrderByDescending(x => PriorityRank(x.Priority)).ThenBy(x => x.CreatedAt).ToList();
    public IReadOnlyList<ScheduleBooking> Bookings() => _bookings.Values.OrderByDescending(x => x.StartsAt).ToList();

    public IReadOnlyList<ScheduleSlot> Slots(string? specialty, string? unit, DateOnly? date) => _slots.Values
        .Where(x => string.IsNullOrWhiteSpace(specialty) || x.Specialty.Contains(specialty, StringComparison.OrdinalIgnoreCase))
        .Where(x => string.IsNullOrWhiteSpace(unit) || x.Unit.Contains(unit, StringComparison.OrdinalIgnoreCase))
        .Where(x => date is null || DateOnly.FromDateTime(x.Start.LocalDateTime) == date)
        .OrderBy(x => x.Start)
        .ToList();

    public ScheduleBooking Book(BookSlotRequest request)
    {
        if (!_slots.TryGetValue(request.SlotId, out var slot)) throw new KeyNotFoundException();
        Reserve(slot);
        var now = DateTimeOffset.UtcNow;
        var booking = new ScheduleBooking(
            Guid.NewGuid(), request.SlotId, request.CitizenId, request.CitizenName.Trim(), slot.Specialty, slot.Unit, slot.Start,
            request.Priority?.Trim() ?? "routine", "scheduled", request.Source?.Trim() ?? "regulation", now, now, null, null, null);
        _bookings[booking.Id] = booking;
        return booking;
    }

    public ScheduleBooking TransitionBooking(Guid bookingId, BookingTransitionRequest request)
    {
        if (!_bookings.TryGetValue(bookingId, out var current)) throw new KeyNotFoundException();
        var target = (request.Status ?? string.Empty).Trim().ToLowerInvariant();
        var allowed = current.Status switch
        {
            "scheduled" => new[] { "checked_in", "completed", "cancelled", "no_show" },
            "checked_in" => new[] { "completed", "cancelled" },
            _ => Array.Empty<string>()
        };
        if (!allowed.Contains(target)) throw new InvalidOperationException($"Transição de agenda inválida: {current.Status} → {target}.");
        if (target == "cancelled" && string.IsNullOrWhiteSpace(request.Reason)) throw new ArgumentException("Cancelamento exige motivo.");

        if (target == "cancelled" && _slots.TryGetValue(current.SlotId, out var slot)) Release(slot);
        var updated = current with
        {
            Status = target,
            UpdatedAt = DateTimeOffset.UtcNow,
            ClosureReason = request.Reason?.Trim(),
            ClosedBy = request.Actor?.Trim()
        };
        _bookings[bookingId] = updated;
        return updated;
    }

    public ScheduleBooking Reschedule(Guid bookingId, RescheduleBookingRequest request)
    {
        if (!_bookings.TryGetValue(bookingId, out var current)) throw new KeyNotFoundException();
        if (current.Status != "scheduled") throw new InvalidOperationException("Somente agendamento ativo pode ser remarcado.");
        if (current.SlotId == request.NewSlotId) return current;
        if (!_slots.TryGetValue(request.NewSlotId, out var target)) throw new KeyNotFoundException();
        if (!_slots.TryGetValue(current.SlotId, out var origin)) throw new InvalidOperationException("Slot original não encontrado.");

        Reserve(target);
        try
        {
            Release(origin);
            var updated = current with
            {
                SlotId = target.Id,
                Specialty = target.Specialty,
                Unit = target.Unit,
                StartsAt = target.Start,
                UpdatedAt = DateTimeOffset.UtcNow,
                RescheduledFromSlotId = origin.Id,
                ClosureReason = request.Reason?.Trim(),
                ClosedBy = request.Actor?.Trim()
            };
            _bookings[bookingId] = updated;
            return updated;
        }
        catch
        {
            Release(target);
            throw;
        }
    }

    public WaitlistEntry Enqueue(CreateWaitlistRequest request)
    {
        var entry = new WaitlistEntry(Guid.NewGuid(), request.CitizenId, request.CitizenName.Trim(), request.Specialty.Trim(), request.PreferredUnit?.Trim(), request.Priority?.Trim() ?? "routine", request.RequestedBy?.Trim() ?? "regulation", "waiting", null, DateTimeOffset.UtcNow, null);
        _waitlist[entry.Id] = entry;
        return entry;
    }

    public ScheduleSlot Block(Guid slotId, BlockSlotRequest request)
    {
        if (!_slots.TryGetValue(slotId, out var slot)) throw new KeyNotFoundException();
        lock (slot)
        {
            if (slot.Booked > 0 && request.Blocked) throw new InvalidOperationException("Não é possível bloquear horário com agendamento ativo sem remanejamento.");
            slot.Blocked = request.Blocked;
            slot.BlockReason = request.Blocked ? request.Reason?.Trim() ?? "bloqueio operacional" : null;
        }
        return slot;
    }

    public ScheduleQuota AdjustQuota(Guid quotaId, AdjustQuotaRequest request)
    {
        if (!_quotas.TryGetValue(quotaId, out var current)) throw new KeyNotFoundException();
        if (request.RegulationPercent + request.UnitPercent + request.ReservePercent != 100)
            throw new ArgumentException("A soma das cotas deve ser 100%.");
        var updated = current with { RegulationPercent = request.RegulationPercent, UnitPercent = request.UnitPercent, ReservePercent = request.ReservePercent, UpdatedAt = DateTimeOffset.UtcNow };
        _quotas[quotaId] = updated;
        return updated;
    }

    public WaitlistEntry Promote(Guid entryId, PromoteWaitlistRequest request)
    {
        if (!_waitlist.TryGetValue(entryId, out var entry)) throw new KeyNotFoundException();
        if (entry.Status != "waiting") throw new InvalidOperationException("Entrada da fila já foi processada.");
        var booking = Book(new BookSlotRequest(request.SlotId, entry.CitizenId, entry.CitizenName, entry.Priority, "waitlist"));
        var updated = entry with { Status = "promoted", BookingId = booking.Id, PromotedAt = DateTimeOffset.UtcNow };
        _waitlist[entryId] = updated;
        return updated;
    }

    public object LossReport()
    {
        var bookings = Bookings();
        var slots = _slots.Values.ToList();
        var bySpecialty = slots.GroupBy(x => x.Specialty).OrderBy(x => x.Key).Select(group =>
        {
            var specialtyBookings = bookings.Where(x => x.Specialty == group.Key).ToList();
            var capacity = group.Sum(x => x.Capacity);
            var occupied = specialtyBookings.Count(x => x.Status is "scheduled" or "checked_in" or "completed" or "no_show");
            return new
            {
                specialty = group.Key,
                slots = group.Count(),
                capacity,
                occupied,
                scheduled = specialtyBookings.Count(x => x.Status == "scheduled"),
                completed = specialtyBookings.Count(x => x.Status == "completed"),
                noShow = specialtyBookings.Count(x => x.Status == "no_show"),
                cancelled = specialtyBookings.Count(x => x.Status == "cancelled"),
                occupancyPercent = capacity == 0 ? 0 : Math.Round((double)occupied / capacity * 100, 2)
            };
        }).ToArray();
        return new
        {
            bookings = bookings.Count,
            scheduled = bookings.Count(x => x.Status == "scheduled"),
            checkedIn = bookings.Count(x => x.Status == "checked_in"),
            completed = bookings.Count(x => x.Status == "completed"),
            noShow = bookings.Count(x => x.Status == "no_show"),
            cancelled = bookings.Count(x => x.Status == "cancelled"),
            bySpecialty,
            generatedAt = DateTimeOffset.UtcNow
        };
    }

    private static void Reserve(ScheduleSlot slot)
    {
        lock (slot)
        {
            if (slot.Blocked) throw new InvalidOperationException("Horário bloqueado pela unidade.");
            if (slot.Booked >= slot.Capacity) throw new InvalidOperationException("Horário sem capacidade disponível.");
            slot.Booked++;
        }
    }

    private static void Release(ScheduleSlot slot)
    {
        lock (slot) slot.Booked = Math.Max(0, slot.Booked - 1);
    }

    private void SeedGrid(string specialty, string unit, DayOfWeek day, TimeOnly starts, TimeOnly ends, int minutes, int overbook)
    {
        var grid = new ScheduleGrid(Guid.NewGuid(), specialty, unit, day, starts, ends, minutes, overbook, true);
        _grids[grid.Id] = grid;
    }

    private void GenerateSlots(ScheduleGrid grid, DateOnly startDate, int horizonDays)
    {
        for (var d = 0; d < horizonDays; d++)
        {
            var day = startDate.AddDays(d);
            if (day.DayOfWeek != grid.DayOfWeek) continue;
            for (var cursor = grid.StartsAt; cursor < grid.EndsAt; cursor = cursor.AddMinutes(grid.DurationMinutes))
            {
                var start = new DateTimeOffset(day.ToDateTime(cursor), TimeSpan.FromHours(-3));
                var end = start.AddMinutes(grid.DurationMinutes);
                var slot = new ScheduleSlot(Guid.NewGuid(), grid.Id, grid.Specialty, grid.Unit, start, end, 1 + grid.OverbookCapacity, 0, false, null);
                _slots[slot.Id] = slot;
            }
        }
    }

    private static int PriorityRank(string priority) => priority.ToLowerInvariant() switch { "emergency" => 5, "very_high" => 4, "high" => 3, "moderate" => 2, _ => 1 };
}

public sealed record ScheduleGrid(Guid Id, string Specialty, string Unit, DayOfWeek DayOfWeek, TimeOnly StartsAt, TimeOnly EndsAt, int DurationMinutes, int OverbookCapacity, bool Active);
public sealed class ScheduleSlot(Guid id, Guid gridId, string specialty, string unit, DateTimeOffset start, DateTimeOffset end, int capacity, int booked, bool blocked, string? blockReason)
{
    public Guid Id { get; } = id;
    public Guid GridId { get; } = gridId;
    public string Specialty { get; } = specialty;
    public string Unit { get; } = unit;
    public DateTimeOffset Start { get; } = start;
    public DateTimeOffset End { get; } = end;
    public int Capacity { get; } = capacity;
    public int Booked { get; set; } = booked;
    public bool Blocked { get; set; } = blocked;
    public string? BlockReason { get; set; } = blockReason;
}
public sealed record ScheduleQuota(Guid Id, string Specialty, string Unit, string Scope, int RegulationPercent, int UnitPercent, int ReservePercent, DateTimeOffset UpdatedAt);
public sealed record ScheduleBooking(
    Guid Id, Guid SlotId, Guid CitizenId, string CitizenName, string Specialty, string Unit, DateTimeOffset StartsAt,
    string Priority, string Status, string Source, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    Guid? RescheduledFromSlotId, string? ClosureReason, string? ClosedBy);
public sealed record WaitlistEntry(Guid Id, Guid CitizenId, string CitizenName, string Specialty, string? PreferredUnit, string Priority, string RequestedBy, string Status, Guid? BookingId, DateTimeOffset CreatedAt, DateTimeOffset? PromotedAt);
public sealed record BookSlotRequest(Guid SlotId, Guid CitizenId, string CitizenName, string? Priority, string? Source);
public sealed record BookingTransitionRequest(string Status, string? Reason, string? Actor);
public sealed record RescheduleBookingRequest(Guid NewSlotId, string? Reason, string? Actor);
public sealed record CreateWaitlistRequest(Guid CitizenId, string CitizenName, string Specialty, string? PreferredUnit, string? Priority, string? RequestedBy);
public sealed record BlockSlotRequest(bool Blocked, string? Reason);
public sealed record AdjustQuotaRequest(int RegulationPercent, int UnitPercent, int ReservePercent);
public sealed record PromoteWaitlistRequest(Guid SlotId);
