using System.Security.Claims;
using HouseholdTasks.Server.Data;
using HouseholdTasks.Server.Data.Models;
using HouseholdTasks.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace HouseholdTasks.Server.Endpoints;

public static class TaskEndpoints
{
    public static void MapTaskEndpoints(this WebApplication app)
    {
        // Public, read-only, no login required — for the living-room "Today's tasks" screen.
        // Shows today's tasks plus anything still incomplete from earlier dates, so a
        // forgotten task keeps showing (highlighted as overdue) instead of quietly
        // disappearing the day after it was due.
        app.MapGet("/api/tasks/today", async (AppDbContext db) =>
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var tasks = await GetTodayAndOverdueTasks(db, today);
            return Results.Ok(tasks);
        }).AllowAnonymous();

        var group = app.MapGroup("/api/tasks").RequireAuthorization();

        // date filter (yyyy-MM-dd), optional mine=true to only return tasks assigned to caller
        group.MapGet("/", async (DateOnly date, bool? mine, AppDbContext db, ClaimsPrincipal user) =>
        {
            var tasks = await GetTasksForDate(db, date);

            if (mine == true)
            {
                var memberId = GetMemberId(user);
                tasks = tasks.Where(t => t.AssignedFamilyMemberIds.Contains(memberId)).ToList();
            }

            return Results.Ok(tasks);
        });

        group.MapPost("/", async (CreateHouseholdTaskDto dto, AppDbContext db, ClaimsPrincipal user) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Title))
                return Results.BadRequest("Title is required.");

            var task = new HouseholdTask
            {
                Title = dto.Title.Trim(),
                Description = dto.Description,
                Category = dto.Category,
                DueDate = dto.DueDate,
                Recurrence = dto.Recurrence,
                CreatedByMemberId = GetMemberId(user)
            };

            foreach (var memberId in dto.AssignedFamilyMemberIds.Distinct())
            {
                task.Assignments.Add(new TaskAssignment { FamilyMemberId = memberId });
            }

            db.Tasks.Add(task);
            await db.SaveChangesAsync();

            return Results.Created($"/api/tasks/{task.Id}", task.Id);
        });

        group.MapPost("/{id:int}/complete", async (int id, AppDbContext db, ClaimsPrincipal user) =>
        {
            var task = await db.Tasks.Include(t => t.Assignments).FirstOrDefaultAsync(t => t.Id == id);
            if (task is null) return Results.NotFound();

            var memberId = GetMemberId(user);
            var isAssigned = task.Assignments.Any(a => a.FamilyMemberId == memberId);
            var isAdmin = user.IsInRole("Admin");

            if (!isAssigned && !isAdmin)
                return Results.Forbid();

            task.IsCompleted = true;
            task.CompletedAtUtc = DateTime.UtcNow;
            task.CompletedByMemberId = memberId;

            // Recurring task: spin up the next occurrence now, based on this task's
            // original due date (not today) so the cadence doesn't drift if it was
            // completed late.
            if (task.Recurrence != RecurrenceType.None)
            {
                var nextDueDate = task.Recurrence switch
                {
                    RecurrenceType.Daily => task.DueDate.AddDays(1),
                    RecurrenceType.Weekly => task.DueDate.AddDays(7),
                    RecurrenceType.Monthly => task.DueDate.AddMonths(1),
                    _ => task.DueDate
                };

                var nextTask = new HouseholdTask
                {
                    Title = task.Title,
                    Description = task.Description,
                    Category = task.Category,
                    DueDate = nextDueDate,
                    Recurrence = task.Recurrence,
                    CreatedByMemberId = task.CreatedByMemberId
                };
                foreach (var assignment in task.Assignments)
                {
                    nextTask.Assignments.Add(new TaskAssignment { FamilyMemberId = assignment.FamilyMemberId });
                }
                db.Tasks.Add(nextTask);
            }

            await db.SaveChangesAsync();

            return Results.Ok();
        });

        // Admin-only: reset a task that was marked done incorrectly.
        group.MapPost("/{id:int}/reset", async (int id, AppDbContext db) =>
        {
            var task = await db.Tasks.FindAsync(id);
            if (task is null) return Results.NotFound();

            task.IsCompleted = false;
            task.CompletedAtUtc = null;
            task.CompletedByMemberId = null;
            await db.SaveChangesAsync();

            return Results.Ok();
        }).RequireAuthorization("AdminOnly");

        group.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var task = await db.Tasks.FindAsync(id);
            if (task is null) return Results.NotFound();

            db.Tasks.Remove(task);
            await db.SaveChangesAsync();
            return Results.Ok();
        }).RequireAuthorization("AdminOnly");
    }

    private static int GetMemberId(ClaimsPrincipal user)
    {
        var claim = user.FindFirstValue("household_member_id");
        return claim is null ? 0 : int.Parse(claim);
    }

    private static async Task<List<HouseholdTaskDto>> GetTasksForDate(AppDbContext db, DateOnly date)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        return await db.Tasks
            .Where(t => t.DueDate == date)
            .Include(t => t.Assignments).ThenInclude(a => a.FamilyMember)
            .Include(t => t.CompletedByMember)
            .OrderBy(t => t.IsCompleted).ThenBy(t => t.Category).ThenBy(t => t.Title)
            .Select(t => new HouseholdTaskDto
            {
                Id = t.Id,
                Title = t.Title,
                Description = t.Description,
                Category = t.Category,
                DueDate = t.DueDate,
                IsCompleted = t.IsCompleted,
                IsOverdue = !t.IsCompleted && t.DueDate < today,
                Recurrence = t.Recurrence,
                CompletedAtUtc = t.CompletedAtUtc,
                CompletedByName = t.CompletedByMember != null ? t.CompletedByMember.Name : null,
                AssignedFamilyMemberIds = t.Assignments.Select(a => a.FamilyMemberId).ToList(),
                AssignedFamilyMemberNames = t.Assignments.Select(a => a.FamilyMember.Name).ToList()
            })
            .ToListAsync();
    }

    // Today's tasks (whatever their status) + any still-incomplete task from a prior date.
    // Old completed tasks still roll off naturally; forgotten ones stick around instead.
    private static async Task<List<HouseholdTaskDto>> GetTodayAndOverdueTasks(AppDbContext db, DateOnly today)
    {
        return await db.Tasks
            .Where(t => t.DueDate == today || (t.DueDate < today && !t.IsCompleted))
            .Include(t => t.Assignments).ThenInclude(a => a.FamilyMember)
            .Include(t => t.CompletedByMember)
            .OrderBy(t => t.IsCompleted)
            .ThenByDescending(t => t.DueDate < today) // overdue first among the incomplete ones
            .ThenBy(t => t.Category).ThenBy(t => t.Title)
            .Select(t => new HouseholdTaskDto
            {
                Id = t.Id,
                Title = t.Title,
                Description = t.Description,
                Category = t.Category,
                DueDate = t.DueDate,
                IsCompleted = t.IsCompleted,
                IsOverdue = !t.IsCompleted && t.DueDate < today,
                Recurrence = t.Recurrence,
                CompletedAtUtc = t.CompletedAtUtc,
                CompletedByName = t.CompletedByMember != null ? t.CompletedByMember.Name : null,
                AssignedFamilyMemberIds = t.Assignments.Select(a => a.FamilyMemberId).ToList(),
                AssignedFamilyMemberNames = t.Assignments.Select(a => a.FamilyMember.Name).ToList()
            })
            .ToListAsync();
    }
}
