using Rhino;
using Rhino.Commands;
using Rhino.Input;
using RhinoPhotoMatch.Core;

namespace RhinoPhotoMatch.Commands
{
    public class RotatePhotoPlaneCommand : Command
    {
        public override string EnglishName => "PMRotatePhotoPlane";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var registry = RhinoPhotoMatchPlugin.Instance.GetRegistry(doc);

            if (registry.Pairs.Count == 0)
            {
                RhinoApp.WriteLine("PMRotatePhotoPlane: no photo planes in registry.");
                return Result.Failure;
            }

            var pair = PickPair(registry);
            if (pair == null) return Result.Cancel;

            string angle = "90";
            var res = RhinoGet.GetString("Rotation (90, 180, 270)", false, ref angle);
            if (res != Result.Success && res != Result.Nothing) return Result.Cancel;

            if (!int.TryParse(angle.Trim(), out int degrees) ||
                (degrees != 90 && degrees != 180 && degrees != 270))
            {
                RhinoApp.WriteLine($"PMRotatePhotoPlane: invalid rotation \"{angle}\". Enter 90, 180, or 270.");
                return Result.Failure;
            }

            PicturePlaneManager.RotatePlaneImage(doc, pair, degrees);
            RhinoApp.WriteLine($"Rotated \"{pair.Name}\" by {degrees}°. Total rotation: {pair.RotationDegrees}°");
            return Result.Success;
        }

        private static PhotoPlanePair? PickPair(PhotoPlaneRegistry registry)
        {
            if (registry.Pairs.Count == 1) return registry.Pairs[0];

            var names = new System.Collections.Generic.List<string>();
            foreach (var p in registry.Pairs) names.Add(p.Name);

            string pick = names[0];
            var res = RhinoGet.GetString(
                $"Photo plane to rotate ({string.Join(", ", names)})", false, ref pick);
            if (res != Result.Success) return null;

            return registry.FindByName(pick);
        }
    }
}
