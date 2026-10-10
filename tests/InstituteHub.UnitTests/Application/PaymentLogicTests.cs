using InstituteHub.Application.Common;
using InstituteHub.Application.Payments;

namespace InstituteHub.UnitTests.Application;

public class PaymentAllocatorTests
{
    private static readonly AllocatableDue September = new(Guid.CreateVersion7(), new DateOnly(2026, 9, 5), 3000);
    private static readonly AllocatableDue October = new(Guid.CreateVersion7(), new DateOnly(2026, 10, 5), 3000);

    [Fact]
    public void Pays_the_oldest_due_first()
    {
        var plan = PaymentAllocator.OldestFirst(4000, [October, September]);

        plan.Allocations.Select(a => (a.FeeDueId, a.Amount)).ShouldBe(new[] { (September.FeeDueId, 3000m), (October.FeeDueId, 1000m) });
        plan.Unallocated.ShouldBe(0);
    }

    [Fact]
    public void Extra_money_is_kept_as_advance()
    {
        var plan = PaymentAllocator.OldestFirst(7000, [September, October]);
        plan.Allocated.ShouldBe(6000);
        plan.Unallocated.ShouldBe(1000);
    }

    [Fact]
    public void No_dues_means_everything_is_advance() =>
        PaymentAllocator.OldestFirst(500, []).Unallocated.ShouldBe(500);

    [Fact]
    public void Manual_split_is_validated()
    {
        PaymentAllocator.Validate(5000, [new(October.FeeDueId, 3000), new(September.FeeDueId, 2000)], [September, October]).ShouldBeNull();
        PaymentAllocator.Validate(5000, [new(October.FeeDueId, 3500)], [September, October]).ShouldNotBeNull();          // more than balance
        PaymentAllocator.Validate(1000, [new(October.FeeDueId, 2000)], [September, October]).ShouldNotBeNull();          // more than paid
        PaymentAllocator.Validate(1000, [new(Guid.CreateVersion7(), 500)], [September, October]).ShouldNotBeNull();      // unknown due
        PaymentAllocator.Validate(1000, [new(October.FeeDueId, 0)], [September, October]).ShouldNotBeNull();             // zero
    }

    [Fact]
    public void Receipt_number_format() =>
        PaymentService.FormatReceiptNumber("ABC", "2026-27", 42).ShouldBe("ABC/2026-27/00042");
}

public class AmountInWordsTests
{
    [Theory]
    [InlineData(0, "Rupees Zero Only")]
    [InlineData(21, "Rupees Twenty-One Only")]
    [InlineData(12500, "Rupees Twelve Thousand Five Hundred Only")]
    [InlineData(100000, "Rupees One Lakh Only")]
    [InlineData(10000000, "Rupees One Crore Only")]
    [InlineData(123456789, "Rupees Twelve Crore Thirty-Four Lakh Fifty-Six Thousand Seven Hundred Eighty-Nine Only")]
    public void Whole_rupees(int amount, string expected) => AmountInWords.Rupees(amount).ShouldBe(expected);

    [Fact]
    public void Paise() => AmountInWords.Rupees(125000.50m).ShouldBe("Rupees One Lakh Twenty-Five Thousand and Fifty Paise Only");
}
