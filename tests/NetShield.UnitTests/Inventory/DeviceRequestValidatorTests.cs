using FluentAssertions;

using FluentValidation.Results;

using NetShield.Contracts.Inventory;

using NetShield.Inventory.Devices;
using NetShield.Inventory.Endpoints;

namespace NetShield.UnitTests.Inventory;

/// <summary>
/// Shape validation at the boundary (CONVENTIONS.md §4), so a handler may assume valid input.
/// </summary>
public sealed class DeviceRequestValidatorTests
{
    private static readonly CreateDeviceRequestValidator Validator = new();

    [Fact]
    public void Validate_AMinimalRequest_Passes() =>
        Validator.Validate(Request()).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_AnEmptyHostname_Fails(string hostname) =>
        Failures(Request() with { Hostname = hostname }).Should().Contain("Hostname");

    [Fact]
    public void Validate_AHostnameOverTheColumnWidth_Fails() =>
        Failures(Request() with { Hostname = new string('a', 256) }).Should().Contain("Hostname");

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("2001:db8::1")]
    public void Validate_AnAddressOfEitherFamily_Passes(string address) =>
        Validator.Validate(Request() with { PrimaryIpAddress = address }).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("999.1.1.1")]
    [InlineData("10.0.0.1/24")]
    public void Validate_SomethingThatIsNotAnAddress_Fails(string address) =>
        Failures(Request() with { PrimaryIpAddress = address }).Should().Contain("PrimaryIpAddress");

    [Fact]
    public void Validate_AVendorOutsideTheEnum_Fails() =>
        Failures(Request() with { Vendor = (DeviceVendor)999 }).Should().Contain("Vendor");

    [Fact]
    public void Validate_MoreTagsThanTheLimit_Fails()
    {
        CreateDeviceRequest request = Request() with
        {
            Tags = [.. Enumerable.Range(0, DeviceTags.MaximumCount + 1).Select(index => $"tag-{index}")]
        };

        Validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ATagOverTheLimit_Fails()
    {
        CreateDeviceRequest request = Request() with
        {
            Tags = [new string('t', DeviceTags.MaximumLength + 1)]
        };

        Validator.Validate(request).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_TagsAtTheLimit_Passes()
    {
        CreateDeviceRequest request = Request() with
        {
            Tags = [.. Enumerable.Range(0, DeviceTags.MaximumCount).Select(index => $"tag-{index}")]
        };

        Validator.Validate(request).IsValid.Should().BeTrue();
    }

    /// <summary>
    /// The update shape carries the same rules. They are written out in both validators rather
    /// than shared, so this is the test that says the two have not drifted apart.
    /// </summary>
    [Fact]
    public void Validate_TheUpdateShape_RefusesWhatTheCreateShapeRefuses()
    {
        UpdateDeviceRequestValidator updates = new();

        updates.Validate(new UpdateDeviceRequest("core-sw-01", "10.0.0.1")).IsValid.Should().BeTrue();
        updates.Validate(new UpdateDeviceRequest("", "10.0.0.1")).IsValid.Should().BeFalse();
        updates.Validate(new UpdateDeviceRequest("core-sw-01", "nonsense")).IsValid.Should().BeFalse();
    }

    private static CreateDeviceRequest Request() => new("core-sw-01", "10.0.0.1");

    private static IEnumerable<string> Failures(CreateDeviceRequest request) =>
        Validator.Validate(request).Errors.Select((ValidationFailure failure) => failure.PropertyName);
}
