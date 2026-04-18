using Rhino;
using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;
using RhinoPhotoMatch.Core;

namespace RhinoPhotoMatch.Commands
{
    /// <summary>
    /// PMScalePhotoPlane — adjusts a photo plane's distance from the camera and its scale factor.
    /// Distance moves the plane closer/farther in the scene (affects depth ordering).
    /// Scale zooms the plane in/out relative to the camera FOV.
    /// </summary>
    public class ScalePhotoPlaneCommand : Rhino.Commands.Command
    {
        public override string EnglishName => "PMScalePhotoPlane";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var registry = RhinoPhotoMatchPlugin.Instance.Registry;

            if (registry.Pairs.Count == 0)
            {
                RhinoApp.WriteLine("PMScalePhotoPlane: no photo planes in this document.");
                return Result.Nothing;
            }

            // 1. Pick plane
            var go = new GetOption();
            go.SetCommandPrompt("Select photo plane to scale");
            foreach (var pair in registry.Pairs)
                go.AddOption(pair.Name.Replace(" ", "_"));

            go.Get();
            if (go.CommandResult() != Result.Success)
                return go.CommandResult();

            int idx = go.Option().Index - 1;
            if (idx < 0 || idx >= registry.Pairs.Count)
                return Result.Failure;

            var target = registry.Pairs[idx];

            // 2. Adjust Distance
            double distance = target.Distance;
            RhinoApp.WriteLine($"Current distance: {distance:F3}  scale: {target.Scale:F3}");

            if (RhinoGet.GetNumber($"Distance from camera <{distance:F3}>", true, ref distance, 0.01, 1000) == Result.Success)
                target.Distance = distance;

            // 3. Adjust Scale (1.0 = fills FOV)
            double scale = target.Scale;
            if (RhinoGet.GetNumber($"Scale factor <{scale:F3}>  (1.0 = fill FOV)", true, ref scale, 0.01, 10) == Result.Success)
                target.Scale = scale;

            doc.Views.Redraw();
            return Result.Success;
        }
    }
}
