using System.Security.Claims;
using DegreePilot.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DegreePilot.Api.Application;

public sealed class WorkspaceAccess(AcademicDbContext db)
{
    public async Task<bool> CanReadAsync(ClaimsPrincipal actor, string studentId, CancellationToken ct) =>
        actor.IsInRole("student") ? actor.FindFirstValue(ClaimTypes.NameIdentifier) == studentId :
        actor.IsInRole("advisor") && await AssignedAsync(actor.FindFirstValue(ClaimTypes.NameIdentifier)!, studentId, ct);

    public Task<bool> AssignedAsync(string advisorId, string studentId, CancellationToken ct) =>
        db.AdvisorAssignments.AsNoTracking().AnyAsync(a => a.AdvisorId == advisorId && a.StudentId == studentId && a.Active, ct);
}

public static class AuditTrail
{
    public static void Add(AcademicDbContext db, TimeProvider clock, string actor, string action,
        string resource, int revision = 1, string hash = "") => db.MutationAudits.Add(new()
        {
            Id = Guid.NewGuid(),
            ActorId = actor,
            Action = action,
            ResourceId = resource,
            Revision = revision,
            ContentHash = hash,
            OccurredAt = clock.GetUtcNow()
        });
}

public sealed class WorkflowException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
