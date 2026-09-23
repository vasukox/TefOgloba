using Permoda.Pay.Domain.Common;

namespace Permoda.Pay.Domain.Tests.Common;

public sealed class ResultTests
{
    [Fact]
    public void Success_CreatesSuccessfulResultWithoutError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(DomainError.None, result.Error);
    }

    [Fact]
    public void Failure_CreatesFailedResultWithTypedError()
    {
        var error = DomainError.Validation("test.validation", "Validation failed.");

        var result = Result.Failure(error);

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
        Assert.Equal(DomainErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public void FailedGenericResult_ThrowsWhenValueIsRead()
    {
        var result = Result<string>.Failure(
            DomainError.Conflict("test.conflict", "Conflict detected."));

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Failure_RejectsEmptyError()
    {
        Assert.Throws<ArgumentException>(() => Result.Failure(DomainError.None));
        Assert.Throws<ArgumentException>(() => Result<string>.Failure(DomainError.None));
    }
}
