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
        public string Name { get; }
        public string FullPath { get; }
        public bool IsDirectory { get; }
        public ObservableCollection<NodeVM> Children { get; } = new();

        public string Icon => IsDirectory ? "📁" : "📄";
        public string SortKey => (IsDirectory ? "0_" : "1_") + Name.ToLowerInvariant();

        private bool _isIncluded;
        public bool IsIncluded
        {
            get => _isIncluded;
            set
            {
                if (value == _isIncluded) return;

                // Мы работаем в терминах Include: true = не исключён
                // Если желаемое состояние отличается — вызываем Toggle в core
                var currentExcluded = _core.IsPathEffectivelyExcluded(FullPath);
                var desiredExcluded = !value;
                if (currentExcluded != desiredExcluded)
                {
                    _core.ToggleExclude(FullPath);
                }

                _isIncluded = !_core.IsPathEffectivelyExcluded(FullPath);
                OnPropertyChanged();
                // Обновим детей и родителей (визуально)
                RefreshRecursive();
            }
        }

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
                    // Добавляем папку, и сразу наполняем её (для простоты)
                    d.Children.Clear();
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
            // Пересчитать IsIncluded по core
            _isIncluded = !_core.IsPathEffectivelyExcluded(FullPath);
            OnPropertyChanged(nameof(IsIncluded));

            foreach (var c in Children)
                c.RefreshRecursive();
        }
    }
}