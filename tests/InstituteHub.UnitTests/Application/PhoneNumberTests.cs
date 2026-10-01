using InstituteHub.Application.Common;

namespace InstituteHub.UnitTests.Application;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("9876543210", "+919876543210")]
    [InlineData("98765 43210", "+919876543210")]
    [InlineData("098765-43210", "+919876543210")]
    [InlineData("+91 98765 43210", "+919876543210")]
    [InlineData("919876543210", "+919876543210")]
    [InlineData("+44 20 7946 0958", "+442079460958")]
    public void Normalize_returns_E164(string input, string expected) =>
        PhoneNumber.Normalize(input).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567890")]   // Indian mobile numbers start with 6–9
    [InlineData("abc")]
    public void Normalize_returns_null_for_invalid_numbers(string? input) =>
        PhoneNumber.Normalize(input).ShouldBeNull();
}
