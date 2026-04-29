using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using RhinoPhotoMatch.Core;

namespace RhinoPhotoMatch.Commands
{
    /// <summary>
    /// PMExtractPhotoPlane — bakes the currently-virtual photo plane into a real
    /// Rhino mesh object with the photo applied as a document material.
    /// The mesh is positioned at the linked camera's current world-space location.
    /// </summary>
    public class ExtractPhotoPlaneCommand : Rhino.Commands.Command
    {
        public override string EnglishName => "PMExtractPhotoPlane";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var plugin   = RhinoPhotoMatchPlugin.Instance;
            var registry = plugin.GetRegistry(doc);

            if (registry.Pairs.Count == 0)
            {
                RhinoApp.WriteLine("PMExtractPhotoPlane: no photo planes in the registry.");
                return Result.Failure;
            }

            // Let the user pick which plane to extract
            var names = new System.Collections.Generic.List<string>();
            foreach (var p in registry.Pairs) names.Add(p.Name);

            string pick = names[0];
            if (names.Count > 1)
            {
                var result = Rhino.Input.RhinoGet.GetString(
                    $"Photo plane to extract ({string.Join(", ", names)})",
                    false, ref pick);
                if (result != Result.Success) return result;
            }

            var pair = registry.FindByName(pick);
            if (pair == null)
            {
                RhinoApp.WriteLine($"PMExtractPhotoPlane: no photo plane named \"{pick}\".");
                return Result.Failure;
            }

            // Find the linked viewport
            var vp = PicturePlaneManager.FindViewport(doc, pair.ActiveViewportId);
            if (vp == null)
            {
                RhinoApp.WriteLine("PMExtractPhotoPlane: the linked viewport is no longer open.");
                return Result.Failure;
            }

            // Build the mesh at the camera's current world-space position
            var mesh = PicturePlaneManager.ComputePlaneMesh(vp, pair);
            if (mesh == null)
            {
                RhinoApp.WriteLine("PMExtractPhotoPlane: could not compute plane mesh (degenerate camera?).");
                return Result.Failure;
            }

            // Add photo material to the document
            int matIndex = PicturePlaneManager.AddPhotoMaterial(doc, pair.ImagePath, pair.Name + "_mat");

            // Set up object attributes
            var attr = new ObjectAttributes
            {
                Name            = pair.Name + "_extracted",
                MaterialIndex   = matIndex,
                MaterialSource  = ObjectMaterialSource.MaterialFromObject
            };

            // Add mesh to document
            var objId = doc.Objects.AddMesh(mesh, attr);
            if (objId == System.Guid.Empty)
            {
                RhinoApp.WriteLine("PMExtractPhotoPlane: failed to add mesh to document.");
                return Result.Failure;
            }

            doc.Views.Redraw();
            RhinoApp.WriteLine($"PMExtractPhotoPlane: extracted \"{pair.Name}\" → mesh object with material.");
            return Result.Success;
        }
    }
}
