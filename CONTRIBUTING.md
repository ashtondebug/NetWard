# Contributing to NetWard

Thank you for helping improve NetWard 网界卫士. The project is an early Windows prototype; contributions must preserve its documented safety boundaries and distinguish implemented behavior from planned work.

## Before starting

1. Read [README.md](./README.md), [the complete owner-requested requirements and current status](./docs/product-requirements.md), [the architecture and implementation status](./docs/architecture.md), and [the security design](./docs/security.md).
2. Check [the development roadmap](./docs/roadmap.md) and existing issues before starting overlapping work.
3. For substantial architectural changes, filtering mechanisms, identity collection, kiosk behavior, or system configuration, open an issue or proposal first. Explain the Windows versions, privileges, bypass cases, user notice, rollback, and recovery path.

## Development environment

- Windows with .NET SDK 9.0.
- .NET Framework 4.8 or later to run the applications.
- NuGet connectivity for declared package dependencies.

Build and run the project's checks from the repository root:

```powershell
dotnet build .\NetWard.sln --configuration Release
.\scripts\test.ps1
```

The test script builds the solution, runs policy/persistence/proxy integration tests, and performs proxy CLI smoke tests. Tests should use local loopback services and must not depend on third-party websites or production endpoints.

## Change expectations

- Keep changes focused and follow existing C# 7.3, .NET Framework 4.8, WinForms, and PowerShell patterns.
- Add or update tests for behavior, persistence, schema migrations, and failure cases.
- Update README, architecture, security, installation, or compatibility documentation whenever observable behavior or limitations change.
- Never claim complete device-wide enforcement, HTTPS URL visibility, a trusted administrator identity, kiosk recovery, or Windows-version support without implementation and corresponding tests.
- Preserve the owner's requested product behavior documented in [`docs/product-requirements.md`](./docs/product-requirements.md); if a requirement conflicts with browser/OS security constraints, document the boundary and propose a consented, recoverable architecture rather than faking the behavior.
- Do not add domain-category lists from guesswork. Category data must come from an owner-provided, reviewed source and include provenance and update expectations.
- Treat HTTP URL paths and query strings as sensitive. Do not collect credentials, cookies, page contents, or form data.
- The owner explicitly decided not to use the webcam. Do not add camera or screen capture, and do not claim evidence was collected when it was not.
- The requested audit fields include endpoint IP, complete URL where observable, interactive Windows username, browsing duration, and 30-day retention. HTTPS paths, the real device identity behind the loopback proxy, and meaningful browsing duration require additional approved instrumentation; disclose unavailable values instead of fabricating them.
- Access-request functionality is expected to include endpoint/reason/attempted-URL context; Windows administrator notifications; allow with expiry, deny, and duration-based ignore/deduplication. Keep the form clearly marked as nonfunctional until authenticated transport and review UI exist.
- The requested fullscreen flow is a managed kiosk session launched before browsing, with Edge then Chrome preference and a default-browser fallback that may not be fullscreen. A legitimate administrator exit and tested recovery are mandatory.
- Adapt the Windows management experience for the requested Windows 7/10/11 targets, but claim compatibility only after testing each supported OS/browser combination.
- Do not add TLS interception, WFP drivers, firewall/DNS/hosts changes, service installation, persistence hooks, or restrictions on user recovery without an approved security design, explicit authorization, and a tested uninstall/recovery plan.
- Do not commit credentials, private keys, tokens, personal data, build output, or local databases.
- Preserve existing user data and unrelated worktree changes. Use sequential, transactional SQLite migrations and test upgrades from supported older schemas.

## Pull requests

Pull requests should include:

1. A concise problem statement and scope.
2. The implementation and user-visible behavior.
3. Tests/build commands run and their results.
4. Documentation changes and any remaining limitations.
5. Security, privacy, compatibility, and rollback considerations when applicable.

Keep pull requests reviewable and avoid unrelated formatting or generated-file churn. Do not mark an item complete in the roadmap until implementation, relevant tests, and documentation agree.

## Reporting security issues

Do not publish credentials, exploitable details, or sensitive user data in an issue. Report security-sensitive concerns privately to the repository owner and include the affected version, impact, and a safe reproduction summary.
