# VSBoth Extension Guide

VS Code embedded in Visual Studio 2022 via Win32 window reparenting. Single project: `VSBothExtension`.

## Critical Patterns

**Static process management** - `VSCodeToolWindowControl.xaml.cs` uses static fields to persist VS Code across tool window close/open:
```csharp
private static IntPtr s_vsCodeHwnd;
private static Process? s_vsCodeProcess;
```
Tool window control instances are disposable, but VS Code process lives until VS shutdown.

**Kill by HWND PID, not launcher** - VS Code spawns child processes. Must terminate by window handle:
```csharp
GetWindowThreadProcessId(s_vsCodeHwnd, out uint pid);
Process.GetProcessById((int)pid).Kill();
```

**Win32 embedding**: Launch `code.exe --new-window` → Find window via `EnumWindows` → `SetParent()` → Remove `WS_CAPTION`/`WS_THICKFRAME`, add `WS_CHILD` → `SetWindowPos()` on resize.

**Don't fight Electron** - VS Code's internal resize cursors (sidebar/split sashes) use custom hit-testing. Not fixable via window styles.

## Key Files

- `VSBothExtensionPackage.cs` - Entry point, `Dispose()` calls `ShutdownVSCode()`
- `VSCodeToolWindowControl.xaml.cs` - Win32 interop, process lifecycle
- `VSCommands.vsct` - Adds "VSCode Window" to View menu
- `source.extension.vsixmanifest` - VSIX metadata (icon: 90x90 PNG)

## Build

Press **F5** to deploy to Experimental Instance. Clean `obj/` folder if namespace changes cause build errors. Don't add generated `VSCommands.cto` to project.
