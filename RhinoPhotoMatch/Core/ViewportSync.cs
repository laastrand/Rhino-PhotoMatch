using System;
using Rhino;
using Rhino.Display;

namespace RhinoPhotoMatch.Core
{
    /// <summary>
    /// Applies a <see cref="CalibrationResult"/> to a Rhino viewport,
    /// setting camera location, direction, and FOV.
    /// </summary>
    public static class ViewportSync
    {
        /// <summary>
        /// Applies the solved camera pose to the viewport linked to <paramref name="pair"/>.
        /// Returns false if the viewport is no longer open.
        /// </summary>
        public static bool Apply(RhinoDoc doc, PhotoPlanePair pair, CalibrationResult result)
        {
            // Find the owning RhinoView so we can modify and redraw it
            RhinoView? targetView = null;
            foreach (var view in doc.Views)
            {
                if (view.ActiveViewport.Id == pair.ActiveViewportId)
                {
                    targetView = view;
                    break;
                }
            }
            if (targetView == null) return false;

            var vp = targetView.ActiveViewport;

            // Switch to perspective if not already
            if (!vp.IsPerspectiveProjection)
                vp.ChangeToPerspectiveProjection(true, 50.0);

            // FOV → 35mm equivalent lens length (36mm film width standard)
            double lensLength = 18.0 / Math.Tan(result.FovRadians / 2.0);
            lensLength = Math.Max(1.0, Math.Min(lensLength, 2000.0)); // clamp to sane range

            vp.SetCameraLocation(result.CameraLocation, false);
            vp.SetCameraDirection(result.CameraDirection, true);
            vp.Camera35mmLensLength = lensLength;
            vp.CameraUp = result.CameraUp;

            RhinoApp.WriteLine("=== ViewportSync applied ===");
            RhinoApp.WriteLine($"  Location set:  {result.CameraLocation}");
            RhinoApp.WriteLine($"  Location got:  {vp.CameraLocation}");
            RhinoApp.WriteLine($"  Dir set:       {result.CameraDirection}");
            RhinoApp.WriteLine($"  Dir got:       {vp.CameraDirection}");
            RhinoApp.WriteLine($"  Up set:        {result.CameraUp}");
            RhinoApp.WriteLine($"  Up got:        {vp.CameraUp}");
            RhinoApp.WriteLine($"  LensLength:    {lensLength:F1}mm");
            RhinoApp.WriteLine($"  Reproj error:  {result.ReprojectionError:F2}px");

            targetView.Redraw();
            doc.Views.Redraw();
            return true;
        }

        /// <summary>
        /// Converts horizontal FOV in degrees to a 35mm-equivalent lens length in mm.
        /// </summary>
        public static double FovToLensLength(double horizontalFovDegrees)
        {
            double fovRad = horizontalFovDegrees * Math.PI / 180.0;
            return 18.0 / Math.Tan(fovRad / 2.0);
        }

        /// <summary>
        /// Converts a 35mm-equivalent lens length in mm to horizontal FOV in degrees.
        /// </summary>
        public static double LensLengthToFov(double lensLengthMm)
        {
            return 2.0 * Math.Atan(18.0 / lensLengthMm) * (180.0 / Math.PI);
        }
    }
}
