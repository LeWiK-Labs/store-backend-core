namespace LeWiK.Store.App.Common.Results;

public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }
    
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess ^ (error == Error.None)) throw new InvalidOperationException($"Inconsistent Result state.");
        IsSuccess = isSuccess;
        Error = error;
    }
    
    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);
    public static Result<T> Success<T>(T value) => new(value,true,Error.None);
    public static Result<T> Failure<T>(Error error) => new(default, false, error);
    public static implicit operator Result(Error error) => Failure(error);
}

public class Result<T> : Result
{
    private readonly T? _value;
    protected internal Result(T? value, bool ok, Error error) : base(ok, error) => _value = value;
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("A failed result has no Value.");
    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure<T>(error);
}

public enum ErrorType
{
    Failure,        // 500 - unexpected domain failure
    Validation,     // 400
    NotFound,       // 404
    Conflict,       // 409
    Unauthorized,   // 401
    Forbidden       // 403
}

public sealed record Error(string Code, string Message, ErrorType Type = ErrorType.Failure)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error NotFound(string code, string message)   => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string code, string message)   => new(code, message, ErrorType.Conflict);
}
