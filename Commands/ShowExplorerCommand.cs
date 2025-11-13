using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;

namespace repotxt.Commands
{
    internal sealed class ShowExplorerCommand
    {
        public const int CommandId = 0x0300;
        public static readonly Guid CommandSet = new Guid("7c6ecb0d-7f2f-49b4-9b1e-f78284616f50");
        private readonly AsyncPackage _package;

        private ShowExplorerCommand(AsyncPackage package, OleMenuCommandService mcs)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _package = package;

            var cmdId = new CommandID(CommandSet, CommandId);
            var cmd = new OleMenuCommand(OnInvoke, cmdId);
            cmd.BeforeQueryStatus += (s, e) =>
            {
                var c = (OleMenuCommand)s;
                c.Visible = true;
                c.Checked = true;
                c.Supported = true;
                c.Enabled = true; // всегда включена
            };
            mcs.AddCommand(cmd);
            var a = mcs.FindCommand(cmdId);
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var mcs = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService
                ?? throw new InvalidOperationException("OleMenuCommandService not found");
            _ = new ShowExplorerCommand(package, mcs);
        }

        // ВАЖНО: не async void
        private void OnInvoke(object sender, EventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var window = await _package.ShowToolWindowAsync(typeof(UI.RepoToolWindow), 0, true, _package.DisposalToken);
                    if (window == null || window.Frame == null)
                        throw new InvalidOperationException("Cannot create repotxt window (frame is null).");
                }
                catch (Exception ex)
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    VsShellUtilities.ShowMessageBox(
                        _package,
                        ex.ToString(),
                        "repotxt error",
                        OLEMSGICON.OLEMSGICON_CRITICAL,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                }
            })
            .FileAndForget("repotxt/ShowExplorer");
        }
    }
}