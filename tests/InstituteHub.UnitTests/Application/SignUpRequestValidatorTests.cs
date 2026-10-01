using InstituteHub.Application.Tenants;

namespace InstituteHub.UnitTests.Application;

public class SignUpRequestValidatorTests
{
    private readonly SignUpRequestValidator _validator = new();

    private static SignUpRequest Valid() =>
        new("Sharma Science Classes", "Ravi Sharma", "98765 43210", "ravi@example.com", "Secret#123");

    [Fact]
    public void Valid_request_passes() => _validator.Validate(Valid()).IsValid.ShouldBeTrue();

    [Fact]
    public void Invalid_phone_fails_with_plain_message()
    {
        var result = _validator.Validate(Valid() with { Phone = "12345" });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(SignUpRequest.Phone)
                                         && e.ErrorMessage.Contains("valid mobile number"));
    }

    [Theory]
    [InlineData("", "Ravi", "ravi@example.com", "Secret#123")]
    [InlineData("Inst", "", "ravi@example.com", "Secret#123")]
    [InlineData("Inst", "Ravi", "not-an-email", "Secret#123")]
    [InlineData("Inst", "Ravi", "ravi@example.com", "short")]
    public void Missing_or_bad_fields_fail(string institute, string owner, string email, string password)
    {
        var request = new SignUpRequest(institute, owner, "9876543210", email, password);
        _validator.Validate(request).IsValid.ShouldBeFalse();
    }
}
