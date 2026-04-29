using System;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using RhinoPhotoMatch.Core;

namespace RhinoPhotoMatch.Commands
{
    /// <summary>
    /// PMSolveVanishingPoints — computes vanishing points from the lines added with
    /// PMAddVanishingLine, derives focal length and camera roll, then applies them
    /// to the linked viewport.
    ///
    /// What is applied:
    ///   • Focal length → 35mm lens length on the viewport
    ///   • Roll correction → rotates the camera around its look axis so the horizon
    ///     appears horizontal
    ///
    /// What is reported but NOT applied automatically:
    ///   • Camera tilt (pitch) — how many degrees the camera is looking up or down
    ///     from horizontal.  The user should adjust this manually if needed.
    /// </summary>
    public class SolveVanishingPointsCommand : Command
    {
        public override string EnglishName => "PMSolveVanishingPoints";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var registry = RhinoPhotoMatchPlugin.Instance.GetRegistry(doc);
            if (registry.Pairs.Count == 0)
            {
                RhinoApp.WriteLine("PMSolveVanishingPoints: no photo planes in the registry.");
                return Result.Failure;
            }

            var pair = PickPair(registry);
            if (pair == null) return Result.Cancel;

            if (pair.VanishingLines.Count < 4)
            {
                RhinoApp.WriteLine("PMSolveVanishingPoints: add at least 2 X-axis lines and 2 Y-axis lines first.");
                return Result.Failure;
            }

            // ---- Solve ----
            var result = VanishingPointSolver.Solve(pair.VanishingLines, pair.PixelWidth, pair.PixelHeight);
            if (result == null) return Result.Failure;

            pair.LastVanishingResult = result;

            // ---- Report ----
            RhinoApp.WriteLine("PMSolveVanishingPoints results:");
            RhinoApp.WriteLine($"  VpX              : ({result.VpX.X:F0}, {result.VpX.Y:F0}) px from centre");
            RhinoApp.WriteLine($"  VpY              : ({result.VpY.X:F0}, {result.VpY.Y:F0}) px from centre");
            if (result.VpZ.HasValue)
                RhinoApp.WriteLine($"  VpZ              : ({result.VpZ.Value.X:F0}, {result.VpZ.Value.Y:F0}) px from centre");
            RhinoApp.WriteLine($"  Focal length     : {result.FocalLengthPixels:F1} px");
            RhinoApp.WriteLine($"  Lens length      : {result.LensLengthMm:F1} mm  (35mm equiv)");
            RhinoApp.WriteLine($"  Horizon offset   : {result.HorizonY:F1} px  ({(result.HorizonY > 0 ? "above" : "below")} centre)");
            RhinoApp.WriteLine($"  Camera tilt      : {result.CameraTiltDegrees:F1}°  ({(result.CameraTiltDegrees > 0 ? "looking down" : "looking up")})");
            RhinoApp.WriteLine($"  Camera roll      : {result.HorizonAngle * 180.0 / Math.PI:F1}°  (will be corrected)");

            // ---- Apply to viewport ----
            var vp = PicturePlaneManager.FindViewport(doc, pair.ActiveViewportId);
            if (vp == null)
            {
                RhinoApp.WriteLine("PMSolveVanishingPoints: linked viewport is no longer open — results not applied.");
                doc.Views.Redraw();
                return Result.Success;
            }

            if (!vp.IsPerspectiveProjection)
                vp.ChangeToPerspectiveProjection(true, 50.0);

            // 1. Apply focal length as 35mm lens length
            vp.Camera35mmLensLength = result.LensLengthMm;

            // 2. Roll correction — rotate the camera around its look axis so the horizon
            //    appears horizontal.  The horizon angle is the angle of the V1→V2 vector
            //    from horizontal.  Since the horizon is a line (not a vector), normalise to
            //    the range (−90°, +90°] so we always take the smaller rotation.
            double h = result.HorizonAngle;
            if (h >  Math.PI / 2) h -= Math.PI;
            if (h < -Math.PI / 2) h += Math.PI;

            // Rotating the camera CCW (positive angle around the look-into-scene axis)
            // makes image content appear CW from the user's view — which corrects a
            // horizon that is tilted CCW (positive horizonAngle).
            // Therefore: rollCorrection = +h  (same sign as horizon angle).
            double rollCorrection = h;
            if (Math.Abs(rollCorrection) > 1e-4)
            {
                var lookDir = vp.CameraDirection;
                lookDir.Unitize();
                vp.Rotate(rollCorrection, lookDir, vp.CameraLocation);
            }

            // Redraw
            foreach (var view in doc.Views)
                if (view.ActiveViewport.Id == pair.ActiveViewportId)
                { view.Redraw(); break; }
            doc.Views.Redraw();

            RhinoApp.WriteLine("PMSolveVanishingPoints: lens length and roll applied.");
            if (Math.Abs(result.CameraTiltDegrees) > 1.0)
                RhinoApp.WriteLine($"  Note: camera tilt ({result.CameraTiltDegrees:F1}°) was NOT auto-applied. " +
                                   "Adjust manually by rotating the viewport if needed.");

            return Result.Success;
        }

        private static PhotoPlanePair? PickPair(PhotoPlaneRegistry registry)
        {
            if (registry.Pairs.Count == 1) return registry.Pairs[0];

            var names = new System.Collections.Generic.List<string>();
            foreach (var p in registry.Pairs) names.Add(p.Name);
            string pick = names[0];
            var res = Rhino.Input.RhinoGet.GetString(
                $"Photo plane ({string.Join(", ", names)})", false, ref pick);
            return res == Result.Success ? registry.FindByName(pick) : null;
        }
    }
}
