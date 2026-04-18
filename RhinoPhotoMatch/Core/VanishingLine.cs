using Rhino.Geometry;

namespace RhinoPhotoMatch.Core
{
    /// <summary>Which real-world direction a vanishing line is parallel to.</summary>
    public enum VanishingAxis { X, Y, Z }

    /// <summary>
    /// A line segment drawn on the photo plane, representing one real-world parallel edge.
    /// Endpoints are in photo pixel coordinates (origin top-left, Y down).
    /// </summary>
    public class VanishingLine
    {
        public Point2d      PixelA { get; }
        public Point2d      PixelB { get; }
        public VanishingAxis Axis  { get; }

        public VanishingLine(Point2d pixelA, Point2d pixelB, VanishingAxis axis)
        {
            PixelA = pixelA;
            PixelB = pixelB;
            Axis   = axis;
        }
    }

    /// <summary>
    /// Result from <see cref="VanishingPointSolver.Solve"/>.
    /// All Point2d coordinates are in image-centre space (origin at image centre, Y up).
    /// </summary>
    public class VanishingPointResult
    {
        public Point2d  VpX               { get; set; }
        public Point2d  VpY               { get; set; }
        public Point2d? VpZ               { get; set; }

        /// <summary>Focal length derived from f = sqrt(−V1·V2), in pixels.</summary>
        public double FocalLengthPixels { get; set; }

        /// <summary>35mm-equivalent lens length: f_px * 36 / imageWidth.</summary>
        public double LensLengthMm { get; set; }

        /// <summary>Y coordinate of the horizon line at the image centre (image-centre coords, Y up).
        /// Positive = horizon above centre = camera tilting downward.</summary>
        public double HorizonY { get; set; }

        /// <summary>Angle of the V1→V2 vector from horizontal in radians. Non-zero = camera roll.</summary>
        public double HorizonAngle { get; set; }

        /// <summary>Derived camera tilt in degrees (positive = looking down, horizon above centre).</summary>
        public double CameraTiltDegrees { get; set; }
    }
}
