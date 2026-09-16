# Security and privacy reporting

## Current availability

This project is currently a private source preview. There is no published
release, security response SLA, or enabled public vulnerability-reporting
endpoint advertised by this document.

If you already have access, contact the maintainer using an existing private
channel and agree on a safe reporting route before sharing sensitive details.
Do not put credentials or private account data in an Issue, pull request,
commit, or attachment, even while the repository is private: that material may
later become visible when the repository or discussion is shared.

Before any public launch, the maintainer must establish and verify a private
reporting channel, such as GitHub private vulnerability reporting where
available, and update this document with the confirmed route. Do not assume
that adding this file enables that feature.

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
