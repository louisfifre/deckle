# **Reference Manual: Architectural Conventions and Standards for Unpackaged, Self-Contained.NET Windows Desktop Applications**

## **Domain 1: C\# and.NET Code Style**

Continuous alignment with first-party C\# conventions ensures that a solo open-source project remains maintainable as the codebase scales.1 Codifying these style choices directly within the compiler pipeline transforms static analysis into an automated code-review mechanism.1

### **PascalCase Capitalization for Shared Namespaces and Public Types**

* **Convention**: PascalCase Capitalization.  
* **Rule**: Every public class, struct, record, interface, enum, property, method, and namespace must utilize PascalCase.3 Abbreviations or acronyms must not be capitalized if they exceed two letters (e.g., use IioController instead of IIOController).3 Type parameter names must be descriptive, prefixed with the uppercase letter T, and indicate any generic constraints if applicable.3  
* **Source**: [C\# Identifier Names \- Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names).3  
* **Applicability to this project**: Adopt. Standardizes type readability across the host application and twenty distinct feature modules.  
* **Caveats / tensions**: Record parameter definitions deviate from standard parameter camelCase rules by compiling directly into public properties, meaning primary constructor arguments must default to PascalCase.3

### **Field Prefixing and Casing**

* **Convention**: Private Instance and Static Field Prefixes.  
* **Rule**: Private instance fields must be declared using camelCase prefixed with a single underscore (\_instanceField).3 Private static fields must use the s\_ prefix (s\_staticField) to clearly signal thread-shared state to developers.3 Constant identifiers, both fields and local variables, must be declared in PascalCase.3  
* **Source**: [C\# Identifier Names \- Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names).3  
* **Applicability to this project**: Adopt. Distinguishing thread-static local variables from instance variables is critical for safe multithreaded hardware interactions.  
* **Caveats / tensions**: The default code generation template in Visual Studio does not enforce the s\_ prefix for static variables out-of-the-box, necessitating custom configuration in .editorconfig.3

### **Asynchronous Method Signatures and Context Preservation**

* **Convention**: Task-Based Asynchronous Pattern (TAP).  
* **Rule**: Asynchronous methods returning Task or ValueTask must append the Async suffix to their identifiers.4 Non-UI libraries (such as background processing or speech-to-text subsystems) must append ConfigureAwait(false) to prevent thread marshaling back to the caller's execution synchronization context.4 Conversely, code-behind classes and ViewModels interacting with WinUI 3 controls must omit ConfigureAwait to safely resume execution on the main thread's UI scheduler.6  
* **Source**: [C\# Coding Conventions \- Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions).4  
* **Applicability to this project**: Adopt. Eliminates UI thread stuttering during high-frequency processing loops while ensuring background tasks run off-thread.4  
* **Caveats / tensions**: Mixing ConfigureAwait(false) in downstream modules with UI execution paths can lead to thread access crashes if background tasks attempt to directly update bound data properties.6

### **Non-Nullable Reference Context Enforcement**

* **Convention**: Nullable Reference Types (NRT).  
* **Rule**: Enable \<Nullable\>enable\</Nullable\> globally across the solution.1 Compile-time annotations must be enforced to treat reference types as non-nullable unless explicitly declared with the nullable question mark (?) or initialized utilizing the null-forgiving operator (\!).1  
* **Source**:([https://smithery.ai/skills/NotMyself/dotnet-centralized-packages](https://smithery.ai/skills/NotMyself/dotnet-centralized-packages)).1  
* **Applicability to this project**: Adopt. Significantly reduces NullReferenceExceptions during development, providing static analysis benefits for a solo maintainer.  
* **Caveats / tensions**: WinUI 3 XAML code-behind properties and bound controls generated during compilation can throw nullability warnings that require defensive null checking or compiler suppression.

### **Structural Formatting and Bracing Styles**

* **Convention**: Allman Bracing and Indentation.  
* **Rule**: Code blocks must follow the Allman bracing style, placing opening and closing curly braces on their own lines, aligned to the current indentation level.4 Indentation must utilize four space characters rather than tab characters.4 Line-scoped namespaces must be used to eliminate unnecessary horizontal code nesting.4  
* **Source**: [C\# Coding Conventions \- Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions).4  
* **Applicability to this project**: Adopt. Standardizes structure across all feature modules and ensures readability in terminal editors.  
* **Caveats / tensions**: Visual Studio code snippets can occasionally default to inline block styles for brief lambda expressions, demanding regular code cleanups.

## **Domain 2: Repository and Solution Structure**

Managing twenty separate modular subsystems behind a single executable entry point requires consistent MSBuild configurations to prevent solution files from falling out of sync.2

| Key Solution File | Evaluation Stage | Principal Architectural Responsibility |
| :---- | :---- | :---- |
| Directory.Build.props | Early compilation pass 11 | Declares global compilation parameters, targets, and warning policies.1 |
| Directory.Build.targets | Late compilation pass 11 | Handles conditional post-build steps, test references, and automation hooks.2 |
| Directory.Packages.props | Prior to NuGet restore 12 | Consolidates and pins dependency version definitions across all projects.1 |
| global.json | Prior to SDK evaluation | Locks the exact version of the.NET SDK used for compilation. |

### **Solution Hierarchy and Module Isolation**

* **Convention**: Modular Directory Layout.  
* **Rule**: Maintain physical separation of folders at the repository root.10 All active application projects must reside under /src, with automated test projects housed within /tests.10 Every feature module must compile to its own class library (.dll), exposing only interface contracts to the host application project to maintain strict decoupling.2  
* **Source**:([https://github.com/microsoft/powertoys](https://github.com/microsoft/powertoys)).10  
* **Applicability to this project**: Adopt. Prevents circular dependencies as the twenty modules scale.  
* **Caveats / tensions**: Restricting module-to-module references requires strict use of dependency injection, which increases initial development overhead for the solo maintainer.

### **Universal Compilation Policies**

* **Convention**: Shared MSBuild Properties.  
* **Rule**: Centralize common MSBuild compilation properties inside a single Directory.Build.props file located at the repository root.2 This file must define the SDK target framework (net10.0-windows10.0.22621), C\# language version (latest), nullable context, and treat compiler warnings as build errors.1  
* **Source**:([https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-by-directory?view=visualstudio](https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-by-directory?view=visualstudio)).11  
* **Applicability to this project**: Adopt. Saves time by removing redundant configuration blocks from twenty-plus project files.  
* **Caveats / tensions**: Properties declared inside Directory.Build.props are evaluated extremely early.11 Project-specific values that depend on target-framework evaluation can behave unexpectedly if referenced inside the early properties file rather than project files or targets.11

### **Decoupled Subsystem Testing Authorization**

* **Convention**: Centralized Internals Visibility.  
* **Rule**: Expose internal helper classes to their respective unit-test assemblies without cluttering production source files with inline attributes.2 This is achieved by declaring target project names and appending InternalsVisibleTo assemblies inside a central Directory.Build.targets or shared properties file.2  
* **Source**:([https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-by-directory?view=visualstudio](https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-by-directory?view=visualstudio)).11  
* **Applicability to this project**: Adopt. Simplifies unit testing across twenty modules by automating permission settings.  
* **Caveats / tensions**: If assembly and test project names are refactored, the central targets file must be updated to prevent build compilation errors.13

### **Central Package Management (CPM)**

* **Convention**: NuGet Central Package Management.  
* **Rule**: Enable \<ManagePackageVersionsCentrally\>true\</ManagePackageVersionsCentrally\> in a root-level Directory.Packages.props file.12 Specify all third-party package versions using \<PackageVersion /\> tags within this central file.1 Downsream project files must declare \<PackageReference /\> references containing only the package ID, omitting the version attribute.1 To prevent transitively introduced version mismatches, \<CentralPackageTransitivePinningEnabled\>true\</CentralPackageTransitivePinningEnabled\> must be enabled.1 Version overrides inside projects must be strictly prohibited by setting \<CentralPackageVersionOverrideEnabled\>false\</CentralPackageVersionOverrideEnabled\>.12  
* **Source**: [NuGet Central Package Management \- Microsoft Learn](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management).12  
* **Applicability to this project**: Adopt. Ensures that all twenty modules utilize identical library versions, eliminating runtime conflicts.1  
* **Caveats / tensions**: Triggers a NU1507 warning if multiple package sources are defined, requiring package source mapping inside nuget.config to resolve.12

## **Domain 3: WinUI 3 and Windows App SDK Implementation**

Deploying an unpackaged, self-contained desktop application changes how the WinUI 3 runtime behaves.14 Standard APIs that assume MSIX packaging must be systematically replaced.15

### **Unpackaged Project Targeting**

* **Convention**: Package-Free Runtime Configuration.  
* **Rule**: Explicitly declare \<WindowsPackageType\>None\</WindowsPackageType\> in the host executable project file to disable MSIX packaging.14 This configures the application to run without a package identity.14  
* **Source**:([https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app)).14  
* **Applicability to this project**: Adopt. Required since the application is distributed directly via ZIP or small installer.14  
* **Caveats / tensions**: The absence of package identity prevents the direct use of standard WinRT background tasks, push notifications, and automatic Windows Store updates, requiring traditional Win32 fallback alternatives.8

### **Dynamic Bootstrapper Initialization**

* **Convention**: Windows App SDK Bootstrapping.  
* **Rule**: Unpackaged applications must locate and initialize the Windows App SDK runtime dynamically at startup.8 The host application must call WindowsAppRuntime.Bootstrap.Initialize() prior to invoking any WinUI 3 XAML layout code.8  
* **Source**:([https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps)).18  
* **Applicability to this project**: Adopt. Crucial for loading the runtime dlls in an unpackaged environment.18  
* **Caveats / tensions**: Errors during bootstrap initialization will prevent the application from launching, making robust startup exception handling and logging mandatory.8

### **Unpackaged Self-Contained Deployment**

* **Convention**: Embedded App SDK Assembly Bundling.  
* **Rule**: To remove external dependencies, set \<WindowsAppSDKSelfContained\>true\</WindowsAppSDKSelfContained\> in the host project.14 This copies the native binaries of the Windows App SDK directly into the build output folder.16 Since WindowsPackageType is None, the build pipeline automatically enables WindowsAppSdkUndockedRegFreeWinRTInitialize to support activation-free WinRT type consumption at runtime.16  
* **Source**:([https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)).16  
* **Applicability to this project**: Adopt. Eliminates the need for users to run a separate Windows App SDK installer, which is a major usability benefit.14  
* **Caveats / tensions**: Increases the installation directory footprint by approximately 50 MB and prevents the OS from sharing memory pages with other apps using the Windows App SDK.17

### **Compiled XAML Data Binding (x:Bind)**

* **Convention**: High-Performance Compile-Time Binding.  
* **Rule**: Data bindings inside XAML pages must use {x:Bind} instead of legacy {Binding}.6 {x:Bind} is strongly typed and evaluated at compile time, generating standard C\# code that accesses properties directly, which minimizes resource usage.7  
* **Source**:([https://platform.uno/docs/articles/wpf-winui-equivalents.html](https://platform.uno/docs/articles/wpf-winui-equivalents.html)).6  
* **Applicability to this project**: Adopt. Essential for meeting the high performance bar required for local processing.  
* **Caveats / tensions**: {x:Bind} defaults to a OneTime binding mode, meaning developers must explicitly specify Mode=OneWay for properties that need to update dynamically at runtime.6

### **XAML Dialog Threading Integration**

* **Convention**: XamlRoot Window Association.  
* **Rule**: Every ContentDialog displayed in the application must have its XamlRoot property explicitly assigned to the active page root before calling ShowAsync().9  
* **Source**:([https://mcpservers.org/agent-skills/github/winui3-migration-guide](https://mcpservers.org/agent-skills/github/winui3-migration-guide)).9  
* **Applicability to this project**: Adopt. Mandatory to prevent the app from throwing an InvalidOperationException and crashing on launch.9  
* **Caveats / tensions**: Reusable modular services that display dialogs must be designed to accept a XamlRoot context from the active view.

## **Domain 4: Windows UX and Fluent Design Principles**

To match first-party Windows 11 experiences (like Settings, File Explorer, or PowerToys), the user interface must use standard Fluent design materials and layout structures.20

### **Signature Materials: Mica and Background Acrylic**

* **Convention**: Windows 11 Fluent Backdrops.  
* **Rule**: Apply opaque **Mica** as the primary background material for persistent windows.21 Mica tints with the desktop wallpaper and naturally supports active/inactive states.22 Use semi-transparent **Acrylic** exclusively for short-lived, light-dismiss surfaces such as flyouts, dropdowns, and context menus.22  
* **Source**:([https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/materials](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/materials)).22  
* **Applicability to this project**: Adopt. Critical for matching first-party OS consistency (e.g., File Explorer, PowerToys).20  
* **Caveats / tensions**: Hiding window frames can cause visual issues when dragging windows across multi-monitor setups, though older multi-screen backdrop rendering crashes are resolved.22

### **Custom Title Bar Integration and Interaction**

* **Convention**: Full-Width Window Canvas Extension.  
* **Rule**: Extend client content into the title bar space by setting AppWindow.TitleBar.ExtendsContentIntoTitleBar \= true.22 Register draggable window regions by calling Window.SetTitleBar on a parent layout container.23 All custom click-through regions or manual drag rectangles must be calculated in physical pixels rather than logical points, and recalculated dynamically whenever the window size or display scale changes.23  
* **Source**:([https://aka.platform.uno/windowing](https://aka.platform.uno/windowing)).23  
* **Applicability to this project**: Adopt. Maximizes vertical space for app content while supporting Windows snap assist behaviors.20  
* **Caveats / tensions**: Interactive controls (such as buttons or combo boxes) placed in custom title bars must be positioned with a higher z-order than the drag region, or they will be unclickable.24

### **Interactive Notification Management**

* **Convention**: Taskbar Tray Fallback.  
* **Rule**: Because unpackaged applications lack package identity, they cannot reliably trigger standard Windows toast notifications with custom action buttons without complex sparse packaging.8 To ensure a consistent offline experience, the application must run as a background tray utility utilizing standard Win32 system tray icons for status and notifications.8  
* **Source**:([https://learn.microsoft.com/en-us/windows/configuration/taskbar/](https://learn.microsoft.com/en-us/windows/configuration/taskbar/)).26  
* **Applicability to this project**: Adopt. Essential for a utility with offline background processes.  
* **Caveats / tensions**: Standard Win32 tray notifications look dated compared to Windows 11 style notifications, requiring a custom design or tray wrapper.

## **Domain 5: Versioning and Releases**

To build trust and maintain transparency in an open-source project, releases must follow predictable versioning rules.1

### **Semantic Versioning (SemVer 2.0.0)**

* **Convention**: Semantic Versioning.  
* **Rule**: Release versions must follow the MAJOR.MINOR.PATCH format. Use 0.y.z semantics to signal rapid development iterations where minor version changes may introduce breaking API updates.1  
* **Source**:([https://semver.org](https://semver.org))  
* **Applicability to this project**: Adopt. Already in place; enforces release discipline for local modules.  
* **Caveats / tensions**: Requires updating version definitions across twenty separate assembly files, making centralized version management in Directory.Build.props highly recommended.2

### **Changelog Curation**

* **Convention**: Keep a Changelog.  
* **Rule**: Document changes in a root-level CHANGELOG.md file following the "Keep a Changelog" standard. Group modifications under clear headings: Added, Changed, Deprecated, Removed, Fixed, and Security.1  
* **Source**: [Keep a Changelog Guide](https://keepachangelog.com)  
* **Applicability to this project**: Adopt. Already in place; keeps the community informed of updates and changes.  
* **Caveats / tensions**: Writing changelogs manually adds some overhead, but this can be automated using Conventional Commits.1

### **Release Asset Packaging**

* **Convention**: Standardized Build Artifact Names.  
* **Rule**: Release assets must follow a predictable naming format: \<AppName\>-\<Version\>-\<Architecture\>-Unpackaged-SelfContained.zip (e.g., HostApp-1.2.0-x64-Unpackaged-SelfContained.zip).  
* **Source**:([https://github.com/microsoft/powertoys](https://github.com/microsoft/powertoys)).10  
* **Applicability to this project**: Adopt. Makes it easy for users to download the correct build for their machine.  
* **Caveats / tensions**: None.

## **Domain 6: Git and Collaboration**

A structured Git workflow helps keep repository history clean and manageable, even for a solo developer.2

### **Git Commit Categorization**

* **Convention**: Conventional Commits.  
* **Rule**: Commit messages must be prefixed with a structural category: feat: for new features, fix: for bug fixes, chore: for maintenance, and refactor: for code improvements.1  
* **Source**:([https://www.conventionalcommits.org](https://www.conventionalcommits.org))  
* **Applicability to this project**: Adopt. Already in place; allows for automated changelog generation.  
* **Caveats / tensions**: None.

### **Local Branch Multi-Tasking Management**

* **Convention**: Git Worktree Architecture.  
* **Rule**: Avoid switching branches in a large, multi-module workspace. Use git worktree to check out multiple branches into separate physical directories on your machine.10  
* **Source**:([https://git-scm.com/docs/git-worktree](https://git-scm.com/docs/git-worktree))  
* **Applicability to this project**: Adopt. Already in place; saves significant build-cache-invalidation time when jumping between different modules.  
* **Caveats / tensions**: Each active worktree maintains its own physical directory on disk, increasing local storage usage.

### **Open Source Repository Access Control**

* **Convention**: Sourcing Contribution Workflows.  
* **Rule**: Maintain primary branches using strict access rules. While standard for teams, a solo maintainer should skip complex CODEOWNERS setups and pull request validation rules to minimize development friction.  
* **Source**:([https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features))  
* **Applicability to this project**: Skip. CODEOWNERS and PR branch protection rules are overkill for a solo developer.  
* **Caveats / tensions**: Keeping branch protection active on the main branch can slow down quick hotfixes.

## **Domain 7: Windows Packaging and Deployment (Unpackaged Focus)**

Because the application is deployed unpackaged and self-contained, it operates as a traditional Win32 application and cannot rely on automated MSIX deployment services.14

| Deployment Parameter | Standard MSIX App | Unpackaged Local App (Current Choice) |
| :---- | :---- | :---- |
| **Default Folder Path** | %PROGRAMFILES%\\WindowsApps 27 | %LOCALAPPDATA%\\Programs\\\<AppName\> 28 |
| **Registry Access** | Virtualized and isolated | Global user-level registry access |
| **Shortcut Creation** | Automatic via package manifest 14 | Programmatically written .lnk file 28 |
| **Control Panel Presence** | Managed by the OS | Programmatically registered uninstall keys 29 |

### **Unpackaged Application Installation Paths**

* **Convention**: Per-User Non-Elevated Target Directories.  
* **Rule**: Default the installation path for unpackaged applications to %LOCALAPPDATA%\\Programs\\\<AppName\>.28 This folder allows the installer to write binaries and assets without requiring administrative UAC elevation.28  
* **Source**:([https://github.com/Bill-Stewart/SyncthingWindowsSetup/](https://github.com/Bill-Stewart/SyncthingWindowsSetup/)).28  
* **Applicability to this project**: Adopt. Bypasses administrative UAC elevation dialogs, providing a smooth, lightweight installation experience for direct-download distributions.  
* **Caveats / tensions**: Installing to a user-writable folder like AppData means other processes running under the same user context can modify the binaries, which can be a security concern.

### **Unpackaged Registry Cleanup**

* **Convention**: Add/Remove Programs Registry Integration.  
* **Rule**: Write uninstaller registry keys to HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\\<AppGuid\>.29 Write required string properties (DisplayName, DisplayVersion, Publisher, UninstallString, and InstallLocation) to ensure the application displays and can be removed via Settings.30  
* **Source**:([https://learn.microsoft.com/en-us/windows/win32/msi/uninstall-registry-key](https://learn.microsoft.com/en-us/windows/win32/msi/uninstall-registry-key)).30  
* **Applicability to this project**: Adopt. Essential for a clean uninstallation process.29  
* **Caveats / tensions**: Clean uninstallation is entirely the installer's responsibility since the OS does not auto-clean directories for unpackaged installations.8

### **Program Shortcut Integration**

* **Convention**: Standard Start Menu Shell Shortcuts.  
* **Rule**: Write a standard Windows shortcut file (.lnk) directly to the user's Start Menu programs directory under %APPDATA%\\Microsoft\\Windows\\Start Menu\\Programs\\\<AppName\>.lnk.28  
* **Source**:([https://github.com/Bill-Stewart/SyncthingWindowsSetup/](https://github.com/Bill-Stewart/SyncthingWindowsSetup/)).28  
* **Applicability to this project**: Adopt. Standard convention for making local applications discoverable by Windows Search.14  
* **Caveats / tensions**: Shortcuts must be manually removed by the uninstaller to avoid dead link remnants.

### **Direct-Download Update Mechanisms**

* **Convention**: Automatic Update Pipelines.  
* **Rule**: Because the app runs unpackaged, developers must implement a custom update check loop.14 Periodically fetch the latest release tag from the GitHub Releases API, compare it to the local assembly version, and prompt the user to download the update package if a newer version is available.  
* **Source**:([https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app)).14  
* **Applicability to this project**: Adopt. Critical for keeping offline users up to date with the latest performance and security fixes.  
* **Caveats / tensions**: Downloading and running executables from the web triggers SmartScreen warnings, meaning updates must be handled carefully to maintain user trust.15

## **Domain 8: OSS Project Hygiene and Documentation**

Proper open-source documentation is essential for attracting contributors and building trust with users.1

### **Architecture Decision Logging**

* **Convention**: Architecture Decision Records (ADRs).  
* **Rule**: Document important design decisions, library choices, and structural shifts in markdown files inside the /docs/adr/ folder. Name files sequentially: NNNN-title.md (e.g., 0003-use-local-sqlite.md).  
* **Source**:([https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions))  
* **Applicability to this project**: Adopt. Already in place; highly useful for keeping track of why certain technical decisions were made as the project scales.  
* **Caveats / tensions**: Writing ADRs adds some administrative overhead, but is very helpful for maintaining a long-term architectural record.

### **Security Disclosure Process**

* **Convention**: Security Vulnerability Reporting.  
* **Rule**: Add a SECURITY.md file to the repository root that defines a clear, private channel for reporting security issues, rather than opening public GitHub issues.  
* **Source**:([https://docs.github.com/en/code-security/getting-started/adding-a-security-policy-to-your-repository](https://docs.github.com/en/code-security/getting-started/adding-a-security-policy-to-your-repository))  
* **Applicability to this project**: Adopt. Necessary for building trust with open-source users concerned about local hardware telemetry and privacy.  
* **Caveats / tensions**: None.

## **Domain 9: Observability and Diagnostic Logging**

Logging is essential for diagnostics, but must be designed to have minimal impact on application performance.5

### **EventSource Logging Architecture**

* **Convention**: Event Tracing for Windows (ETW) Schema Design.  
* **Rule**: Inherit all custom trace providers from System.Diagnostics.Tracing.EventSource.5 Define a unique dot-separated name using EventSourceAttribute following the format Publisher.AppName.Subsystem.5  
* **Source**:([https://learn.microsoft.com/en-us/dotnet/core/diagnostics/eventsource-instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/eventsource-instrumentation)).5  
* **Applicability to this project**: Adopt. Already in place; provides high-performance logging with virtually zero performance overhead when disabled.5  
* **Caveats / tensions**: GUIDs are implicitly derived from the name; do not define them explicitly unless backward compatibility is required.5

### **High-Performance Structured Telemetry**

* **Convention**: Binary Trace Generation.  
* **Rule**: Avoid string formatting in logging methods.5 Log methods must pass primitive arguments directly to WriteEvent to support fast binary serialization by ETW.5 To minimize performance impact during high-frequency cycles, check IsEnabled() before evaluating complex log arguments.5  
* **Source**:([https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.tracing.eventsource.writeevent](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.tracing.eventsource.writeevent)).35  
* **Applicability to this project**: Adopt. Critical for logging high-frequency processing loops without impacting UI thread responsiveness.  
* **Caveats / tensions**: Wrapper method parameters must match the arguments passed to WriteEvent exactly to prevent runtime exceptions.5

C\#  
public sealed class ProcessingEventSource : EventSource  
{  
    public static readonly ProcessingEventSource Log \= new();

    // The throwOnEventWriteErrors constructor parameter can be used to raise  
    // exceptions if writing to ETW fails \[32, 36\]  
    private ProcessingEventSource() : base(throwOnEventWriteErrors: false) { }

     
    public void SpeechModelLoaded(string modelName, double latencyMs)  
    {  
        // Check IsEnabled first to avoid unnecessary allocations   
        if (IsEnabled(EventLevel.Informational, Keywords.SpeechProcessing))  
        {  
            WriteEvent(1, latencyMs, modelName);  
        }  
    }

    public static class Keywords  
    {  
        public const EventKeywords SpeechProcessing \= (EventKeywords)0x0001;  
    }  
}

## **Domain 10: Testing**

Consistent test structures make it easy to verify functionality as feature modules are added or updated.13

### **Test Project Suffixes and Directory Structure**

* **Convention**: Match Production Project Hierarchy.  
* **Rule**: Test projects must reside in the /tests folder and mirror the structure of the /src folder, appending a .Tests suffix to their names (e.g., ModuleA.Tests.csproj).13  
* **Source**:([https://github.com/microsoft/powertoys](https://github.com/microsoft/powertoys)).13  
* **Applicability to this project**: Adopt. Keeps the test suite clean and organized as the twenty feature modules grow.  
* **Caveats / tensions**: Testing visual WinUI 3 controls requires a custom running thread context.

### **Dynamic Dependency Isolation**

* **Convention**: Decoupled Unit Testing.  
* **Rule**: Avoid testing modules using live hardware or local AI engines. Use interfaces to isolate dependencies and mock external services, allowing tests to run quickly and reliably without external dependencies.  
* **Source**:([https://learn.microsoft.com/en-us/dotnet/core/testing/](https://learn.microsoft.com/en-us/dotnet/core/testing/))  
* **Applicability to this project**: Adopt. Essential for verifying local logic without requiring actual hardware attachments.  
* **Caveats / tensions**: Mocking complex subsystems can occasionally fail to catch real-world integration issues.

## **Domain 11: Security and Supply Chain**

For open-source projects distributed over the web, supply chain security is critical for maintaining user trust.15

### **Reproducible Build Chains**

* **Convention**: Deterministic Compilation.  
* **Rule**: Enforce \<ContinuousIntegrationBuild\>true\</ContinuousIntegrationBuild\> in shared MSBuild properties during production CI release workflows.37 This ensures that compile-time constants (such as absolute paths) are removed, resulting in binaries that match the source code exactly.37  
* **Source**:([https://sbomify.com/guides/dotnet/](https://sbomify.com/guides/dotnet/)).37  
* **Applicability to this project**: Adopt. Builds deep trust with open-source users by confirming that published binaries correspond exactly to repository commits.  
* **Caveats / tensions**: Must only be run on formal CI runners, as it requires absolute, locked git state verification.

### **Software Bill of Materials (SBOM) Generation**

* **Convention**: SPDX Dependency Inventory.  
* **Rule**: Automatically generate an SPDX 2.2 compliant SBOM for the unpackaged release directory using Microsoft's open-source sbom-tool.38  
* **Source**:([https://github.blog/enterprise-software/governance-and-compliance/introducing-self-service-sboms/](https://github.blog/enterprise-software/governance-and-compliance/introducing-self-service-sboms/)).40  
* **Applicability to this project**: Adapt. Highly recommended for open-source supply chain verification, but can be configured as a low-touch automated step in GitHub Actions.  
* **Caveats / tensions**: File-level scanning can produce noise on massive Windows platform builds, requiring exclusion filters.38

## **Domain 12: Accessibility and Internationalization**

To meet the quality bar of first-party Microsoft applications, desktop software must support modern Windows accessibility integrations and globalization workflows.41

### **Accessible Visual Interactivity**

* **Convention**: Keyboard Navigation Focus Rings.  
* **Rule**: Ensure every custom control supports tab navigation (IsTabStop \= true) and displays a clear visual focus ring on keyboard interaction.41  
* **Source**:([https://learn.microsoft.com/en-us/uwp/api/windows.ui.xaml.automation.automationproperties](https://learn.microsoft.com/en-us/uwp/api/windows.ui.xaml.automation.automationproperties)).41  
* **Applicability to this project**: Adopt. Required to pass the first-party Microsoft desktop quality bar.41  
* **Caveats / tensions**: Custom layouts require explicit tab indexing to prevent erratic pointer jumps.

### **XAML Component Labeling**

* **Convention**: Screen Reader Automation Labeling.  
* **Rule**: Every interactive control must expose an accessible name.41 If a control does not display text directly (e.g., an icon-only play button), developers must assign an explicit name using the AutomationProperties.Name attached property.41 Hardcoded strings in XAML must be avoided by resolving localized values dynamically from resource files using the x:Uid localization identifier.41  
* **Source**: [AutomationProperties attached properties \- Microsoft Learn](https://learn.microsoft.com/en-us/uwp/api/windows.ui.xaml.automation.automationproperties).41  
* **Applicability to this project**: Adopt. Required for screen reader compatibility, ensuring first-party app quality.41  
* **Caveats / tensions**: None.

## **Domain 13: Configuration and User-Settings Persistence**

Local-first desktop applications must persist user state and system parameters reliably without relying on cloud-synchronized storage containers.15

| Persistence Target Path | Sync Configuration | Recommended Data Payload |
| :---- | :---- | :---- |
| %APPDATA% (..\\AppData\\Roaming) | Roams with domain profile 27 | Small, text-based configurations and UI preferences.43 |
| %LOCALAPPDATA% (..\\AppData\\Local) | Locked to physical device 27 | Large databases, processing caches, and machine-specific models.27 |

### **Local Configuration Isolation**

* **Convention**: Roaming vs Local Directory Partitioning.  
* **Rule**: Store structural assets and AI model files in local application directories (%LOCALAPPDATA%), while reserving user configuration files for the roaming directory (%APPDATA%).42  
* **Source**:([https://www.reddit.com/r/Windows10/comments/1dnilhs/is\_it\_too\_much\_to\_ask\_developers\_to\_use\_appdata/](https://www.reddit.com/r/Windows10/comments/1dnilhs/is_it_too_much_to_ask_developers_to_use_appdata/)).44  
* **Applicability to this project**: Adopt. Standard practice for offline-by-default applications that utilize heavy local speech models.  
* **Caveats / tensions**: Large models placed in roaming profiles will slow down corporate network logins.27

### **File System Mutation Monitoring**

* **Convention**: Reactive Local File Observers.  
* **Rule**: Monitor active settings files and locally processed data directories using the StorageLibraryChangeTracker API to handle changes made by outside utilities.45  
* **Source**:([https://learn.microsoft.com/en-us/windows/apps/develop/files/change-tracking-filesystem](https://learn.microsoft.com/en-us/windows/apps/develop/files/change-tracking-filesystem)).45  
* **Applicability to this project**: Adopt. Essential for keeping UI indicators in sync with local speech models or hardware state changes.  
* **Caveats / tensions**: Must declare appropriate library capabilities inside the file manifest to allow directory change monitoring.45

## **Prioritized Adoption Summary**

This prioritized roadmap is tailored specifically for a solo developer managing a modular, unpackaged WinUI 3 application. It separates high-impact core requirements from complex, enterprise-level overhead.

                     ┌──────────────────────────────┐  
                     │          MUST-HAVE           │  
                     │  \- Self-Contained Deployment │  
                     │  \- Non-Admin AppData Settings│  
                     │  \- x:Bind & XamlRoot Fixes   │  
                     │  \- EventSource Logging       │  
                     └──────────────┬───────────────┘  
                                    │  
                                    ▼  
                     ┌──────────────────────────────┐  
                     │         RECOMMENDED          │  
                     │  \- Central Package Mgt (CPM) │  
                     │  \- Directory.Build.props     │  
                     │  \- Custom Titlebar Integration│  
                     │  \- Resw Localization         │  
                     └──────────────┬───────────────┘  
                                    │  
                                    ▼  
                     ┌──────────────────────────────┐  
                     │      OPTIONAL / OVERKILL     │  
                     │  \- Single-File Publication   │  
                     │  \- Automated UI Test Suites  │  
                     │  \- Formal SBOM Generation    │  
                     └──────────────────────────────┘

### **Must-Have**

These foundational patterns must be implemented immediately. Skipping these items will result in compilation failures, runtime crashes, or installation hurdles for unpackaged, unsigned deployments:

* **Self-Contained Deployment**: Set \<WindowsAppSDKSelfContained\>true\</WindowsAppSDKSelfContained\> in the host application's project file.14 This ensures the application runs on the user's machine without requiring a separate Windows App SDK installer.16  
* **Non-Admin AppData Settings**: Persist settings as local JSON files inside %APPDATA%\\AppName or %LOCALAPPDATA%\\AppName.19 Avoid calling ApplicationData.Current.LocalSettings (which throws package identity errors) and do not attempt to install binaries directly to %PROGRAMFILES% (which requires elevated admin rights for writing).15  
* **Compiled Bindings and XamlRoot Setup**: Enforce compiled {x:Bind} bindings for responsiveness and ensure XamlRoot properties are populated on ContentDialog instances before calling ShowAsync() to prevent threading crashes.6  
* **EventSource Diagnostic Logging**: Implement diagnostic logs by inheriting from EventSource with explicit event IDs.5 This provides high-performance tracing for background hardware and speech-to-text processing.5  
* **Standardized Solution Layout**: Enforce clear directory separation using a /src, /tests, /docs, and /scripts structure to maintain clean boundaries between the entry-point application and the individual feature modules.10

### **Recommended**

These items represent industry best practices that reduce code maintenance effort and help achieve a first-party visual style. They are highly recommended as the application scales:

* **Central Package Management (CPM)**: Manage NuGet dependency versions centrally inside a single Directory.Packages.props file to prevent version mismatches across the solution's modules.1  
* **Directory.Build.props**: Consolidate shared MSBuild build properties into a single file in the repository root to keep project files lightweight.1  
* **Custom Title Bar Integration**: Extend the client canvas into the title bar using ExtendsContentIntoTitleBar \= true and configure physical-pixel drag areas to match the aesthetic of Windows 11 applications.22  
* **Resw Localization**: Store user-facing strings in .resw files grouped under BCP-47 language directories.46 This keeps localized text isolated from execution logic, keeping the codebase organized and making it easy for open-source contributors to submit new language translations.46  
* **Git conventional commits**: Enforce standardized commit prefixes (feat:, fix:, chore:) to automate changelog generation during releases.

### **Optional / Likely Overkill**

These enterprise-level compliance patterns introduce significant development overhead and are less critical for a solo-developer project:

* **Single-File Publication**: True single-file execution is not supported by the native runtime dependencies of the Windows App SDK.16 Wrapping compilation outputs inside a standard desktop installer is a simpler, more effective approach.14  
* **Automated UI Test Suites**: Implementing automated UI test suites using complex testing frameworks is fragile and difficult to maintain for a solo developer. Focus instead on testing core logical engines via standard xUnit unit tests.  
* **Formal SBOM Generation**: Although automated SBOM scanning is easy to configure, it is not a high-priority requirement unless the application enters strictly regulated enterprise distribution pipelines.48

#### **Sources des citations**

1. dotnet-centralized-packages \- Skill \- Smithery, consulté le juin 4, 2026, [https://smithery.ai/skills/NotMyself/dotnet-centralized-packages](https://smithery.ai/skills/NotMyself/dotnet-centralized-packages)  
2. Understand Directory.Build.props: Centralizing .NET Project Configurations : r/dotnet \- Reddit, consulté le juin 4, 2026, [https://www.reddit.com/r/dotnet/comments/1bpmtqq/understand\_directorybuildprops\_centralizing\_net/](https://www.reddit.com/r/dotnet/comments/1bpmtqq/understand_directorybuildprops_centralizing_net/)  
3. C\# identifier naming rules and conventions \- Microsoft Learn, consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names)  
4. .NET Coding Conventions \- C\# | Microsoft Learn, consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)  
5. Instrument Code to Create EventSource Events \- .NET \- Microsoft Learn, consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/dotnet/core/diagnostics/eventsource-instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/eventsource-instrumentation)  
6. WPF to WinUI XAML Equivalents Reference \- Uno Platform, consulté le juin 4, 2026, [https://platform.uno/docs/articles/wpf-winui-equivalents.html](https://platform.uno/docs/articles/wpf-winui-equivalents.html)  
7. Migrate WPF app patterns to WinUI 3 \- Windows \- Microsoft Learn, consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/migrate-to-windows-app-sdk/wpf-patterns-winui3](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/migrate-to-windows-app-sdk/wpf-patterns-winui3)  
8. WinUI vs WPF in 2026: A Practical Comparison for .NET Desktop Developers | CTCO, consulté le juin 4, 2026, [https://www.ctco.blog/posts/winui-vs-wpf-2026-practical-comparison/](https://www.ctco.blog/posts/winui-vs-wpf-2026-practical-comparison/)  
9. winui3-migration-guide | Agent Skills Library \- Awesome MCP Servers, consulté le juin 4, 2026, [https://mcpservers.org/agent-skills/github/winui3-migration-guide](https://mcpservers.org/agent-skills/github/winui3-migration-guide)  
10. Microsoft PowerToys is a collection of utilities that supercharge productivity and customization on Windows \- GitHub, consulté le juin 4, 2026, [https://github.com/microsoft/powertoys](https://github.com/microsoft/powertoys)  
11. Customize the build by folder \- MSBuild \- Microsoft Learn, consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-by-directory?view=visualstudio](https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-by-directory?view=visualstudio)  
12. Central Package Management | Microsoft Learn, consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management)  
13. Directory.Build.props \- microsoft/PowerToys \- GitHub, consulté le juin 4, 2026, [https://github.com/microsoft/PowerToys/blob/main/Directory.Build.props](https://github.com/microsoft/PowerToys/blob/main/Directory.Build.props)  
14. Distribute an unpackaged WinUI 3 app \- Windows apps | Microsoft ..., consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app)  
15. Packaging overview \- Windows apps | Microsoft Learn, consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/packaging/](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/packaging/)  
16. Windows App SDK deployment guide for self-contained apps ..., consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)  
17. Windows App SDK deployment overview \- Microsoft Learn, consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/deploy-overview)  
18. Windows App SDK deployment guide for framework-dependent apps packaged with external location or unpackaged \- Microsoft Learn, consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps)  
19. winui3-migration-guide — AI agent skill | explainx.ai, consulté le juin 4, 2026, [https://explainx.ai/skills/github/awesome-copilot/winui3-migration-guide](https://explainx.ai/skills/github/awesome-copilot/winui3-migration-guide)  
20. WinUI Gallery 2.8: Jump Lists, Title Bar, and Clipboard Samples for Windows 11, consulté le juin 4, 2026, [https://windowsforum.com/threads/winui-gallery-2-8-jump-lists-title-bar-and-clipboard-samples-for-windows-11.404193/](https://windowsforum.com/threads/winui-gallery-2-8-jump-lists-title-bar-and-clipboard-samples-for-windows-11.404193/)  
21. Windows 11 \- Grokipedia, consulté le juin 4, 2026, [https://grokipedia.com/page/Windows\_11](https://grokipedia.com/page/Windows_11)  
22. Windows App SDK 1.1 release notes \- Windows apps | Microsoft Learn, consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-1](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-1)  
23. Windowing \- Uno Platform, consulté le juin 4, 2026, [https://aka.platform.uno/windowing](https://aka.platform.uno/windowing)  
24. Title bar customization \- UWP applications \- Microsoft Learn, consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/windows/uwp/ui-input/title-bar](https://learn.microsoft.com/en-us/windows/uwp/ui-input/title-bar)  
25. WinUI 3 controls in a customized title bar are not clickable \- Microsoft Learn, consulté le juin 4, 2026, [https://learn.microsoft.com/en-my/answers/questions/1104852/winui-3-controls-in-a-customized-title-bar-are-not](https://learn.microsoft.com/en-my/answers/questions/1104852/winui-3-controls-in-a-customized-title-bar-are-not)  
26. Configure the Windows Taskbar Using Policy Settings | Microsoft Learn, consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/windows/configuration/taskbar/](https://learn.microsoft.com/en-us/windows/configuration/taskbar/)  
27. AppData | LocalAppData | ProgramData Explanations, Differences, Use Cases, consulté le juin 4, 2026, [https://www.advancedinstaller.com/appdata-localappdata-programdata.html](https://www.advancedinstaller.com/appdata-localappdata-programdata.html)  
28. Bill-Stewart/SyncthingWindowsSetup: Syncthing Windows Setup \- GitHub, consulté le juin 4, 2026, [https://github.com/Bill-Stewart/SyncthingWindowsSetup/](https://github.com/Bill-Stewart/SyncthingWindowsSetup/)  
29. windows \- How is the list of 'Add/Remove Programs' built? \- Stack Overflow, consulté le juin 4, 2026, [https://stackoverflow.com/questions/49073126/how-is-the-list-of-add-remove-programs-built/49078220](https://stackoverflow.com/questions/49073126/how-is-the-list-of-add-remove-programs-built/49078220)  
30. Windows Installer Properties for the Uninstall Registry Key \- Win32 ..., consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/windows/win32/msi/uninstall-registry-key](https://learn.microsoft.com/en-us/windows/win32/msi/uninstall-registry-key)  
31. Getting Started with EventSource \- .NET \- Microsoft Learn, consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/dotnet/core/diagnostics/eventsource-getting-started](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/eventsource-getting-started)  
32. EventSource Constructor (System.Diagnostics.Tracing) | Microsoft Learn, consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.tracing.eventsource.-ctor?view=net-10.0](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.tracing.eventsource.-ctor?view=net-10.0)  
33. EventSourceAttribute Class (System.Diagnostics.Tracing) \- Microsoft Learn, consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.tracing.eventsourceattribute?view=net-10.0](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.tracing.eventsourceattribute?view=net-10.0)  
34. EventSourceAttribute.Guid Property (System.Diagnostics.Tracing) \- Microsoft Learn, consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.tracing.eventsourceattribute.guid?view=net-10.0](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.tracing.eventsourceattribute.guid?view=net-10.0)  
35. EventSource.WriteEvent Method (System.Diagnostics.Tracing) | Microsoft Learn, consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.tracing.eventsource.writeevent?view=net-10.0](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.tracing.eventsource.writeevent?view=net-10.0)  
36. SBOM Generation Guide for .NET \- NuGet \- Sbomify, consulté le juin 4, 2026, [https://sbomify.com/guides/dotnet/](https://sbomify.com/guides/dotnet/)  
37. How to create an SBOM for a Windows 11 image : r/devsecops \- Reddit, consulté le juin 4, 2026, [https://www.reddit.com/r/devsecops/comments/1to21y7/how\_to\_create\_an\_sbom\_for\_a\_windows\_11\_image/](https://www.reddit.com/r/devsecops/comments/1to21y7/how_to_create_an_sbom_for_a_windows_11_image/)  
38. sbom-tool-api-reference.md \- GitHub, consulté le juin 4, 2026, [https://github.com/microsoft/sbom-tool/blob/main/docs/sbom-tool-api-reference.md](https://github.com/microsoft/sbom-tool/blob/main/docs/sbom-tool-api-reference.md)  
39. Introducing self-service SBOMs \- The GitHub Blog, consulté le juin 4, 2026, [https://github.blog/enterprise-software/governance-and-compliance/introducing-self-service-sboms/](https://github.blog/enterprise-software/governance-and-compliance/introducing-self-service-sboms/)  
40. AutomationProperties Class (Windows.UI.Xaml.Automation) \- Microsoft Learn, consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/uwp/api/windows.ui.xaml.automation.automationproperties?view=winrt-28000](https://learn.microsoft.com/en-us/uwp/api/windows.ui.xaml.automation.automationproperties?view=winrt-28000)  
41. What is %appdata% folder? : r/windows \- Reddit, consulté le juin 4, 2026, [https://www.reddit.com/r/windows/comments/a6c2je/what\_is\_appdata\_folder/](https://www.reddit.com/r/windows/comments/a6c2je/what_is_appdata_folder/)  
42. What is AppData, and what are Local, LocalLow, and Roaming? \- XDA Developers, consulté le juin 4, 2026, [https://www.xda-developers.com/appdata/](https://www.xda-developers.com/appdata/)  
43. Is it too much to ask developers to use appdata on Windows properly? even for Microsoft themself : r/Windows10 \- Reddit, consulté le juin 4, 2026, [https://www.reddit.com/r/Windows10/comments/1dnilhs/is\_it\_too\_much\_to\_ask\_developers\_to\_use\_appdata/](https://www.reddit.com/r/Windows10/comments/1dnilhs/is_it_too_much_to_ask_developers_to_use_appdata/)  
44. Track file system changes in the background \- Windows apps | Microsoft Learn, consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/windows/apps/develop/files/change-tracking-filesystem](https://learn.microsoft.com/en-us/windows/apps/develop/files/change-tracking-filesystem)  
45. The WinUI3Localizer is a NuGet package that helps you localize your WinUI 3 app. \- GitHub, consulté le juin 4, 2026, [https://github.com/AndrewKeepCoding/WinUI3Localizer](https://github.com/AndrewKeepCoding/WinUI3Localizer)  
46. Localize strings in your UI and app package manifest \- Windows apps | Microsoft Learn, consulté le juin 4, 2026, [https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/mrtcore/localize-strings](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/mrtcore/localize-strings)  
47. Guide to SBOM Tools: 5 Picks for Enterprise Security Teams | Wiz, consulté le juin 4, 2026, [https://www.wiz.io/academy/application-security/top-open-source-sbom-tools](https://www.wiz.io/academy/application-security/top-open-source-sbom-tools)