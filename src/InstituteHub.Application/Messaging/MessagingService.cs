using System.Text.Json;
using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Billing;
using InstituteHub.Application.Common;
using InstituteHub.Domain.Attendance;
using InstituteHub.Domain.Fees;
using InstituteHub.Domain.Messaging;
using InstituteHub.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InstituteHub.Application.Messaging;

/// <summary>
/// WhatsApp/SMS messages to guardians (design doc 5.5 / 6.6): fee reminders, payment receipts and absent alerts,
/// the message log, and delivery-status updates from the provider's webhook.
/// The Send* methods are run by background jobs for one institute at a time (JobTenantContext); each message is
/// written to message_logs, and repeats are prevented so a retried job never messages a parent twice.
/// </summary>
public sealed class MessagingService(
    IAppDbContext db,
    ITenantProvider tenant,
    ICurrentUser user,
    IClock clock,
    IMessageSender sender,
    IReceiptLinks receiptLinks,
    IBackgroundJobs jobs,
    IFeatureService featureService,
    ILogger<MessagingService> logger)
{
    private enum Outcome { Sent, Failed, Skipped }

    private sealed record Institute(Guid Id, string Name, string TimeZone, TenantStatus Status);
    private sealed record Recipient(Guid GuardianId, string Name, string Phone, bool WhatsAppOptIn);
    private sealed record TemplateInfo(Guid? TenantId, string Body, string? ProviderTemplateId);

    private readonly Dictionary<(string, MessageChannel), TemplateInfo?> _templates = [];

    /// <summary>Jobs run without a signed-in user; people may only trigger these as Owner/Staff.</summary>
    private bool MaySend => !user.IsAuthenticated || user.CanManage();

    // ------------------------------------------------------------------ fee reminders

    /// <summary>
    /// Today's fee reminders for the current institute (FeeReminderJob, daily 10:00): dues due in 3 days, due today,
    /// or overdue and not reminded for 7 days, grouped by primary guardian so a parent of two children gets one
    /// message with the total.
    /// </summary>
    public async Task<MessagingRunResult> SendFeeRemindersAsync(CancellationToken ct = default)
    {
        if (!MaySend || await GetInstituteAsync(ct) is not { } institute) return MessagingRunResult.Empty;
        var features = await GetFeaturesAsync(institute, ct);
        if (!features.Reminders) return MessagingRunResult.Empty;

        var zone = FindTimeZone(institute.TimeZone);
        var today = clock.Today(institute.TimeZone);
        var horizon = today.AddDays(ReminderRules.DaysBefore);

        var dues = await db.FeeDues.AsNoTracking()
            .Where(d => (d.Status == FeeDueStatus.Pending || d.Status == FeeDueStatus.PartiallyPaid) && d.DueDate <= horizon)
            .Select(d => new { d.Id, d.StudentId, d.DueDate, d.LastReminderAt, Balance = d.Amount - d.DiscountAmount - d.PaidAmount })
            .ToListAsync(ct);

        var selected = dues
            .Where(d => d.Balance > 0)
            .Select(d => new { Due = d, Kind = ReminderRules.Classify(d.DueDate, today, LocalDate(d.LastReminderAt, zone)) })
            .Where(x => x.Kind != ReminderKind.None)
            .ToList();
        if (selected.Count == 0) return MessagingRunResult.Empty;

        var studentIds = selected.Select(x => x.Due.StudentId).Distinct().ToList();
        var guardians = await PrimaryGuardiansAsync(studentIds, ct);
        var names = await StudentNamesAsync(studentIds, ct);

        var tally = new Tally { Skipped = studentIds.Count(id => !guardians.ContainsKey(id)) };

        foreach (var group in selected.Where(x => guardians.ContainsKey(x.Due.StudentId))
                                      .GroupBy(x => guardians[x.Due.StudentId].GuardianId))
        {
            var items = group.ToList();
            var to = guardians[items[0].Due.StudentId];
            var children = items.Select(x => x.Due.StudentId).Distinct().ToList();
            var overdue = items.Any(x => x.Kind == ReminderKind.Overdue);

            var values = new Dictionary<string, string>
            {
                ["guardian"] = to.Name,
                ["student"] = MessageFormat.Names(children.Select(id => names.GetValueOrDefault(id, "your ward")).ToList()),
                ["amount"] = MessageFormat.Amount(items.Sum(x => x.Due.Balance)),
                ["due_date"] = MessageFormat.Date(items.Min(x => x.Due.DueDate)),
                ["institute"] = institute.Name,
            };

            var outcome = await SendOneAsync(
                to, children.Count == 1 ? children[0] : null,
                overdue ? MessageTemplateCodes.FeeOverdue : MessageTemplateCodes.FeeReminder,
                values, MessageRelated.FeeDue, items[0].Due.Id, features, ct);

            if (outcome == Outcome.Sent)
            {
                // A direct UPDATE: no clash with the xmin token if a payment touches the same due meanwhile.
                var ids = items.Select(x => x.Due.Id).ToList();
                var now = clock.UtcNow;
                await db.FeeDues
                    .Where(d => ids.Contains(d.Id))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(d => d.LastReminderAt, now)
                        .SetProperty(d => d.ReminderCount, d => (short)(d.ReminderCount + 1)), ct);
            }

            tally.Add(outcome);
        }

        return tally.ToResult();
    }

    // ------------------------------------------------------------------ receipts

    /// <summary>
    /// Sends PAYMENT_RECEIPT with a public receipt link to the student's primary guardian (SendReceiptJob, queued
    /// after a payment is saved). Skipped when a receipt message for this payment already went out, unless resending.
    /// </summary>
    public async Task<MessagingRunResult> SendPaymentReceiptAsync(Guid paymentId, bool resend = false, CancellationToken ct = default)
    {
        if (!MaySend || await GetInstituteAsync(ct) is not { } institute) return MessagingRunResult.Empty;

        var payment = await db.Payments.AsNoTracking()
            .Where(p => p.Id == paymentId)
            .Select(p => new { p.Id, p.StudentId, p.Amount, p.ReceiptNumber, p.IsCancelled })
            .FirstOrDefaultAsync(ct);
        if (payment is null || payment.IsCancelled) return MessagingRunResult.Empty;

        if (!resend && await db.MessageLogs.AnyAsync(m => m.TemplateCode == MessageTemplateCodes.PaymentReceipt
                                                          && m.RelatedEntity == MessageRelated.Payment
                                                          && m.RelatedId == paymentId
                                                          && m.Status != MessageStatus.Failed, ct))
        {
            return new MessagingRunResult(0, 0, 1);
        }

        var guardians = await PrimaryGuardiansAsync([payment.StudentId], ct);
        if (!guardians.TryGetValue(payment.StudentId, out var to)) return new MessagingRunResult(0, 0, 1);
        var names = await StudentNamesAsync([payment.StudentId], ct);

        var values = new Dictionary<string, string>
        {
            ["guardian"] = to.Name,
            ["student"] = names.GetValueOrDefault(payment.StudentId, "your ward"),
            ["amount"] = MessageFormat.Amount(payment.Amount),
            ["receipt_no"] = payment.ReceiptNumber,
            ["receipt_link"] = receiptLinks.Create(institute.Id, payment.Id),
            ["institute"] = institute.Name,
        };

        var features = await GetFeaturesAsync(institute, ct);
        var outcome = await SendOneAsync(to, payment.StudentId, MessageTemplateCodes.PaymentReceipt, values,
            MessageRelated.Payment, payment.Id, features, ct);

        var tally = new Tally();
        tally.Add(outcome);
        return tally.ToResult();
    }

    // ------------------------------------------------------------------ absent alerts

    /// <summary>
    /// ABSENT_ALERT to guardians of students marked absent in a register (AbsentAlertJob, queued after attendance
    /// is saved). Only for today's register, and at most once per student per register, even if it is saved again.
    /// </summary>
    public async Task<MessagingRunResult> SendAbsentAlertsAsync(Guid attendanceSessionId, CancellationToken ct = default)
    {
        if (!MaySend || await GetInstituteAsync(ct) is not { } institute) return MessagingRunResult.Empty;
        var features = await GetFeaturesAsync(institute, ct);
        if (!features.AbsentAlerts) return MessagingRunResult.Empty;

        var session = await db.AttendanceSessions.AsNoTracking()
            .Where(s => s.Id == attendanceSessionId)
            .Select(s => new
            {
                s.Id, s.SessionDate,
                BatchName = db.Batches.Where(b => b.Id == s.BatchId).Select(b => b.Name).FirstOrDefault(),
                Absent = s.Records.Where(r => r.Status == AttendanceStatus.Absent).Select(r => r.StudentId).ToList(),
            })
            .FirstOrDefaultAsync(ct);
        if (session is null || session.Absent.Count == 0 || session.SessionDate != clock.Today(institute.TimeZone))
            return MessagingRunResult.Empty;

        var alreadyAlerted = await db.MessageLogs.AsNoTracking()
            .Where(m => m.TemplateCode == MessageTemplateCodes.AbsentAlert
                        && m.RelatedEntity == MessageRelated.AttendanceSession
                        && m.RelatedId == session.Id
                        && m.StudentId != null
                        && m.Status != MessageStatus.Failed)
            .Select(m => m.StudentId!.Value)
            .ToListAsync(ct);

        var studentIds = session.Absent.Except(alreadyAlerted).ToList();
        if (studentIds.Count == 0) return MessagingRunResult.Empty;

        var guardians = await PrimaryGuardiansAsync(studentIds, ct);
        var names = await StudentNamesAsync(studentIds, ct);

        var tally = new Tally { Skipped = studentIds.Count(id => !guardians.ContainsKey(id)) };
        foreach (var studentId in studentIds.Where(guardians.ContainsKey))
        {
            var values = new Dictionary<string, string>
            {
                ["guardian"] = guardians[studentId].Name,
                ["student"] = names.GetValueOrDefault(studentId, "your ward"),
                ["batch"] = session.BatchName ?? "class",
                ["date"] = MessageFormat.Date(session.SessionDate),
                ["institute"] = institute.Name,
            };
            var outcome = await SendOneAsync(guardians[studentId], studentId, MessageTemplateCodes.AbsentAlert, values,
                MessageRelated.AttendanceSession, session.Id, features, ct);
            tally.Add(outcome);
        }

        return tally.ToResult();
    }

    // ------------------------------------------------------------------ actions from pages

    /// <summary>Queues today's reminders for this institute now (Owner/Staff).</summary>
    public Result QueueFeeRemindersNow()
    {
        if (!user.CanManage()) return Result.Failure(Error.Forbidden());
        jobs.EnqueueFeeReminders();
        return Result.Success();
    }

    /// <summary>Sends the WhatsApp/SMS receipt of a payment again (Owner/Staff).</summary>
    public async Task<Result> QueueReceiptAsync(Guid paymentId, CancellationToken ct = default)
    {
        if (!user.CanManage()) return Result.Failure(Error.Forbidden());
        var payment = await db.Payments.AsNoTracking()
            .Where(p => p.Id == paymentId).Select(p => new { p.IsCancelled }).FirstOrDefaultAsync(ct);
        if (payment is null) return Result.Failure(Error.NotFound("Payment"));
        if (payment.IsCancelled) return Result.Failure(Error.Validation("Payment", "A cancelled receipt cannot be sent."));

        jobs.EnqueueReceipt(paymentId, resend: true);
        return Result.Success();
    }

    // ------------------------------------------------------------------ delivery status (webhook)

    /// <summary>
    /// Applies a delivery update from the provider (POST /webhooks/whatsapp). The webhook has no signed-in user,
    /// so the message is found by the provider's message id across institutes; statuses never move backwards.
    /// Returns false when no message has that id.
    /// </summary>
    public async Task<bool> ApplyDeliveryStatusAsync(string providerMessageId, MessageStatus status, string? error, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerMessageId)) return false;

        var query = db.MessageLogs.IgnoreQueryFilters().Where(m => m.ProviderMessageId == providerMessageId && !m.IsDeleted);
        if (tenant.CurrentTenantId is { } current) query = query.Where(m => m.TenantId == current);

        var logs = await query.ToListAsync(ct);
        if (logs.Count == 0) return false;

        foreach (var log in logs.Where(l => MessageStatusRules.CanMove(l.Status, status)))
        {
            log.Status = status;
            if (status == MessageStatus.Failed) log.Error = Truncate(error ?? "The provider reported the message as failed.", 500);
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    // ------------------------------------------------------------------ message log

    public async Task<MessageLogPage> ListAsync(MessageLogQuery query, CancellationToken ct = default)
    {
        var empty = new MessageLogPage([], 0, new MessageStatusCounts(0, 0, 0, 0), 1, query.PageSize);
        if (!user.CanManage() || await GetInstituteAsync(ct) is not { } institute) return empty;
        var zone = FindTimeZone(institute.TimeZone);

        var logs = db.MessageLogs.AsNoTracking();
        if (query.From is { } from)
        {
            var start = StartOfDayUtc(from, zone);
            logs = logs.Where(m => m.CreatedAt >= start);
        }
        if (query.To is { } to)
        {
            var end = StartOfDayUtc(to.AddDays(1), zone);
            logs = logs.Where(m => m.CreatedAt < end);
        }
        if (!string.IsNullOrWhiteSpace(query.TemplateCode)) logs = logs.Where(m => m.TemplateCode == query.TemplateCode);

        var rows = WithNames(logs);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            rows = rows.Where(x => x.Log.ToPhone.Contains(term)
                                   || (x.GuardianName != null && x.GuardianName.ToLower().Contains(term))
                                   || (x.StudentName != null && x.StudentName.ToLower().Contains(term)));
        }

        // Counts for the period ignore the status filter, so the chips show the whole picture.
        var byStatus = await rows.Select(x => x.Log.Status)
            .GroupBy(s => s)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        int CountOf(MessageStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;
        var counts = new MessageStatusCounts(CountOf(MessageStatus.Sent), CountOf(MessageStatus.Delivered),
            CountOf(MessageStatus.Read), CountOf(MessageStatus.Failed));

        if (query.Status is { } status) rows = rows.Where(x => x.Log.Status == status);

        var total = await rows.CountAsync(ct);
        var page = Math.Max(1, query.Page);
        var items = await ToItems(rows
                .OrderByDescending(x => x.Log.CreatedAt)
                .Skip((page - 1) * query.PageSize).Take(query.PageSize))
            .ToListAsync(ct);

        return new MessageLogPage(items, total, counts, page, query.PageSize);
    }

    /// <summary>Messages about one record, e.g. the WhatsApp receipt of a payment. Newest first.</summary>
    public async Task<IReadOnlyList<MessageLogItem>> ListForAsync(string relatedEntity, Guid relatedId, CancellationToken ct = default)
    {
        if (!user.CanManage()) return [];
        return await ToItems(WithNames(db.MessageLogs.AsNoTracking()
                    .Where(m => m.RelatedEntity == relatedEntity && m.RelatedId == relatedId))
                .OrderByDescending(x => x.Log.CreatedAt))
            .ToListAsync(ct);
    }

    /// <summary>A member-init projection, so EF can still filter, group and sort on it (a record constructor cannot).</summary>
    private sealed class LogRow
    {
        public MessageLog Log { get; init; } = null!;
        public string? GuardianName { get; init; }
        public string? StudentName { get; init; }
    }

    private IQueryable<LogRow> WithNames(IQueryable<MessageLog> logs) =>
        logs.Select(m => new LogRow
        {
            Log = m,
            GuardianName = db.Guardians.Where(g => g.Id == m.GuardianId).Select(g => g.FullName).FirstOrDefault(),
            StudentName = db.Students.Where(s => s.Id == m.StudentId)
                .Select(s => s.FirstName + (s.LastName == null ? "" : " " + s.LastName)).FirstOrDefault(),
        });

    private static IQueryable<MessageLogItem> ToItems(IQueryable<LogRow> rows) =>
        rows.Select(x => new MessageLogItem(
            x.Log.Id, x.Log.CreatedAt, x.Log.TemplateCode, x.Log.Channel, x.Log.ToPhone,
            x.GuardianName, x.Log.StudentId, x.StudentName,
            x.Log.Status, x.Log.Error, x.Log.ProviderMessageId));

    // ------------------------------------------------------------------ helpers

    /// <summary>Renders the template, sends it, and saves a message_logs row with the result.</summary>
    private async Task<Outcome> SendOneAsync(
        Recipient to, Guid? studentId, string code, Dictionary<string, string> values,
        string relatedEntity, Guid relatedId, MessagingFeatures features, CancellationToken ct)
    {
        // WhatsApp needs the parent's opt-in; otherwise fall back to SMS when the plan includes it.
        MessageChannel? channel = to.WhatsAppOptIn ? MessageChannel.WhatsApp : features.Sms ? MessageChannel.Sms : null;
        if (channel is not { } ch) return Outcome.Skipped;

        var template = await FindTemplateAsync(code, ch, ct);
        if (template is null)
        {
            logger.LogWarning("No active {TemplateCode} template for {Channel}; message skipped", code, ch);
            return Outcome.Skipped;
        }

        var log = new MessageLog
        {
            GuardianId = to.GuardianId,
            StudentId = studentId,
            TemplateCode = code,
            Channel = ch,
            ToPhone = to.Phone,
            Payload = JsonSerializer.Serialize(values),
            RelatedEntity = relatedEntity,
            RelatedId = relatedId,
        };

        SendResult result;
        try
        {
            result = await sender.SendAsync(
                new OutgoingMessage(ch, to.Phone, code, template.ProviderTemplateId, TemplateRenderer.Render(template.Body, values), values), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Sending {TemplateCode} by {Channel} failed", code, ch);
            result = SendResult.Fail(ex.Message);
        }

        log.Status = result.Success ? MessageStatus.Sent : MessageStatus.Failed;
        log.ProviderMessageId = result.ProviderMessageId;
        log.Error = result.Error is null ? null : Truncate(result.Error, 500);
        log.SentAt = result.Success ? clock.UtcNow : null;

        db.MessageLogs.Add(log);
        await db.SaveChangesAsync(ct);
        return result.Success ? Outcome.Sent : Outcome.Failed;
    }

    private sealed class Tally
    {
        public int Sent { get; private set; }
        public int Failed { get; private set; }
        public int Skipped { get; set; }

        public void Add(Outcome outcome)
        {
            switch (outcome)
            {
                case Outcome.Sent: Sent++; break;
                case Outcome.Failed: Failed++; break;
                default: Skipped++; break;
            }
        }

        public MessagingRunResult ToResult() => new(Sent, Failed, Skipped);
    }

    private async Task<TemplateInfo?> FindTemplateAsync(string code, MessageChannel channel, CancellationToken ct)
    {
        if (_templates.TryGetValue((code, channel), out var cached)) return cached;

        // The institute's own template wins over the system default (tenant_id null).
        var candidates = await db.MessageTemplates.AsNoTracking()
            .Where(t => t.Code == code && t.Channel == channel && t.IsActive)
            .Select(t => new TemplateInfo(t.TenantId, t.Body, t.ProviderTemplateId))
            .ToListAsync(ct);
        var template = candidates.OrderByDescending(t => t.TenantId is not null).FirstOrDefault();
        _templates[(code, channel)] = template;
        return template;
    }

    private async Task<Institute?> GetInstituteAsync(CancellationToken ct)
    {
        if (tenant.CurrentTenantId is not { } id) return null;
        return await db.Tenants.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new Institute(t.Id, t.Name, t.TimeZone, t.Status))
            .FirstOrDefaultAsync(ct);
    }

    private async Task<MessagingFeatures> GetFeaturesAsync(Institute institute, CancellationToken ct) =>
        await featureService.GetLimitsAsync(ct) is { } limits
            ? new MessagingFeatures(limits.Has(PlanFeatures.Reminders), limits.Has(PlanFeatures.Sms), limits.Has(PlanFeatures.AbsentAlerts))
            : new MessagingFeatures(institute.Status == TenantStatus.Trial, false, false);

    /// <summary>Reads {"reminders": true, "sms": false, "absent_alerts": true}; no plan = reminders only.</summary>
    public static MessagingFeatures ParseFeatures(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new MessagingFeatures(true, false, false);
        var on = PlanFeatures.Parse(json);
        return new MessagingFeatures(on.Contains(PlanFeatures.Reminders), on.Contains(PlanFeatures.Sms), on.Contains(PlanFeatures.AbsentAlerts));
    }

    /// <summary>Primary guardian of each student (or any guardian when none is marked primary).</summary>
    private async Task<Dictionary<Guid, Recipient>> PrimaryGuardiansAsync(IReadOnlyCollection<Guid> studentIds, CancellationToken ct)
    {
        var links = await db.StudentGuardians.AsNoTracking()
            .Where(sg => studentIds.Contains(sg.StudentId))
            .Select(sg => new
            {
                sg.StudentId, sg.IsPrimary, sg.GuardianId,
                sg.Guardian!.FullName, sg.Guardian.Phone, sg.Guardian.WhatsAppOptIn,
            })
            .ToListAsync(ct);

        return links
            .Where(l => !string.IsNullOrWhiteSpace(l.Phone))
            .GroupBy(l => l.StudentId)
            .ToDictionary(g => g.Key, g =>
            {
                var l = g.OrderByDescending(x => x.IsPrimary).First();
                return new Recipient(l.GuardianId, l.FullName, l.Phone, l.WhatsAppOptIn);
            });
    }

    private async Task<Dictionary<Guid, string>> StudentNamesAsync(IReadOnlyCollection<Guid> studentIds, CancellationToken ct) =>
        await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, Name = s.FirstName + (s.LastName == null ? "" : " " + s.LastName) })
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

    private static TimeZoneInfo FindTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "India Standard Time", "India Standard Time");
        }
    }

    private static DateOnly? LocalDate(DateTimeOffset? instant, TimeZoneInfo zone) =>
        instant is { } value ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, zone).DateTime) : null;

    private static DateTimeOffset StartOfDayUtc(DateOnly date, TimeZoneInfo zone) =>
        new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), zone), TimeSpan.Zero);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
