namespace InstituteHub.Infrastructure.Persistence.Seeding;

/// <summary>Bound from the "Seed" configuration section. Keep passwords in user-secrets / env vars.</summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string? PlatformAdminEmail { get; set; }
    public string? PlatformAdminPassword { get; set; }

    /// <summary>Development only: create a demo institute with 30 fake students.</summary>
    public bool DemoData { get; set; }
    public string? DemoOwnerEmail { get; set; } = "owner@demo.local";
    public string? DemoOwnerPassword { get; set; }
}
