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
        private string? _currentRoot;
        private string? _lastSolutionRoot;

        public static readonly RoutedUICommand NavigateToCommand =
            new RoutedUICommand("NavigateTo", "NavigateTo", typeof(RepoExplorerControl));

        public static readonly RoutedUICommand ToggleIncludeCommand =
            new RoutedUICommand("ToggleInclude", "ToggleInclude", typeof(RepoExplorerControl));

        private readonly Dictionary<string, List<NodeInit>> _dirCache =
            new(StringComparer.OrdinalIgnoreCase);

        public RepoExplorerControl()
        {
            InitializeComponent();
            DataContext = this;
            if (Core is not null)
            {
                Core.SolutionChanged += OnSolutionChanged;
                _lastSolutionRoot = Core.SolutionRoot;
            }
            Loaded += RepoExplorerControl_Loaded;

            CommandBindings.Add(new CommandBinding(NavigateToCommand, NavigateToCommand_Executed, NavigateToCommand_CanExecute));
            CommandBindings.Add(new CommandBinding(ToggleIncludeCommand, ToggleIncludeCommand_Executed, ToggleIncludeCommand_CanExecute));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, UndoCommand_Executed, UndoCommand_CanExecute));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, RedoCommand_Executed, RedoCommand_CanExecute));

            Tree.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(Tree_Expanded));
            Tree.AddHandler(TreeViewItem.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(TreeViewItem_PreviewMouseLeftButtonDown), true);
            Tree.AddHandler(TreeViewItem.PreviewMouseRightButtonDownEvent, new MouseButtonEventHandler(TreeViewItem_PreviewMouseRightButtonDown), true);

            _ = BuildTreeAsync();
        }

        private void OnSolutionChanged(object? sender, EventArgs e)
        {
            var newRoot = Core?.SolutionRoot;
            bool rootChanged = false;

            if (!string.IsNullOrEmpty(newRoot) || !string.IsNullOrEmpty(_lastSolutionRoot))
            {
                var normNew = NormalizeDirSafe(newRoot);
                var normLast = NormalizeDirSafe(_lastSolutionRoot);
                rootChanged = !StringComparer.OrdinalIgnoreCase.Equals(normNew, normLast);
            }

            _lastSolutionRoot = newRoot;
            _dirCache.Clear();

            if (rootChanged)
            {
                _currentRoot = null;
                _ = BuildTreeAsync(refreshSolution: true);
            }
            else
            {
                foreach (var node in RootNodes)
                    node.RefreshRecursive();

                UpdateCurrentFolderUI();
            }
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

        private static string NormalizeDir(string path)
        {
            try { path = Path.GetFullPath(path); } catch { }
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static List<NodeInit> BuildLevel(RepoAnalyzerCore core, string root, int depth, Dictionary<string, List<NodeInit>>? cache)
        {
            if (depth <= 0) return new List<NodeInit>();

            if (depth == 1 && cache != null)
            {
                var key = NormalizeDir(root);
                lock (cache)
                {
                    if (cache.TryGetValue(key, out var cached))
                        return cached;
                }
            }

            var result = new List<NodeInit>();
            IEnumerable<string> dirs = Enumerable.Empty<string>();
            IEnumerable<string> files = Enumerable.Empty<string>();
            try { dirs = Directory.EnumerateDirectories(root).Where(d => !core.ShouldHideDirectory(d)); } catch { }
            try { files = Directory.EnumerateFiles(root).Where(f => !core.ShouldHideFile(f)); } catch { }

            foreach (var d in dirs)
            {
                List<NodeInit>? children = null;
                bool hasAny;
                if (depth > 1)
                {
                    children = BuildLevel(core, d, depth - 1, cache);
                    hasAny = children.Count > 0;
                }
                else
                {
                    hasAny = true;
                }
                result.Add(new NodeInit { Path = d, IsDirectory = true, HasAnyChildren = hasAny, Children = children });
            }

            foreach (var f in files)
            {
                result.Add(new NodeInit { Path = f, IsDirectory = false, HasAnyChildren = false, Children = null });
            }

            if (depth == 1 && cache != null)
            {
                var key = NormalizeDir(root);
                lock (cache)
                {
                    cache[key] = result;
                }
            }

            return result;
        }

        private async Task BuildTreeAsync(bool refreshSolution = true)
        {
            ToggleRefreshUI(true);
            if (refreshSolution && Core is not null)
                await Core.RefreshSolutionInfoAsync();

            if (Core?.SolutionRoot == null || !Directory.Exists(Core.SolutionRoot))
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                RootNodes.Clear();
                RootNodes.Add(NodeVM.Empty("No solution opened"));
                _currentRoot = null;
                _dirCache.Clear();
                UpdateCurrentFolderUI();
                ToggleRefreshUI(false);
                return;
            }

            var solutionRoot = Core.SolutionRoot;
            if (string.IsNullOrEmpty(_currentRoot) || !Directory.Exists(_currentRoot) || !IsPathUnderRoot(_currentRoot, solutionRoot))
            {
                _currentRoot = solutionRoot;
            }

            var effectiveRoot = _currentRoot!;
            var data = await Task.Run(() => BuildLevel(Core, effectiveRoot, 1, _dirCache));

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            RootNodes.Clear();
            foreach (var n in data)
            {
                var vm = NodeVM.FromPath(n.Path, Core, n.HasAnyChildren, 0);
                if (n.Children != null && n.Children.Count > 0)
                {
                    var children = n.Children.Select(c => NodeVM.FromPath(c.Path, Core, c.HasAnyChildren, 1)).ToList();
                    vm.SetChildren(children);
                }
                RootNodes.Add(vm);
            }
            UpdateCurrentFolderUI();
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
            if (Core == null) return;
            ToggleRefreshUI(true);
            var data = await Task.Run(() => BuildLevel(Core, vm.FullPath, 1, _dirCache));
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var children = data.Select(c => NodeVM.FromPath(c.Path, Core!, c.HasAnyChildren, vm.Level + 1)).ToList();
            vm.SetChildren(children);
            ToggleRefreshUI(false);
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            _dirCache.Clear();
            _ = BuildTreeAsync();
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            Core?.ResetManualRules();
        }

        private void HideAll_Click(object sender, RoutedEventArgs e)
        {
            if (Core?.SolutionRoot == null)
                return;

            var root = _currentRoot;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return;

            Core.ToggleExclude(root);

            foreach (var node in RootNodes)
                node.RefreshRecursive();

            UpdateCurrentFolderUI();
        }

        private static string? NormalizeDirSafe(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            try { path = Path.GetFullPath(path); } catch { }
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            // Заглушка: настройки ещё не реализованы
        }

        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            _ = GenerateAndOpenReportAsync();
        }

        private async Task GenerateAndOpenReportAsync()
        {
            if (Core == null) return;
            var report = await Core.GenerateReportAsync(_currentRoot);
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

        private void TreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            var item = FindAncestor<TreeViewItem>(dep);
            if (item == null) return;
            item.IsSelected = true;
            item.Focus();
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

        private void NavigateToDirectory(string directoryPath)
        {
            if (Core?.SolutionRoot == null)
                return;

            try
            {
                if (!Directory.Exists(directoryPath))
                    return;

                if (!IsPathUnderRoot(directoryPath, Core.SolutionRoot))
                    return;

                _currentRoot = Path.GetFullPath(directoryPath);
                _ = BuildTreeAsync(false);
            }
            catch
            {
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Core?.SolutionRoot == null)
                return;

            var solutionRoot = Core.SolutionRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var current = _currentRoot;

            if (string.IsNullOrEmpty(current))
            {
                _currentRoot = solutionRoot;
                _ = BuildTreeAsync(false);
                return;
            }

            try
            {
                var normalizedCurrent = Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(normalizedCurrent, solutionRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var parent = Path.GetDirectoryName(normalizedCurrent);
                if (string.IsNullOrEmpty(parent) || !IsPathUnderRoot(parent, solutionRoot))
                {
                    _currentRoot = solutionRoot;
                }
                else
                {
                    _currentRoot = parent;
                }
                _ = BuildTreeAsync(false);
            }
            catch
            {
            }
        }

        private void UpdateCurrentFolderUI()
        {
            if (Core?.SolutionRoot == null || !Directory.Exists(Core.SolutionRoot))
            {
                BackBtn.IsEnabled = false;
                CurrentPathText.Text = string.Empty;
                if (HideAllBtn != null)
                {
                    HideAllBtn.IsEnabled = false;
                    HideAllIcon.Text = "\uE890";
                }
                return;
            }

            var solutionRoot = Core.SolutionRoot;
            var current = _currentRoot;
            if (string.IsNullOrEmpty(current) || !Directory.Exists(current) || !IsPathUnderRoot(current, solutionRoot))
            {
                current = solutionRoot;
            }

            var rel = GetRelativePathForDisplay(solutionRoot, current);
            CurrentPathText.Text = rel;

            BackBtn.IsEnabled = !string.Equals(
                solutionRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);

            if (HideAllBtn != null)
            {
                HideAllBtn.IsEnabled = true;
                bool isExcluded = Core.IsPathEffectivelyExcluded(current);
                HideAllIcon.Text = isExcluded ? "\uE8F5" : "\uE890";
            }
        }

        private static string GetRelativePathForDisplay(string root, string current)
        {
            try
            {
                var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var fullCurrent = Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                string rel;
                if (fullCurrent.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                {
                    rel = fullCurrent.Substring(fullRoot.Length);
                }
                else
                {
                    rel = fullCurrent;
                }

                rel = rel.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         .Replace(Path.DirectorySeparatorChar, '/')
                         .Replace(Path.AltDirectorySeparatorChar, '/');

                if (string.IsNullOrEmpty(rel))
                    return "/";

                var segments = rel.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length <= 2)
                {
                    return "/" + string.Join("/", segments);
                }

                var tail = string.Join("/", segments.Skip(Math.Max(0, segments.Length - 2)));
                return "/.../" + tail;
            }
            catch
            {
                return "/";
            }
        }

        private static bool IsPathUnderRoot(string path, string root)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void NavigateToCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = e.Parameter is NodeVM vm && vm.IsDirectory;
        }

        private void NavigateToCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Parameter is NodeVM vm && vm.IsDirectory)
            {
                NavigateToDirectory(vm.FullPath);
            }
        }

        private void ToggleIncludeCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = e.Parameter is NodeVM;
        }

        private void ToggleIncludeCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Parameter is NodeVM vm)
            {
                vm.IsIncluded = !vm.IsIncluded;
            }
        }

        private void UndoCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = Core?.CanUndo ?? false;
        }

        private void UndoCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Core?.Undo();
        }

        private void RedoCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = Core?.CanRedo ?? false;
        }

        private void RedoCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Core?.Redo();
        }
    }
}