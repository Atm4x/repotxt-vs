using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using repotxt.Core;
using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;

namespace repotxt.Commands
{
    internal sealed class GenerateReportCommand
    {
        public const int CommandId = 0x0200;
        public static readonly Guid CommandSet = new Guid("7c6ecb0d-7f2f-49b4-9b1e-f78284616f50");

        private readonly AsyncPackage _package;
        private readonly RepoAnalyzerCore _core;

        private GenerateReportCommand(AsyncPackage package, RepoAnalyzerCore core, OleMenuCommandService commandService)
        {
            _package = package;
            _core = core;

            var cmdId = new CommandID(CommandSet, CommandId);
            var cmd = new OleMenuCommand(ExecuteAsync, cmdId);
            cmd.BeforeQueryStatus += (s, e) =>
            {
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
            _ = new GenerateReportCommand(package, core, commandService);
        }

        private async void ExecuteAsync(object sender, EventArgs e)
        {
            try
            {
                var report = await _core.GenerateReportAsync();

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var dte = await _package.GetServiceAsync(typeof(SDTE)) as DTE2;
                if (dte == null)
                    return;

                // Новый текстовый файл
                dte.ItemOperations.NewFile(@"General\Text File", "repotxt.md");
                var doc = dte.ActiveDocument;
                if (doc != null)
                {
                    var textDoc = doc.Object("TextDocument") as TextDocument;
                    if (textDoc != null)
                    {
                        var ep = textDoc.StartPoint.CreateEditPoint();
                        ep.Insert(report);
                    }
                }
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