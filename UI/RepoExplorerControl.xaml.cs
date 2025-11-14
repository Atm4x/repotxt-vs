// UI/RepoExplorerControl.xaml.cs
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using repotxt.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

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
                Core.SolutionChanged += OnSolutionChanged;
            }
            Loaded += RepoExplorerControl_Loaded;
            Tree.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(Tree_Expanded));
            _ = BuildTreeAsync();
        }

        private void OnSolutionChanged(object? sender, EventArgs e)
        {
            _ = BuildTreeAsync();
        }

        private void RepoExplorerControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (Core != null)
                WrapToggle.IsChecked = Core.WrapLongLines;
        }

        private sealed class NodeInit
        {
            public string Path { get; init; } = "";
            public bool IsDirectory { get; init; }
            public bool HasAnyChildren { get; init; }
            public List<NodeInit>? Children { get; init; }
        }

        private static bool HasVisibleChildren(RepoAnalyzerCore core, string dir)
        {
            try
            {
                foreach (var d in Directory.EnumerateDirectories(dir))
                    if (!core.ShouldHideDirectory(d)) return true;
                foreach (var f in Directory.EnumerateFiles(dir))
                    if (!core.ShouldHideFile(f)) return true;
            }
            catch { }
            return false;
        }

        private static List<NodeInit> BuildLevel(RepoAnalyzerCore core, string root, int depth)
        {
            var result = new List<NodeInit>();
            IEnumerable<string> dirs = Enumerable.Empty<string>();
            IEnumerable<string> files = Enumerable.Empty<string>();
            try { dirs = Directory.EnumerateDirectories(root).Where(d => !core.ShouldHideDirectory(d)); } catch { }
            try { files = Directory.EnumerateFiles(root).Where(f => !core.ShouldHideFile(f)); } catch { }

            foreach (var d in dirs.OrderBy(Path.GetFileName))
            {
                List<NodeInit>? children = null;
                if (depth > 1)
                {
                    children = BuildLevel(core, d, depth - 1);
                }
                var hasAny = children != null ? children.Count > 0 : HasVisibleChildren(core, d);
                result.Add(new NodeInit { Path = d, IsDirectory = true, HasAnyChildren = hasAny, Children = children });
            }

            foreach (var f in files.OrderBy(Path.GetFileName))
            {
                result.Add(new NodeInit { Path = f, IsDirectory = false, HasAnyChildren = false, Children = null });
            }

            return result;
        }

        private async Task BuildTreeAsync()
        {
            ToggleRefreshUI(true);
            if (Core is not null)
                await Core.RefreshSolutionInfoAsync();

            if (Core?.SolutionRoot == null || !Directory.Exists(Core.SolutionRoot))
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                RootNodes.Clear();
                RootNodes.Add(NodeVM.Empty("No solution opened"));
                ToggleRefreshUI(false);
                return;
            }

            var root = Core.SolutionRoot;
            var data = await Task.Run(() => BuildLevel(Core, root, 2));

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            RootNodes.Clear();
            foreach (var n in data)
            {
                var vm = NodeVM.FromPath(n.Path, Core, n.HasAnyChildren);
                if (n.Children != null && n.Children.Count > 0)
                {
                    var children = n.Children.Select(c => NodeVM.FromPath(c.Path, Core, c.HasAnyChildren)).ToList();
                    vm.SetChildren(children);
                }
                RootNodes.Add(vm);
            }
            ToggleRefreshUI(false);
        }

        private void Tree_Expanded(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is TreeViewItem tvi && tvi.DataContext is NodeVM vm && vm.IsDirectory)
            {
                if (vm.Children.Count > 0) return;
                _ = ExpandAsync(vm);
            }
        }

        private async Task ExpandAsync(NodeVM vm)
        {
            ToggleRefreshUI(true);
            var data = await Task.Run(() => BuildLevel(Core!, vm.FullPath, 1));
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var children = data.Select(c => NodeVM.FromPath(c.Path, Core!, c.HasAnyChildren)).ToList();
            vm.SetChildren(children);
            ToggleRefreshUI(false);
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            _ = BuildTreeAsync();
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            Core?.ResetManualRules();
            _ = BuildTreeAsync();
        }

        private void Defaults_Click(object sender, RoutedEventArgs e)
        {
            Core?.ResetToDefaults();
            if (Core != null)
                WrapToggle.IsChecked = Core.WrapLongLines;
            _ = BuildTreeAsync();
        }

        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            _ = GenerateAndOpenReportAsync();
        }

        private async Task GenerateAndOpenReportAsync()
        {
            if (Core == null) return;
            var report = await Core.GenerateReportAsync();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await RepotxtServices.Package!.GetServiceAsync(typeof(SDTE)) as DTE2;
            if (dte != null)
            {
                dte.ItemOperations.NewFile(@"General\Text File", "repotxt.md");
                var doc = dte.ActiveDocument;
                if (doc != null)
                {
                    var textDoc = doc.Object("TextDocument") as TextDocument;
                    textDoc?.StartPoint.CreateEditPoint().Insert(report);
                }
            }
        }

        private void WrapToggle_Click(object sender, RoutedEventArgs e)
        {
            if (Core == null) return;
            Core.WrapLongLines = WrapToggle.IsChecked == true;
        }

        private void OpenFile(string fullPath)
        {
            _ = OpenFileAsync(fullPath);
        }

        private async Task OpenFileAsync(string fullPath)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await RepotxtServices.Package!.GetServiceAsync(typeof(SDTE)) as DTE2;
            if (dte != null && File.Exists(fullPath))
            {
                dte.ItemOperations.OpenFile(fullPath);
            }
        }

        private void TreeViewItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            if (FindAncestor<ToggleButton>(dep) != null) return;
            var item = FindAncestor<TreeViewItem>(dep);
            if (item == null) return;
            if (item.DataContext is NodeVM vm)
            {
                if (vm.IsDirectory)
                {
                    item.IsExpanded = !item.IsExpanded;
                    e.Handled = true;
                }
                else
                {
                    OpenFile(vm.FullPath);
                }
            }
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T t) return t;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void ToggleRefreshUI(bool busy)
        {
            RefreshBtn.IsEnabled = !busy;
        }
    }
}