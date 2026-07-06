# Commercial Infrastructure Setup - 2026-07-06

Status: live external setup record. No secrets are stored here.

## Stripe

Connected Stripe account:

- Display name: `OctaDock`
- Account ID: `acct_1TqGvTKNDvjYLyJh`

Live catalog created:

| Kind | Name | Stripe ID | Active | Amount | Lookup key |
| --- | --- | --- | --- | --- | --- |
| Product | `Octadock Local` | `prod_UpwxhSr4r3Fg1r` | Yes | - | - |
| Price | `Local Beta one-time $49` | `price_1TqH3lKNDvjYLyJhld4yYPIZ` | Yes | `$49.00 USD` | `octadock_local_beta_usd_49` |
| Price | `Local 1.0 one-time $59 inactive` | `price_1TqH3mKNDvjYLyJhNsTOajmK` | No | `$59.00 USD` | `octadock_local_1_0_usd_59` |

Launch integration rule:

- The license service must validate the exact active beta price ID or lookup key before issuing a license.
- Do not issue licenses for arbitrary `checkout.session.completed` events just because `payment_status=paid`.
- Do not publish a live Payment Link or Checkout entry point until legal URLs, tax posture, webhook delivery, and license-email delivery are all verified.

Not completed by connector:

- Stripe webhook endpoint creation was not exposed by the current MCP tool search.
- Account public details, Terms URL, Privacy URL, Stripe Tax / Managed Payments eligibility, and webhook failure alerts still need dashboard verification.

## DNS

Domain: `octadock.com`

Registrar/DNS: Namecheap.

Current website records are still Namecheap parking:

| Host | Type | Value |
| --- | --- | --- |
| `www` | `CNAME` | `parkingpage.namecheap.com.` |
| `@` | `URL` | `http://www.octadock.com/` |

FastMail external-DNS records added:

| Host | Type | Value | Priority |
| --- | --- | --- | --- |
| `@` | `MX` | `in1-smtp.messagingengine.com.` | `10` |
| `@` | `MX` | `in2-smtp.messagingengine.com.` | `20` |
| `@` | `TXT` | `v=spf1 include:spf.messagingengine.com ?all` | - |
| `fm1._domainkey` | `CNAME` | `fm1.octadock.com.dkim.fmhosted.com.` | - |
| `fm2._domainkey` | `CNAME` | `fm2.octadock.com.dkim.fmhosted.com.` | - |
| `fm3._domainkey` | `CNAME` | `fm3.octadock.com.dkim.fmhosted.com.` | - |
| `_dmarc` | `TXT` | `v=DMARC1; p=none;` | - |

Next DNS steps:

- Replace parking records only after the website/API host target is known.
- If FastMail shows domain-specific alternate DKIM values, replace the three DKIM CNAME targets with the values from FastMail's domain setup screen.
- After mailbox verification, create/verify `support@octadock.com`, `postmaster@octadock.com`, and whatever sender address the license service will use.
