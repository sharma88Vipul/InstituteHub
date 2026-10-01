using InstituteHub.Domain.Attendance;
using InstituteHub.Domain.Batches;
using InstituteHub.Domain.Billing;
using InstituteHub.Domain.Fees;
using InstituteHub.Domain.Messaging;
using InstituteHub.Domain.Students;
using InstituteHub.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace InstituteHub.Application.Abstractions;

/// <summary>
/// What Application services may use from the database. Implemented by AppDbContext in Infrastructure,
/// so every query here is already filtered to the current tenant.
/// </summary>
public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<Student> Students { get; }
    DbSet<Guardian> Guardians { get; }
    DbSet<StudentGuardian> StudentGuardians { get; }
    DbSet<Batch> Batches { get; }
    DbSet<Enrollment> Enrollments { get; }
    DbSet<AttendanceSession> AttendanceSessions { get; }
    DbSet<AttendanceRecord> AttendanceRecords { get; }
    DbSet<FeePlan> FeePlans { get; }
    DbSet<FeeDue> FeeDues { get; }
    DbSet<Payment> Payments { get; }
    DbSet<PaymentAllocation> PaymentAllocations { get; }
    DbSet<ReceiptCounter> ReceiptCounters { get; }
    DbSet<MessageTemplate> MessageTemplates { get; }
    DbSet<MessageLog> MessageLogs { get; }
    DbSet<SubscriptionPlan> SubscriptionPlans { get; }
    DbSet<TenantSubscription> TenantSubscriptions { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
