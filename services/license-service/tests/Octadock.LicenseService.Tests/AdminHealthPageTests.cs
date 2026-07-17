using FluentAssertions;
using Octadock.LicenseService.Admin;
using Octadock.LicenseService.Data;
using Xunit;

namespace Octadock.LicenseService.Tests;

/// <summary>
/// The founder launch-health page (WS6): a self-contained HTML page that labels every
/// number with its source, renders founder-gated numbers as "no data by design", and
/// shows a loud banner when no app-level auth is configured.
/// </summary>
public class AdminHealthPageTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);

    private static LaunchHealthSnapshot Sample()
        => new(
            LicensesTotal: 10,
            LicensesActive: 8,
            LicensesRevoked: 2,
            LicensesIssuedLast24h: 4,
            WebhookEventsTotal: 25,
            MostRecentWebhookReceivedAt: Now.AddMinutes(-15),
            ActivationsSucceeded: 18,
            ActivationsFailed: 2,
            IssuedNotActivated: 1)
        { ReconciliationDiff = 0, PaidSessionSourceConfigured = true };

    [Fact]
    public void Page_is_self_contained_html_with_no_external_assets()
    {
        string html = AdminHealthPage.Render(Sample(), Now);

        html.Should().StartWith("<!doctype html>");
        html.Should().Contain("Launch Health");
        // No external resource references.
        html.Should().NotContain("http://");
        html.Should().NotContain("https://");
        html.Should().NotContain("<script");
        html.Should().NotContain("src=");
    }

    [Fact]
    public void Page_renders_live_numbers_with_their_sources()
    {
        string html = AdminHealthPage.Render(Sample(), Now);

        html.Should().Contain("Licenses issued (last 24h)");
        html.Should().Contain("DB: licenses.created_at within 24h");
        html.Should().Contain("90.0%", "18/20 activation success rate");
        html.Should().Contain("15 min ago", "webhook received 15 min before now");
    }

    [Fact]
    public void Founder_gated_numbers_render_no_data_by_design()
    {
        string html = AdminHealthPage.Render(Sample(), Now);

        html.Should().Contain("Email delivered");
        html.Should().Contain("Email bounced");
        html.Should().Contain("License resend count");
        html.Should().Contain("no data by design");
        html.Should().Contain("founder-gated");
    }

    [Fact]
    public void Page_contains_no_unauthenticated_fallback()
    {
        string html = AdminHealthPage.Render(Sample(), Now);
        html.Should().NotContain("UNAUTHENTICATED");
    }

    [Fact]
    public void Nonzero_reconciliation_diff_is_flagged_critical()
    {
        LaunchHealthSnapshot diffHealth = Sample() with { ReconciliationDiff = 3 };
        string html = AdminHealthPage.Render(diffHealth, Now);

        html.Should().Contain("class=\"value crit\"", "a paid-but-no-key diff must be visually flagged");
    }
}
