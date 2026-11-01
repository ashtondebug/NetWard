# NetWard Modern Demo

This directory contains two independent WinUI 3 interface demos:

- **AdminDemo** — management-side overview, device list, access-policy view, and event list.
- **AgentDemo** — managed-device status and policy summary.

These are UI demonstrations, not a working network access-control system. They do not pair devices, connect to a management service, persist or synchronize policies, record real browsing events, or filter network traffic. Labels and sample data in both applications are illustrative only.

## Requirements

- Windows 10 build 19041 is configured as the minimum target platform; runtime compatibility has not been verified across Windows versions.
- .NET 9 SDK.
- .NET 9 Runtime and Windows App Runtime 2.6.4 on machines running the default framework-dependent build.
- NuGet access to restore `Microsoft.WindowsAppSDK` 2.6.4.

The demos target x64. Their target framework and SDK version are project build settings, not a compatibility guarantee for a future production release.
Both apps declare Per-Monitor V2 DPI awareness so Windows renders the WinUI interface at the display's native scaling instead of bitmap-scaling an unaware window.

## Build and publish

The default framework-dependent publish is significantly faster to cold-start and smaller to distribute, but needs .NET 9 Runtime and Windows App Runtime 2.6.4 already installed on the target machine. From the repository root:

```powershell
dotnet publish demos\modern\NetWard.Modern.AdminDemo\NetWard.Modern.AdminDemo.csproj -c Release -r win-x64 --self-contained false -p:WindowsAppSDKSelfContained=false -p:PublishDir=..\publish\admin\
dotnet publish demos\modern\NetWard.Modern.AgentDemo\NetWard.Modern.AgentDemo.csproj -c Release -r win-x64 --self-contained false -p:WindowsAppSDKSelfContained=false -p:PublishDir=..\publish\agent\
```

Run `NetWard.Modern.AdminDemo.exe` from `demos\modern\publish\admin\` or `NetWard.Modern.AgentDemo.exe` from `demos\modern\publish\agent\`. Keep the entire published directory together when running or distributing an app; the executable is not a standalone file.

Both apps start with a small loading window while WinUI prepares the full interface. The theme button cycles between following the system, light, and dark modes; each launch starts by following the current Windows theme.

For a self-contained alternative that does not require the shared .NET or Windows App Runtime installations, publish to separate directories:

```powershell
dotnet publish demos\modern\NetWard.Modern.AdminDemo\NetWard.Modern.AdminDemo.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:PublishDir=..\publish\admin-self-contained\
dotnet publish demos\modern\NetWard.Modern.AgentDemo\NetWard.Modern.AgentDemo.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:PublishDir=..\publish\agent-self-contained\
```

This self-contained package is substantially larger and cold-started more slowly on the tested machine.

The publish target copies each generated WinUI `.pri` resource index beside its executable. WinUI requires this file to load the app's XAML at runtime.

## Solution

Open `NetWard.ModernDemo.sln` in Visual Studio, or build both projects from the repository root:

```powershell
dotnet build demos\modern\NetWard.ModernDemo.sln -c Release
```

## Project layout

```text
demos/modern/
├── NetWard.ModernDemo.sln
├── NetWard.Modern.AdminDemo/   # WinUI 3 management-side demo
├── NetWard.Modern.AgentDemo/   # WinUI 3 managed-device demo
└── publish/                    # Local publish output (ignored by Git)
```

The Modern demo is separate from the existing Legacy application and website. It is intended to preview a newer Windows UI direction; it does not yet implement the underlying management, pairing, policy, logging, or filtering services.
