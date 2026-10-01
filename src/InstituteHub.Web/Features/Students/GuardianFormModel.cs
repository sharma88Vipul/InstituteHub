using InstituteHub.Application.Students;
using InstituteHub.Domain.Students;

namespace InstituteHub.Web.Features.Students;

/// <summary>Editable guardian fields, or a link to an existing guardian found by phone (siblings).</summary>
public sealed class GuardianFormModel
{
    public string Phone { get; set; } = "";
    public string FullName { get; set; } = "";
    public GuardianRelation Relation { get; set; } = GuardianRelation.Father;
    public string? AltPhone { get; set; }
    public string? Email { get; set; }
    public bool WhatsAppOptIn { get; set; } = true;

    public Guid? LinkedGuardianId { get; set; }
    public string? LinkedGuardianName { get; set; }
    public IReadOnlyList<GuardianMatch> Matches { get; set; } = [];
    public string? LastLookedUpPhone { get; set; }

    public GuardianInput ToInput() => new(FullName, Relation, Phone, AltPhone, Email, WhatsAppOptIn);

    public static GuardianFormModel From(GuardianSummary g) => new()
    {
        Phone = g.Phone,
        FullName = g.FullName,
        Relation = g.Relation,
        AltPhone = g.AltPhone,
        Email = g.Email,
        WhatsAppOptIn = g.WhatsAppOptIn,
    };
}
