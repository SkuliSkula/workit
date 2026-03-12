namespace Workit.Shared.Api;

public sealed class ApiResult
{
    private ApiResult(bool isSuccess, string? errorMessage)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }

    public static ApiResult Success() => new(true, null);

    public static ApiResult Failure(string errorMessage) => new(false, errorMessage);
}

public sealed class ApiResult<T>
{
    private ApiResult(bool isSuccess, T? value, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? ErrorMessage { get; }

    public static ApiResult<T> Success(T? value) => new(true, value, null);

    public static ApiResult<T> Failure(string errorMessage) => new(false, default, errorMessage);
}
