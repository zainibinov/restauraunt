using System.ComponentModel.DataAnnotations;

namespace AvailabilityServiceApi;

// ---------- halls ----------

public class HallRequest
{
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    public string? Description { get; set; }
}

public class HallResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

// ---------- tables ----------

public class TableRequest
{
    [Range(1, 1000)] public int TableNumber { get; set; }
    [Range(1, 100)] public int Capacity { get; set; }
    /// <summary>Standard, Vip or Bar</summary>
    [Required] public string TableType { get; set; } = "";
}

public class TableResponse
{
    public Guid Id { get; set; }
    public int TableNumber { get; set; }
    public int Capacity { get; set; }
    public string TableType { get; set; } = "";
}

// ---------- working hours ----------

public class WorkingHoursRequest
{
    /// <summary>0 = Sunday ... 6 = Saturday</summary>
    [Range(0, 6)] public int DayOfWeek { get; set; }
    public TimeSpan OpenTime { get; set; }
    public TimeSpan CloseTime { get; set; }
    [Range(5, 240)] public int SlotStepMinutes { get; set; }
}

public class WorkingHoursResponse
{
    public Guid Id { get; set; }
    public int DayOfWeek { get; set; }
    public TimeSpan OpenTime { get; set; }
    public TimeSpan CloseTime { get; set; }
    public int SlotStepMinutes { get; set; }
}

// ---------- blackout dates ----------

public class BlackoutDateRequest
{
    public DateTime Date { get; set; }
    [Required, MaxLength(200)] public string Reason { get; set; } = "";
    public bool IsFullHall { get; set; } = true;
    /// <summary>null = the whole restaurant</summary>
    public Guid? HallId { get; set; }
}

public class BlackoutDateResponse
{
    public Guid Id { get; set; }
    public DateTime Date { get; set; }
    public string Reason { get; set; } = "";
    public bool IsFullHall { get; set; }
    public Guid? HallId { get; set; }
}

// ---------- slots ----------

/// <summary>Query parameters of GET /api/halls/{hallId}/slots; all are optional.</summary>
public class FreeSlotsRequest
{
    public DateTime? Date { get; set; }
    public TimeSpan? Time { get; set; }
    [Range(1, 100)] public int? GuestsCount { get; set; }
    public string? TableType { get; set; }
}

public class SlotResponse
{
    public TimeSpan SlotTime { get; set; }
    public Guid TableId { get; set; }
    public int TableNumber { get; set; }
    public int Capacity { get; set; }
    public bool IsAvailable { get; set; }
}

public class SlotAvailabilityResponse
{
    public TimeSpan SlotTime { get; set; }
    public bool IsAvailable { get; set; }
}

public class TableAvailabilityResponse
{
    public Guid TableId { get; set; }
    public DateTime Date { get; set; }
    public List<SlotAvailabilityResponse> Slots { get; set; } = new();
}

/// <summary>What BookingService will ask over gRPC later (GetTableInfo).</summary>
public record TableInfoDto(Guid TableId, Guid HallId, int Capacity, string TableType);
