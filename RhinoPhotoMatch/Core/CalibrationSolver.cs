using System;
using System.Collections.Generic;
using OpenCvSharp;
using RhinoPoint3d = Rhino.Geometry.Point3d;
using RhinoPoint2d = Rhino.Geometry.Point2d;
using RhinoVector3d = Rhino.Geometry.Vector3d;

namespace RhinoPhotoMatch.Core
{
    /// <summary>
    /// Solved camera pose returned by <see cref="CalibrationSolver.SolvePnP"/>.
    /// All vectors are in Rhino world-space (Y-up, right-handed).
    /// </summary>
    public class CalibrationResult
    {
        public RhinoPoint3d  CameraLocation    { get; set; }
        public RhinoVector3d CameraDirection   { get; set; }
        public RhinoVector3d CameraUp          { get; set; }
        /// <summary>Horizontal field of view in radians (same value used for calibration).</summary>
        public double   FovRadians        { get; set; }
        /// <summary>Mean reprojection error in pixels.</summary>
        public double   ReprojectionError { get; set; }
    }

    public static class CalibrationSolver
    {
        /// <summary>
        /// Runs OpenCV SolvePnP on the supplied point correspondences.
        /// World points must be in Rhino world-space (Y-up).
        /// Image points must be in pixel coordinates (origin top-left, Y down).
        /// </summary>
        /// <param name="worldPoints">3D model points.</param>
        /// <param name="imagePoints">Matching 2D image pixels (top-left origin).</param>
        /// <param name="imageWidth">Photo width in pixels.</param>
        /// <param name="imageHeight">Photo height in pixels.</param>
        /// <param name="horizontalFovDegrees">Horizontal field of view of the camera lens.</param>
        /// <returns>Solved pose, or null on failure.</returns>
        public static CalibrationResult? SolvePnP(
            IList<RhinoPoint3d>  worldPoints,
            IList<RhinoPoint2d>  imagePoints,
            int             imageWidth,
            int             imageHeight,
            double          horizontalFovDegrees)
        {
            if (worldPoints.Count < 4)
            {
                Rhino.RhinoApp.WriteLine("CalibrationSolver: need at least 4 reference pairs.");
                return null;
            }
            if (worldPoints.Count != imagePoints.Count)
            {
                Rhino.RhinoApp.WriteLine("CalibrationSolver: world and image point counts differ.");
                return null;
            }

            // ---- Camera intrinsics from horizontal FOV ----
            double fovRad = horizontalFovDegrees * Math.PI / 180.0;
            double fx = (imageWidth / 2.0) / Math.Tan(fovRad / 2.0);
            double cx = imageWidth  / 2.0;
            double cy = imageHeight / 2.0;

            double[,] camData = { { fx, 0, cx }, { 0, fx, cy }, { 0, 0, 1 } };
            using var camMat  = Mat.FromArray(camData);
            using var distMat = Mat.Zeros(1, 4, MatType.CV_64FC1);

            // Use Nx3 / Nx2 single-channel float arrays — OpenCV SolvePnP requires this shape.
            // (Mat.FromArray(Point3f[]) produces Nx1 CV_32FC3 which some builds reject.)
            var objData = new float[worldPoints.Count, 3];
            var imgData = new float[worldPoints.Count, 2];
            var imgPts  = new Point2f[worldPoints.Count]; // kept for reprojection error
            for (int i = 0; i < worldPoints.Count; i++)
            {
                objData[i, 0] = (float)worldPoints[i].X;
                objData[i, 1] = (float)worldPoints[i].Y;
                objData[i, 2] = (float)worldPoints[i].Z;
                imgData[i, 0] = (float)imagePoints[i].X;
                imgData[i, 1] = (float)imagePoints[i].Y;
                imgPts[i]     = new Point2f((float)imagePoints[i].X, (float)imagePoints[i].Y);
            }
            using var objMat = Mat.FromArray(objData); // Nx3 CV_32F
            using var imgMat = Mat.FromArray(imgData); // Nx2 CV_32F

            // ---- Solve: EPnP for robust initial estimate, then iterative refinement ----
            // EPnP (flag=1) is non-iterative and never hangs, even near-coplanar.
            // SOLVEPNP_ITERATIVE (flag=0) with useExtrinsicGuess=true then refines to sub-pixel accuracy.
            using var rvec = new Mat();
            using var tvec = new Mat();
            try
            {
                Cv2.SolvePnP(objMat, imgMat, camMat, distMat, rvec, tvec,
                    useExtrinsicGuess: false,
                    flags: (SolvePnPFlags)1);   // 1 = SOLVEPNP_EPNP — initial estimate

                if (!rvec.Empty() && !tvec.Empty())
                    Cv2.SolvePnP(objMat, imgMat, camMat, distMat, rvec, tvec,
                        useExtrinsicGuess: true,
                        flags: (SolvePnPFlags)0);  // 0 = SOLVEPNP_ITERATIVE — refine
            }
            catch (Exception ex)
            {
                Rhino.RhinoApp.WriteLine($"CalibrationSolver: SolvePnP failed — {ex.Message}");
                return null;
            }
            if (rvec.Empty() || tvec.Empty())
            {
                Rhino.RhinoApp.WriteLine("CalibrationSolver: solver returned empty result. Check reference points.");
                return null;
            }

            // ---- Rotation vector → matrix ----
            using var rmat = new Mat();
            Cv2.Rodrigues(rvec, rmat);

            double r00 = rmat.At<double>(0, 0), r01 = rmat.At<double>(0, 1), r02 = rmat.At<double>(0, 2);
            double r10 = rmat.At<double>(1, 0), r11 = rmat.At<double>(1, 1), r12 = rmat.At<double>(1, 2);
            double r20 = rmat.At<double>(2, 0), r21 = rmat.At<double>(2, 1), r22 = rmat.At<double>(2, 2);
            double tx  = tvec.At<double>(0, 0);
            double ty  = tvec.At<double>(1, 0);
            double tz  = tvec.At<double>(2, 0);

            // ---- Camera pose in world space ----
            // OpenCV camera space: X right, Y down, Z forward (into scene).
            // Rhino camera space:  X right, Y up,   Z backward (toward viewer).
            // Apply Rx180 = diag(1,-1,-1): R' = Rx180*R, t' = Rx180*t.
            // Camera position = -R'^T * t'. Since Rx180² = I this simplifies to -R^T * t.
            var camLoc = new RhinoPoint3d(
                -(r00 * tx + r10 * ty + r20 * tz),
                -(r01 * tx + r11 * ty + r21 * tz),
                -(r02 * tx + r12 * ty + r22 * tz));

            // Camera look direction in world = -(third row of R') = -(Rx180 applied to row 2 of R)
            //   = -(-r20, -r21, -r22) = (r20, r21, r22)   [rows of R, not columns]
            var camDir = new RhinoVector3d(r20, r21, -r22);

            // Camera up in world = second row of R' = Rx180 applied to row 1 of R
            //   = (-r10, -r11, -r12)
            var camUp = new RhinoVector3d(-r10, -r11, -r12);

            camDir.Unitize();
            camUp.Unitize();

            // ---- Reprojection error ----
            using var projMat = new Mat();
            Cv2.ProjectPoints(objMat, rvec, tvec, camMat, distMat, projMat);
            projMat.GetArray(out Point2f[] projected);
            double totalErr = 0;
            for (int i = 0; i < projected.Length; i++)
            {
                double dx = projected[i].X - imgPts[i].X;
                double dy = projected[i].Y - imgPts[i].Y;
                totalErr += Math.Sqrt(dx * dx + dy * dy);
            }

            return new CalibrationResult
            {
                CameraLocation    = camLoc,
                CameraDirection   = camDir,
                CameraUp          = camUp,
                FovRadians        = fovRad,
                ReprojectionError = totalErr / projected.Length
            };
        }
    }
}
