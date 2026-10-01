using System.Reflection;
using InstituteHub.Application.Abstractions;
using InstituteHub.Domain.Attendance;
using InstituteHub.Domain.Auditing;
using InstituteHub.Domain.Batches;
using InstituteHub.Domain.Billing;
using InstituteHub.Domain.Common;
using InstituteHub.Domain.Fees;
using InstituteHub.Domain.Messaging;
using InstituteHub.Domain.Students;
using InstituteHub.Domain.Tenants;
using InstituteHub.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.Infrastructure.Persistence;

/// <summary>
/// Single DbContext for Identity and business data (PostgreSQL, snake_case).
/// Every tenant-owned entity gets a global query filter so a query can never cross institutes.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantProvider tenant)
    : IdentityDbContext<AppUser, AppRole, Guid>(options), IAppDbContext
{
    /// <summary>Evaluated by EF per query. Null (no tenant context) matches no tenant rows.</summary>
    private Guid? CurrentTenantId => tenant.CurrentTenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Guardian> Guardians => Set<Guardian>();
    public DbSet<StudentGuardian> StudentGuardians => Set<StudentGuardian>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<AttendanceSession> AttendanceSessions => Set<AttendanceSession>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<FeePlan> FeePlans => Set<FeePlan>();
    public DbSet<FeeDue> FeeDues => Set<FeeDue>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<ReceiptCounter> ReceiptCounters => Set<ReceiptCounter>();
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<MessageLog> MessageLogs => Set<MessageLog>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money: numeric(12,2) everywhere unless a configuration says otherwise.
        configurationBuilder.Properties<decimal>().HavePrecision(12, 2);
        // Enums: readable varchar strings, safe to reorder.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in builder.Model.GetEntityTypes().ToList())
        {
            var clr = entityType.ClrType;
            if (entityType.BaseType is not null || entityType.IsOwned()) continue;

            string? filter = clr switch
            {
                _ when typeof(TenantEntity).IsAssignableFrom(clr) => nameof(ApplyTenantAndSoftDeleteFilter),
                _ when typeof(ITenantOwned).IsAssignableFrom(clr) => nameof(ApplyTenantFilter),
                _ when clr == typeof(MessageTemplate) => null, // configured below
                _ when typeof(BaseEntity).IsAssignableFrom(clr) => nameof(ApplySoftDeleteFilter),
                _ => null,
            };
            if (filter is null) continue;

            typeof(AppDbContext)
                .GetMethod(filter, BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(clr)
                .Invoke(this, [builder]);
        }

        // System templates (tenant_id null) are visible to every tenant.
        builder.Entity<MessageTemplate>().HasQueryFilter(t =>
            (t.TenantId == null || t.TenantId == CurrentTenantId) && !t.IsDeleted);
    }

    private void ApplyTenantAndSoftDeleteFilter<T>(ModelBuilder builder) where T : TenantEntity =>
        builder.Entity<T>().HasQueryFilter(e => e.TenantId == CurrentTenantId && !e.IsDeleted);

    private void ApplyTenantFilter<T>(ModelBuilder builder) where T : class, ITenantOwned =>
        builder.Entity<T>().HasQueryFilter(e => EF.Property<Guid>(e, nameof(ITenantOwned.TenantId)) == CurrentTenantId);

    private void ApplySoftDeleteFilter<T>(ModelBuilder builder) where T : BaseEntity =>
        builder.Entity<T>().HasQueryFilter(e => !e.IsDeleted);
}
