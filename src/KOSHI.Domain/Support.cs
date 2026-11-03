namespace KOSHI.Domain;

public enum SupportSender { Device = 0, Admin = 1 }

/// <summary>A message in the two-way support chat between an employee's agent and administrators.</summary>
public sealed class SupportMessage
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public SupportSender Sender { get; set; }

    /// <summary>Set for admin messages.</summary>
    public Guid? SenderUserId { get; set; }
    public string? SenderName { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    /// <summary>When the other side has seen it. Device messages are read by admins; admin messages by the device.</summary>
    public DateTime? ReadAt { get; set; }
}
