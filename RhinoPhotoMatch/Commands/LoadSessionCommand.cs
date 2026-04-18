using Rhino;
using Rhino.Commands;
using RhinoPhotoMatch.Core;

namespace RhinoPhotoMatch.Commands
{
    /// <summary>
    /// PMLoadSession — restores photo plane pairs that were saved with PMSaveSession
    /// (or auto-saved on document save) from the Rhino document strings.
    /// </summary>
    public class LoadSessionCommand : Command
    {
        public override string EnglishName => "PMLoadSession";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var plugin = RhinoPhotoMatchPlugin.Instance;

            int n = SessionSerializer.Load(doc, plugin.Registry, plugin.Conduit);

            if (n < 0)
            {
                RhinoApp.WriteLine("PMLoadSession: no saved session found in this document.");
                return Result.Nothing;
            }

            if (n == 0)
            {
                RhinoApp.WriteLine("PMLoadSession: session data found but no new planes were loaded " +
                                   "(they may already be active, or viewports could not be matched).");
                return Result.Nothing;
            }

            doc.Views.Redraw();
            RhinoApp.WriteLine($"PMLoadSession: {n} plane(s) restored.");
            return Result.Success;
        }
    }
}
