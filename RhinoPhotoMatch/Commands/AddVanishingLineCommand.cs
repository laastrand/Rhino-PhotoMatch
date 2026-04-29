using Rhino;
using Rhino.Commands;
using Rhino.Geometry;
using Rhino.Input;
using Rhino.Input.Custom;
using RhinoPhotoMatch.Core;

namespace RhinoPhotoMatch.Commands
{
    /// <summary>
    /// PMAddVanishingLine — click two points along a real-world parallel edge on the photo
    /// and assign it to an axis group (X, Y or Z).  Run multiple times to build up lines.
    /// Use PMSolveVanishingPoints when done.
    /// </summary>
    public class AddVanishingLineCommand : Command
    {
        public override string EnglishName => "PMAddVanishingLine";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var registry = RhinoPhotoMatchPlugin.Instance.GetRegistry(doc);
            if (registry.Pairs.Count == 0)
            {
                RhinoApp.WriteLine("PMAddVanishingLine: no photo planes in the registry.");
                return Result.Failure;
            }

            var pair = PickPair(registry);
            if (pair == null) return Result.Cancel;

            var linkedVp = PicturePlaneManager.FindViewport(doc, pair.ActiveViewportId);
            if (linkedVp == null)
            {
                RhinoApp.WriteLine("PMAddVanishingLine: linked viewport is no longer open.");
                return Result.Failure;
            }

            // Find the RhinoView so we can activate it
            Rhino.Display.RhinoView? linkedView = null;
            foreach (var v in doc.Views)
                if (v.ActiveViewport.Id == pair.ActiveViewportId) { linkedView = v; break; }

            // ---- Choose axis ----
            var go = new GetOption();
            go.SetCommandPrompt("Vanishing line axis (lines parallel to this real-world direction)");
            int ixX = go.AddOption("X");
            int ixY = go.AddOption("Y");
            int ixZ = go.AddOption("Z");
            go.Get();
            if (go.CommandResult() != Result.Success) return Result.Cancel;

            VanishingAxis axis;
            int chosen = go.Option().Index;
            if      (chosen == ixX) axis = VanishingAxis.X;
            else if (chosen == ixY) axis = VanishingAxis.Y;
            else                    axis = VanishingAxis.Z;

            // ---- Pick lines in a loop ----
            if (linkedView != null) doc.Views.ActiveView = linkedView;

            RhinoApp.WriteLine($"Drawing {axis}-axis vanishing lines on \"{pair.Name}\".  Click two points per line.  Press Enter to finish.");

            int added = 0;
            while (true)
            {
                // Compute photo plane for picking constraint
                if (!PicturePlaneManager.ComputePlaneFrame(linkedVp, pair,
                        out var center, out var camRight, out var camUp,
                        out _, out _))
                {
                    RhinoApp.WriteLine("PMAddVanishingLine: could not compute photo plane.");
                    break;
                }
                var photoPlane = new Plane(center, camRight, camUp);

                // First point
                var gp1 = new GetPoint();
                gp1.SetCommandPrompt($"Point 1 of {axis}-axis line #{added + 1} (Enter to finish)");
                gp1.AcceptNothing(true);
                gp1.Constrain(photoPlane, false);
                gp1.Get();
                if (gp1.CommandResult() == Result.Nothing) break;
                if (gp1.CommandResult() != Result.Success) break;
                var hit1 = gp1.Point();

                // Second point
                var gp2 = new GetPoint();
                gp2.SetCommandPrompt($"Point 2 of {axis}-axis line #{added + 1}");
                gp2.Constrain(photoPlane, false);
                gp2.Get();
                if (gp2.CommandResult() != Result.Success) break;
                var hit2 = gp2.Point();

                // Convert 3D hits → pixel coordinates (same back-projection as SetReferencePoints)
                var local1 = hit1 - center;
                var local2 = hit2 - center;

                if (!PicturePlaneManager.ComputePlaneFrame(linkedVp, pair,
                        out center, out camRight, out camUp,
                        out double planeW, out double planeH))
                    break;

                double u1 = (Vector3d.Multiply(local1, camRight) / (planeW / 2.0) + 1.0) / 2.0;
                double v1 = (Vector3d.Multiply(local1, camUp)    / (planeH / 2.0) + 1.0) / 2.0;
                double u2 = (Vector3d.Multiply(local2, camRight) / (planeW / 2.0) + 1.0) / 2.0;
                double v2 = (Vector3d.Multiply(local2, camUp)    / (planeH / 2.0) + 1.0) / 2.0;

                // Clamp to [0,1]
                u1 = System.Math.Max(0, System.Math.Min(1, u1));
                v1 = System.Math.Max(0, System.Math.Min(1, v1));
                u2 = System.Math.Max(0, System.Math.Min(1, u2));
                v2 = System.Math.Max(0, System.Math.Min(1, v2));

                // UV → pixel (origin top-left, Y down)
                var pixA = new Point2d(u1 * pair.PixelWidth,  (1.0 - v1) * pair.PixelHeight);
                var pixB = new Point2d(u2 * pair.PixelWidth,  (1.0 - v2) * pair.PixelHeight);

                pair.VanishingLines.Add(new VanishingLine(pixA, pixB, axis));
                pair.LastVanishingResult = null;   // invalidate cached solve
                added++;

                RhinoApp.WriteLine($"  {axis}-axis line #{added} added.");
                doc.Views.Redraw();
            }

            RhinoApp.WriteLine($"PMAddVanishingLine: {added} line(s) added.  " +
                               $"Total {axis}-axis lines: {CountAxis(pair.VanishingLines, axis)}.");
            return added > 0 ? Result.Success : Result.Cancel;
        }

        private static int CountAxis(System.Collections.Generic.List<VanishingLine> lines, VanishingAxis axis)
        {
            int n = 0;
            foreach (var l in lines) if (l.Axis == axis) n++;
            return n;
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

    /// <summary>
    /// PMClearVanishingLines — removes all vanishing lines (and the cached solve result)
    /// from a photo plane pair so you can start fresh.
    /// </summary>
    public class ClearVanishingLinesCommand : Command
    {
        public override string EnglishName => "PMClearVanishingLines";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var registry = RhinoPhotoMatchPlugin.Instance.GetRegistry(doc);
            if (registry.Pairs.Count == 0)
            {
                RhinoApp.WriteLine("PMClearVanishingLines: no photo planes in the registry.");
                return Result.Failure;
            }

            PhotoPlanePair? pair = null;
            if (registry.Pairs.Count == 1)
            {
                pair = registry.Pairs[0];
            }
            else
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (var p in registry.Pairs) names.Add(p.Name);
                string pick = names[0];
                var res = Rhino.Input.RhinoGet.GetString(
                    $"Photo plane ({string.Join(", ", names)})", false, ref pick);
                if (res != Result.Success) return Result.Cancel;
                pair = registry.FindByName(pick);
            }

            if (pair == null) return Result.Cancel;

            int n = pair.VanishingLines.Count;
            pair.VanishingLines.Clear();
            pair.LastVanishingResult = null;
            doc.Views.Redraw();

            RhinoApp.WriteLine($"PMClearVanishingLines: removed {n} line(s) from \"{pair.Name}\".");
            return Result.Success;
        }
    }
}
