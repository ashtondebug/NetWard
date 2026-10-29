# Contributing to NetWard

Thank you for helping improve NetWard 网界卫士. The project is an early Windows prototype; contributions must preserve its documented safety boundaries and distinguish implemented behavior from planned work.

## Before starting

1. Read [README.md](./README.md), [the architecture and implementation status](./docs/architecture.md), and [the security design](./docs/security.md).
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
- Do not add domain-category lists from guesswork. Category data must come from an owner-provided, reviewed source and include provenance and update expectations.
- Treat HTTP URL paths and query strings as sensitive. Do not collect credentials, cookies, page contents, or form data.
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
