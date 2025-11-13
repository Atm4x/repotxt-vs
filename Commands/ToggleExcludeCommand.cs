using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using repotxt.Core;
using System;
using System.ComponentModel.Design;
using System.Linq;
using System.Threading.Tasks;

namespace repotxt.Commands
{
    internal sealed class ToggleExcludeCommand
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet = new Guid("7c6ecb0d-7f2f-49b4-9b1e-f78284616f50");

        private readonly AsyncPackage _package;
        private readonly RepoAnalyzerCore _core;

        private ToggleExcludeCommand(AsyncPackage package, RepoAnalyzerCore core, OleMenuCommandService commandService)
        {
            _package = package;
            _core = core;

            var cmdId = new CommandID(CommandSet, CommandId);
            var cmd = new OleMenuCommand(Execute, cmdId);
            cmd.BeforeQueryStatus += (s, e) =>
            {
                // Активна, только если открыто решение и есть выделение
                cmd.Visible = _core.SolutionRoot != null;
                cmd.Enabled = _core.SolutionRoot != null;
            };
            commandService.AddCommand(cmd);
        }

        public static async Task InitializeAsync(AsyncPackage package, RepoAnalyzerCore core)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService
                ?? throw new InvalidOperationException("OleMenuCommandService not found");
            _ = new ToggleExcludeCommand(package, core, commandService);
        }

        private async void Execute(object sender, EventArgs e)
        {
            try
            {
                var sel = await SelectionHelpers.GetSelectedPathsAsync(_package);
                if (sel.Count == 0) return;

                _core.ToggleExcludeMultiple(sel.Select(s => s.FullPath));

                // маленький тост
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                VsShellUtilities.ShowMessageBox(
                    _package,
                    "Toggled include/exclude for selected items.",
                    "repotxt",
                    OLEMSGICON.OLEMSGICON_INFO,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
            catch (Exception ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                VsShellUtilities.ShowMessageBox(
                    _package, ex.Message, "repotxt error",
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }
    }
}