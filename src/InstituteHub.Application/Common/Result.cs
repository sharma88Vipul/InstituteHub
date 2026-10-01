using FluentValidation.Results;

namespace InstituteHub.Application.Common;

/// <summary>Outcome of a use case. Expected failures are returned, not thrown.</summary>
public class Result
{
    protected Result(bool isSuccess, IReadOnlyList<Error> errors)
    {
        IsSuccess = isSuccess;
        Errors = errors;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public IReadOnlyList<Error> Errors { get; }
    public Error? FirstError => Errors.Count > 0 ? Errors[0] : null;

    public static Result Success() => new(true, []);
    public static Result Failure(params Error[] errors) => new(false, errors);
    public static Result<T> Success<T>(T value) => new(value);
    public static Result<T> Failure<T>(params Error[] errors) => new(errors);

    public static Result Invalid(ValidationResult validation) =>
        new(false, validation.Errors.Select(e => Error.Validation(e.PropertyName, e.ErrorMessage)).ToArray());

    public static Result<T> Invalid<T>(ValidationResult validation) =>
        new(validation.Errors.Select(e => Error.Validation(e.PropertyName, e.ErrorMessage)).ToArray());
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T value) : base(true, []) => _value = value;
    internal Result(IReadOnlyList<Error> errors) : base(false, errors) { }

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("Cannot read the value of a failed result.");

    public static implicit operator Result<T>(T value) => new(value);
    public static implicit operator Result<T>(Error error) => new([error]);
}
