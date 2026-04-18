using System.IO;
using Eto.Forms;
using Rhino;
using Rhino.Commands;
using Rhino.Input;
using RhinoPhotoMatch.Core;

namespace RhinoPhotoMatch.Commands
{
    /// <summary>
    /// PMImportPhoto — file picker shortcut that delegates to PhotoPlaneRegistry.CreatePair.
    /// Equivalent to PMAddPhotoPlane; kept for discoverability.
    /// </summary>
    public class ImportPhotoCommand : Rhino.Commands.Command
    {
        public override string EnglishName => "PMImportPhoto";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            return AddPhotoPlaneCommand.RunShared(doc);
        }
    }
}
