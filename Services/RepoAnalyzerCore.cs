using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace repotxt.Core
{
    public sealed class RepoAnalyzerCore
    {
        private readonly AsyncPackage _package;
        private IVsSolution? _vsSolution;
        private uint _solutionEventsCookie;
        private VsSolutionEvents? _eventsSink;

        private string? _solutionRoot;
        private string? _solutionName;
        private string? _stateFilePath;

        // Вручную заданные пользователем правила
        private readonly HashSet<string> _manualIncludes = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _manualExcludes = new(StringComparer.OrdinalIgnoreCase);

        // Базовые директории, которые не хотим тащить в отчёт по умолчанию
        private static readonly HashSet<string> DefaultSkipDirs = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".vs", "bin", "obj", "node_modules", ".idea", ".vscode", "packages" };

        // Бинарные/не-текстовые расширения (контент не читаем)
        private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png",".jpg",".jpeg",".gif",".bmp",".ico",".exe",".dll",".pdb",".zip",".tar",".gz",".7z",".rar",".pdf",
            ".doc",".docx",".xls",".xlsx",".ppt",".pptx",".bin",".class",".obj"
        };

        public static async Task<RepoAnalyzerCore> CreateAsync(AsyncPackage package)
        {
            var core = new RepoAnalyzerCore(package);
            await core.InitializeAsync();
            return core;
        }

        private RepoAnalyzerCore(AsyncPackage package)
        {
            _package = package;
        }

        public string? SolutionRoot => _solutionRoot;
        public string? SolutionName => _solutionName;

        public event EventHandler? SolutionChanged;

        private async Task InitializeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            _vsSolution = await _package.GetServiceAsync(typeof(SVsSolution)) as IVsSolution;

            // Первая попытка прочитать текущее решение
            await RefreshSolutionInfoAsync();

            // Подписка на события открытия/закрытия решения
            if (_vsSolution != null && _solutionEventsCookie == 0)
            {
                _eventsSink = new VsSolutionEvents(this);
                _vsSolution.AdviseSolutionEvents(_eventsSink, out _solutionEventsCookie);
            }
        }

        // Явно перечитать информацию о решении. Возвращает true, если решение открыто.
        public async Task<bool> RefreshSolutionInfoAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            string? solDir = null;
            string? solFile = null;

            if (_vsSolution != null)
            {
                _vsSolution.GetSolutionInfo(out solDir, out solFile, out _);
            }

            if (string.IsNullOrEmpty(solDir))
            {
                // Fallback через DTE
                var dte = await _package.GetServiceAsync(typeof(SDTE)) as DTE2;
                var full = dte?.Solution?.FullName;
                if (!string.IsNullOrEmpty(full))
                {
                    solDir = Path.GetDirectoryName(full);
                    solFile = full;
                }
            }

            var oldRoot = _solutionRoot;

            if (!string.IsNullOrEmpty(solDir) && Directory.Exists(solDir))
            {
                _solutionRoot = solDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                _solutionName = !string.IsNullOrEmpty(solFile)
                    ? Path.GetFileNameWithoutExtension(solFile)
                    : new DirectoryInfo(_solutionRoot).Name;

                _stateFilePath = Path.Combine(_solutionRoot, ".repotxt.state.json");
                LoadState();

                if (!StringComparer.OrdinalIgnoreCase.Equals(oldRoot, _solutionRoot))
                {
                    SolutionChanged?.Invoke(this, EventArgs.Empty);
                }
                return true;
            }

            // Решение закрыто
            if (_solutionRoot != null)
            {
                _solutionRoot = null;
                _solutionName = null;
                _stateFilePath = null;
                _manualIncludes.Clear();
                _manualExcludes.Clear();
                SolutionChanged?.Invoke(this, EventArgs.Empty);
            }
            return false;
        }

        private sealed class VsSolutionEvents : IVsSolutionEvents
        {
            private readonly RepoAnalyzerCore _core;
            public VsSolutionEvents(RepoAnalyzerCore core) => _core = core;

            public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded) => VSConstants.S_OK;
            public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel) => VSConstants.S_OK;
            public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved) => VSConstants.S_OK;
            public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy) => VSConstants.S_OK;

            public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel) => VSConstants.S_OK;
            public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy) => VSConstants.S_OK;

            public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel) => VSConstants.S_OK;
            public int OnBeforeCloseSolution(object pUnkReserved) => VSConstants.S_OK;

            public int OnAfterCloseSolution(object pUnkReserved)
            {
                _ = _core.RefreshSolutionInfoAsync();
                return VSConstants.S_OK;
            }

            public int OnAfterOpenSolution(object pUnkReserved, int fNew)
            {
                _ = _core.RefreshSolutionInfoAsync();
                return VSConstants.S_OK;
            }

            public int OnAfterMergeSolution(object pUnkReserved) => VSConstants.S_OK;
        }

        #region State
        private sealed class StateModel
        {
            public List<string>? Includes { get; set; }
            public List<string>? Excludes { get; set; }
        }

        private void LoadState()
        {
            try
            {
                if (!string.IsNullOrEmpty(_stateFilePath) && File.Exists(_stateFilePath))
                {
                    var json = File.ReadAllText(_stateFilePath, Encoding.UTF8);
                    var state = JsonConvert.DeserializeObject<StateModel>(json) ?? new StateModel();

                    _manualIncludes.Clear();
                    _manualExcludes.Clear();

                    if (state.Includes != null)
                        foreach (var p in state.Includes) _manualIncludes.Add(Norm(p));
                    if (state.Excludes != null)
                        foreach (var p in state.Excludes) _manualExcludes.Add(Norm(p));
                }
            }
            catch { /* ignore */ }
        }

        private void SaveState()
        {
            try
            {
                if (string.IsNullOrEmpty(_stateFilePath)) return;

                var state = new StateModel
                {
                    Includes = _manualIncludes.ToList(),
                    Excludes = _manualExcludes.ToList()
                };

                var json = JsonConvert.SerializeObject(state, Formatting.Indented);
                File.WriteAllText(_stateFilePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch { /* ignore */ }
        }
        #endregion

        public void ResetManualRules()
        {
            _manualIncludes.Clear();
            _manualExcludes.Clear();
            SaveState();
        }

        #region Public API (toggle + report)
        public void ToggleExcludeMultiple(IEnumerable<string> fullPaths)
        {
            foreach (var p in fullPaths) ToggleExclude(p);
            SaveState();
        }

        public void ToggleExclude(string fullPath)
        {
            var p = Norm(fullPath);
            var isDir = Directory.Exists(p);
            var isCurrentlyExcluded = IsPathEffectivelyExcluded(p);

            _manualIncludes.Remove(p);
            _manualExcludes.Remove(p);

            if (isCurrentlyExcluded)
            {
                _manualIncludes.Add(p);
                if (isDir)
                {
                    var toRemove = _manualExcludes.Where(x => IsDescendantOf(x, p)).ToList();
                    foreach (var r in toRemove) _manualExcludes.Remove(r);
                }
            }
            else
            {
                _manualExcludes.Add(p);
                if (isDir)
                {
                    var toRemove = _manualIncludes.Where(x => IsDescendantOf(x, p)).ToList();
                    foreach (var r in toRemove) _manualIncludes.Remove(r);
                }
            }
            SaveState();
        }

        public async Task<string> GenerateReportAsync()
        {
            if (string.IsNullOrEmpty(_solutionRoot) || !Directory.Exists(_solutionRoot))
                return "No solution opened";

            var sb = new StringBuilder();
            var name = _solutionName ?? new DirectoryInfo(_solutionRoot).Name;

            var flat = new List<string>();
            BuildFlatStructure(_solutionRoot, flat);

            sb.AppendLine($"Folder Structure: {name}");
            foreach (var line in flat) sb.AppendLine(line);
            sb.AppendLine();

            var files = EnumerateVisibleFiles(_solutionRoot).ToList();
            foreach (var file in files)
            {
                var rel = ToPosix(Rel(file));
                sb.AppendLine($"File: {rel}");
                sb.Append("Content: ");
                sb.AppendLine(ReadFileContent(file));
                sb.AppendLine();
            }

            return await Task.FromResult(sb.ToString());
        }
        #endregion

        #region Traversal/Include logic
        private string Norm(string path)
        {
            try { path = Path.GetFullPath(path); } catch { }
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private bool IsDescendantOf(string path, string root)
        {
            var p = Norm(path) + Path.DirectorySeparatorChar;
            var r = Norm(root) + Path.DirectorySeparatorChar;
            return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }

        private string Rel(string abs)
        {
            if (string.IsNullOrEmpty(_solutionRoot)) return abs;
            var r = Norm(_solutionRoot) + Path.DirectorySeparatorChar;
            var a = Norm(abs);
            if (a.StartsWith(r, StringComparison.OrdinalIgnoreCase))
                return a.Substring(r.Length);
            return a;
        }

        private static string ToPosix(string p)
            => p.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

        private bool IsDefaultSkippedDirectory(string dirPath)
        {
            var name = new DirectoryInfo(dirPath).Name;
            return DefaultSkipDirs.Contains(name);
        }

        private bool IsDefaultSkippedFile(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            return BinaryExtensions.Contains(ext);
        }

        public bool IsPathEffectivelyExcluded(string fullPath)
        {
            var p = Norm(fullPath);
            if (_manualIncludes.Contains(p)) return false;

            var cur = p;
            while (!string.IsNullOrEmpty(cur))
            {
                if (_manualExcludes.Contains(cur)) return true;
                var parent = Path.GetDirectoryName(cur);
                if (string.IsNullOrEmpty(parent) || parent == cur) break;
                cur = parent;
            }
            return false;
        }

        private bool IsFolderVisuallyExcluded(string folderPath)
        {
            if (!IsPathEffectivelyExcluded(folderPath)) return false;
            return !FolderContainsManualIncludes(folderPath);
        }

        private bool FolderContainsManualIncludes(string folderPath)
        {
            var f = Norm(folderPath) + Path.DirectorySeparatorChar;
            foreach (var inc in _manualIncludes)
            {
                if ((inc + Path.DirectorySeparatorChar).StartsWith(f, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private IEnumerable<string> EnumerateVisibleFiles(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();

                if (IsDefaultSkippedDirectory(dir) && !FolderContainsManualIncludes(dir))
                    continue;

                if (IsFolderVisuallyExcluded(dir))
                    continue;

                IEnumerable<string> files = Enumerable.Empty<string>();
                try { files = Directory.EnumerateFiles(dir); } catch { }

                foreach (var f in files)
                {
                    if (IsDefaultSkippedFile(f)) continue;
                    if (IsPathEffectivelyExcluded(f)) continue;
                    yield return f;
                }

                IEnumerable<string> subdirs = Enumerable.Empty<string>();
                try { subdirs = Directory.EnumerateDirectories(dir); } catch { }

                foreach (var sd in subdirs) stack.Push(sd);
            }
        }

        private void BuildFlatStructure(string root, List<string> result)
        {
            void Walk(string dir)
            {
                if (IsDefaultSkippedDirectory(dir) && !FolderContainsManualIncludes(dir))
                    return;

                var entries = new List<(string path, bool isDir)>();
                try { entries.AddRange(Directory.EnumerateDirectories(dir).Select(d => (d, true))); } catch { }
                try { entries.AddRange(Directory.EnumerateFiles(dir).Select(f => (f, false))); } catch { }

                entries.Sort((a, b) =>
                {
                    var byType = (a.isDir ? 0 : 1).CompareTo(b.isDir ? 0 : 1);
                    return byType != 0 ? byType : StringComparer.OrdinalIgnoreCase.Compare(
                        Path.GetFileName(a.path), Path.GetFileName(b.path));
                });

                foreach (var (path, isDir) in entries)
                {
                    if (isDir)
                    {
                        if (!IsFolderVisuallyExcluded(path))
                        {
                            var rel = ToPosix(Rel(path)) + "/";
                            result.Add(rel);
                            Walk(path);
                        }
                    }
                    else
                    {
                        if (IsDefaultSkippedFile(path)) continue;
                        if (IsPathEffectivelyExcluded(path)) continue;
                        var rel = ToPosix(Rel(path));
                        result.Add(rel);
                    }
                }
            }
            Walk(root);
        }

        private string ReadFileContent(string filePath)
        {
            try
            {
                if (IsDefaultSkippedFile(filePath))
                    return "[Binary file, content not displayed]";
                return File.ReadAllText(filePath);
            }
            catch
            {
                return "[Unable to read file content]";
            }
        }
        #endregion
    }
}