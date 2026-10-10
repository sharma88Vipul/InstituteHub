using System.Text;
using InstituteHub.Application.Billing;
using InstituteHub.Application.Common;
using InstituteHub.Application.Reports;
using InstituteHub.Application.Students;
using InstituteHub.Application.Tenants;
using InstituteHub.Domain.Students;

namespace InstituteHub.UnitTests.Application;

public class CsvTests
{
    [Fact]
    public void Parses_quotes_commas_and_line_breaks_inside_fields()
    {
        var rows = Csv.Parse("﻿Name,Address\r\n\"Sharma, Aarav\",\"12 \"\"A\"\" Block\nLudhiana\"\r\n\r\nDiya,\n");

        rows.Count.ShouldBe(3);
        rows[0].ShouldBe(new[] { "Name", "Address" });
        rows[1].ShouldBe(new[] { "Sharma, Aarav", "12 \"A\" Block\nLudhiana" });
        rows[2].ShouldBe(new[] { "Diya", "" });
    }

    [Fact]
    public void Writes_excel_friendly_csv_and_neutralises_formulas()
    {
        var bytes = Csv.Write(["Name", "Amount", "Note"], [new object?[] { "Sharma, Aarav", 1250.5m, "=HYPERLINK(\"x\")" }]);

        bytes.Take(3).ShouldBe(Encoding.UTF8.GetPreamble());
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        text.ShouldBe("Name,Amount,Note\r\n\"Sharma, Aarav\",1250.5,\"'=HYPERLINK(\"\"x\"\")\"\r\n");
    }

    [Fact]
    public void Round_trips_through_write_and_parse()
    {
        var bytes = Csv.Write(["A", "B"], [new object?[] { "x,y", "line1\nline2" }]);
        var rows = Csv.Parse(Encoding.UTF8.GetString(bytes));
        rows[1].ShouldBe(new[] { "x,y", "line1\nline2" });
    }
}

public class StudentCsvTests
{
    private static readonly DateOnly Today = new(2026, 10, 8);
    private static readonly ImportBatch Morning = new(Guid.CreateVersion7(), "Class 10 Maths – Morning", true);
    private static readonly ImportBatch NoPlan = new(Guid.CreateVersion7(), "Spoken English", false);

    private static List<ImportRow> Parse(string csv) =>
        StudentCsv.Parse(csv, Today, [Morning, NoPlan], maxRows: 1000).Rows;

    [Fact]
    public void Valid_row_becomes_an_admission_request()
    {
        var rows = Parse(
            "First Name,Last_Name,DOB,Guardian Name,Relation,Guardian Phone,WhatsApp,Batch\n" +
            "Aarav,Sharma,15-08-2010,Rakesh Sharma,father,98765 43210,yes,class 10 maths – morning\n");

        var row = rows.ShouldHaveSingleItem();
        row.IsValid.ShouldBeTrue(string.Join("; ", row.Errors));
        var r = row.Request!;
        r.FirstName.ShouldBe("Aarav");
        r.LastName.ShouldBe("Sharma");
        r.DateOfBirth.ShouldBe(new DateOnly(2010, 8, 15));
        r.AdmissionDate.ShouldBe(Today);
        r.BatchId.ShouldBe(Morning.Id);
        r.NewGuardian!.Phone.ShouldBe("+919876543210");
        r.NewGuardian.Relation.ShouldBe(GuardianRelation.Father);
        r.NewGuardian.WhatsAppOptIn.ShouldBeTrue();
        row.LineNumber.ShouldBe(2);
    }

    [Fact]
    public void Problems_are_reported_per_row_in_plain_language()
    {
        var rows = Parse(
            "AdmissionNo,FirstName,GuardianName,GuardianPhone,DateOfBirth,Batch,WhatsAppConsent\n" +
            "A1,,Parent,12345,31-02-2010,Unknown batch,maybe\n" +
            "A1,Diya,Parent,9876543211,,Spoken English,\n");

        rows[0].IsValid.ShouldBeFalse();
        rows[0].Errors.ShouldContain("First name is missing.");
        rows[0].Errors.ShouldContain(e => e.Contains("not a valid mobile number"));
        rows[0].Errors.ShouldContain(e => e.Contains("Date of birth"));
        rows[0].Errors.ShouldContain(e => e.Contains("no active batch"));
        rows[0].Errors.ShouldContain(e => e.Contains("Yes or No"));

        rows[1].Errors.ShouldContain(e => e.Contains("appears twice"));
        rows[1].Errors.ShouldContain(e => e.Contains("no default fee plan"));
    }

    [Fact]
    public void Missing_required_columns_stop_the_import()
    {
        var (rows, fileErrors) = StudentCsv.Parse("Name,Phone\nAarav,9876543210\n", Today, [], 1000);
        rows.ShouldBeEmpty();
        fileErrors.Count.ShouldBe(3);
    }

    [Fact]
    public void Too_many_rows_are_refused()
    {
        var csv = "FirstName,GuardianName,GuardianPhone\n" + string.Concat(Enumerable.Repeat("A,B,9876543210\n", 3));
        var (_, fileErrors) = StudentCsv.Parse(csv, Today, [], maxRows: 2);
        fileErrors.ShouldHaveSingleItem().ShouldContain("at most 2");
    }

    [Fact]
    public void Template_has_every_column_and_parses_cleanly()
    {
        var text = Encoding.UTF8.GetString(StudentImportService.Template());
        var (rows, fileErrors) = StudentCsv.Parse(text, Today, [], 1000);
        fileErrors.ShouldBeEmpty();
        rows.ShouldHaveSingleItem().IsValid.ShouldBeTrue();
    }
}

public class DuesAgeingTests
{
    private static readonly DateOnly Today = new(2026, 10, 8);

    [Theory]
    [InlineData(5, 0)]
    [InlineData(0, 0)]
    [InlineData(-1, 1)]
    [InlineData(-30, 1)]
    [InlineData(-31, 2)]
    [InlineData(-60, 2)]
    [InlineData(-61, 3)]
    [InlineData(-90, 3)]
    [InlineData(-91, 4)]
    public void Dues_fall_into_the_right_bucket(int offsetDays, int bucket) =>
        DuesAgeing.Bucket(Today.AddDays(offsetDays), Today).ShouldBe(bucket);
}

public class PlanFeaturesTests
{
    [Fact]
    public void Only_true_flags_are_on()
    {
        var on = PlanFeatures.Parse("""{"reminders": true, "sms": false, "absent_alerts": "yes"}""");
        on.ShouldBe(new[] { PlanFeatures.Reminders }, ignoreOrder: true);
    }

    [Fact]
    public void Broken_json_switches_everything_off() => PlanFeatures.Parse("{not json").ShouldBeEmpty();

    [Fact]
    public void Trials_have_every_feature()
    {
        var trial = new PlanLimits("GROWTH", "Growth", 250, IsTrial: true, null, PlanFeatures.Parse("{}"));
        PlanFeatures.All.ShouldAllBe(f => trial.Has(f));

        var paid = trial with { IsTrial = false };
        paid.Has(PlanFeatures.Sms).ShouldBeFalse();
    }

    [Fact]
    public void Usage_reports_room_left()
    {
        var usage = new PlanUsage(new PlanLimits("STARTER", "Starter", 75, false, null, PlanFeatures.Parse(null)), 70);
        usage.RemainingStudents.ShouldBe(5);
        usage.UsedPercent.ShouldBe(70 * 100.0 / 75);
    }
}

public class InstituteProfileValidatorTests
{
    private static InstituteProfileRequest Valid() => new(
        "Sharma Classes", "Neha Sharma", "9876543210", "office@sharma.in", "12 Model Town", "Ludhiana", "Punjab", "141002",
        "03ABCDE1234F1Z5", "SCL");

    [Fact]
    public void A_complete_profile_is_valid() =>
        new InstituteProfileRequestValidator().Validate(Valid()).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("S")]
    [InlineData("SCL/26")]
    [InlineData("TOOLONGPREFIX")]
    [InlineData("")]
    public void Receipt_prefix_must_be_2_to_10_letters_or_digits(string prefix) =>
        new InstituteProfileRequestValidator().Validate(Valid() with { ReceiptPrefix = prefix }).IsValid.ShouldBeFalse();

    [Fact]
    public void Pin_code_and_gstin_are_checked()
    {
        var result = new InstituteProfileRequestValidator().Validate(Valid() with { Pincode = "1410", Gstin = "123" });
        result.Errors.Select(e => e.PropertyName).ShouldBe(new[] { "Pincode", "Gstin" }, ignoreOrder: true);
    }
}
