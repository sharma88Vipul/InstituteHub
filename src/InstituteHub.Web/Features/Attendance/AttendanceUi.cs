using InstituteHub.Domain.Attendance;
using MudBlazor;

namespace InstituteHub.Web.Features.Attendance;

/// <summary>Shared colours and labels for attendance statuses.</summary>
public static class AttendanceUi
{
    public static Color ColorOf(AttendanceStatus status) => status switch
    {
        AttendanceStatus.Present => Color.Success,
        AttendanceStatus.Absent => Color.Error,
        AttendanceStatus.Late => Color.Warning,
        _ => Color.Info,
    };

    /// <summary>CSS colour for the left border of a register row.</summary>
    public static string BorderOf(AttendanceStatus status) => status switch
    {
        AttendanceStatus.Present => "var(--mud-palette-success)",
        AttendanceStatus.Absent => "var(--mud-palette-error)",
        AttendanceStatus.Late => "var(--mud-palette-warning)",
        _ => "var(--mud-palette-info)",
    };

    public static string Percent(double? value) => value is { } v ? $"{v:0.#}%" : "–";
}
