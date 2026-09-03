using FluentAssertions;

using NetShield.Platform.Results;

namespace NetShield.UnitTests.Platform;

/// <summary>
/// Covers the outcome type every handler returns (CONVENTIONS.md §2). The point of these is
/// that a failure carries enough to answer the caller without an exception ever being thrown.
/// </summary>
public sealed class ResultTests
{
    [Fact]
    public void Success_HasNoError()
    {
        Result.Success.IsSuccess.Should().BeTrue();
        Result.Success.Error.Should().BeNull();
    }

    [Fact]
    public void Failure_CarriesTheError()
    {
        Error error = Error.Conflict("device.duplicate-ip", "That address is already in use.");

        Result result = Result.Failure(error);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeSameAs(error);
    }

    [Fact]
    public void Error_ConvertsImplicitly_SoAHandlerCanReturnItDirectly()
    {
        Result result = Error.NotFound("device.not-found", "No such device.");

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(ErrorKind.NotFound);
    }

    [Fact]
    public void ResultOfT_Success_CarriesTheValue()
    {
        Result<string> result = Result<string>.Success("core-sw-01");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("core-sw-01");
    }

    [Fact]
    public void ResultOfT_Value_ConvertsImplicitly_SoAHandlerCanReturnItDirectly()
    {
        Result<int> result = 42;

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void ResultOfT_Value_Throws_WhenTheResultFailed()
    {
        Result<string> result = Error.NotFound("device.not-found", "No such device.");

        Func<string> read = () => result.Value;

        read.Should().Throw<InvalidOperationException>(
            "reading the value of a failed result is a bug at the call site, not a branch")
            .WithMessage("*device.not-found*");
    }

    [Fact]
    public void Validation_CarriesPerFieldFailures()
    {
        Error error = Error.Validation(
            "device.invalid",
            "The device is not valid.",
            new Dictionary<string, string[]> { ["hostname"] = ["Required."] });

        error.Failures.Should().ContainKey("hostname");
    }

    [Theory]
    [InlineData(ErrorKind.Validation, 400)]
    [InlineData(ErrorKind.Unauthenticated, 401)]
    [InlineData(ErrorKind.Forbidden, 403)]
    [InlineData(ErrorKind.NotFound, 404)]
    [InlineData(ErrorKind.Conflict, 409)]
    [InlineData(ErrorKind.Unprocessable, 422)]
    [InlineData(ErrorKind.RateLimited, 429)]
    public void ErrorKind_MapsToTheStatusCode_ConventionsRequires(ErrorKind kind, int expected) =>
        kind.ToStatusCode().Should().Be(expected, "CONVENTIONS.md §4 fixes this table");

    [Fact]
    public void EveryErrorKind_HasAStatusCodeAndATitle()
    {
        foreach (ErrorKind kind in Enum.GetValues<ErrorKind>())
        {
            kind.ToStatusCode().Should().BeInRange(400, 499);
            kind.ToTitle().Should().NotBeNullOrWhiteSpace();
        }
    }
}
