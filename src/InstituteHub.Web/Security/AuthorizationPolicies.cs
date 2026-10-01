using InstituteHub.Application.Common;
using Microsoft.AspNetCore.Authorization;

namespace InstituteHub.Web.Security;

/// <summary>
/// Named policies from design doc 5.2. Use [Authorize(Policy = Policies.X)] on pages and
/// RequireAuthorization(Policies.X) on endpoints. Teacher "own batches only" rules are enforced in services.
/// </summary>
public static class Policies
{
    public const string PlatformAdmin = nameof(PlatformAdmin);
    public const string ManageInstitute = nameof(ManageInstitute);   // settings, users, subscription
    public const string ViewStudents = nameof(ViewStudents);
    public const string ManageStudents = nameof(ManageStudents);
    public const string ManageBatches = nameof(ManageBatches);       // batches and fee plans
    public const string MarkAttendance = nameof(MarkAttendance);
    public const string RecordPayments = nameof(RecordPayments);
    public const string CancelPayments = nameof(CancelPayments);     // cancel, waive, discount
    public const string SendReminders = nameof(SendReminders);
    public const string ViewFinanceReports = nameof(ViewFinanceReports);
    public const string ViewAttendanceReports = nameof(ViewAttendanceReports);
}

public static class AuthorizationPolicies
{
    public static void Configure(AuthorizationOptions o)
    {
        o.AddPolicy(Policies.PlatformAdmin, p => p.RequireRole(Roles.PlatformAdmin));
        o.AddPolicy(Policies.ManageInstitute, p => p.RequireRole(Roles.Owner).RequireClaim(AppClaimTypes.TenantId));

        o.AddPolicy(Policies.ViewStudents, TenantRoles(Roles.Owner, Roles.Staff, Roles.Teacher));
        o.AddPolicy(Policies.ManageStudents, TenantRoles(Roles.Owner, Roles.Staff));
        o.AddPolicy(Policies.ManageBatches, TenantRoles(Roles.Owner, Roles.Staff));
        o.AddPolicy(Policies.MarkAttendance, TenantRoles(Roles.Owner, Roles.Staff, Roles.Teacher));
        o.AddPolicy(Policies.RecordPayments, TenantRoles(Roles.Owner, Roles.Staff));
        o.AddPolicy(Policies.CancelPayments, TenantRoles(Roles.Owner));
        o.AddPolicy(Policies.SendReminders, TenantRoles(Roles.Owner, Roles.Staff));
        o.AddPolicy(Policies.ViewFinanceReports, TenantRoles(Roles.Owner, Roles.Staff));
        o.AddPolicy(Policies.ViewAttendanceReports, TenantRoles(Roles.Owner, Roles.Staff, Roles.Teacher));
    }

    private static Action<AuthorizationPolicyBuilder> TenantRoles(params string[] roles) =>
        p => p.RequireAuthenticatedUser().RequireClaim(AppClaimTypes.TenantId).RequireRole(roles);
}
