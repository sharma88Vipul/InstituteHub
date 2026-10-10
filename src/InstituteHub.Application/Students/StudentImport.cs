using System.Globalization;
using InstituteHub.Application.Abstractions;
using InstituteHub.Application.Billing;
using InstituteHub.Application.Common;
using InstituteHub.Domain.Students;
using Microsoft.EntityFrameworkCore;

namespace InstituteHub.Application.Students;

/// <summary>A batch students can be imported into (looked up by name in the CSV).</summary>
public sealed record ImportBatch(Guid Id, string Name, bool HasDefaultFeePlan);

/// <summary>One CSV data row: the admission it becomes, or why it cannot be imported.</summary>
public sealed record ImportRow(
    int LineNumber,
    string StudentName,
    string? GuardianName,
    string? GuardianPhone,
    string? BatchName,
    AdmissionRequest? Request,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0 && Request is not null;
}

public sealed record ImportPreview(IReadOnlyList<ImportRow> Rows, IReadOnlyList<string> FileErrors, string? LimitWarning)
{
    public int ValidCount => Rows.Count(r => r.IsValid);
    public int ErrorCount => Rows.Count(r => !r.IsValid);
    public bool CanImport => FileErrors.Count == 0 && LimitWarning is null && ValidCount > 0;
}

public sealed record ImportFailure(int LineNumber, string StudentName, string Message);

public sealed record ImportResult(int Imported, IReadOnlyList<ImportFailure> Failed);

/// <summary>
/// Turns a students CSV into admission requests (pure, unit-tested). Columns are matched by header name, ignoring
/// case, spaces and underscores; only FirstName, GuardianName and GuardianPhone are required.
/// </summary>
public static class StudentCsv
{
    public static readonly IReadOnlyList<string> Columns =
    [
        "AdmissionNo", "FirstName", "LastName", "Gender", "DateOfBirth", "Class", "School", "StudentPhone", "Address",
        "AdmissionDate", "GuardianName", "Relation", "GuardianPhone", "GuardianEmail", "WhatsAppConsent", "Batch",
    ];

    public static readonly IReadOnlyList<string> ExampleRow =
    [
        "", "Aarav", "Sharma", "Male", "15-08-2010", "Class 10", "DAV Public School", "", "12 Model Town, Ludhiana",
        "", "Rakesh Sharma", "Father", "9876543210", "rakesh@example.com", "Yes", "",
    ];

    private static readonly string[] DateFormats = ["dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd.MM.yyyy"];

    // Accepted header spellings (normalised: lower case, no spaces/underscores/dashes/dots).
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["admissionno"] = "AdmissionNo", ["admissionnumber"] = "AdmissionNo", ["admno"] = "AdmissionNo",
        ["firstname"] = "FirstName", ["lastname"] = "LastName", ["surname"] = "LastName",
        ["gender"] = "Gender",
        ["dateofbirth"] = "DateOfBirth", ["dob"] = "DateOfBirth",
        ["class"] = "Class", ["classgrade"] = "Class", ["grade"] = "Class",
        ["school"] = "School", ["schoolname"] = "School",
        ["studentphone"] = "StudentPhone", ["phone"] = "StudentPhone", ["mobile"] = "StudentPhone",
        ["address"] = "Address",
        ["admissiondate"] = "AdmissionDate", ["joiningdate"] = "AdmissionDate",
        ["guardianname"] = "GuardianName", ["parentname"] = "GuardianName", ["fathername"] = "GuardianName",
        ["relation"] = "Relation",
        ["guardianphone"] = "GuardianPhone", ["parentphone"] = "GuardianPhone", ["guardianmobile"] = "GuardianPhone",
        ["guardianemail"] = "GuardianEmail", ["parentemail"] = "GuardianEmail",
        ["whatsappconsent"] = "WhatsAppConsent", ["whatsapp"] = "WhatsAppConsent", ["whatsappoptin"] = "WhatsAppConsent",
        ["batch"] = "Batch", ["batchname"] = "Batch",
    };

    public static (List<ImportRow> Rows, List<string> FileErrors) Parse(
        string text, DateOnly today, IReadOnlyCollection<ImportBatch> batches, int maxRows)
    {
        var fileErrors = new List<string>();
        var rows = new List<ImportRow>();
        var lines = Csv.Parse(text);
        if (lines.Count == 0)
        {
            fileErrors.Add("The file is empty.");
            return (rows, fileErrors);
        }

        var index = new Dictionary<string, int>();
        for (var i = 0; i < lines[0].Length; i++)
        {
            if (Aliases.TryGetValue(Normalise(lines[0][i]), out var column) && !index.ContainsKey(column)) index[column] = i;
        }
        foreach (var required in new[] { "FirstName", "GuardianName", "GuardianPhone" })
        {
            if (!index.ContainsKey(required)) fileErrors.Add($"Column \"{required}\" is missing. Use the template's header row.");
        }
        if (fileErrors.Count > 0) return (rows, fileErrors);

        if (lines.Count - 1 > maxRows)
        {
            fileErrors.Add($"The file has {lines.Count - 1} students; import at most {maxRows} at a time.");
            return (rows, fileErrors);
        }

        var byName = batches.GroupBy(b => b.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var admissionNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var n = 1; n < lines.Count; n++)
        {
            var cells = lines[n];
            string? Get(string column) =>
                index.TryGetValue(column, out var i) && i < cells.Length && cells[i].Trim() is { Length: > 0 } v ? v : null;

            var errors = new List<string>();
            var firstName = Get("FirstName");
            var lastName = Get("LastName");
            var studentName = string.Join(' ', new[] { firstName, lastName }.Where(x => x is not null));
            if (firstName is null) errors.Add("First name is missing.");

            var admissionNo = Get("AdmissionNo");
            if (admissionNo is not null && !admissionNos.Add(admissionNo))
                errors.Add($"Admission number {admissionNo} appears twice in the file.");

            var dob = ParseDate(Get("DateOfBirth"), "Date of birth", errors);
            var admissionDate = ParseDate(Get("AdmissionDate"), "Admission date", errors) ?? today;
            if (admissionDate > today) errors.Add("Admission date cannot be in the future.");

            var studentPhone = Get("StudentPhone");
            if (studentPhone is not null && PhoneNumber.Normalize(studentPhone) is null)
                errors.Add($"Student phone \"{studentPhone}\" is not a valid mobile number.");

            var guardianName = Get("GuardianName");
            if (guardianName is null) errors.Add("Guardian name is missing.");
            var guardianPhoneRaw = Get("GuardianPhone");
            var guardianPhone = PhoneNumber.Normalize(guardianPhoneRaw);
            if (guardianPhoneRaw is null) errors.Add("Guardian phone is missing.");
            else if (guardianPhone is null) errors.Add($"Guardian phone \"{guardianPhoneRaw}\" is not a valid mobile number.");

            var relation = GuardianRelation.Guardian;
            if (Get("Relation") is { } rel && !Enum.TryParse(rel, ignoreCase: true, out relation))
                errors.Add($"Relation \"{rel}\" is not one of Father, Mother, Guardian, Self.");

            var consent = false;
            if (Get("WhatsAppConsent") is { } c)
            {
                switch (c.ToLowerInvariant())
                {
                    case "yes" or "y" or "true" or "1": consent = true; break;
                    case "no" or "n" or "false" or "0": consent = false; break;
                    default: errors.Add($"WhatsAppConsent \"{c}\" should be Yes or No."); break;
                }
            }

            Guid? batchId = null;
            var batchName = Get("Batch");
            if (batchName is not null)
            {
                if (!byName.TryGetValue(batchName, out var batch))
                    errors.Add($"There is no active batch named \"{batchName}\".");
                else if (!batch.HasDefaultFeePlan)
                    errors.Add($"Batch \"{batch.Name}\" has no default fee plan. Set one on the batch, or leave Batch empty.");
                else
                    batchId = batch.Id;
            }

            AdmissionRequest? request = null;
            if (errors.Count == 0)
            {
                request = new AdmissionRequest(
                    AdmissionNo: admissionNo,
                    FirstName: firstName!,
                    LastName: lastName,
                    Gender: Get("Gender"),
                    DateOfBirth: dob,
                    Phone: studentPhone,
                    SchoolName: Get("School"),
                    ClassGrade: Get("Class"),
                    Address: Get("Address"),
                    AdmissionDate: admissionDate,
                    Notes: "Imported from CSV",
                    ExistingGuardianId: null,
                    NewGuardian: new GuardianInput(guardianName!, relation, guardianPhone!, null, Get("GuardianEmail"), consent),
                    BatchId: batchId,
                    FeePlanId: null,
                    DiscountAmount: 0);
            }

            rows.Add(new ImportRow(n + 1, studentName.Length > 0 ? studentName : "(no name)", guardianName, guardianPhone ?? guardianPhoneRaw,
                batchName, request, errors));
        }

        if (rows.Count == 0) fileErrors.Add("The file has a header row but no students.");
        return (rows, fileErrors);
    }

    private static string Normalise(string header) =>
        new(header.Trim().ToLowerInvariant().Where(ch => ch is not (' ' or '_' or '-' or '.')).ToArray());

    private static DateOnly? ParseDate(string? value, string label, List<string> errors)
    {
        if (value is null) return null;
        if (DateOnly.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
        errors.Add($"{label} \"{value}\" is not a date like 25-06-2010.");
        return null;
    }
}

/// <summary>CSV import of students (design doc 5.3 /students/import): template, preview with errors, then import.</summary>
public sealed class StudentImportService(
    IAppDbContext db, ICurrentUser user, IClock clock, StudentService students, IFeatureService features)
{
    public const int MaxRows = 1000;
    public const long MaxFileBytes = 1024 * 1024;

    public static byte[] Template() => Csv.Write(StudentCsv.Columns, [StudentCsv.ExampleRow]);

    public async Task<Result<ImportPreview>> PreviewAsync(string csvText, CancellationToken ct = default)
    {
        if (!user.CanManage()) return Result.Failure<ImportPreview>(Error.Forbidden());

        var batches = await db.Batches.AsNoTracking()
            .Where(b => b.IsActive)
            .Select(b => new ImportBatch(b.Id, b.Name, b.DefaultFeePlanId != null))
            .ToListAsync(ct);

        var (rows, fileErrors) = StudentCsv.Parse(csvText, clock.Today(), batches, MaxRows);

        // Admission numbers already used in the institute.
        var given = rows.Where(r => r.Request?.AdmissionNo is not null).Select(r => r.Request!.AdmissionNo!).ToList();
        if (given.Count > 0)
        {
            var taken = (await db.Students.AsNoTracking()
                    .Where(s => given.Contains(s.AdmissionNo))
                    .Select(s => s.AdmissionNo)
                    .ToListAsync(ct))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].Request?.AdmissionNo is { } no && taken.Contains(no))
                {
                    rows[i] = rows[i] with
                    {
                        Request = null,
                        Errors = [.. rows[i].Errors, $"Admission number {no} is already used by another student."],
                    };
                }
            }
        }

        string? limitWarning = null;
        var valid = rows.Count(r => r.IsValid);
        if (valid > 0 && await features.CanAddStudentsAsync(valid, ct) is { IsFailure: true } limit)
        {
            limitWarning = limit.FirstError?.Message;
        }

        return new ImportPreview(rows, fileErrors, limitWarning);
    }

    /// <summary>
    /// Imports every valid row through the normal admission (plan limit, admission number, enrollment and fee dues).
    /// A guardian whose phone is already registered is linked instead of duplicated, so siblings share one guardian.
    /// </summary>
    public async Task<Result<ImportResult>> ImportAsync(string csvText, CancellationToken ct = default)
    {
        var preview = await PreviewAsync(csvText, ct);
        if (preview.IsFailure) return Result.Failure<ImportResult>(preview.Errors.ToArray());
        if (preview.Value.FileErrors.Count > 0)
            return Result.Failure<ImportResult>(Error.Validation("File", preview.Value.FileErrors[0]));
        if (preview.Value.LimitWarning is { } warning)
            return Result.Failure<ImportResult>(Error.LimitReached(warning));

        var imported = 0;
        var failed = new List<ImportFailure>();
        foreach (var row in preview.Value.Rows)
        {
            ct.ThrowIfCancellationRequested();
            if (!row.IsValid)
            {
                failed.Add(new ImportFailure(row.LineNumber, row.StudentName, string.Join(" ", row.Errors)));
                continue;
            }

            var request = row.Request!;
            var phone = request.NewGuardian!.Phone;
            var existingGuardian = await db.Guardians.AsNoTracking()
                .Where(g => g.Phone == phone)
                .OrderBy(g => g.CreatedAt)
                .Select(g => (Guid?)g.Id)
                .FirstOrDefaultAsync(ct);
            if (existingGuardian is { } guardianId)
                request = request with { ExistingGuardianId = guardianId, NewGuardian = null };

            var result = await students.AdmitAsync(request, ct);
            if (result.IsSuccess) imported++;
            else failed.Add(new ImportFailure(row.LineNumber, row.StudentName, string.Join(" ", result.Errors.Select(e => e.Message))));
        }

        return new ImportResult(imported, failed);
    }
}
