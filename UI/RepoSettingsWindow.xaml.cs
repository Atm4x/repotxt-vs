// UI/RepoSettingsWindow.xaml.cs
using repotxt.Core;
using System;
using System.Linq;
using System.Windows;

namespace repotxt.UI
{
    public partial class RepoSettingsWindow : Window
    {
        private readonly RepoAnalyzerCore _core;

        public RepoSettingsWindow(RepoAnalyzerCore core)
        {
            InitializeComponent();
            _core = core ?? throw new ArgumentNullException(nameof(core));

            RespectGitIgnoreCheckBox.IsChecked = _core.RespectGitIgnore;

            HiddenDirsTextBox.Text = string.Join(Environment.NewLine, _core.HiddenDirPatterns);
            HiddenFilesTextBox.Text = string.Join(Environment.NewLine, _core.HiddenFilePatterns);
            AutoDirsTextBox.Text = string.Join(Environment.NewLine, _core.AutoIgnoreDirPatterns);
            AutoFilesTextBox.Text = string.Join(Environment.NewLine, _core.AutoIgnoreFilePatterns);
        }

        private static string[] SplitLines(string text) =>
            text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var hiddenDirs = SplitLines(HiddenDirsTextBox.Text)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            var hiddenFiles = SplitLines(HiddenFilesTextBox.Text)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            var autoDirs = SplitLines(AutoDirsTextBox.Text)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            var autoFiles = SplitLines(AutoFilesTextBox.Text)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            _core.RespectGitIgnore = RespectGitIgnoreCheckBox.IsChecked == true;
            _core.UpdateFilteringPatterns(hiddenDirs, hiddenFiles, autoDirs, autoFiles);

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}