# Security and privacy reporting

## Current availability

This project is a public source preview. There are no packaged releases or
guaranteed security response times. Reports should identify the affected commit.

## Report a vulnerability privately

Use [GitHub private vulnerability reporting](https://github.com/Abo-Fat/copilot-usage-monitor/security/advisories/new)
for this repository. Sign in to GitHub and submit a private report to the
maintainer through that form rather than opening a public Issue or pull request.

If the reporting form is unavailable, do not post the vulnerability details
publicly. Use an existing private channel to agree on an alternative route.
Never include credentials or private account data in a report, commit, or
attachment; private reporting is not a reason to share real secrets.

Non-sensitive bug reports may use the
[issue tracker](https://github.com/Abo-Fat/copilot-usage-monitor/issues) after
removing personal data.

## What to include

Provide the affected commit/version, Windows and WebView2 versions, a concise
description of impact, and reproduction steps using synthetic accounts or
offline test fixtures. Include only the minimum redacted error details needed.
Do not test against accounts or systems without authorization.

Do not attach:

- PATs, passwords, authentication headers, cookies, or session tokens.
- The application's `GitHubWebView2` profile or another browser's profile.
- Real `settings.json`, `features-snapshot.json`, or legacy snapshots.
- Full page captures, billing diagnostics, crash dumps, or unredacted screenshots.
- Private names, emails, local user paths, organization data, or account usage.

## Local data and possible exposure

Application settings, cached usage, and its independent browser session live
under `%LOCALAPPDATA%\CopilotUsage`. They are not source files. Keep them out of
Git, source archives, build artifacts, and support attachments. Protect access
to the device and this directory; a logged-in browser session has account
privileges.

The in-app sign-out clears this application's browser data, but is not a
global GitHub session revocation and does not remove settings or usage caches.
See the README for local cleanup instructions.

If a credential or session may have been exposed, revoke the affected token or
session through GitHub first and investigate the exposure. Deleting a file,
adding it to `.gitignore`, or changing repository visibility does not remove
copies or erase earlier Git history.
