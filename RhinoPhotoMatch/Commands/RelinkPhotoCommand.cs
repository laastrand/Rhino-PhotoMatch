using System.IO;
using Eto.Forms;
using Rhino;
using Rhino.Commands;
using Rhino.Input.Custom;
using RhinoPhotoMatch.Core;

namespace RhinoPhotoMatch.Commands
{
    /// <summary>
    /// PMRelinkPhoto — re-points a photo plane's material texture to a new file path.
    /// Use this after editing the photo externally (e.g. removing background in Photoshop).
    /// </summary>
    public class RelinkPhotoCommand : Rhino.Commands.Command
    {
        public override string EnglishName => "PMRelinkPhoto";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var registry = RhinoPhotoMatchPlugin.Instance.Registry;

            if (registry.Pairs.Count == 0)
            {
                RhinoApp.WriteLine("PMRelinkPhoto: no photo planes in this document.");
                return Result.Nothing;
            }

            // 1. Pick which plane to relink
            var go = new GetOption();
            go.SetCommandPrompt("Select photo plane to relink");

            foreach (var pair in registry.Pairs)
                go.AddOption(pair.Name.Replace(" ", "_"));

            go.Get();
            if (go.CommandResult() != Result.Success)
                return go.CommandResult();

            int idx = go.Option().Index - 1;
            if (idx < 0 || idx >= registry.Pairs.Count)
                return Result.Failure;

            var target = registry.Pairs[idx];

            // 2. Pick the new image file
            using var dialog = new OpenFileDialog
            {
                Title = $"Select new image for \"{target.Name}\"",
                Filters =
                {
                    new FileFilter("Image files", ".jpg", ".jpeg", ".png"),
                    new FileFilter("All files", ".*")
                },
                CurrentFilterIndex = 0
            };

            if (dialog.ShowDialog(null) != DialogResult.Ok)
                return Result.Cancel;

            string newPath = dialog.FileName;
            string ext = Path.GetExtension(newPath).ToLowerInvariant();

            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png")
            {
                RhinoApp.WriteLine($"PMRelinkPhoto: unsupported format \"{ext}\". Only JPG and PNG are supported.");
                return Result.Failure;
            }

            // 3. Update material and refresh
            registry.RelinkPhoto(doc, target, newPath, RhinoPhotoMatchPlugin.Instance.Conduit);
            return Result.Success;
        }
    }
}
