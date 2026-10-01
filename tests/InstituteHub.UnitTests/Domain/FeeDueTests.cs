using InstituteHub.Domain.Fees;

namespace InstituteHub.UnitTests.Domain;

public class FeeDueTests
{
    private static FeeDue NewDue(decimal amount = 1000m, decimal discount = 0m) =>
        new() { Title = "Instalment 1 of 4", Amount = amount, DiscountAmount = discount, DueDate = new DateOnly(2026, 10, 5) };

    [Fact]
    public void ApplyPayment_partial_amount_marks_due_partially_paid()
    {
        var due = NewDue();

        due.ApplyPayment(400m);

        due.PaidAmount.ShouldBe(400m);
        due.Balance.ShouldBe(600m);
        due.Status.ShouldBe(FeeDueStatus.PartiallyPaid);
    }

    [Fact]
    public void ApplyPayment_full_balance_after_discount_marks_due_paid()
    {
        var due = NewDue(amount: 1000m, discount: 100m);

        due.ApplyPayment(900m);

        due.Balance.ShouldBe(0m);
        due.Status.ShouldBe(FeeDueStatus.Paid);
    }

    [Fact]
    public void ApplyPayment_more_than_balance_throws()
    {
        var due = NewDue();
        Should.Throw<InvalidOperationException>(() => due.ApplyPayment(1000.01m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ApplyPayment_non_positive_amount_throws(decimal amount)
    {
        var due = NewDue();
        Should.Throw<ArgumentOutOfRangeException>(() => due.ApplyPayment(amount));
    }

    [Fact]
    public void ReversePayment_back_to_zero_marks_due_pending()
    {
        var due = NewDue();
        due.ApplyPayment(1000m);

        due.ReversePayment(1000m);

        due.PaidAmount.ShouldBe(0m);
        due.Status.ShouldBe(FeeDueStatus.Pending);
    }

    [Fact]
    public void Waive_with_payments_throws()
    {
        var due = NewDue();
        due.ApplyPayment(10m);
        Should.Throw<InvalidOperationException>(() => due.Waive());
    }
}
