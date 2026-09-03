using FluentAssertions;

using NetShield.Identity.Passwords;

using NetShield.Platform.Results;

namespace NetShield.UnitTests.Identity;

/// <summary>
/// The policy is applied to every password the system stores, the seeded administrator's
/// included, so a hole in it is a hole in every account.
/// </summary>
public sealed class PasswordPolicyTests
{
    private static PasswordPolicy Policy(PasswordPolicyOptions? options = null) =>
        new(TestOptions.For(options ?? new PasswordPolicyOptions()));

    [Fact]
    public void Check_WithALongMixedPassword_Succeeds()
    {
        Result result = Policy().Check("Correct-Horse-42", "admin", "admin@example.test");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Check_WithAShortPassword_IsRejected()
    {
        Result result = Policy().Check("Sh0rt!", "admin", email: null);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(ErrorKind.Unprocessable);
        result.Error.Code.Should().Be(PasswordPolicy.RejectionCode);
    }

    [Fact]
    public void Check_WithTooFewCharacterClasses_IsRejected()
    {
        Result result = Policy().Check("alllowercaseletters", "admin", email: null);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Check_WithThreeOfFourCharacterClasses_Succeeds()
    {
        Result result = Policy().Check("LowerAndUpper1234", "admin", email: null);

        result.IsSuccess.Should().BeTrue("the default requires three of the four classes, not all four");
    }

    [Fact]
    public void Check_WithAPasswordLongerThanTheMaximum_IsRejected()
    {
        PasswordPolicyOptions options = new() { MaximumLength = 64 };

        Result result = Policy(options).Check(new string('a', 65) + "B1!", "admin", email: null);

        result.IsSuccess.Should().BeFalse("an unbounded password is an unbounded amount of hashing");
    }

    [Theory]
    [InlineData("Administrator1!")]
    [InlineData("aDMINISTRATOR1!")]
    public void Check_WhenThePasswordRepeatsTheUsername_IsRejected(string username)
    {
        Result result = Policy().Check("Administrator1!", username, email: null);

        result.IsSuccess.Should().BeFalse("case is not a difference worth crediting here");
    }

    [Fact]
    public void Check_WhenThePasswordRepeatsTheEmail_IsRejected()
    {
        Result result = Policy().Check("Admin@example.Test1", "admin", "Admin@example.Test1");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Check_WhenRejected_NamesTheFieldTheClientSent()
    {
        Result result = Policy().Check("short", "admin", email: null);

        result.Error!.Failures.Should().ContainKey("newPassword");
    }

    [Fact]
    public void Check_WithSeveralProblems_ReportsAllOfThem()
    {
        Result result = Policy().Check("admin", "admin", email: null);

        result.Error!.Failures!["newPassword"].Should().HaveCountGreaterThan(1,
            "a caller correcting one rule at a time is a caller making several round trips");
    }
}
