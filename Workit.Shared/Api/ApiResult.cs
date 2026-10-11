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
    private ApiResult(bool isSuccess, T? value, string? errorMessage, int? statusCode = null)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorMessage = errorMessage;
        StatusCode = statusCode;
    }

    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? ErrorMessage { get; }

    /// <summary>
    /// The HTTP status of a failed call, when the far end answered at all; null for
    /// a timeout or a parse error. Lets a caller tell "it is gone" (404) from
    /// "it could not be reached" — which must never be treated the same way.
    /// </summary>
    public int? StatusCode { get; }

    /// <summary>True only when the far end said, in so many words, that it does not exist.</summary>
    public bool IsNotFound => StatusCode == 404;

    public static ApiResult<T> Success(T? value) => new(true, value, null);

    public static ApiResult<T> Failure(string errorMessage, int? statusCode = null) => new(false, default, errorMessage, statusCode);
}
