using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using repotxt.Core;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace repotxt.UI
{
    public partial class RepoExplorerControl : UserControl
    {
        public ObservableCollection<NodeVM> RootNodes { get; } = new();
        private RepoAnalyzerCore? Core => RepotxtServices.Core;

        public RepoExplorerControl()
        {
            InitializeComponent();
            DataContext = this;
            if (Core is not null)
            {
                Core.SolutionChanged += async (_, __) => await BuildTreeAsync();
            }
            Loaded += RepoExplorerControl_Loaded;
            Tree.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(Tree_Expanded));
            _ = BuildTreeAsync();
        }

        private void RepoExplorerControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (Core != null)
                WrapToggle.IsChecked = Core.WrapLongLines;
        }

        private async Task BuildTreeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (Core is not null)
                await Core.RefreshSolutionInfoAsync();

            if (Core?.SolutionRoot == null || !Directory.Exists(Core.SolutionRoot))
            {
                RootNodes.Clear();
                RootNodes.Add(NodeVM.Empty("No solution opened"));
                return;
            }

            var root = Core.SolutionRoot;
            if (RootNodes.Count == 0 || RootNodes.First().FullPath != root)
            {
                RootNodes.Clear();
                try
                {
                    var dirs = Directory.EnumerateDirectories(root)
                        .OrderBy(p => Path.GetFileName(p));
                    var files = Directory.EnumerateFiles(root)
                        .OrderBy(p => Path.GetFileName(p));

                    foreach (var d in dirs)
                    {
                        var n = NodeVM.FromPath(d, Core);
                        n.EnsureChildrenLoaded(1);
                        RootNodes.Add(n);
                    }
                    foreach (var f in files)
                    {
                        RootNodes.Add(NodeVM.FromPath(f, Core));
                    }
                }
                catch
                {
                    RootNodes.Clear();
                    RootNodes.Add(NodeVM.Empty("Cannot read solution folder"));
                }
            }
            else
            {
                foreach (var n in RootNodes) n.RefreshRecursive();
            }
        }

        private void Tree_Expanded(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is TreeViewItem tvi && tvi.DataContext is NodeVM vm)
            {
                vm.EnsureChildrenLoaded(1);
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await BuildTreeAsync();
        }

        private async void Reset_Click(object sender, RoutedEventArgs e)
        {
            Core?.ResetManualRules();
            foreach (var n in RootNodes) n.RefreshRecursive();
            await Task.CompletedTask;
        }

        private async void Defaults_Click(object sender, RoutedEventArgs e)
        {
            Core?.ResetToDefaults();
            if (Core != null)
                WrapToggle.IsChecked = Core.WrapLongLines;
            foreach (var n in RootNodes) n.RefreshRecursive();
            await Task.CompletedTask;
        }

        private async void Generate_Click(object sender, RoutedEventArgs e)
        {
            if (Core == null) return;

            var report = await Core.GenerateReportAsync();

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await RepotxtServices.Package!.GetServiceAsync(typeof(SDTE)) as DTE2;
            if (dte == null) return;

            dte.ItemOperations.NewFile(@"General\Text File", "repotxt.md");
            var doc = dte.ActiveDocument;
            if (doc != null)
            {
                var textDoc = doc.Object("TextDocument") as TextDocument;
                textDoc?.StartPoint.CreateEditPoint().Insert(report);
            }
        }

        private async void WrapToggle_Click(object sender, RoutedEventArgs e)
        {
            if (Core == null) return;
            Core.WrapLongLines = WrapToggle.IsChecked == true;
            await Task.CompletedTask;
        }
    }
}