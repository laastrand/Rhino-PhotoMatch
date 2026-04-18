using Rhino;
using Rhino.Commands;
using RhinoPhotoMatch.Core;

namespace RhinoPhotoMatch.Commands
{
    /// <summary>
    /// PMSaveSession — serialises all photo plane pairs into the Rhino document strings
    /// so they are preserved when the .3dm file is saved.
    /// </summary>
    public class SaveSessionCommand : Command
    {
        public override string EnglishName => "PMSaveSession";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var plugin = RhinoPhotoMatchPlugin.Instance;

            if (plugin.Registry.Pairs.Count == 0)
            {
                RhinoApp.WriteLine("PMSaveSession: no photo planes to save.");
                return Result.Nothing;
            }

            SessionSerializer.Save(doc, plugin.Registry);
            RhinoApp.WriteLine($"PMSaveSession: {plugin.Registry.Pairs.Count} plane(s) saved to document.");
            return Result.Success;
        }
    }
}
