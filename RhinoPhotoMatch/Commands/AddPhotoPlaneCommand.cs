using System.IO;
using Eto.Forms;
using Rhino;
using Rhino.Commands;
using Rhino.Input;
using RhinoPhotoMatch.Core;

namespace RhinoPhotoMatch.Commands
{
    /// <summary>
    /// PMAddPhotoPlane — picks a JPG or PNG, creates a flat mesh photo plane and a linked
    /// named camera. Can be run multiple times to add multiple planes to the same document.
    /// </summary>
    public class AddPhotoPlaneCommand : Rhino.Commands.Command
    {
        public override string EnglishName => "PMAddPhotoPlane";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode) => RunShared(doc);

        /// <summary>Shared logic used by both PMAddPhotoPlane and PMImportPhoto.</summary>
        internal static Result RunShared(RhinoDoc doc)
        {
            // 1. File picker
            using var dialog = new OpenFileDialog
            {
                Title = "Select Photo (JPG or PNG)",
                Filters =
                {
                    new FileFilter("Image files", ".jpg", ".jpeg", ".png"),
                    new FileFilter("All files", ".*")
                },
                CurrentFilterIndex = 0
            };

            if (dialog.ShowDialog(null) != DialogResult.Ok)
                return Result.Cancel;

            string imagePath = dialog.FileName;
            string ext = Path.GetExtension(imagePath).ToLowerInvariant();

            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png")
            {
                RhinoApp.WriteLine($"PMAddPhotoPlane: unsupported format \"{ext}\". Only JPG and PNG are supported.");
                return Result.Failure;
            }

            // 2. Auto-generate a name; let user optionally rename it
            var registry = RhinoPhotoMatchPlugin.Instance.GetRegistry(doc);
            string autoName = registry.GenerateAutoName();

            string name = autoName;
            var nameResult = RhinoGet.GetString($"Name for this photo plane <{autoName}>", true, ref name);
            if (nameResult == Result.Cancel)
                return Result.Cancel;
            // Result.Nothing means the user pressed Enter — keep the default
            if (nameResult == Result.Nothing || string.IsNullOrWhiteSpace(name))
                name = autoName;

            // Ensure the name is unique
            if (registry.FindByName(name) != null)
            {
                RhinoApp.WriteLine($"PMAddPhotoPlane: a photo plane named \"{name}\" already exists.");
                return Result.Failure;
            }

            // 3. Create pair via registry
            var pair = registry.CreatePair(doc, imagePath, name);
            if (pair == null)
                return Result.Failure;

            // Bake the plane frame from the live camera so calibration picks
            // use stable image coordinates regardless of later camera movement.
            var vp = PicturePlaneManager.FindViewport(doc, pair.ActiveViewportId);
            if (vp != null)
                PicturePlaneManager.BakePlaneFrame(vp, pair);

            RhinoApp.WriteLine($"Registry now has {registry.Pairs.Count} photo plane(s). Redrawing...");
            doc.Views.Redraw();
            return Result.Success;
        }
    }
}
