using InstituteHub.Domain.Fees;

namespace InstituteHub.UnitTests.Domain;

public class ReceiptCounterTests
{
    [Theory]
    [InlineData(2026, 4, 1, "2026-27")]
    [InlineData(2027, 3, 31, "2026-27")]
    [InlineData(2026, 3, 31, "2025-26")]
    [InlineData(2099, 12, 1, "2099-00")]
    public void FinancialYearFor_follows_april_to_march(int y, int m, int d, string expected) =>
        ReceiptCounter.FinancialYearFor(new DateOnly(y, m, d)).ShouldBe(expected);
}
