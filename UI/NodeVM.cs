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

        private bool _childrenLoaded;
        private bool _hasAnyChildren;
        public bool HasAnyChildren
        {
            get => _hasAnyChildren;
            private set { if (_hasAnyChildren != value) { _hasAnyChildren = value; OnPropertyChanged(); } }
        }

        private bool _isIncluded;
        public bool IsIncluded
        {
            get => _isIncluded;
            set
            {
                if (value == _isIncluded) return;
                var desiredExcluded = !value;
                var currentExcluded = _core.IsPathEffectivelyExcluded(FullPath);
                if (desiredExcluded != currentExcluded)
                {
                    _core.ToggleExclude(FullPath);
                }
                _isIncluded = !_core.IsPathEffectivelyExcluded(FullPath);
                OnPropertyChanged();
                RefreshRecursive();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        private NodeVM(string name, string fullPath, bool isDir, RepoAnalyzerCore core)
        {
            Name = name;
            FullPath = fullPath;
            IsDirectory = isDir;
            _core = core;
            _isIncluded = !_core.IsPathEffectivelyExcluded(fullPath);
            _hasAnyChildren = isDir && HasEntriesSafe(fullPath);
        }

        public static NodeVM Empty(string text) => new NodeVM(text, "", false, RepotxtServices.Core!) { _isIncluded = true };

        public static NodeVM FromPath(string path, RepoAnalyzerCore core)
        {
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) name = path;
            return new NodeVM(name, path, Directory.Exists(path), core);
        }

        private static bool HasEntriesSafe(string dir)
        {
            try
            {
                using var e = Directory.EnumerateFileSystemEntries(dir).GetEnumerator();
                return e.MoveNext();
            }
            catch { return false; }
        }

        public void EnsureChildrenLoaded(int depth = 1)
        {
            if (!IsDirectory) return;
            if (_childrenLoaded) return;
            LoadChildren(depth);
            _childrenLoaded = true;
            HasAnyChildren = Children.Count > 0 || HasEntriesSafe(FullPath);
        }

        public void LoadChildren(int depth = 1)
        {
            if (!IsDirectory) return;
            try
            {
                var dirs = Directory.EnumerateDirectories(FullPath)
                    .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);
                var files = Directory.EnumerateFiles(FullPath)
                    .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);

                foreach (var d in dirs)
                {
                    var child = FromPath(d, _core);
                    Children.Add(child);
                    if (depth > 1) child.EnsureChildrenLoaded(depth - 1);
                }
                foreach (var f in files)
                {
                    Children.Add(FromPath(f, _core));
                }
            }
            catch { }
        }

        public void RefreshRecursive()
        {
            _isIncluded = !_core.IsPathEffectivelyExcluded(FullPath);
            OnPropertyChanged(nameof(IsIncluded));
            foreach (var c in Children)
                c.RefreshRecursive();
        }
    }
}