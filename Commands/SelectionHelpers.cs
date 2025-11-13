using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace repotxt.Commands
{
    internal static class SelectionHelpers
    {
        public sealed class SelectedPath
        {
            public string FullPath { get; init; } = string.Empty;
            public bool IsDirectory { get; init; }
        }

        public static async Task<IReadOnlyList<SelectedPath>> GetSelectedPathsAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var result = new List<SelectedPath>();
            var dte = await package.GetServiceAsync(typeof(SDTE)) as DTE2;
            if (dte == null) return result;

            Array selectedItems = dte.ToolWindows?.SolutionExplorer?.SelectedItems as Array ?? Array.Empty<object>();
            foreach (UIHierarchyItem uiItem in selectedItems)
            {
                if (uiItem.Object is ProjectItem pi)
                {
                    // Физическая папка?
                    bool isFolder = string.Equals(pi.Kind, EnvDTE.Constants.vsProjectItemKindPhysicalFolder, StringComparison.OrdinalIgnoreCase);

                    // У ProjectItem может быть несколько файлов (partial classes и т.п.). Берём первый.
                    string full = null!;
                    try { full = pi.FileCount > 0 ? pi.FileNames[1] : null!; } catch { }
                    if (!string.IsNullOrEmpty(full))
                    {
                        if (isFolder || Directory.Exists(full))
                            result.Add(new SelectedPath { FullPath = full, IsDirectory = true });
                        else
                            result.Add(new SelectedPath { FullPath = full, IsDirectory = false });
                    }
                }
                else if (uiItem.Object is Project prj)
                {
                    // Проект → его директория
                    var full = prj?.FullName;
                    if (!string.IsNullOrEmpty(full))
                    {
                        var dir = Path.GetDirectoryName(full);
                        if (!string.IsNullOrEmpty(dir))
                            result.Add(new SelectedPath { FullPath = dir, IsDirectory = true });
                    }
                }
                else if (uiItem.Object is Solution sol)
                {
                    if (!string.IsNullOrEmpty(sol.FullName))
                    {
                        var dir = Path.GetDirectoryName(sol.FullName);
                        if (!string.IsNullOrEmpty(dir))
                            result.Add(new SelectedPath { FullPath = dir, IsDirectory = true });
                    }
                }
            }

            return result;
        }
    }
}