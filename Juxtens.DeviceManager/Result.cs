namespace Juxtens.DeviceManager;

public readonly struct Result<T, TError>
{
    private readonly T? _value;
    private readonly TError? _error;
    private readonly bool _isSuccess;

    private Result(T value)
    {
        _value = value;
        _error = default;
        _isSuccess = true;
    }

    private Result(TError error)
    {
        _value = default;
        _error = error;
        _isSuccess = false;
    }

    public bool IsSuccess => _isSuccess;
    public bool IsError => !_isSuccess;

    public T Value => _isSuccess ? _value! : throw new InvalidOperationException("Result is error");
    public TError Error => !_isSuccess ? _error! : throw new InvalidOperationException("Result is success");

    public static Result<T, TError> Success(T value) => new(value);
    public static Result<T, TError> Failure(TError error) => new(error);

    public TResult Match<TResult>(Func<T, TResult> success, Func<TError, TResult> failure) =>
        _isSuccess ? success(_value!) : failure(_error!);

    public Result<TNext, TError> Map<TNext>(Func<T, TNext> mapper) =>
        _isSuccess ? Result<TNext, TError>.Success(mapper(_value!)) : Result<TNext, TError>.Failure(_error!);

    public Result<TNext, TError> Bind<TNext>(Func<T, Result<TNext, TError>> binder) =>
        _isSuccess ? binder(_value!) : Result<TNext, TError>.Failure(_error!);

    public Result<T, TNextError> MapError<TNextError>(Func<TError, TNextError> mapper) =>
        _isSuccess ? Result<T, TNextError>.Success(_value!) : Result<T, TNextError>.Failure(mapper(_error!));
}

public readonly struct Unit
{
    public static Unit Value => default;
}
