using InstituteHub.Application.Messaging;
using InstituteHub.Domain.Messaging;

namespace InstituteHub.UnitTests.Application;

public class ReminderRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 8);

    [Fact]
    public void Due_in_three_days_gets_an_upcoming_reminder() =>
        ReminderRules.Classify(Today.AddDays(3), Today, null).ShouldBe(ReminderKind.Upcoming);

    [Fact]
    public void Due_today_gets_a_reminder() =>
        ReminderRules.Classify(Today, Today, null).ShouldBe(ReminderKind.DueToday);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void Other_future_dates_get_nothing(int days) =>
        ReminderRules.Classify(Today.AddDays(days), Today, null).ShouldBe(ReminderKind.None);

    [Fact]
    public void Overdue_and_never_reminded_gets_an_overdue_reminder() =>
        ReminderRules.Classify(Today.AddDays(-1), Today, null).ShouldBe(ReminderKind.Overdue);

    [Fact]
    public void Overdue_is_repeated_only_every_seven_days()
    {
        ReminderRules.Classify(Today.AddDays(-20), Today, Today.AddDays(-6)).ShouldBe(ReminderKind.None);
        ReminderRules.Classify(Today.AddDays(-20), Today, Today.AddDays(-7)).ShouldBe(ReminderKind.Overdue);
    }

    [Fact]
    public void Never_twice_on_the_same_day()
    {
        ReminderRules.Classify(Today, Today, Today).ShouldBe(ReminderKind.None);
        ReminderRules.Classify(Today.AddDays(3), Today, Today).ShouldBe(ReminderKind.None);
    }
}

public class TemplateRendererTests
{
    [Fact]
    public void Fills_placeholders_and_blanks_unknown_ones()
    {
        var text = TemplateRenderer.Render(
            "Dear {{guardian}}, Rs {{ amount }} for {{student}} is due on {{due_date}}.{{missing}}",
            new Dictionary<string, string> { ["guardian"] = "Mr Sharma", ["amount"] = "12,500", ["student"] = "Aarav", ["due_date"] = "11 Oct 2026" });

        text.ShouldBe("Dear Mr Sharma, Rs 12,500 for Aarav is due on 11 Oct 2026.");
    }
}

public class MessageStatusRulesTests
{
    [Theory]
    [InlineData(MessageStatus.Sent, MessageStatus.Delivered, true)]
    [InlineData(MessageStatus.Delivered, MessageStatus.Read, true)]
    [InlineData(MessageStatus.Sent, MessageStatus.Read, true)]
    [InlineData(MessageStatus.Read, MessageStatus.Delivered, false)]
    [InlineData(MessageStatus.Delivered, MessageStatus.Sent, false)]
    [InlineData(MessageStatus.Sent, MessageStatus.Failed, true)]
    [InlineData(MessageStatus.Delivered, MessageStatus.Failed, false)]
    [InlineData(MessageStatus.Failed, MessageStatus.Delivered, false)]
    [InlineData(MessageStatus.Sent, MessageStatus.Sent, false)]
    public void Status_never_moves_backwards(MessageStatus from, MessageStatus to, bool allowed) =>
        MessageStatusRules.CanMove(from, to).ShouldBe(allowed);

    [Theory]
    [InlineData("delivered", MessageStatus.Delivered)]
    [InlineData("READ", MessageStatus.Read)]
    [InlineData("undelivered", MessageStatus.Failed)]
    [InlineData("sent", MessageStatus.Sent)]
    public void Provider_words_are_mapped(string word, MessageStatus expected) =>
        MessageStatusRules.Parse(word).ShouldBe(expected);

    [Fact]
    public void Unknown_provider_words_are_ignored() => MessageStatusRules.Parse("typing").ShouldBeNull();
}

public class MessageFormatTests
{
    [Fact]
    public void Amounts_use_indian_grouping()
    {
        MessageFormat.Amount(125000).ShouldBe("1,25,000");
        MessageFormat.Amount(1250.5m).ShouldBe("1,250.50");
    }

    [Fact]
    public void Names_are_joined_in_plain_english()
    {
        MessageFormat.Names(["Aarav"]).ShouldBe("Aarav");
        MessageFormat.Names(["Aarav", "Diya"]).ShouldBe("Aarav and Diya");
        MessageFormat.Names(["Aarav", "Diya", "Kabir"]).ShouldBe("Aarav, Diya and Kabir");
    }

    [Fact]
    public void Phone_numbers_are_masked_for_logs() =>
        MessageFormat.MaskPhone("+919876543210").ShouldBe("+91******3210");

    [Fact]
    public void Plan_features_are_read_from_json()
    {
        MessagingService.ParseFeatures("""{"reminders": true, "sms": false}""")
            .ShouldBe(new MessagingFeatures(true, false, false));
        MessagingService.ParseFeatures("""{"reminders": true, "sms": true, "absent_alerts": true}""")
            .ShouldBe(new MessagingFeatures(true, true, true));
        MessagingService.ParseFeatures(null).ShouldBe(new MessagingFeatures(true, false, false));
    }
}
