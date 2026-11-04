namespace KidWatch.Core.Functional;

/// <summary>
/// Minimal Result type: a success carrying a value, or a failure carrying an
/// error message. Keeps parsing/validation total (no exceptions at the edges).
/// </summary>
public readonly record struct Result<T>
{
    private readonly T? _value;
    private readonly string? _error;

    public bool IsOk { get; }
    public bool IsError => !IsOk;

    private Result(T value) { _value = value; _error = null; IsOk = true; }
    private Result(string error) { _value = default; _error = error; IsOk = false; }

    public static Result<T> Ok(T value) => new(value);
    public static Result<T> Fail(string error) => new(error);

    public T Value => IsOk
        ? _value!
        : throw new InvalidOperationException($"Result is an error: {_error}");

    public string Error => IsError
        ? _error!
        : throw new InvalidOperationException("Result is Ok, no error.");

    public Result<TOut> Map<TOut>(Func<T, TOut> f) =>
        IsOk ? Result<TOut>.Ok(f(_value!)) : Result<TOut>.Fail(_error!);

    public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> f) =>
        IsOk ? f(_value!) : Result<TOut>.Fail(_error!);

    public T IfError(T fallback) => IsOk ? _value! : fallback;

    public TOut Match<TOut>(Func<T, TOut> ok, Func<string, TOut> error) =>
        IsOk ? ok(_value!) : error(_error!);
}
