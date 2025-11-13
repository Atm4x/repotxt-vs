using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using repotxt.Commands;
using repotxt.Core;
using repotxt.UI;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace repotxt
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(RepoToolWindow))]
    [Guid(repotxtPackage.PackageGuidString)]
    [ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class repotxtPackage : AsyncPackage
    {
        public const string PackageGuidString = "483a8767-861e-42d5-896b-ff189190d8fb";
        private RepoAnalyzerCore? _core;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            _core = await RepoAnalyzerCore.CreateAsync(this);
            RepotxtServices.Initialize(this, _core);

            //await ToggleExcludeCommand.InitializeAsync(this, _core);
            //await GenerateReportCommand.InitializeAsync(this, _core);
            await ShowExplorerCommand.InitializeAsync(this); // новая команда – открыть Tool Window
        }
    }
}