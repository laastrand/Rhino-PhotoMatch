using Rhino;
using Rhino.Commands;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.Input.Custom;
using RhinoPhotoMatch.Core;
using System.Collections.Generic;
using System.Drawing;

namespace RhinoPhotoMatch.Commands
{
    /// <summary>
    /// PMSetReferencePoints — interactively collects 3D model point ↔ 2D photo pixel
    /// correspondences for a chosen photo plane pair.
    ///
    /// Workflow per point:
    ///   1. Pick a 3D point on the model (any viewport).
    ///   2. Activate the photo viewport and click the matching spot on the photo.
    ///      The click is constrained to the photo plane so the 3D hit is
    ///      back-projected to UV / pixel coordinates automatically.
    ///   3. Repeat; press Enter (no point) to finish.
    /// </summary>
    public class SetReferencePointsCommand : Rhino.Commands.Command
    {
        public override string EnglishName => "PMSetReferencePoints";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var registry = RhinoPhotoMatchPlugin.Instance.Registry;

            if (registry.Pairs.Count == 0)
            {
                RhinoApp.WriteLine("PMSetReferencePoints: no photo planes in the registry.");
                return Result.Failure;
            }

            // Pick which pair to work with
            var pair = PickPair(registry);
            if (pair == null) return Result.Cancel;

            var linkedVp = PicturePlaneManager.FindViewport(doc, pair.ActiveViewportId);
            if (linkedVp == null)
            {
                RhinoApp.WriteLine("PMSetReferencePoints: linked viewport is no longer open.");
                return Result.Failure;
            }

            // Find the RhinoView for the linked viewport (needed to activate it)
            RhinoView? linkedView = null;
            foreach (var v in doc.Views)
                if (v.ActiveViewport.Id == pair.ActiveViewportId) { linkedView = v; break; }

            RhinoApp.WriteLine($"Collecting reference points for \"{pair.Name}\".");
            RhinoApp.WriteLine("Step 1: pick a 3D point on the model.  Step 2: click the matching spot on the photo.  Press Enter to finish.");

            // Draw conduit for markers
            var markerConduit = new ReferenceMarkerConduit(pair.ReferencePairs);
            markerConduit.Enabled = true;

            try
            {
                while (true)
                {
                    // ---- Step 1: 3D world point ----
                    var gp3d = new GetPoint();
                    gp3d.SetCommandPrompt($"Pick 3D point #{pair.ReferencePairs.Count + 1} on model (Enter to finish)");
                    gp3d.AcceptNothing(true);
                    gp3d.Get();

                    if (gp3d.CommandResult() == Result.Nothing) break;
                    if (gp3d.CommandResult() != Result.Success) break;

                    var worldPt = gp3d.Point();

                    // ---- Step 2: 2D photo point (click in linked viewport) ----
                    // Activate the linked viewport so the user sees the photo
                    if (linkedView != null)
                        doc.Views.ActiveView = linkedView;

                    // Use the baked frame so UV back-projection is stable regardless
                    // of camera movement between picks.
                    if (!pair.FrameBaked)
                    {
                        RhinoApp.WriteLine("PMSetReferencePoints: plane frame not baked — recreate the photo plane.");
                        break;
                    }
                    var center   = pair.PlaneCenter;
                    var camRight = pair.PlaneRight;
                    var camUp    = pair.PlaneUp;
                    var planeW   = pair.PlaneWorldW;
                    var planeH   = pair.PlaneWorldH;

                    var photoPlane = new Plane(center, camRight, camUp);

                    var gp2d = new GetPoint();
                    gp2d.SetCommandPrompt($"Click matching location on photo for point #{pair.ReferencePairs.Count + 1}");
                    gp2d.Constrain(photoPlane, false);
                    gp2d.Get();

                    if (gp2d.CommandResult() != Result.Success) break;

                    var hitPt = gp2d.Point();

                    // Back-project 3D hit → image UV → pixel coords
                    var local = hitPt - center;
                    double u = (Vector3d.Multiply(local, camRight) / (planeW / 2.0) + 1.0) / 2.0;
                    double v = (Vector3d.Multiply(local, camUp)    / (planeH / 2.0) + 1.0) / 2.0;

                    // Clamp to [0,1] in case the click landed slightly outside
                    u = System.Math.Max(0, System.Math.Min(1, u));
                    v = System.Math.Max(0, System.Math.Min(1, v));

                    // Image pixel coords: origin top-left, Y down (OpenCV convention)
                    double pixelX = u * pair.PixelWidth;
                    double pixelY = (1.0 - v) * pair.PixelHeight;

                    pair.ReferencePairs.Add((worldPt, new Point2d(pixelX, pixelY)));

                    RhinoApp.WriteLine($"  Pair {pair.ReferencePairs.Count}: world ({worldPt.X:F3}, {worldPt.Y:F3}, {worldPt.Z:F3})  →  image ({pixelX:F1}, {pixelY:F1}) px");
                    doc.Views.Redraw();
                }
            }
            finally
            {
                markerConduit.Enabled = false;
                doc.Views.Redraw();
            }

            RhinoApp.WriteLine($"PMSetReferencePoints: {pair.ReferencePairs.Count} pair(s) stored for \"{pair.Name}\".");
            return pair.ReferencePairs.Count > 0 ? Result.Success : Result.Cancel;
        }

        // ---- Helpers ----

        private static PhotoPlanePair? PickPair(PhotoPlaneRegistry registry)
        {
            if (registry.Pairs.Count == 1) return registry.Pairs[0];

            var names = new System.Collections.Generic.List<string>();
            foreach (var p in registry.Pairs) names.Add(p.Name);

            string pick = names[0];
            var res = Rhino.Input.RhinoGet.GetString(
                $"Photo plane to calibrate ({string.Join(", ", names)})", false, ref pick);
            if (res != Result.Success) return null;

            return registry.FindByName(pick);
        }
    }

    // ------------------------------------------------------------------ //
    //  Internal conduit — draws numbered spheres at collected world points
    // ------------------------------------------------------------------ //

    internal sealed class ReferenceMarkerConduit : DisplayConduit
    {
        private readonly IReadOnlyList<(Point3d WorldPoint, Point2d ImagePoint)> _pairs;

        public ReferenceMarkerConduit(
            IReadOnlyList<(Point3d WorldPoint, Point2d ImagePoint)> pairs)
        {
            _pairs = pairs;
        }

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            for (int i = 0; i < _pairs.Count; i++)
            {
                var pt = _pairs[i].WorldPoint;
                e.Display.DrawPoint(pt, PointStyle.Circle, 8, Color.Yellow);
                e.Display.Draw2dText($"{i + 1}", Color.Yellow, pt, false, 14);
            }
        }
    }
}
