using HouseholdTasks.Shared.Dtos;

namespace HouseholdTasks.Server.Data.Models;

public class FamilyMember
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }

    public List<TaskAssignment> Assignments { get; set; } = new();
}

public class HouseholdTask
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TaskCategory Category { get; set; }
    public DateOnly DueDate { get; set; }
    public TimeOnly DueTime { get; set; } = new(23, 59);
    public RecurrenceType Recurrence { get; set; }
    public TaskAssignmentMode AssignmentMode { get; set; }

    public bool IsCompleted { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? CompletedByMemberId { get; set; }
    public FamilyMember? CompletedByMember { get; set; }

    public int CreatedByMemberId { get; set; }

    public List<TaskAssignment> Assignments { get; set; } = new();
}

public class TaskAssignment
{
    public int Id { get; set; }

    public int HouseholdTaskId { get; set; }
    public HouseholdTask HouseholdTask { get; set; } = null!;

    public int FamilyMemberId { get; set; }
    public FamilyMember FamilyMember { get; set; } = null!;

    /// <summary>Position in the rotation roster (0-based), in the order people were assigned.</summary>
    public int RosterOrder { get; set; }

    /// <summary>Whether this person is "on duty" for this specific occurrence. True for
    /// everyone unless the task is Rotating, in which case exactly one is true.</summary>
    public bool IsActiveTurn { get; set; } = true;
}

/// <summary>An FCM registration token for one device/browser. A member can have several
/// (phone + tablet + laptop), so this is many-to-one against FamilyMember, not a single
/// column on it. Token itself is unique — if the same browser somehow re-registers under a
/// different member (e.g. someone signs in as themselves on a shared family tablet after
/// someone else already had), the newest registration wins that token.</summary>
public class DeviceToken
{
    public int Id { get; set; }

    public int FamilyMemberId { get; set; }
    public FamilyMember FamilyMember { get; set; } = null!;

    public string Token { get; set; } = string.Empty;
    public string Platform { get; set; } = "web";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
}
