using Rhino;
using Rhino.Commands;
using RhinoPhotoMatch.UI;

namespace RhinoPhotoMatch.Commands
{
    /// <summary>
    /// PMPanel — opens the Photo Match dockable panel.
    /// </summary>
    public class PanelCommand : Rhino.Commands.Command
    {
        public override string EnglishName => "PMPanel";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            Rhino.UI.Panels.OpenPanel(PhotoMatchPanel.PanelId);
            return Result.Success;
        }
    }
}
