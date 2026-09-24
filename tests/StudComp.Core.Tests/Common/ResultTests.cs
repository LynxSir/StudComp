using StudComp.Core.Common;

namespace StudComp.Core.Tests.Common;

public class ResultTests
{
    private static readonly Error SampleError = new("archivist.file_locked", "Файл открыт в другой программе");

    [Fact]
    public void Success_is_not_a_failure_and_carries_no_error()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.True(result.Error.IsNone);
    }

    [Fact]
    public void Failure_carries_the_error()
    {
        var result = Result.Failure(SampleError);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal(SampleError, result.Error);
    }

    [Fact]
    public void Failure_from_code_and_message_builds_the_error()
    {
        var result = Result.Failure(SampleError.Code, SampleError.Message);

        Assert.Equal(SampleError, result.Error);
    }

    [Fact]
    public void Generic_success_exposes_the_value()
    {
        Result<int> result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.True(result.Error.IsNone);
    }

    [Fact]
    public void Generic_failure_hides_the_value_behind_an_exception()
    {
        var result = Result<int>.Failure(SampleError);

        Assert.True(result.IsFailure);
        var exception = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains(SampleError.Code, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Value_converts_implicitly_to_a_successful_result()
    {
        Result<string> result = "ok";

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value);
    }

    [Fact]
    public void Error_converts_implicitly_to_a_failed_result()
    {
        Result<string> generic = SampleError;
        Result plain = SampleError;

        Assert.True(generic.IsFailure);
        Assert.True(plain.IsFailure);
        Assert.Equal(SampleError, generic.Error);
        Assert.Equal(SampleError, plain.Error);
    }

    [Fact]
    public void Match_picks_the_success_branch()
    {
        var result = Result<int>.Success(7);

        var text = result.Match(value => $"value:{value}", error => $"error:{error.Code}");

        Assert.Equal("value:7", text);
    }

    [Fact]
    public void Match_picks_the_failure_branch()
    {
        var result = Result<int>.Failure(SampleError);

        var text = result.Match(value => $"value:{value}", error => $"error:{error.Code}");

        Assert.Equal($"error:{SampleError.Code}", text);
    }

    [Fact]
    public void WithoutValue_keeps_the_outcome()
    {
        Assert.True(Result<int>.Success(1).WithoutValue().IsSuccess);

        var failure = Result<int>.Failure(SampleError).WithoutValue();

        Assert.True(failure.IsFailure);
        Assert.Equal(SampleError, failure.Error);
    }

    [Fact]
    public void Default_error_is_none()
    {
        Assert.True(Error.None.IsNone);
        Assert.False(new Error("some.code", "message").IsNone);
    }
}
