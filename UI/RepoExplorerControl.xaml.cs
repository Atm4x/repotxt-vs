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

        public RepoExplorerControl()
        {
            InitializeComponent();
            DataContext = this;
            _ = BuildTreeAsync();
        }

        private RepoAnalyzerCore? Core => RepotxtServices.Core;

        private async Task BuildTreeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            RootNodes.Clear();

            if (Core?.SolutionRoot == null || !Directory.Exists(Core.SolutionRoot))
            {
                RootNodes.Add(NodeVM.Empty("No solution opened"));
                return;
            }

            // Заполняем корень содержимым (папки + файлы)
            var root = Core.SolutionRoot;
            var nodes = NodeVM.BuildChildren(root, Core).OrderBy(n => n.SortKey).ToList();
            foreach (var n in nodes)
                RootNodes.Add(n);
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await BuildTreeAsync();
        }

        private async void Reset_Click(object sender, RoutedEventArgs e)
        {
            Core?.ResetManualRules();
            await BuildTreeAsync();
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
                if (textDoc != null)
                {
                    var ep = textDoc.StartPoint.CreateEditPoint();
                    ep.Insert(report);
                }
            }
        }
    }
}