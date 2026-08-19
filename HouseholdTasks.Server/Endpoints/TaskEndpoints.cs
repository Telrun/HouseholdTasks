using System.Security.Claims;
using HouseholdTasks.Server.Data;
using HouseholdTasks.Server.Data.Models;
using HouseholdTasks.Server.Services;
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

        // Public, read-only, exact-date lookup — used for the "show tomorrow" toggle on the
        // living-room screen. No overdue-merging here; it's just "what's due on this date".
        app.MapGet("/api/tasks/date/{date}", async (DateOnly date, AppDbContext db) =>
        {
            var tasks = await GetTasksForDate(db, date);
            return Results.Ok(tasks);
        }).AllowAnonymous();

        var group = app.MapGroup("/api/tasks").RequireAuthorization();

        // date filter (yyyy-MM-dd), optional mine=true to only return tasks it's currently
        // this caller's turn on (for a rotating task, that's just whoever's up this occurrence)
        group.MapGet("/", async (DateOnly date, bool? mine, AppDbContext db, ClaimsPrincipal user) =>
        {
            var tasks = await GetTasksForDate(db, date);

            if (mine == true)
            {
                var memberId = GetMemberId(user);
                tasks = tasks.Where(t => t.ActiveAssigneeIds.Contains(memberId)).ToList();
            }

            return Results.Ok(tasks);
        });

        group.MapPost("/", async (CreateHouseholdTaskDto dto, AppDbContext db, ClaimsPrincipal user, PushNotificationSender pushSender) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Title))
                return Results.BadRequest("Title is required.");

            var task = new HouseholdTask
            {
                Title = dto.Title.Trim(),
                Description = dto.Description,
                Category = dto.Category,
                DueDate = dto.DueDate,
                DueTime = dto.DueTime,
                Recurrence = dto.Recurrence,
                AssignmentMode = dto.AssignmentMode,
                CreatedByMemberId = GetMemberId(user)
            };

            var roster = dto.AssignedFamilyMemberIds.Distinct().ToList();
            var isRotating = dto.AssignmentMode == TaskAssignmentMode.Rotating
                && dto.Recurrence != RecurrenceType.None
                && roster.Count > 1;

            for (var i = 0; i < roster.Count; i++)
            {
                task.Assignments.Add(new TaskAssignment
                {
                    FamilyMemberId = roster[i],
                    RosterOrder = i,
                    // Rotating tasks start with the first person in the roster on duty;
                    // everyone else stays "in the roster" but isn't on duty yet.
                    IsActiveTurn = !isRotating || i == 0
                });
            }

            db.Tasks.Add(task);
            await db.SaveChangesAsync();

            // Notify whoever's actually on the hook right now — for a shared task that's
            // everyone in the roster; for a rotating one, only the person starting it off
            // (the rest will get their own notification once it's their turn).
            var notifyIds = isRotating ? new List<int> { roster[0] } : roster;
            foreach (var memberId in notifyIds)
            {
                await pushSender.SendToMemberAsync(
                    memberId,
                    "Ny oppgave",
                    task.Title,
                    "/my-tasks");
            }

            return Results.Created($"/api/tasks/{task.Id}", task.Id);
        });

        group.MapPost("/{id:int}/complete", async (int id, AppDbContext db, ClaimsPrincipal user, PushNotificationSender pushSender) =>
        {
            var task = await db.Tasks.Include(t => t.Assignments).ThenInclude(a => a.FamilyMember)
                .FirstOrDefaultAsync(t => t.Id == id);
            if (task is null) return Results.NotFound();

            var memberId = GetMemberId(user);
            // On a rotating task, only whoever is currently on duty (or an admin) can
            // complete it — everyone else is "in the roster" for a future turn, not now.
            var isAssigned = task.Assignments.Any(a => a.FamilyMemberId == memberId && a.IsActiveTurn);
            var isAdmin = user.IsInRole("Admin");

            if (!isAssigned && !isAdmin)
                return Results.Forbid();

            task.IsCompleted = true;
            task.CompletedAtUtc = DateTime.UtcNow;
            task.CompletedByMemberId = memberId;

            int? notifyNextTurnMemberId = null;

            // Recurring task: spin up the next occurrence now, based on this task's
            // original due date (not today) so the cadence doesn't drift if it was
            // completed late. Due time carries over unchanged — it's a time-of-day, not
            // tied to any particular occurrence.
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
                    DueTime = task.DueTime,
                    Recurrence = task.Recurrence,
                    AssignmentMode = task.AssignmentMode,
                    CreatedByMemberId = task.CreatedByMemberId
                };

                var rosterCount = task.Assignments.Count;
                var isRotating = task.AssignmentMode == TaskAssignmentMode.Rotating && rosterCount > 1;

                // Whoever was on duty this time tells us where to pick up next time.
                var currentTurnOrder = task.Assignments.FirstOrDefault(a => a.IsActiveTurn)?.RosterOrder ?? 0;
                var nextTurnOrder = isRotating ? (currentTurnOrder + 1) % rosterCount : currentTurnOrder;

                foreach (var assignment in task.Assignments)
                {
                    var isNextActive = !isRotating || assignment.RosterOrder == nextTurnOrder;
                    nextTask.Assignments.Add(new TaskAssignment
                    {
                        FamilyMemberId = assignment.FamilyMemberId,
                        RosterOrder = assignment.RosterOrder,
                        IsActiveTurn = isNextActive
                    });

                    // Only worth a "it's your turn" notification when the turn actually
                    // moved to someone new — not for a Shared task where everyone's
                    // always "active" and this would fire for the whole roster every time.
                    if (isRotating && isNextActive)
                    {
                        notifyNextTurnMemberId = assignment.FamilyMemberId;
                    }
                }
                db.Tasks.Add(nextTask);
            }

            await db.SaveChangesAsync();

            if (notifyNextTurnMemberId is not null)
            {
                await pushSender.SendToMemberAsync(
                    notifyNextTurnMemberId.Value,
                    "Din tur",
                    task.Title,
                    "/my-tasks");
            }

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

        // Admin-only: edit an existing task's details and reassign it. Reassigning always
        // restarts the rotation at roster position 0 — trying to preserve "whose turn is it"
        // across an arbitrary roster edit isn't something there's a sensible default for.
        group.MapPut("/{id:int}", async (int id, CreateHouseholdTaskDto dto, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Title))
                return Results.BadRequest("Title is required.");

            var task = await db.Tasks.Include(t => t.Assignments).FirstOrDefaultAsync(t => t.Id == id);
            if (task is null) return Results.NotFound();

            task.Title = dto.Title.Trim();
            task.Description = dto.Description;
            task.Category = dto.Category;
            task.DueDate = dto.DueDate;
            task.DueTime = dto.DueTime;
            task.Recurrence = dto.Recurrence;
            task.AssignmentMode = dto.AssignmentMode;

            // Replace the assignment list wholesale rather than trying to diff it.
            db.TaskAssignments.RemoveRange(task.Assignments);
            task.Assignments.Clear();

            var roster = dto.AssignedFamilyMemberIds.Distinct().ToList();
            var isRotating = dto.AssignmentMode == TaskAssignmentMode.Rotating
                && dto.Recurrence != RecurrenceType.None
                && roster.Count > 1;

            for (var i = 0; i < roster.Count; i++)
            {
                task.Assignments.Add(new TaskAssignment
                {
                    FamilyMemberId = roster[i],
                    RosterOrder = i,
                    IsActiveTurn = !isRotating || i == 0
                });
            }

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

        var tasks = await db.Tasks
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
                DueTime = t.DueTime,
                IsCompleted = t.IsCompleted,
                IsOverdue = !t.IsCompleted && t.DueDate < today,
                Recurrence = t.Recurrence,
                AssignmentMode = t.AssignmentMode,
                CompletedAtUtc = t.CompletedAtUtc,
                CompletedByName = t.CompletedByMember != null ? t.CompletedByMember.Name : null,
                AssignedFamilyMemberIds = t.Assignments.OrderBy(a => a.RosterOrder).Select(a => a.FamilyMemberId).ToList(),
                AssignedFamilyMemberNames = t.Assignments.OrderBy(a => a.RosterOrder).Select(a => a.FamilyMember.Name).ToList(),
                IsRotating = t.AssignmentMode == TaskAssignmentMode.Rotating && t.Assignments.Count > 1,
                ActiveAssigneeIds = t.Assignments.Where(a => a.IsActiveTurn).Select(a => a.FamilyMemberId).ToList(),
                ActiveAssigneeNames = t.Assignments.Where(a => a.IsActiveTurn).Select(a => a.FamilyMember.Name).ToList()
            })
            .ToListAsync();

        MarkOverdueDueToday(tasks, today);
        return tasks;
    }

    // Today's tasks (whatever their status) + any still-incomplete task from a prior date.
    // Old completed tasks still roll off naturally; forgotten ones stick around instead.
    private static async Task<List<HouseholdTaskDto>> GetTodayAndOverdueTasks(AppDbContext db, DateOnly today)
    {
        var tasks = await db.Tasks
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
                DueTime = t.DueTime,
                IsCompleted = t.IsCompleted,
                IsOverdue = !t.IsCompleted && t.DueDate < today,
                Recurrence = t.Recurrence,
                AssignmentMode = t.AssignmentMode,
                CompletedAtUtc = t.CompletedAtUtc,
                CompletedByName = t.CompletedByMember != null ? t.CompletedByMember.Name : null,
                AssignedFamilyMemberIds = t.Assignments.OrderBy(a => a.RosterOrder).Select(a => a.FamilyMemberId).ToList(),
                AssignedFamilyMemberNames = t.Assignments.OrderBy(a => a.RosterOrder).Select(a => a.FamilyMember.Name).ToList(),
                IsRotating = t.AssignmentMode == TaskAssignmentMode.Rotating && t.Assignments.Count > 1,
                ActiveAssigneeIds = t.Assignments.Where(a => a.IsActiveTurn).Select(a => a.FamilyMemberId).ToList(),
                ActiveAssigneeNames = t.Assignments.Where(a => a.IsActiveTurn).Select(a => a.FamilyMember.Name).ToList()
            })
            .ToListAsync();

        MarkOverdueDueToday(tasks, today);
        return tasks;
    }

    // The SQL projection above only flags a task overdue when its DueDate is a past date —
    // that part is safe to translate to SQL. Same-day "due at 14:00, it's now 16:00, still
    // not done" overdue detection needs a DateTime comparison against the current instant,
    // which isn't worth fighting EF's SQL translation for, so it's done here in memory
    // instead, after the (small) result set is already materialized.
    private static void MarkOverdueDueToday(List<HouseholdTaskDto> tasks, DateOnly today)
    {
        var now = DateTime.Now;
        foreach (var task in tasks)
        {
            if (!task.IsCompleted && !task.IsOverdue && task.DueDate == today)
            {
                if (now > task.DueDate.ToDateTime(task.DueTime))
                {
                    task.IsOverdue = true;
                }
            }
        }
    }
}
