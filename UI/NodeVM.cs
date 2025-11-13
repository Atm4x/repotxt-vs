using repotxt.Core;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace repotxt.UI
{
    public class NodeVM : INotifyPropertyChanged
    {
        private readonly RepoAnalyzerCore _core;
        public string SortKey => (IsDirectory ? "0_" : "1_") + Name.ToLowerInvariant();
        public string Name { get; }
        public string FullPath { get; }
        public bool IsDirectory { get; }
        public ObservableCollection<NodeVM> Children { get; } = new();

        public bool HasChildren => IsDirectory && Children.Count > 0;

        // Визуал: включён (true) = НЕ исключён
        private bool _isIncluded;
        public bool IsIncluded
        {
            get => _isIncluded;
            set
            {
                if (value == _isIncluded) return;

                // хотим: true = включён => excluded=false
                var desiredExcluded = !value;
                var currentExcluded = _core.IsPathEffectivelyExcluded(FullPath);
                if (desiredExcluded != currentExcluded)
                {
                    _core.ToggleExclude(FullPath);
                }

                _isIncluded = !_core.IsPathEffectivelyExcluded(FullPath);
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsExcluded));
                OnPropertyChanged(nameof(EyeGlyph));
                RefreshRecursive();
            }
        }

        public bool IsExcluded => !IsIncluded;

        // Глифы (Segoe MDL2 Assets)
        public string EyeGlyph => IsExcluded ? "\uE8F5" /*Hide*/ : "\uE890" /*View*/;
        public string FolderGlyphClosed => "\uE8B7"; // Folder
        public string FolderGlyphOpen => "\uE838"; // OpenFolder
        public string FileGlyph => "\uE7C3"; // Page

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? p = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        private NodeVM(string name, string fullPath, bool isDir, RepoAnalyzerCore core)
        {
            Name = name;
            FullPath = fullPath;
            IsDirectory = isDir;
            _core = core;

            _isIncluded = !_core.IsPathEffectivelyExcluded(fullPath);
        }

        public static NodeVM Empty(string text) =>
            new NodeVM(text, "", false, RepotxtServices.Core!) { _isIncluded = true };

        public static NodeVM FromPath(string path, RepoAnalyzerCore core)
        {
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) name = path;
            return new NodeVM(name, path, Directory.Exists(path), core);
        }

        public static ObservableCollection<NodeVM> BuildChildren(string directoryPath, RepoAnalyzerCore core)
        {
            var list = new ObservableCollection<NodeVM>();

            try
            {
                var dirs = Directory.EnumerateDirectories(directoryPath)
                    .Select(p => FromPath(p, core)).OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase);
                var files = Directory.EnumerateFiles(directoryPath)
                    .Select(p => FromPath(p, core)).OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase);

                foreach (var d in dirs)
                {
                    foreach (var ch in BuildChildren(d.FullPath, core))
                        d.Children.Add(ch);
                    list.Add(d);
                }
                foreach (var f in files)
                    list.Add(f);
            }
            catch { /* ignore IO exceptions */ }

            return list;
        }

        public void RefreshRecursive()
        {
            _isIncluded = !_core.IsPathEffectivelyExcluded(FullPath);
            OnPropertyChanged(nameof(IsIncluded));
            OnPropertyChanged(nameof(IsExcluded));
            OnPropertyChanged(nameof(EyeGlyph));

            foreach (var c in Children)
                c.RefreshRecursive();
        }
    }
}