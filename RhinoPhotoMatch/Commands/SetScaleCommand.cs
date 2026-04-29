using System;
using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using Rhino.Input.Custom;
using RhinoPhotoMatch.Core;

namespace RhinoPhotoMatch.Commands
{
    /// <summary>
    /// PMSetScale — pins the camera position in 3D space by matching two model points
    /// to their corresponding locations on the photo plane.
    ///
    /// Prerequisites: the camera orientation and FOV must already be calibrated
    /// (via PMSolveVanishingPoints or PMCalibrate) before running this command.
    ///
    /// Math: for a 3D model point P that should appear at image-centre pixel (px, py),
    /// the camera C must lie on the ray  C = P − z·rayDir  where
    ///   rayDir = (px/f)·right + (py/f)·up + lookDir
    /// Two such ray constraints form a 3×2 least-squares system that uniquely
    /// solves for the two depths z1, z2 and hence the camera position.
    /// </summary>
    public class SetScaleCommand : Command
    {
        public override string EnglishName => "PMSetScale";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var registry = RhinoPhotoMatchPlugin.Instance.GetRegistry(doc);
            if (registry.Pairs.Count == 0)
            {
                RhinoApp.WriteLine("PMSetScale: no photo planes in the registry.");
                return Result.Failure;
            }

            var pair = PickPair(registry);
            if (pair == null) return Result.Cancel;

            var linkedVp = PicturePlaneManager.FindViewport(doc, pair.ActiveViewportId);
            if (linkedVp == null)
            {
                RhinoApp.WriteLine("PMSetScale: linked viewport is no longer open.");
                return Result.Failure;
            }

            Rhino.Display.RhinoView? linkedView = null;
            foreach (var v in doc.Views)
                if (v.ActiveViewport.Id == pair.ActiveViewportId) { linkedView = v; break; }

            // ---- Camera frame from current viewport ----
            var lookDir = linkedVp.CameraDirection; lookDir.Unitize();
            var camUp   = linkedVp.CameraUp;        camUp.Unitize();
            var right   = Vector3d.CrossProduct(lookDir, camUp); right.Unitize();

            // Focal length in pixels: f_px = lensLengthMm * imageWidth / 36
            double f_px = linkedVp.Camera35mmLensLength * pair.PixelWidth / 36.0;
            if (f_px < 1)
            {
                RhinoApp.WriteLine("PMSetScale: focal length is invalid. Run PMSolveVanishingPoints first.");
                return Result.Failure;
            }

            RhinoApp.WriteLine("PMSetScale: pick 2 model points and their matching photo locations.");
            RhinoApp.WriteLine("  The camera orientation and FOV must already be calibrated.");

            Point3d[] modelPts = new Point3d[2];
            Point2d[] imagePts = new Point2d[2];   // image-centre coords (Y up)

            for (int i = 0; i < 2; i++)
            {
                // ---- Step A: pick 3D model point (any viewport) ----
                var gpModel = new GetPoint();
                gpModel.SetCommandPrompt($"Pick model point {i + 1} of 2  (snap to model geometry)");
                gpModel.Get();
                if (gpModel.CommandResult() != Result.Success) return Result.Cancel;
                modelPts[i] = gpModel.Point();

                // ---- Step B: pick matching point on photo plane (linked viewport) ----
                if (linkedView != null) doc.Views.ActiveView = linkedView;

                if (!PicturePlaneManager.ComputePlaneFrame(linkedVp, pair,
                        out var center, out var planeRight, out var planeUp,
                        out double planeW, out double planeH))
                {
                    RhinoApp.WriteLine("PMSetScale: could not compute photo plane frame.");
                    return Result.Failure;
                }

                var photoPlane = new Plane(center, planeRight, planeUp);
                var gpPhoto = new GetPoint();
                gpPhoto.SetCommandPrompt($"Click matching location on photo for point {i + 1}");
                gpPhoto.Constrain(photoPlane, false);
                gpPhoto.Get();
                if (gpPhoto.CommandResult() != Result.Success) return Result.Cancel;

                // Convert 3D hit → image-centre pixel coords
                var local = gpPhoto.Point() - center;
                double icX = Vector3d.Multiply(local, planeRight) * pair.PixelWidth  / planeW;
                double icY = Vector3d.Multiply(local, planeUp)    * pair.PixelHeight / planeH;
                imagePts[i] = new Point2d(icX, icY);

                RhinoApp.WriteLine($"  Pair {i + 1}: model ({modelPts[i].X:F2}, {modelPts[i].Y:F2}, {modelPts[i].Z:F2})" +
                                   $"  →  image ({icX:F1}, {icY:F1}) px from centre");
            }

            // ---- Solve for camera position ----
            // For each correspondence i:  modelPts[i] - C = zi * rayi
            // where rayi = (icX/f)·right + (icY/f)·up + lookDir  (un-normalized back-projection ray)
            //
            // Two correspondences: dP = P0 - P1 = z0·ray0 - z1·ray1
            // Least-squares normal equations for [z0, z1]:
            //   A = [ray0 | -ray1]  (3×2),  A^T A [z0; z1] = A^T dP

            var ray0 = (imagePts[0].X / f_px) * right + (imagePts[0].Y / f_px) * camUp + lookDir;
            var ray1 = (imagePts[1].X / f_px) * right + (imagePts[1].Y / f_px) * camUp + lookDir;
            var dP   = (Vector3d)(modelPts[0] - modelPts[1]);

            double r00 =  ray0 * ray0;
            double r01 = -ray0 * ray1;   // = r10
            double r11 =  ray1 * ray1;

            double b0 =  ray0 * dP;
            double b1 = -ray1 * dP;

            double det = r00 * r11 - r01 * r01;
            if (Math.Abs(det) < 1e-10)
            {
                RhinoApp.WriteLine("PMSetScale: the two rays are nearly parallel — pick points further apart.");
                return Result.Failure;
            }

            double z0 = (b0 * r11 - b1 * r01) / det;
            double z1 = (r00 * b1 - r01 * b0) / det;

            if (z0 <= 0 || z1 <= 0)
            {
                RhinoApp.WriteLine($"PMSetScale: one or both model points are behind the camera (z0={z0:F2}, z1={z1:F2})." +
                                   "  Check that the model points are in front of the camera.");
                return Result.Failure;
            }

            // Average the two independent estimates for robustness
            var C0 = modelPts[0] - z0 * ray0;
            var C1 = modelPts[1] - z1 * ray1;
            var camPos = new Point3d((C0.X + C1.X) / 2, (C0.Y + C1.Y) / 2, (C0.Z + C1.Z) / 2);

            double residual = C0.DistanceTo(C1);
            RhinoApp.WriteLine($"  Solved camera position : ({camPos.X:F3}, {camPos.Y:F3}, {camPos.Z:F3})");
            RhinoApp.WriteLine($"  Residual (estimate gap): {residual:F3} model units" +
                               (residual > 1.0 ? "  ← large — check point picks" : ""));

            // ---- Apply ----
            var camTarget = camPos + lookDir * 1000.0;
            linkedVp.SetCameraLocation(camPos, false);
            linkedVp.SetCameraTarget(camTarget, false);

            // Restore roll — SetCameraTarget resets the up vector; re-apply it
            var currentUp = linkedVp.CameraUp;
            double sinA = Vector3d.CrossProduct(currentUp, camUp) * lookDir;
            double cosA = currentUp * camUp;
            double roll = Math.Atan2(sinA, cosA);
            if (Math.Abs(roll) > 1e-4)
                linkedVp.Rotate(roll, lookDir, camPos);

            if (linkedView != null) linkedView.Redraw();
            doc.Views.Redraw();

            RhinoApp.WriteLine("PMSetScale: camera position updated.");
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
