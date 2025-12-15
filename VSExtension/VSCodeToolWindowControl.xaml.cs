using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;
using System.Windows.Forms.Integration;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Package = Microsoft.VisualStudio.Shell.Package;

namespace VSExtension
{
    public partial class VSCodeToolWindowControl : System.Windows.Controls.UserControl
    {
        // Static fields to keep VS Code alive across tool window close/open cycles
        private static IntPtr s_vsCodeHwnd = IntPtr.Zero;
        private static System.Diagnostics.Process? s_vsCodeProcess = null;
        private static bool s_isInitialized = false;

        private WinForms.Panel? _hostPanel;

        public VSCodeToolWindowControl()
        {
            InitializeComponent();
            Unloaded += UserControl_Unloaded;
        }

        private async void UserControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                await InitializeAsync();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error initializing VSCode window: {ex.Message}");
            }
        }

        private async Task InitializeAsync()
        {
            // Don't initialize if already done
            if (_hostPanel != null)
                return;

            // Create Windows Forms panel
            _hostPanel = new WinForms.Panel { Dock = WinForms.DockStyle.Fill };
            _hostPanel.Resize += (_, __) => ResizeEmbeddedVsCode();
            FormsHost.Child = _hostPanel;

            // Check if VS Code is already running from a previous session
            if (s_isInitialized && s_vsCodeHwnd != IntPtr.Zero && IsWindow(s_vsCodeHwnd))
            {
                // Reuse existing VS Code instance
                SetParent(s_vsCodeHwnd, _hostPanel.Handle);
                ResizeEmbeddedVsCode();
                ShowWindow(s_vsCodeHwnd, SW_SHOW);
            }
            else
            {
                // Get the solution folder path
                string? solutionFolder = await GetSolutionFolderAsync();
                if (solutionFolder != null)
                {
                    await LaunchAndEmbedVsCodeAsync(solutionFolder);
                }
            }
        }

        private async Task<string?> GetSolutionFolderAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
            if (dte?.Solution != null && !string.IsNullOrEmpty(dte.Solution.FullName))
            {
                return Path.GetDirectoryName(dte.Solution.FullName);
            }

            return null;
        }

        #region Win32
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsWindow(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int GWL_STYLE = -16;
        private const int WS_CHILD = 0x40000000;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;

        private const int SW_RESTORE = 9;
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        #endregion

        private async Task LaunchAndEmbedVsCodeAsync(string workingDirectory)
        {
            try
            {
                string? codeExePath = FindCodeExecutable();
                if (codeExePath == null)
                {
                    System.Windows.MessageBox.Show("Could not find VSCode executable. Make sure VSCode is installed and 'code' is in your PATH.");
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = codeExePath,
                    Arguments = $"--new-window --disable-workspace-trust \"{workingDirectory}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(codeExePath)
                };

                s_vsCodeProcess = System.Diagnostics.Process.Start(psi);
                if (s_vsCodeProcess == null)
                {
                    System.Windows.MessageBox.Show("Failed to start VSCode.");
                    return;
                }

                // Give VS Code more time to start up
                await Task.Delay(2000);

                IntPtr hwnd = IntPtr.Zero;
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    hwnd = FindVSCodeWindow();
                    if (hwnd != IntPtr.Zero)
                        break;
                    await Task.Delay(500);
                }

                if (hwnd == IntPtr.Zero)
                {
                    System.Windows.MessageBox.Show("Could not find VSCode main window.");
                    return;
                }

                ShowWindow(hwnd, SW_HIDE);
                SetParent(hwnd, _hostPanel!.Handle);

                int style = GetWindowLong(hwnd, GWL_STYLE);
                style &= ~WS_CAPTION;
                style &= ~WS_THICKFRAME;
                style &= ~WS_MINIMIZEBOX;
                style &= ~WS_MAXIMIZEBOX;
                style |= WS_CHILD;
                SetWindowLong(hwnd, GWL_STYLE, style);

                s_vsCodeHwnd = hwnd;
                s_isInitialized = true;
                ResizeEmbeddedVsCode();
                ShowWindow(hwnd, SW_SHOW);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error embedding VSCode: " + ex.Message);
            }
        }

        private string? FindCodeExecutable()
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (pathEnv == null)
                return null;

            var paths = pathEnv.Split(Path.PathSeparator);
            foreach (var path in paths)
            {
                try
                {
                    var codePath = Path.Combine(path, "code.cmd");
                    if (File.Exists(codePath))
                        return codePath;
                    
                    codePath = Path.Combine(path, "code.exe");
                    if (File.Exists(codePath))
                        return codePath;
                }
                catch
                {
                }
            }

            return null;
        }

        private IntPtr FindVSCodeWindow()
        {
            if (s_vsCodeProcess == null)
                return IntPtr.Zero;

            var windows = new List<(IntPtr hwnd, string title, uint processId)>();
            
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd))
                    return true;

                GetWindowThreadProcessId(hWnd, out uint processId);
                
                int length = GetWindowTextLength(hWnd);
                if (length == 0)
                    return true;

                var sb = new StringBuilder(length + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();

                if (!string.IsNullOrEmpty(title))
                {
                    windows.Add((hWnd, title, processId));
                }

                return true;
            }, IntPtr.Zero);

            // First try to match by process ID
            foreach (var (hwnd, title, processId) in windows)
            {
                if (processId == s_vsCodeProcess.Id && 
                    title.Contains("Visual Studio Code", StringComparison.OrdinalIgnoreCase))
                {
                    return hwnd;
                }
            }

            // Fallback: just look for any VS Code window (might be from same instance)
            foreach (var (hwnd, title, _) in windows)
            {
                if (title.Contains("Visual Studio Code", StringComparison.OrdinalIgnoreCase))
                {
                    return hwnd;
                }
            }

            return IntPtr.Zero;
        }

        private void ResizeEmbeddedVsCode()
        {
            if (s_vsCodeHwnd == IntPtr.Zero || _hostPanel == null)
                return;

            var client = _hostPanel.ClientRectangle;
            if (client.Width <= 0 || client.Height <= 0)
                return;

            SetWindowPos(
                s_vsCodeHwnd,
                IntPtr.Zero,
                0, 0,
                client.Width,
                client.Height,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        private void UserControl_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            Cleanup();
        }

        private void Cleanup()
        {
            // Just hide VS Code when the tool window is closed
            if (s_vsCodeHwnd != IntPtr.Zero && IsWindow(s_vsCodeHwnd))
            {
                try
                {
                    ShowWindow(s_vsCodeHwnd, SW_HIDE);
                }
                catch
                {
                    // Ignore if window is already gone
                }
            }

            // Reset instance fields but keep static fields alive
            _hostPanel = null;
        }

        // Call this when Visual Studio is closing to actually terminate VS Code
        public static void ShutdownVSCode()
        {
            if (s_vsCodeHwnd != IntPtr.Zero)
            {
                try
                {
                    ShowWindow(s_vsCodeHwnd, SW_HIDE);
                }
                catch { }
            }

            if (s_vsCodeProcess != null && !s_vsCodeProcess.HasExited)
            {
                try
                {
                    s_vsCodeProcess.Kill();
                    s_vsCodeProcess.Dispose();
                }
                catch { }
            }

            s_vsCodeProcess = null;
            s_vsCodeHwnd = IntPtr.Zero;
            s_isInitialized = false;
        }
    }
}
