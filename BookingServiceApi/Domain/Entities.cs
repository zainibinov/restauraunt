namespace BookingServiceApi;

/// <summary>Stored in the database as a string (VARCHAR(50)).</summary>
public enum BookingStatus
{
    Pending,
    Confirmed,
    Cancelled,
    Completed,
    NoShow
}

public class Booking
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TableId { get; set; }

    public string GuestName { get; set; } = "";
    public string GuestPhone { get; set; } = "";
    public string GuestEmail { get; set; } = "";
    public int GuestCount { get; set; }

    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }

    public BookingStatus Status { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string ConfirmationCode { get; set; } = "";
    public DateTime CreationDate { get; set; }

    public List<BookingSlot> Slots { get; set; } = new();
    public List<BookingStatusHistory> History { get; set; } = new();
    public List<Reminder> Reminders { get; set; } = new();
}

/// <summary>One occupied point of the time grid. UNIQUE(table_id, slot_datetime) is the DB-level protection from double booking.</summary>
public class BookingSlot
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid TableId { get; set; }
    public DateTime SlotDateTime { get; set; }

    public Booking Booking { get; set; } = null!;
}

public class BookingStatusHistory
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public BookingStatus Status { get; set; }
    public DateTime ChangedAt { get; set; }
    /// <summary>The user who changed the status (Guid.Empty = the system).</summary>
    public Guid ChangedBy { get; set; }

    public Booking Booking { get; set; } = null!;
}

public class Reminder
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public DateTime RemindAt { get; set; }
    public bool IsSent { get; set; }

    public Booking Booking { get; set; } = null!;
}
