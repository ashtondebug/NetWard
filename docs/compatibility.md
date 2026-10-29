# Windows compatibility

## Target baseline

- Minimum planned OS: Windows 7 SP1, x86 and x64.
- Planned managed runtime: .NET Framework 4.8, not 4.8.1.
- Proposed UI: WinForms.
- Windows 7 support is a target, not a test result. Windows 7 is out of support.

Microsoft's [.NET Framework system requirements](https://learn.microsoft.com/en-us/dotnet/framework/get-started/system-requirements) list Windows 7 SP1 (32-bit and 64-bit) for separately installing .NET Framework 4.8. They do not list .NET Framework 4.8.1 for Windows 7. A Windows 7 release must therefore target 4.8 and document installer prerequisites; machines without SP1 or the required servicing updates are not assumed compatible.

Microsoft's [WFP overview](https://learn.microsoft.com/en-us/windows/win32/fwp/windows-filtering-platform-start-page) lists client support starting at Windows Vista, but this is not evidence that a specific callout driver, compiler toolchain, signing arrangement, or domain-inspection design works on Windows 7.

The current assessment does not approve a WFP driver. Windows 7 SP1 x86/x64 remain untested for any project-specific WFP filtering, driver build/signing, installation, recovery, or uninstall. See the [WFP feasibility assessment](wfp-feasibility.md) before proposing a driver experiment.

## Environment inspected on 2026-10-29

- Windows 10 Home China, x64, build 26300.
- Windows PowerShell 5.1.
- .NET SDK 9.0.100; .NET runtimes 6.0.25 and 9.0.0.
- Installed .NET Framework release registry value: `534296` (runtime present; this is not a .NET Framework 4.8 targeting pack).
- Visual Studio 2022 Build Tools and MSVC compiler are installed; compiler is not on the default shell PATH.
- No .NET Framework 4.8 reference assemblies were found in the standard reference-assembly locations.
- Windows Kits 10 files exist, but the inspected WDK kernel headers/tools were not found.
- No Windows 7 environment was available during this inspection.
- SQLite persistence uses the `System.Data.SQLite.Core` .NET Framework provider with x86/x64 native interop assets. It runs in the current Windows 10 test environment; Windows 7 behavior of this exact provider/native bundle remains unverified.

The build uses the NuGet .NET Framework reference-assemblies package rather than changing the installed runtime or system environment. No system-wide dependencies, network settings, drivers, or firewall rules have been changed.

## Required validation matrix

Before calling a release Windows 7 compatible, test on clean Windows 7 SP1 x86 and x64 virtual machines, with each required servicing/runtime prerequisite documented:

- Installer, first run, GUI, file permissions, and ordinary uninstall.
- .NET Framework 4.8 availability and TLS certificate validation.
- Management/agent protocol, reconnection, and database behavior.
- Filtering driver install/start/stop/update/rollback only if a driver is approved.
- HTTP, HTTPS SNI, DoH, QUIC/HTTP/3, browser proxy changes, explicit proxies, and VPN limitations.
- Crash recovery, fail-closed behavior, diagnostics, and emergency restoration.

None of these Windows 7 tests have been run.
