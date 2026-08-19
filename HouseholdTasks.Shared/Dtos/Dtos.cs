namespace HouseholdTasks.Shared.Dtos;

public enum TaskCategory
{
    Støvsuging,
    Rengjøring,
    Cooking,
    Rydding,
    Andre
}

public enum RecurrenceType
{
    None,
    Daily,
    Weekly,
    Monthly
}

public enum TaskAssignmentMode
{
    /// <summary>Everyone assigned can complete it, any time — the original behavior.</summary>
    Shared,
    /// <summary>Only relevant for recurring tasks with 2+ people: one person is "on duty"
    /// per occurrence, and the turn passes to the next person in the roster each time it's
    /// completed, wrapping back to the first once everyone's had a turn.</summary>
    Rotating
}

public class UserInfoDto
{
    public bool IsAuthenticated { get; set; }
    public int? FamilyMemberId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public bool IsAdmin { get; set; }
}

public class FamilyMemberDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
}

public class CreateFamilyMemberDto
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
}

public class HouseholdTaskDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TaskCategory Category { get; set; }
    public DateOnly DueDate { get; set; }
    public TimeOnly DueTime { get; set; } = new(23, 59);
    public bool IsCompleted { get; set; }
    public bool IsOverdue { get; set; }
    public RecurrenceType Recurrence { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? CompletedByName { get; set; }

    public TaskAssignmentMode AssignmentMode { get; set; }

    /// <summary>Full roster this task rotates between (in rotation order), even if it isn't their turn.</summary>
    public List<int> AssignedFamilyMemberIds { get; set; } = new();
    public List<string> AssignedFamilyMemberNames { get; set; } = new();

    /// <summary>True when this is a recurring task in Rotating mode with 2+ people.</summary>
    public bool IsRotating { get; set; }

    /// <summary>Who's actually "on duty" this occurrence — same as the full roster unless IsRotating.</summary>
    public List<int> ActiveAssigneeIds { get; set; } = new();
    public List<string> ActiveAssigneeNames { get; set; } = new();
}

public class CreateHouseholdTaskDto
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TaskCategory Category { get; set; }
    public DateOnly DueDate { get; set; }
    public TimeOnly DueTime { get; set; } = new(23, 59);
    public RecurrenceType Recurrence { get; set; }
    public TaskAssignmentMode AssignmentMode { get; set; }
    public List<int> AssignedFamilyMemberIds { get; set; } = new();
}
