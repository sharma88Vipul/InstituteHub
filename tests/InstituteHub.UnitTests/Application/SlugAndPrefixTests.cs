namespace InstituteHub.UnitTests.Application;

public class SlugAndPrefixTests
{
    [Theory]
    [InlineData("Sharma Classes, Ludhiana", "sharma-classes-ludhiana")]
    [InlineData("  A+ Academy!! ", "a-academy")]
    [InlineData("!!!", "institute")]
    public void Slugify(string name, string expected) =>
        InstituteHub.Infrastructure.Identity.SignUpService.Slugify(name).ShouldBe(expected);

    [Theory]
    [InlineData("Sharma Science Classes", "SSC")]
    [InlineData("Excel", "EXC")]
    [InlineData("Bright Future Coaching Centre Jalandhar", "BFCC")]
    [InlineData("123", "INS")]
    public void ReceiptPrefixFor(string name, string expected) =>
        InstituteHub.Infrastructure.Identity.SignUpService.ReceiptPrefixFor(name).ShouldBe(expected);
}
