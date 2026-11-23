namespace AvailabilityServiceApi;

/// <summary>Stored in the database as a string (VARCHAR(50)).</summary>
public enum TableType
{
    Standard,
    Vip,
    Bar
}

public class Hall
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    public List<RestaurantTable> Tables { get; set; } = new();
    public List<BlackoutDate> BlackoutDates { get; set; } = new();
}

public class RestaurantTable
{
    public Guid Id { get; set; }
    public Guid HallId { get; set; }
    public int TableNumber { get; set; }
    public int Capacity { get; set; }
    public TableType TableType { get; set; }

    public Hall Hall { get; set; } = null!;
}

public class WorkingHours
{
    public Guid Id { get; set; }
    /// <summary>0 = Sunday ... 6 = Saturday (same as System.DayOfWeek).</summary>
    public int DayOfWeek { get; set; }
    public TimeSpan OpenTime { get; set; }
    public TimeSpan CloseTime { get; set; }
    public int SlotStepMinutes { get; set; }

    public List<TimeSlot> TimeSlots { get; set; } = new();
}

/// <summary>One point of the time grid (for example 2026-11-20 19:00). The grid is the same for all tables.</summary>
public class TimeSlot
{
    public Guid Id { get; set; }
    public Guid WorkingHoursId { get; set; }
    public DateTime SlotDate { get; set; }
    public TimeSpan SlotTime { get; set; }
    /// <summary>false = the slot is closed manually. Free tables are calculated per request.</summary>
    public bool IsAvailable { get; set; } = true;

    public WorkingHours WorkingHours { get; set; } = null!;
}

public class BlackoutDate
{
    public Guid Id { get; set; }
    public DateTime Date { get; set; }
    public string Reason { get; set; } = "";
    /// <summary>true = the whole day is closed for booking.</summary>
    public bool IsFullHall { get; set; }
    /// <summary>null = the whole restaurant (all halls).</summary>
    public Guid? HallId { get; set; }

    public Hall? Hall { get; set; }
}

/// <summary>A single settings row: how long a table is occupied and the cleaning buffer.</summary>
public class BookingDuration
{
    public Guid Id { get; set; }
    public int DefaultDurationMinutes { get; set; }
    public int CleaningBufferMinutes { get; set; }
}
