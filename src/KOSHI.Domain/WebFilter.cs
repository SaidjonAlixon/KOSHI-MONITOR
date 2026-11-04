namespace KOSHI.Domain;

/// <summary>A website blocked on every agent in all browsers (the domain and all its subdomains).</summary>
public sealed class BlockedSite
{
    public Guid Id { get; set; }

    /// <summary>Normalised host, lower case, without scheme, path or leading "www.".</summary>
    public string Domain { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Note { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public List<SiteAllowance> Allowances { get; set; } = [];
}

/// <summary>Timed exception to a blocked site, for one device or (DeviceId null) for all devices.</summary>
public sealed class SiteAllowance
{
    public Guid Id { get; set; }
    public Guid BlockedSiteId { get; set; }
    public BlockedSite? BlockedSite { get; set; }
    public Guid? DeviceId { get; set; }
    public Device? Device { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? Reason { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? CreatedByEmail { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;
}
