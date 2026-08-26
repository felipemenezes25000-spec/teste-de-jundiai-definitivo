using Jundiai.Api;
using Microsoft.AspNetCore.Http.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});
builder.Services.AddSingleton<DemoStore>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Jundiai-POC"] = "RCE-008-2026";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    await next();
});

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "Jundiai HealthOS POC",
    contract = "RCE 008/2026",
    time = DateTimeOffset.UtcNow
}));

app.MapGet("/api/dashboard", (DemoStore store) => Results.Ok(store.Dashboard()));
app.MapGet("/api/citizens", (DemoStore store) => Results.Ok(store.Citizens()));
app.MapGet("/api/citizens/{id:guid}", (Guid id, DemoStore store) =>
    store.Citizen(id) is { } citizen ? Results.Ok(citizen) : Results.NotFound());

app.MapGet("/api/regulation", (DemoStore store) => Results.Ok(store.Regulation()));
app.MapPost("/api/regulation", (CreateRegulationRequest request, DemoStore store) =>
    Execute(() => Results.Created("/api/regulation", store.CreateRegulation(request))));
app.MapPost("/api/regulation/{id:guid}/transition", (Guid id, RegulationTransitionRequest request, DemoStore store) =>
    Execute(() => store.AdvanceRegulation(id, request.Status, request.ScheduledAt) is { } updated ? Results.Ok(updated) : Results.NotFound()));

app.MapGet("/api/sus/production", (DemoStore store) => Results.Ok(store.Production()));
app.MapGet("/api/sus/billing/batches", (DemoStore store) => Results.Ok(store.BillingBatches()));
app.MapPost("/api/sus/billing/batches", (CreateBillingBatchRequest request, DemoStore store) =>
    Execute(() => Results.Created("/api/sus/billing/batches", store.CreateBillingBatch(request.Competence))));
app.MapPost("/api/sus/billing/batches/{id:guid}/validate", (Guid id, DemoStore store) =>
    Execute(() => store.RevalidateBilling(id) is { } batch ? Results.Ok(batch) : Results.NotFound()));
app.MapPost("/api/sus/billing/batches/{id:guid}/close", (Guid id, DemoStore store) =>
    Execute(() => store.CloseBilling(id) is { } batch ? Results.Ok(batch) : Results.NotFound()));
app.MapGet("/api/sus/billing/batches/{id:guid}/export-demo", (Guid id, DemoStore store) =>
    Execute(() => Results.Text(store.ExportBillingDemonstration(id), "text/plain; charset=utf-8")));

app.MapGet("/api/immunization/lots", (DemoStore store) => Results.Ok(store.VaccineLots()));
app.MapGet("/api/immunization/history", (Guid? citizenId, DemoStore store) => Results.Ok(store.Immunizations(citizenId)));
app.MapPost("/api/immunization/administer", (AdministerVaccineRequest request, DemoStore store) =>
    Execute(() => Results.Created("/api/immunization/history", store.AdministerVaccine(request))));

app.MapGet("/api/pharmacy/inventory", (DemoStore store) => Results.Ok(store.Inventory()));
app.MapGet("/api/pharmacy/movements", (DemoStore store) => Results.Ok(store.InventoryMovements()));
app.MapPost("/api/pharmacy/dispense", (DispenseRequest request, DemoStore store) =>
    Execute(() => Results.Created("/api/pharmacy/movements", store.Dispense(request))));

app.MapGet("/api/psf/households", (DemoStore store) => Results.Ok(store.Households()));
app.MapGet("/api/psf/acs/visits", (DemoStore store) => Results.Ok(store.AcsVisits()));
app.MapPost("/api/psf/acs/sync", (AcsSyncRequest request, DemoStore store) =>
    Execute(() => Results.Ok(new { synced = store.SyncAcs(request) })));

app.MapGet("/api/dental/{citizenId:guid}/odontogram", (Guid citizenId, DemoStore store) =>
    Execute(() => Results.Ok(store.Dental(citizenId))));
app.MapPut("/api/dental/{citizenId:guid}/odontogram/{tooth:int}", (Guid citizenId, int tooth, UpdateToothRequest request, DemoStore store) =>
    Execute(() => Results.Ok(store.UpdateTooth(citizenId, tooth, request))));

app.MapGet("/api/diagnostics/exams", (DemoStore store) => Results.Ok(store.Exams()));
app.MapPost("/api/diagnostics/exams", (ScheduleExamRequest request, DemoStore store) =>
    Execute(() => Results.Created("/api/diagnostics/exams", store.ScheduleExam(request))));

app.MapGet("/api/audit", (DemoStore store) => Results.Ok(store.Audit()));
app.MapPost("/api/audit/events", (AuditRequest request, HttpContext context, DemoStore store) =>
{
    var actor = context.Request.Headers["X-Demo-User"].FirstOrDefault() ?? "poc.operator";
    store.AuditExternal(actor, request.Action, request.Resource, request.Detail);
    return Results.Accepted();
});

app.MapFallbackToFile("index.html");
app.Run();

static IResult Execute(Func<IResult> action)
{
    try
    {
        return action();
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Regra de negócio", detail: ex.Message);
    }
    catch (ArgumentException ex)
    {
        return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Entrada inválida", detail: ex.Message);
    }
}

public sealed record RegulationTransitionRequest(string Status, DateTimeOffset? ScheduledAt);
public sealed record AuditRequest(string Action, string Resource, string? Detail);

public partial class Program { }
