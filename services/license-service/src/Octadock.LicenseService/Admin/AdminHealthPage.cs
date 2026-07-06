using System.Globalization;
using System.Net;
using System.Text;
using Octadock.LicenseService.Data;

namespace Octadock.LicenseService.Admin;

/// <summary>
/// Renders the founder launch-health page (WS6) as a single self-contained HTML
/// string — no external assets, scripts, fonts, or images (nothing to load, nothing
/// to leak). Every number is labelled with its SOURCE, and any number backed by a
/// founder-gated (not-built) pipeline is rendered as "no data by design" rather than
/// fabricated. Pure string building so it is trivially unit-testable.
/// </summary>
public static class AdminHealthPage
{
    private const string NoData = "no data by design";

    /// <summary>
    /// Renders the page. When <paramref name="authenticated"/> is false a loud banner
    /// warns that no app-level auth is in front and a network gate is required.
    /// </summary>
    public static string Render(LaunchHealthSnapshot health, bool authenticated, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(health);

        var sb = new StringBuilder(4096);
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<title>Octadock License Service — Launch Health</title>");
        sb.Append("<style>");
        sb.Append(
            "body{font:14px/1.5 -apple-system,Segoe UI,Roboto,sans-serif;margin:0;background:#0f1115;color:#e6e8eb}" +
            "main{max-width:820px;margin:0 auto;padding:24px}" +
            "h1{font-size:20px;margin:0 0 4px}" +
            ".sub{color:#8b929e;margin:0 0 20px;font-size:13px}" +
            ".banner{background:#7a1220;color:#fff;padding:12px 16px;border-radius:8px;margin:0 0 20px;font-weight:600}" +
            ".ok-banner{background:#123a1e;color:#c9f0d4;padding:10px 16px;border-radius:8px;margin:0 0 20px;font-size:13px}" +
            "table{width:100%;border-collapse:collapse}" +
            "th,td{text-align:left;padding:8px 10px;border-bottom:1px solid #232833;vertical-align:top}" +
            "th{color:#8b929e;font-weight:600;font-size:12px;text-transform:uppercase;letter-spacing:.04em}" +
            ".metric{font-weight:600;color:#e6e8eb}" +
            ".value{font-variant-numeric:tabular-nums}" +
            ".src{color:#8b929e;font-size:12px}" +
            ".gated{color:#c99a2e}" +
            ".crit{color:#ff6b6b}" +
            "footer{color:#5f6672;font-size:12px;margin-top:24px}");
        sb.Append("</style></head><body><main>");

        sb.Append("<h1>Octadock License Service — Launch Health</h1>");
        sb.Append("<p class=\"sub\">Generated ")
          .Append(Enc(now.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture)))
          .Append("</p>");

        if (!authenticated)
        {
            sb.Append("<div class=\"banner\">UNAUTHENTICATED — no app-level token is set. " +
                      "Put a network gate in front (Cloudflare Access + WebAuthn, founder-gated) " +
                      "before exposing this page.</div>");
        }

        sb.Append("<table><thead><tr><th>Metric</th><th>Value</th><th>Source</th></tr></thead><tbody>");

        Row(sb, "Licenses issued (last 24h)",
            health.LicensesIssuedLast24h.ToString(CultureInfo.InvariantCulture),
            "DB: licenses.created_at within 24h");
        Row(sb, "Licenses total",
            health.LicensesTotal.ToString(CultureInfo.InvariantCulture), "DB: licenses");
        Row(sb, "Licenses active",
            health.LicensesActive.ToString(CultureInfo.InvariantCulture), "DB: licenses.status = active");
        Row(sb, "Licenses revoked",
            health.LicensesRevoked.ToString(CultureInfo.InvariantCulture), "DB: licenses.status != active");

        Row(sb, "Webhook events total",
            health.WebhookEventsTotal.ToString(CultureInfo.InvariantCulture), "DB: webhook_events");
        Row(sb, "Most recent webhook received",
            FormatWebhookAge(health.MostRecentWebhookReceivedAt, now),
            "DB: max(webhook_events.received_at)");

        Row(sb, "Reconciliation diff (paid-but-no-key)",
            health.ReconciliationDiff.ToString(CultureInfo.InvariantCulture),
            "ReconciliationService.LastUnreconciledSessions",
            critical: health.ReconciliationDiff > 0);
        Row(sb, "Paid-session source configured",
            health.PaidSessionSourceConfigured ? "yes" : "no (founder-gated Stripe key)",
            "IPaidSessionSource.IsConfigured");

        Row(sb, "Activation success rate",
            FormatRate(health.ActivationSuccessRate, health.ActivationAttempts),
            $"DB: audit_log device.activated vs device.activation_failed (last {health.ActivationAttempts} attempts)");
        Row(sb, "Issued-not-activated",
            FormatRate(health.IssuedNotActivatedRate, health.LicensesActive,
                extra: $"{health.IssuedNotActivated}/{health.LicensesActive}"),
            "DB: active licenses with zero non-deactivated activations");

        GatedRow(sb, "Email delivered", "email pipeline founder-gated (not built)");
        GatedRow(sb, "Email bounced", "email pipeline founder-gated (not built)");
        GatedRow(sb, "License resend count", "resend endpoint founder-gated (not built)");

        sb.Append("</tbody></table>");

        sb.Append("<footer>Production must sit behind Cloudflare Access + WebAuthn (founder-gated, " +
                  "network-level). \"no data by design\" marks metrics whose source is founder-gated and " +
                  "not yet built — they are never fabricated.</footer>");

        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    private static void Row(
        StringBuilder sb, string metric, string value, string source, bool critical = false)
    {
        sb.Append("<tr><td class=\"metric\">").Append(Enc(metric)).Append("</td>");
        sb.Append("<td class=\"value")
          .Append(critical ? " crit" : string.Empty)
          .Append("\">").Append(Enc(value)).Append("</td>");
        sb.Append("<td class=\"src\">").Append(Enc(source)).Append("</td></tr>");
    }

    private static void GatedRow(StringBuilder sb, string metric, string source)
    {
        sb.Append("<tr><td class=\"metric\">").Append(Enc(metric)).Append("</td>");
        sb.Append("<td class=\"value gated\">").Append(NoData).Append("</td>");
        sb.Append("<td class=\"src\">").Append(Enc(source)).Append("</td></tr>");
    }

    private static string FormatWebhookAge(DateTimeOffset? received, DateTimeOffset now)
    {
        if (received is not { } at)
        {
            return "none received yet";
        }

        TimeSpan age = now - at;
        double minutes = Math.Max(0, age.TotalMinutes);
        return $"{minutes.ToString("F0", CultureInfo.InvariantCulture)} min ago";
    }

    private static string FormatRate(double? rate, long denominator, string? extra = null)
    {
        if (denominator == 0 || rate is null)
        {
            return "no attempts yet";
        }

        string percent = (rate.Value * 100).ToString("F1", CultureInfo.InvariantCulture) + "%";
        return extra is null ? percent : $"{percent} ({extra})";
    }

    private static string Enc(string value) => WebUtility.HtmlEncode(value);
}
