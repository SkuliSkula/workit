using FluentAssertions;
using Workit.Shared.Api;

namespace Workit.Tests.Api;

public class ApiResultTests
{
    // --- ApiResult (non-generic) ---

    [Fact]
    public void Success_IsSuccessTrue_ErrorMessageNull()
    {
        var result = ApiResult.Success();

        result.IsSuccess.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Failure_IsSuccessFalse_ErrorMessageSet()
    {
        var result = ApiResult.Failure("Something went wrong");

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Something went wrong");
    }

    // --- ApiResult<T> (generic) ---

    [Fact]
    public void GenericSuccess_IsSuccessTrue_ValueSet()
    {
        var result = ApiResult<string>.Success("hello");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void GenericSuccess_WithNull_IsStillSuccess()
    {
        var result = ApiResult<string>.Success(null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public void GenericFailure_IsSuccessFalse_ValueDefault()
    {
        var result = ApiResult<int>.Failure("Not found");

        result.IsSuccess.Should().BeFalse();
        result.Value.Should().Be(default);
        result.ErrorMessage.Should().Be("Not found");
    }

    [Fact]
    public void GenericFailure_ReferenceType_ValueNull()
    {
        var result = ApiResult<List<string>>.Failure("Error");

        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
    }
}
