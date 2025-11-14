using Microsoft.VisualStudio.Shell;
using System.Runtime.InteropServices;

namespace repotxt.UI
{
    [Guid("0F8F2E3A-0BF0-4E4D-A8B3-6B3D2B6D8C21")]
    public class RepoToolWindow : ToolWindowPane
    {
        public RepoToolWindow() : base(null)
        {
            this.Caption = "Repository Analyzer";
            this.Content = new RepoExplorerControl();
        }
    }
}