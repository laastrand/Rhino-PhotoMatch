using System;
using Rhino;
using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;
using RhinoPhotoMatch.Core;

namespace RhinoPhotoMatch.Commands
{
    /// <summary>
    /// PMCalibrate — solves the camera pose for a photo plane using the stored
    /// reference point pairs, then applies the result to the linked viewport.
    ///
    /// Requires at least 4 reference pairs (set with PMSetReferencePoints).
    /// Prompts the user for the camera's horizontal FOV if not already known.
    /// </summary>
    public class CalibrateCommand : Rhino.Commands.Command
    {
        public override string EnglishName => "PMCalibrate";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var registry = RhinoPhotoMatchPlugin.Instance.Registry;

            if (registry.Pairs.Count == 0)
            {
                RhinoApp.WriteLine("PMCalibrate: no photo planes in the registry.");
                return Result.Failure;
            }

            // Pick pair
            var pair = PickPair(registry);
            if (pair == null) return Result.Cancel;

            if (pair.ReferencePairs.Count < 4)
            {
                RhinoApp.WriteLine($"PMCalibrate: \"{pair.Name}\" has only {pair.ReferencePairs.Count} reference pair(s). Need at least 4. Run PMSetReferencePoints first.");
                return Result.Failure;
            }

            // Prompt for lens — user can enter FOV (degrees) or focal length (mm, 35mm equiv.)
            var linkedVp = PicturePlaneManager.FindViewport(doc, pair.ActiveViewportId);
            double defaultFov    = linkedVp != null ? ViewportSync.LensLengthToFov(linkedVp.Camera35mmLensLength) : 60.0;
            double defaultFocalMm = ViewportSync.FovToLensLength(defaultFov);

            // Use GetNumber with an option to switch input mode
            bool useFocalLength = false;
            double fov = defaultFov;

            while (true)
            {
                var gn = new GetNumber();
                if (!useFocalLength)
                {
                    gn.SetCommandPrompt($"Camera horizontal FOV degrees <{defaultFov:F1}>");
                    gn.SetDefaultNumber(defaultFov);
                    gn.SetLowerLimit(1.0, false);
                    gn.SetUpperLimit(179.0, false);
                    gn.AddOption("FocalLength");
                }
                else
                {
                    gn.SetCommandPrompt($"Camera focal length mm (35mm equiv) <{defaultFocalMm:F1}>");
                    gn.SetDefaultNumber(defaultFocalMm);
                    gn.SetLowerLimit(1.0, false);
                    gn.SetUpperLimit(2000.0, false);
                    gn.AddOption("FOV");
                }

                var getResult = gn.Get();

                if (getResult == GetResult.Cancel) return Result.Cancel;

                if (getResult == GetResult.Option)
                {
                    // Toggle mode
                    useFocalLength = !useFocalLength;
                    continue;
                }

                // Number or Nothing (accept default)
                double val = (getResult == GetResult.Number) ? gn.Number() : (useFocalLength ? defaultFocalMm : defaultFov);

                fov = useFocalLength
                    ? ViewportSync.LensLengthToFov(val)
                    : val;
                break;
            }

            // Extract world and image point lists
            var worldPts = new System.Collections.Generic.List<Rhino.Geometry.Point3d>();
            var imagePts = new System.Collections.Generic.List<Rhino.Geometry.Point2d>();
            foreach (var (wp, ip) in pair.ReferencePairs)
            {
                worldPts.Add(wp);
                imagePts.Add(ip);
            }

            RhinoApp.WriteLine($"PMCalibrate: solving with {worldPts.Count} pairs, FOV = {fov:F1}° ({ViewportSync.FovToLensLength(fov):F1} mm)…");

            CalibrationResult? result;
            try
            {
                result = CalibrationSolver.SolvePnP(
                    worldPts, imagePts,
                    pair.PixelWidth, pair.PixelHeight,
                    fov);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"PMCalibrate: EXCEPTION — {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    RhinoApp.WriteLine($"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                return Result.Failure;
            }

            if (result == null)
            {
                RhinoApp.WriteLine("PMCalibrate: solver failed. Check that your reference points are accurate and not coplanar.");
                return Result.Failure;
            }

            RhinoApp.WriteLine($"PMCalibrate: reprojection error = {result.ReprojectionError:F2} px");
            if (result.ReprojectionError > 10.0)
                RhinoApp.WriteLine("  WARNING: high reprojection error — check that reference points are accurate and not all coplanar.");
            RhinoApp.WriteLine($"  Camera location : ({result.CameraLocation.X:F3}, {result.CameraLocation.Y:F3}, {result.CameraLocation.Z:F3})");
            RhinoApp.WriteLine($"  Camera direction: ({result.CameraDirection.X:F3}, {result.CameraDirection.Y:F3}, {result.CameraDirection.Z:F3})");

            if (!ViewportSync.Apply(doc, pair, result))
            {
                RhinoApp.WriteLine("PMCalibrate: could not apply result — linked viewport no longer open.");
                return Result.Failure;
            }

            doc.Views.Redraw();
            RhinoApp.WriteLine("PMCalibrate: camera updated. Photo plane is now aligned to the calibrated camera.");
            return Result.Success;
        }

        private static PhotoPlanePair? PickPair(PhotoPlaneRegistry registry)
        {
            if (registry.Pairs.Count == 1) return registry.Pairs[0];

            var names = new System.Collections.Generic.List<string>();
            foreach (var p in registry.Pairs) names.Add(p.Name);

            string pick = names[0];
            var res = RhinoGet.GetString(
                $"Photo plane to calibrate ({string.Join(", ", names)})", false, ref pick);
            if (res != Result.Success) return null;

            return registry.FindByName(pick);
        }
    }
}
