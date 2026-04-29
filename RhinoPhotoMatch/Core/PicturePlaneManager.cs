using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace RhinoPhotoMatch.Core
{
    /// <summary>
    /// Shared geometry and viewport helpers used by PhotoPlaneConduit
    /// and commands that operate on photo planes.
    /// </summary>
    public static class PicturePlaneManager
    {
        // ------------------------------------------------------------------ //
        //  Image rotation helpers
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Reads the EXIF Orientation tag and rotates/flips the bitmap to correct it.
        /// Returns the original bitmap unchanged if no rotation is needed or EXIF is unavailable.
        /// </summary>
        public static Bitmap CorrectOrientation(Bitmap bmp)
        {
            const int ExifOrientationId = 0x112;
            if (!bmp.PropertyIdList.Contains(ExifOrientationId)) return bmp;

            var prop = bmp.GetPropertyItem(ExifOrientationId);
            if (prop?.Value == null) return bmp;

            int orientation = BitConverter.ToUInt16(prop.Value, 0);
            switch (orientation)
            {
                case 2: bmp.RotateFlip(RotateFlipType.RotateNoneFlipX);   break;
                case 3: bmp.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
                case 4: bmp.RotateFlip(RotateFlipType.Rotate180FlipX);    break;
                case 5: bmp.RotateFlip(RotateFlipType.Rotate90FlipX);     break;
                case 6: bmp.RotateFlip(RotateFlipType.Rotate90FlipNone);  break;
                case 7: bmp.RotateFlip(RotateFlipType.Rotate270FlipX);    break;
                case 8: bmp.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
            }
            return bmp;
        }

        /// <summary>
        /// Returns the path of a working copy for the given original image,
        /// stored under %LOCALAPPDATA%\RhinoPhotoMatch\working\.
        /// The path is stable across calls for the same original.
        /// </summary>
        public static string MakeWorkingCopyPath(string originalPath)
        {
            string workDir = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "RhinoPhotoMatch", "working");
            Directory.CreateDirectory(workDir);
            var hashBytes = System.Security.Cryptography.MD5.HashData(
                System.Text.Encoding.UTF8.GetBytes(originalPath));
            string prefix = Convert.ToHexString(hashBytes)[..8];
            return Path.Combine(workDir, prefix + "_" + Path.GetFileName(originalPath));
        }

        /// <summary>
        /// Applies the given rotation (90, 180, or 270) to the plane's working image,
        /// updates PixelWidth/PixelHeight and AspectRatio on the pair, and invalidates
        /// the conduit's material cache so the viewport texture reloads on next draw.
        /// The original file at pair.ImagePath is never modified.
        /// </summary>
        public static void RotatePlaneImage(RhinoDoc doc, PhotoPlanePair pair, int degrees)
        {
            var flipType = degrees switch
            {
                90  => RotateFlipType.Rotate90FlipNone,
                180 => RotateFlipType.Rotate180FlipNone,
                270 => RotateFlipType.Rotate270FlipNone,
                _   => throw new ArgumentException("degrees must be 90, 180, or 270")
            };

            // Ensure we have a working copy — never modify the user's original
            if (pair.WorkingImagePath == null)
            {
                pair.WorkingImagePath = MakeWorkingCopyPath(pair.ImagePath);
                File.Copy(pair.ImagePath, pair.WorkingImagePath, overwrite: true);
            }

            string workPath = pair.WorkingImagePath;
            string tmpPath  = workPath + ".tmp";
            string ext      = Path.GetExtension(workPath).ToLowerInvariant();
            var fmt = ext == ".png" ? ImageFormat.Png : ImageFormat.Jpeg;

            // Load → rotate → save to tmp, then atomically replace (avoids file-lock conflict)
            using (var bmp = new Bitmap(workPath))
            {
                bmp.RotateFlip(flipType);
                bmp.Save(tmpPath, fmt);
            }
            File.Move(tmpPath, workPath, overwrite: true);

            if (degrees == 90 || degrees == 270)
            {
                (pair.PixelWidth, pair.PixelHeight) = (pair.PixelHeight, pair.PixelWidth);
                pair.AspectRatio = (double)pair.PixelWidth / pair.PixelHeight;
            }

            pair.RotationDegrees = (pair.RotationDegrees + degrees) % 360;

            pair.ThumbnailBitmap = null; // force reload on next panel refresh

            RhinoPhotoMatchPlugin.Instance.GetConduit(doc).InvalidateMaterial(pair.Name);
            doc.Views.Redraw();
        }

        // ------------------------------------------------------------------ //
        //  Viewport lookup
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Finds the RhinoViewport with the given ID across all open views.
        /// Returns null if the viewport is no longer open.
        /// </summary>
        public static RhinoViewport? FindViewport(RhinoDoc doc, Guid viewportId)
        {
            foreach (var view in doc.Views)
                if (view.ActiveViewport.Id == viewportId)
                    return view.ActiveViewport;
            return null;
        }

        // ------------------------------------------------------------------ //
        //  Plane mesh computation
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Builds the photo plane mesh from a viewport's live camera.
        /// The plane fills the camera FOV (letterboxed to the photo aspect ratio)
        /// at pair.Distance, scaled by pair.Scale.
        /// Returns null if the viewport camera is degenerate.
        /// </summary>
        public static Mesh? ComputePlaneMesh(RhinoViewport vp, PhotoPlanePair pair)
        {
            var camDir   = vp.CameraDirection; if (!camDir.Unitize()) return null;
            var camUp    = vp.CameraUp;        if (!camUp.Unitize())  return null;
            var camRight = Vector3d.CrossProduct(camDir, camUp);
            if (!camRight.Unitize()) return null;

            vp.GetFrustum(out double fl, out double fr, out double fb, out double ft,
                          out double fn, out _);

            double vpW, vpH;
            if (fn > 0)
            {
                vpW = (fr - fl) * pair.Distance / fn;
                vpH = (ft - fb) * pair.Distance / fn;
            }
            else
            {
                // Parallel projection fallback
                vpW = pair.Distance;
                vpH = pair.Distance / pair.AspectRatio;
            }

            double vpAspect = vpW / vpH;
            double planeW, planeH;
            if (pair.AspectRatio >= vpAspect)
            {
                planeW = vpW;
                planeH = vpW / pair.AspectRatio;
            }
            else
            {
                planeH = vpH;
                planeW = vpH * pair.AspectRatio;
            }

            planeW *= pair.Scale;
            planeH *= pair.Scale;

            var center = vp.CameraLocation + camDir * pair.Distance;
            return BuildCameraAlignedMesh(center, camRight, camUp, planeW, planeH);
        }

        // ------------------------------------------------------------------ //
        //  Plane frame (for point back-projection / calibration)
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Returns the world-space frame of the photo plane without building a mesh.
        /// Used by calibration commands to back-project 3D clicks into image UV coords.
        /// Returns false if the viewport camera is degenerate.
        /// </summary>
        public static bool ComputePlaneFrame(
            RhinoViewport vp, PhotoPlanePair pair,
            out Point3d  center,
            out Vector3d camRight, out Vector3d camUp,
            out double   planeW,   out double   planeH)
        {
            center   = Point3d.Origin;
            camRight = camUp = Vector3d.Zero;
            planeW   = planeH = 0;

            var camDir = vp.CameraDirection; if (!camDir.Unitize()) return false;
            var up     = vp.CameraUp;        if (!up.Unitize())     return false;
            camRight   = Vector3d.CrossProduct(camDir, up);
            if (!camRight.Unitize()) return false;
            camUp = up;

            vp.GetFrustum(out double fl, out double fr, out double fb, out double ft,
                          out double fn, out _);

            double vpW, vpH;
            if (fn > 0) { vpW = (fr - fl) * pair.Distance / fn; vpH = (ft - fb) * pair.Distance / fn; }
            else        { vpW = pair.Distance; vpH = pair.Distance / pair.AspectRatio; }

            double vpAspect = vpW / vpH;
            if (pair.AspectRatio >= vpAspect) { planeW = vpW; planeH = vpW / pair.AspectRatio; }
            else                               { planeH = vpH; planeW = vpH * pair.AspectRatio; }

            planeW *= pair.Scale;
            planeH *= pair.Scale;
            center  = vp.CameraLocation + camDir * pair.Distance;
            return true;
        }

        /// <summary>
        /// Bakes the current plane frame (from live camera) into the pair.
        /// Call this once immediately after creating the plane — never again.
        /// </summary>
        public static bool BakePlaneFrame(RhinoViewport vp, PhotoPlanePair pair)
        {
            if (!ComputePlaneFrame(vp, pair,
                    out Point3d center, out Vector3d right, out Vector3d up,
                    out double planeW, out double planeH))
                return false;

            pair.PlaneCenter = center;
            pair.PlaneRight  = right;
            pair.PlaneUp     = up;
            pair.PlaneWorldW = planeW;
            pair.PlaneWorldH = planeH;
            pair.FrameBaked  = true;
            return true;
        }

        // ------------------------------------------------------------------ //
        //  Mesh builder
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Builds a camera-aligned quad mesh centred on <paramref name="center"/>
        /// with UV coordinates mapped to fill the texture.
        /// </summary>
        public static Mesh BuildCameraAlignedMesh(
            Point3d  center,
            Vector3d camRight,
            Vector3d camUp,
            double   width,
            double   height)
        {
            var mesh = new Mesh();

            mesh.Vertices.Add(center - camRight * width / 2 - camUp * height / 2); // bl
            mesh.Vertices.Add(center + camRight * width / 2 - camUp * height / 2); // br
            mesh.Vertices.Add(center + camRight * width / 2 + camUp * height / 2); // tr
            mesh.Vertices.Add(center - camRight * width / 2 + camUp * height / 2); // tl

            mesh.TextureCoordinates.Add(0, 0);
            mesh.TextureCoordinates.Add(1, 0);
            mesh.TextureCoordinates.Add(1, 1);
            mesh.TextureCoordinates.Add(0, 1);

            mesh.Faces.AddFace(0, 1, 2, 3);
            mesh.Normals.ComputeNormals();
            mesh.Compact();

            return mesh;
        }

        // ------------------------------------------------------------------ //
        //  Document material creation (for baked/extracted planes)
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Adds a self-illuminated material with the photo as diffuse (and alpha for PNG)
        /// to the document and returns its index.
        /// </summary>
        public static int AddPhotoMaterial(RhinoDoc doc, string imagePath, string name)
        {
            var mat = new Material
            {
                Name          = name,
                DiffuseColor  = Color.White,
                EmissionColor = Color.White,
                Reflectivity  = 0,
                Shine         = 0,
                Transparency  = 0
            };

            mat.SetBitmapTexture(imagePath);

            if (Path.GetExtension(imagePath).ToLowerInvariant() == ".png")
                mat.SetTransparencyTexture(imagePath);

            return doc.Materials.Add(mat);
        }
    }
}
