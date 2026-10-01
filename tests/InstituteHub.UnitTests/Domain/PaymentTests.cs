using InstituteHub.Domain.Fees;

namespace InstituteHub.UnitTests.Domain;

public class PaymentTests
{
    [Fact]
    public void Cancel_sets_cancellation_fields_once()
    {
        var payment = new Payment { Amount = 500m, ReceiptNumber = "ABC/2026-27/00001" };
        var by = Guid.CreateVersion7();
        var at = DateTimeOffset.UtcNow;

        payment.Cancel("Wrong student", by, at);

        payment.IsCancelled.ShouldBeTrue();
        payment.CancelledBy.ShouldBe(by);
        payment.CancelledAt.ShouldBe(at);
        Should.Throw<InvalidOperationException>(() => payment.Cancel("again", by, at));
    }

    [Fact]
    public void Cancel_requires_a_reason()
    {
        var payment = new Payment { Amount = 500m };
        Should.Throw<ArgumentException>(() => payment.Cancel(" ", Guid.CreateVersion7(), DateTimeOffset.UtcNow));
    }
}
