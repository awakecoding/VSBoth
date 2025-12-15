using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace VSBothExtension
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(VSCodeToolWindow))]
    public sealed class VSBothExtensionPackage : AsyncPackage
    {
        public const string PackageGuidString = "1e8f3b4c-5a6d-4e7f-8b9c-0d1e2f3a4b5c";

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            await VSCodeToolWindowCommand.InitializeAsync(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Shutdown VS Code when Visual Studio is closing
                VSCodeToolWindowControl.ShutdownVSCode();
            }
            base.Dispose(disposing);
        }
    }
}
