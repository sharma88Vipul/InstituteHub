using FluentValidation;
using InstituteHub.Domain.Batches;

namespace InstituteHub.Application.Batches;

public sealed record BatchRequest(
    string Name,
    string? Subject,
    Guid? TeacherId,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    WeekDays DaysOfWeek,
    int? Capacity,
    DateOnly StartDate,
    DateOnly? EndDate,
    Guid? DefaultFeePlanId);

public sealed record BatchListItem(
    Guid Id,
    string Name,
    string? Subject,
    string? TeacherName,
    string Schedule,
    int EnrolledCount,
    int? Capacity,
    bool IsActive);

public sealed record BatchStudent(
    Guid StudentId,
    Guid EnrollmentId,
    string AdmissionNo,
    string FullName,
    DateOnly EnrolledOn,
    string? GuardianPhone);

public sealed record BatchDetails(
    Guid Id,
    string Name,
    string? Subject,
    Guid? TeacherId,
    string? TeacherName,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    WeekDays DaysOfWeek,
    int? Capacity,
    DateOnly StartDate,
    DateOnly? EndDate,
    Guid? DefaultFeePlanId,
    string? DefaultFeePlanName,
    bool IsActive,
    IReadOnlyList<BatchStudent> Students)
{
    public string Schedule => BatchSchedule.Describe(DaysOfWeek, StartTime, EndTime);
}

public sealed record BatchOption(Guid Id, string Name, Guid? DefaultFeePlanId, string Schedule);

public static class BatchSchedule
{
    private static readonly (WeekDays Day, string Label)[] Days =
    [
        (WeekDays.Monday, "Mon"), (WeekDays.Tuesday, "Tue"), (WeekDays.Wednesday, "Wed"),
        (WeekDays.Thursday, "Thu"), (WeekDays.Friday, "Fri"), (WeekDays.Saturday, "Sat"), (WeekDays.Sunday, "Sun"),
    ];

    public static IEnumerable<(WeekDays Day, string Label)> AllDays => Days;

    /// <summary>"Mon, Wed, Fri · 17:00–18:30".</summary>
    public static string Describe(WeekDays days, TimeOnly? start, TimeOnly? end)
    {
        var dayText = days switch
        {
            WeekDays.None => "No days set",
            (WeekDays)127 => "Every day",
            (WeekDays)63 => "Mon–Sat",
            _ => string.Join(", ", Days.Where(d => days.HasFlag(d.Day)).Select(d => d.Label)),
        };

        var time = (start, end) switch
        {
            ({ } s, { } e) => $" · {s:HH:mm}–{e:HH:mm}",
            ({ } s, null) => $" · from {s:HH:mm}",
            _ => "",
        };
        return dayText + time;
    }
}

public sealed class BatchRequestValidator : AbstractValidator<BatchRequest>
{
    public BatchRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Enter a batch name.").MaximumLength(100);
        RuleFor(x => x.Subject).MaximumLength(80);
        RuleFor(x => x.DaysOfWeek).NotEqual(WeekDays.None).WithMessage("Choose at least one day.");
        RuleFor(x => x.EndTime).GreaterThan(x => x.StartTime)
            .When(x => x.StartTime is not null && x.EndTime is not null)
            .WithMessage("The end time must be after the start time.");
        RuleFor(x => x.Capacity).GreaterThan(0).When(x => x.Capacity is not null)
            .WithMessage("Capacity must be more than 0.");
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate).When(x => x.EndDate is not null)
            .WithMessage("The end date cannot be before the start date.");
    }
}
