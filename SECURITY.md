# Security policy

## Reporting a vulnerability

Report vulnerabilities privately through GitHub:
[Report a vulnerability](https://github.com/ophionos/Ophi/security/advisories/new). Do not open a
public issue, pull request, or discussion for a security problem.

Include the affected version (commit), the steps to reproduce, and the impact you expect.

## Supported versions

Ophi has no releases. Only the latest commit on `main` is supported; fixes land there.

## What to expect

Ophi has a single maintainer, so responses are best-effort. You get an acknowledgement in the
advisory thread, and the fix and a credit (if you want one) are published with the advisory.

## Scope

[docs/security.md](docs/security.md) describes the hardening and the accepted gaps. The SSRF check
does not resolve DNS names or re-check after redirects; that is known and tracked in
[#16](https://github.com/ophionos/Ophi/issues/16), so it needs no new report.
