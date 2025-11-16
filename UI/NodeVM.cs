// UI/NodeVM.cs
using repotxt.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace repotxt.UI
{
    public class NodeVM : INotifyPropertyChanged
    {
        private readonly RepoAnalyzerCore _core;
        private const double IndentSize = 22;

        public string SortKey => (IsDirectory ? "0_" : "1_") + Name.ToLowerInvariant();
        public string Name { get; }
        public string FullPath { get; }
        public bool IsDirectory { get; }
        public int Level { get; }
        public Thickness IndentMargin { get; }

        public ObservableCollection<NodeVM> Children { get; } = new();

        private bool _childrenLoaded;
        private bool _hasAnyChildren;
        public bool HasAnyChildren
        {
            get => _hasAnyChildren;
            private set
            {
                if (_hasAnyChildren != value)
                {
                    _hasAnyChildren = value;
                    OnPropertyChanged();
                }
            }
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

        private NodeVM(string name, string fullPath, bool isDir, RepoAnalyzerCore core, int level)
        {
            Name = name;
            FullPath = fullPath;
            IsDirectory = isDir;
            _core = core;
            Level = level;
            IndentMargin = new Thickness(level * IndentSize, 0, 0, 0);

            _isIncluded = !_core.IsPathEffectivelyExcluded(fullPath);
            _hasAnyChildren = false;
        }

        public static NodeVM Empty(string text) => new NodeVM(text, "", false, RepotxtServices.Core!, 0) { _isIncluded = true };

        public static NodeVM FromPath(string path, RepoAnalyzerCore core) => FromPath(path, core, null, 0);

        public static NodeVM FromPath(string path, RepoAnalyzerCore core, bool? hasAnyChildren, int level)
        {
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) name = path;
            var vm = new NodeVM(name, path, Directory.Exists(path), core, level);
            if (hasAnyChildren.HasValue)
            {
                vm._hasAnyChildren = hasAnyChildren.Value;
            }
            else if (vm.IsDirectory)
            {
                vm._hasAnyChildren = HasEntriesSafeFiltered(path, core);
            }
            return vm;
        }

        private static bool HasEntriesSafeFiltered(string dir, RepoAnalyzerCore core)
        {
            try
            {
                foreach (var d in Directory.EnumerateDirectories(dir))
                    if (!core.ShouldHideDirectory(d)) return true;
                foreach (var f in Directory.EnumerateFiles(dir))
                    if (!core.ShouldHideFile(f)) return true;
                return false;
            }
            catch { return false; }
        }

        public void SetChildren(IEnumerable<NodeVM> items)
        {
            Children.Clear();
            foreach (var i in items) Children.Add(i);
            _childrenLoaded = true;
            HasAnyChildren = Children.Count > 0;
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