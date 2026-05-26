namespace GameDB.Domain.Entities;

public partial class Notification
{
    public int NotificationId { get; set; }

    public int UserId { get; set; }

    public string Type { get; set; } = null!;

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? Description { get; set; }

    public virtual User User { get; set; } = null!;
}
