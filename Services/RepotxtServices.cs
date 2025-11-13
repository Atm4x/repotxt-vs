using Microsoft.VisualStudio.Shell;
using repotxt.Core;

namespace repotxt
{
    internal static class RepotxtServices
    {
        public static AsyncPackage? Package { get; private set; }
        public static RepoAnalyzerCore? Core { get; private set; }

        public static void Initialize(AsyncPackage package, RepoAnalyzerCore core)
        {
            Package = package;
            Core = core;
        }
    }
}