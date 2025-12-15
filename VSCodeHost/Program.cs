using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VSCodeHost
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            var form = new MainForm(args);
            form.StartEmbedding();
            Application.Run(form);
        }
    }

    public class MainForm : Form
    {
        private readonly Panel _hostPanel;
        private IntPtr _vsCodeHwnd = IntPtr.Zero;
        private Process? _vsCodeProcess;
        
        private readonly string[] _commandLineArgs;

        public Panel HostPanel => _hostPanel;

        public MainForm() : this(Array.Empty<string>())
        {
        }

        public MainForm(string[] args)
        {
            _commandLineArgs = args;
            
            Text = "VSCode Host Demo";
            Width = 1200;
            Height = 800;

            _hostPanel = new Panel { Dock = DockStyle.Fill };
            _hostPanel.Resize += (_, __) => ResizeEmbeddedVsCode();

            Controls.Add(_hostPanel);

            FormClosing += (_, __) => Cleanup();
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

        public async Task StartEmbedding()
        {
            await LaunchAndEmbedVsCodeAsync();
        }

        private async Task LaunchAndEmbedVsCodeAsync()
        {
            try
            {
                // Find the full path to code.exe
                string? codeExePath = FindCodeExecutable();
                if (codeExePath == null)
                {
                    MessageBox.Show("Could not find VSCode executable. Make sure VSCode is installed and 'code' is in your PATH.");
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = codeExePath,
                    Arguments = string.Join(" ", _commandLineArgs),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(codeExePath)
                };

                _vsCodeProcess = Process.Start(psi);
                if (_vsCodeProcess == null)
                {
                    MessageBox.Show("Failed to start VSCode.");
                    return;
                }

                // Wait a second for VS Code to fully launch (RDM technique)
                await Task.Delay(1000);

                IntPtr hwnd = IntPtr.Zero;
                
                // Try to find VS Code window by enumerating all windows
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    hwnd = FindVSCodeWindow();
                    if (hwnd != IntPtr.Zero)
                        break;
                    
                    await Task.Delay(500);
                }

                if (hwnd == IntPtr.Zero)
                {
                    MessageBox.Show("Could not find VS Code main window.");
                    return;
                }

                // Hide the window first to avoid flickering during reparenting
                ShowWindow(hwnd, SW_HIDE);

                // Reparent the window
                SetParent(hwnd, _hostPanel.Handle);

                // Remove window decorations and make it a child window
                int style = GetWindowLong(hwnd, GWL_STYLE);
                style &= ~WS_CAPTION;
                style &= ~WS_THICKFRAME;
                style &= ~WS_MINIMIZEBOX;
                style &= ~WS_MAXIMIZEBOX;
                style |= WS_CHILD;
                SetWindowLong(hwnd, GWL_STYLE, style);

                _vsCodeHwnd = hwnd;

                // Resize to fit the panel
                ResizeEmbeddedVsCode();

                // Now show the window in its new embedded location
                ShowWindow(hwnd, SW_SHOW);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error embedding VS Code: " + ex.Message);
            }
        }

        private string? FindCodeExecutable()
        {
            // Get PATH environment variable
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
                    // Skip invalid paths
                }
            }

            return null;
        }

        private IntPtr FindVSCodeWindow()
        {
            var windows = new List<(IntPtr hwnd, string title)>();
            
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd))
                    return true;

                int length = GetWindowTextLength(hWnd);
                if (length == 0)
                    return true;

                var sb = new StringBuilder(length + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();

                if (!string.IsNullOrEmpty(title))
                {
                    windows.Add((hWnd, title));
                }

                return true;
            }, IntPtr.Zero);

            // Look for VS Code window - it typically has "Visual Studio Code" in the title
            foreach (var (hwnd, title) in windows)
            {
                if (title.IndexOf("Visual Studio Code", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return hwnd;
                }
            }

            return IntPtr.Zero;
        }

        private void ResizeEmbeddedVsCode()
        {
            if (_vsCodeHwnd == IntPtr.Zero)
                return;

            var client = _hostPanel.ClientRectangle;
            if (client.Width <= 0 || client.Height <= 0)
                return;

            SetWindowPos(
                _vsCodeHwnd,
                IntPtr.Zero,
                0, 0,
                client.Width,
                client.Height,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        private void Cleanup()
        {
            try
            {
                if (_vsCodeProcess != null && !_vsCodeProcess.HasExited)
                {
                    _vsCodeProcess.CloseMainWindow();
                }
            }
            catch { }
        }
    }
}
