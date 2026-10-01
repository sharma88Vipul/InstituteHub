using InstituteHub.Application.Abstractions;

namespace InstituteHub.Application.Common;

/// <summary>Role checks used inside services (design doc 5.2: permissions are enforced in services, not only in the UI).</summary>
public static class CurrentUserExtensions
{
    /// <summary>Owner or Staff: may create and edit students, batches and fee plans.</summary>
    public static bool CanManage(this ICurrentUser user) =>
        user.IsInRole(Roles.Owner) || user.IsInRole(Roles.Staff);

    /// <summary>A teacher who is not also Owner/Staff only sees their own batches.</summary>
    public static bool IsTeacherOnly(this ICurrentUser user) =>
        user.IsInRole(Roles.Teacher) && !user.CanManage();
}
