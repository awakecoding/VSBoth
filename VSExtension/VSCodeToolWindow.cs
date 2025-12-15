using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace VSExtension
{
    [Guid("2f9d4a5b-6c7e-4d8f-9a0b-1c2d3e4f5a6b")]
    public class VSCodeToolWindow : ToolWindowPane
    {
        public VSCodeToolWindow() : base(null)
        {
            this.Caption = "VSCode";
            this.Content = new VSCodeToolWindowControl();
        }
    }
}
