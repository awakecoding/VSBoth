# VSBoth

**Why not use both Visual Studio and VSCode together?**

VSBoth is a Visual Studio extension that embeds Visual Studio Code directly inside a Visual Studio tool window. Work with the power of Visual Studio's debugging and project system while having VS Code's editor, extensions, and terminal right at your fingertips.

## Features

- 🪟 **Seamless Embedding** - VS Code runs inside a Visual Studio tool window, fully integrated into your workspace
- 🔄 **Persistent Sessions** - VS Code stays alive when you close/reopen the tool window, preserving your editor state
- 📂 **Automatic Workspace** - Opens VS Code with your current Visual Studio solution folder
- 🎯 **Isolated Process Management** - Only terminates the embedded VS Code instance when Visual Studio closes, not other VS Code windows
- 🖱️ **Native Experience** - Full VS Code functionality including keyboard shortcuts, extensions, and UI

## How It Works

VSBoth uses Win32 window reparenting to embed the VS Code process:

1. Launches VS Code with `--new-window --disable-workspace-trust`
2. Locates the VS Code main window using Win32 APIs
3. Reparents the window using `SetParent()` to embed it in the Visual Studio tool window
4. Removes window decorations (title bar, borders) to create a seamless embedded experience
5. Manages the VS Code process lifecycle to ensure proper cleanup

The extension uses static fields to keep VS Code alive across tool window sessions, so you don't lose your editor state when docking/undocking or temporarily closing the window.

## Requirements

- Visual Studio 2022 (17.0+)
- .NET 8.0
- Visual Studio Code installed and `code` command available in PATH

## Building

1. Open `VSBoth.sln` in Visual Studio
2. Build the solution (the extension project is `VSBothExtension`)
3. Press F5 to launch the experimental instance

## Usage

1. Open any solution in Visual Studio
2. Go to **View → VSCode** (or use the command palette)
3. VS Code will launch embedded in a tool window, opening your solution folder
4. Dock the window wherever you want in your Visual Studio layout
5. Close Visual Studio when done - it will clean up the embedded VS Code instance

## Technical Details

### Architecture

- **VSBothExtension** - The Visual Studio extension package
  - `VSCodeToolWindow.cs` - Tool window registration
  - `VSCodeToolWindowControl.xaml.cs` - Main embedding logic and Win32 interop
  - `VSCodeToolWindowCommand.cs` - Command to show the tool window
  - `VSBothExtensionPackage.cs` - Extension package with proper cleanup on VS shutdown

### Key Implementation Details

- Uses `EnumWindows` to locate VS Code's main window by title and process ID
- Applies `WS_CHILD` style and removes `WS_CAPTION`, `WS_THICKFRAME`, etc. to embed cleanly
- Tracks the actual VS Code process by window handle PID to ensure correct termination
- Static fields preserve VS Code instance across tool window lifecycle events

### Known Limitations

- **Inner VS Code resize cursors** - VS Code's internal layout sashes (for sidebar, split editors, panels) use custom Electron hit-testing and cannot be disabled from the host process. This is expected behavior and allows users to adjust VS Code's internal layout.
- **Single instance** - Currently supports one embedded VS Code instance per Visual Studio session
- **Process cleanup** - Relies on Visual Studio's `Dispose` lifecycle for cleanup

## License

MIT

## Contributing

Contributions welcome! This is an experimental project exploring Win32 window embedding techniques.
