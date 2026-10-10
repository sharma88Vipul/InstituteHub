namespace InstituteHub.Application.Payments;

/// <summary>An open due that a payment can be applied to.</summary>
public sealed record AllocatableDue(Guid FeeDueId, DateOnly DueDate, decimal Balance);

public sealed record Allocation(Guid FeeDueId, decimal Amount);

public sealed record AllocationPlan(IReadOnlyList<Allocation> Allocations, decimal Unallocated)
{
    public decimal Allocated => Allocations.Sum(a => a.Amount);
}

/// <summary>
/// Splits a payment over open dues (design doc 6.4). Pure functions so the rules are easy to test.
/// </summary>
public static class PaymentAllocator
{
    /// <summary>Oldest due first; anything left over is kept as an advance (unallocated).</summary>
    public static AllocationPlan OldestFirst(decimal amount, IEnumerable<AllocatableDue> dues)
    {
        var allocations = new List<Allocation>();
        var remaining = amount;
        foreach (var due in dues.Where(d => d.Balance > 0).OrderBy(d => d.DueDate))
        {
            if (remaining <= 0) break;
            var apply = Math.Min(remaining, due.Balance);
            allocations.Add(new Allocation(due.FeeDueId, apply));
            remaining -= apply;
        }
        return new AllocationPlan(allocations, Math.Max(0, remaining));
    }

    /// <summary>
    /// Checks a manual split chosen by staff. Returns an error message, or null when it is valid.
    /// Every amount must be positive and not more than that due's balance, and the total cannot exceed the payment.
    /// </summary>
    public static string? Validate(decimal amount, IReadOnlyList<Allocation> allocations, IEnumerable<AllocatableDue> dues)
    {
        var balances = dues.ToDictionary(d => d.FeeDueId, d => d.Balance);
        if (allocations.GroupBy(a => a.FeeDueId).Any(g => g.Count() > 1)) return "A due appears twice in the split.";

        foreach (var a in allocations)
        {
            if (a.Amount <= 0) return "Each amount in the split must be more than zero.";
            if (!balances.TryGetValue(a.FeeDueId, out var balance)) return "The split includes a due that is not open any more. Reload and try again.";
            if (a.Amount > balance) return "An amount in the split is more than that due's balance.";
        }

        return allocations.Sum(a => a.Amount) > amount ? "The split adds up to more than the amount received." : null;
    }
}
