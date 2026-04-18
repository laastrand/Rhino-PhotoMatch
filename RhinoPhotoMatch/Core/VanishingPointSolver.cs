using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace RhinoPhotoMatch.Core
{
    public static class VanishingPointSolver
    {
        // ------------------------------------------------------------------ //
        //  Geometry primitives
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Finds the least-squares best-fit intersection of a set of lines (the vanishing point).
        ///
        /// Lines are supplied as <see cref="VanishingLine"/> objects whose endpoints are in
        /// pixel coordinates (top-left origin, Y down).  The returned point is in
        /// image-centre coordinates (origin at image centre, Y up).
        ///
        /// Method: each line ax+by+c=0 (normalised) contributes to the normal equations
        ///   A^T A p = A^T (−c), which reduce to a 2×2 system solved by Cramer's rule.
        ///
        /// Returns false when fewer than 2 lines are supplied or the system is degenerate
        /// (all lines nearly parallel).
        /// </summary>
        public static bool TryFindVanishingPoint(
            IList<VanishingLine> lines,
            int imageWidth, int imageHeight,
            out Point2d vanishingPoint)
        {
            vanishingPoint = Point2d.Origin;
            if (lines.Count < 2) return false;

            double cx = imageWidth  / 2.0;
            double cy = imageHeight / 2.0;

            double sumAA = 0, sumAB = 0, sumBB = 0, sumAC = 0, sumBC = 0;

            foreach (var line in lines)
            {
                // Pixel (Y↓) → image-centre (Y↑)
                double x1 = line.PixelA.X - cx,  y1 = -(line.PixelA.Y - cy);
                double x2 = line.PixelB.X - cx,  y2 = -(line.PixelB.Y - cy);

                // Line equation: a·x + b·y + c = 0
                double a = y1 - y2;
                double b = x2 - x1;
                double c = x1 * y2 - x2 * y1;

                double len = Math.Sqrt(a * a + b * b);
                if (len < 1e-10) continue;   // degenerate (zero-length) line
                a /= len; b /= len; c /= len;

                sumAA += a * a;  sumAB += a * b;  sumBB += b * b;
                sumAC += a * c;  sumBC += b * c;
            }

            // Cramer's rule for [ sumAA  sumAB ] [px]   [-sumAC]
            //                   [ sumAB  sumBB ] [py] = [-sumBC]
            double det = sumAA * sumBB - sumAB * sumAB;
            if (Math.Abs(det) < 1e-14) return false;

            vanishingPoint = new Point2d(
                (-sumAC * sumBB + sumAB * sumBC) / det,
                (-sumBC * sumAA + sumAB * sumAC) / det);
            return true;
        }

        /// <summary>
        /// Computes focal length in pixels from two vanishing points in image-centre coordinates.
        /// Formula: f = sqrt(−V1·V2).
        /// Returns false when the dot product is ≥ 0 (one-point perspective or bad line placement).
        /// </summary>
        public static bool TryComputeFocalLength(Point2d vp1, Point2d vp2, out double focalLengthPixels)
        {
            double dot = vp1.X * vp2.X + vp1.Y * vp2.Y;
            if (dot >= 0) { focalLengthPixels = 0; return false; }
            focalLengthPixels = Math.Sqrt(-dot);
            return true;
        }

        /// <summary>
        /// Converts focal length in pixels to a 35mm-equivalent lens length in mm.
        /// Uses the 36mm full-frame film width standard.
        /// </summary>
        public static double FocalLengthToLensMm(double focalPx, int imageWidth)
            => focalPx * 36.0 / imageWidth;

        /// <summary>
        /// Returns the Y coordinate of the horizon line (through vp1 and vp2) at x = 0
        /// (the image centre), in image-centre coordinates (Y up).
        /// </summary>
        public static double HorizonYAtCenter(Point2d vp1, Point2d vp2)
        {
            double dx = vp2.X - vp1.X;
            if (Math.Abs(dx) < 1e-10) return (vp1.Y + vp2.Y) / 2.0;
            return vp1.Y + (0.0 - vp1.X) / dx * (vp2.Y - vp1.Y);
        }

        // ------------------------------------------------------------------ //
        //  Full solve
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Solves for vanishing points, focal length and camera parameters from all stored lines.
        /// Requires at least 2 lines each in the X and Y axis groups.
        /// Returns null on failure (reason printed to Rhino command line).
        /// </summary>
        public static VanishingPointResult? Solve(
            IList<VanishingLine> allLines,
            int imageWidth, int imageHeight)
        {
            var linesX = new List<VanishingLine>();
            var linesY = new List<VanishingLine>();
            var linesZ = new List<VanishingLine>();

            foreach (var l in allLines)
            {
                if      (l.Axis == VanishingAxis.X) linesX.Add(l);
                else if (l.Axis == VanishingAxis.Y) linesY.Add(l);
                else                                linesZ.Add(l);
            }

            if (linesX.Count < 2)
            { Rhino.RhinoApp.WriteLine("PMSolveVanishingPoints: need at least 2 X-axis lines."); return null; }
            if (linesY.Count < 2)
            { Rhino.RhinoApp.WriteLine("PMSolveVanishingPoints: need at least 2 Y-axis lines."); return null; }

            if (!TryFindVanishingPoint(linesX, imageWidth, imageHeight, out var vpX) ||
                !TryFindVanishingPoint(linesY, imageWidth, imageHeight, out var vpY))
            {
                Rhino.RhinoApp.WriteLine("PMSolveVanishingPoints: could not compute vanishing points (degenerate lines?).");
                return null;
            }

            if (!TryComputeFocalLength(vpX, vpY, out var f))
            {
                Rhino.RhinoApp.WriteLine(
                    "PMSolveVanishingPoints: V1·V2 ≥ 0 — the two vanishing points are on the same side " +
                    "of the principal point.  Check that X and Y lines converge in opposite directions.");
                return null;
            }

            double horizonY = HorizonYAtCenter(vpX, vpY);

            var result = new VanishingPointResult
            {
                VpX               = vpX,
                VpY               = vpY,
                FocalLengthPixels = f,
                LensLengthMm      = FocalLengthToLensMm(f, imageWidth),
                HorizonY          = horizonY,
                HorizonAngle      = Math.Atan2(vpY.Y - vpX.Y, vpY.X - vpX.X),
                CameraTiltDegrees = Math.Atan(horizonY / f) * 180.0 / Math.PI,
            };

            if (linesZ.Count >= 2 &&
                TryFindVanishingPoint(linesZ, imageWidth, imageHeight, out var vpZ))
                result.VpZ = vpZ;

            return result;
        }
    }
}
