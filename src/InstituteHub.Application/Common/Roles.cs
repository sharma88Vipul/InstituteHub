namespace InstituteHub.Application.Common;

public static class Roles
{
    public const string PlatformAdmin = "PlatformAdmin";
    public const string Owner = "Owner";
    public const string Staff = "Staff";
    public const string Teacher = "Teacher";

    public static readonly IReadOnlyList<string> All = [PlatformAdmin, Owner, Staff, Teacher];
}
