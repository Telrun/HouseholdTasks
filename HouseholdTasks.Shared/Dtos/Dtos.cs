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
    public bool IsCompleted { get; set; }
    public bool IsOverdue { get; set; }
    public RecurrenceType Recurrence { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? CompletedByName { get; set; }
    public List<int> AssignedFamilyMemberIds { get; set; } = new();
    public List<string> AssignedFamilyMemberNames { get; set; } = new();
}

public class CreateHouseholdTaskDto
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TaskCategory Category { get; set; }
    public DateOnly DueDate { get; set; }
    public RecurrenceType Recurrence { get; set; }
    public List<int> AssignedFamilyMemberIds { get; set; } = new();
}
