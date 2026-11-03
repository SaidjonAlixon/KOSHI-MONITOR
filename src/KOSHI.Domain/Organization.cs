namespace KOSHI.Domain;

public sealed class Department
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Branch { get; set; }
    public bool IsDemo { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<Employee> Employees { get; set; } = [];
}

public sealed class Employee
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string EmployeeNumber { get; set; } = string.Empty;
    public string? Email { get; set; }

    /// <summary>Job role; KPI comparisons must only be made between comparable positions.</summary>
    public string? Position { get; set; }
    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public Guid? WorkScheduleId { get; set; }
    public WorkSchedule? WorkSchedule { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDemo { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<Device> Devices { get; set; } = [];
}

public sealed class WorkSchedule
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "Asia/Tashkent";

    /// <summary>Bit mask: bit 0 = Sunday ... bit 6 = Saturday (matches <see cref="DayOfWeek"/>).</summary>
    public int WorkDaysMask { get; set; } = WorkDays.MondayToFriday;
    public TimeOnly WorkStart { get; set; } = new(9, 0);
    public TimeOnly WorkEnd { get; set; } = new(18, 0);
    public TimeOnly? BreakStart { get; set; } = new(13, 0);
    public TimeOnly? BreakEnd { get; set; } = new(14, 0);
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public static class WorkDays
{
    public const int MondayToFriday = 0b0111110;
    public const int All = 0b1111111;

    public static bool Contains(int mask, DayOfWeek day) => (mask & (1 << (int)day)) != 0;
}
