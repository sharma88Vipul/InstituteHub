namespace InstituteHub.Application.Common;

public enum ErrorType { Validation, NotFound, Conflict, Forbidden, LimitReached }

public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static Error NotFound(string what) => new($"{what}.NotFound", $"{what} was not found.", ErrorType.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Forbidden(string message = "You do not have permission to do this.") => new("Forbidden", message, ErrorType.Forbidden);
    public static Error LimitReached(string message) => new("LimitReached", message, ErrorType.LimitReached);
    public static Error Validation(string field, string message) => new(field, message, ErrorType.Validation);

    public static readonly Error ConcurrencyConflict =
        Conflict("Concurrency", "This record was changed by someone else. Please reload and try again.");
}
