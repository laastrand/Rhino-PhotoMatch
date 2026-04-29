using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rhino;

namespace RhinoPhotoMatch.Core
{
    /// <summary>
    /// Tracks all picture plane / named camera pairs for the current session.
    /// No document meshes are created — rendering is handled by PhotoPlaneConduit.
    /// </summary>
    public class PhotoPlaneRegistry
    {
        private readonly List<PhotoPlanePair> _pairs = new();

        public IReadOnlyList<PhotoPlanePair> Pairs => _pairs;

        /// <summary>Raised on the calling thread whenever the registry is mutated.</summary>
        public event EventHandler? Changed;

        private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

        // ------------------------------------------------------------------ //
        //  Create
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Reads the image, captures the current viewport as the starting named view,
        /// and registers the pair. Returns null on failure.
        /// </summary>
        public PhotoPlanePair? CreatePair(RhinoDoc doc, string imagePath, string name)
        {
            // Read pixel dimensions, applying EXIF auto-correction if needed
            int pw, ph;
            string? workingImagePath = null;
            try
            {
                using var bmp = new System.Drawing.Bitmap(imagePath);

                const int ExifOrientationId = 0x112;
                bool hasNonTrivialOrientation = bmp.PropertyIdList.Contains(ExifOrientationId) &&
                    bmp.GetPropertyItem(ExifOrientationId)?.Value is { } ov &&
                    BitConverter.ToUInt16(ov, 0) > 1;

                PicturePlaneManager.CorrectOrientation(bmp);
                pw = bmp.Width;
                ph = bmp.Height;

                if (hasNonTrivialOrientation)
                {
                    workingImagePath = PicturePlaneManager.MakeWorkingCopyPath(imagePath);
                    string ext = System.IO.Path.GetExtension(imagePath).ToLowerInvariant();
                    var fmt = ext == ".png"
                        ? System.Drawing.Imaging.ImageFormat.Png
                        : System.Drawing.Imaging.ImageFormat.Jpeg;
                    bmp.Save(workingImagePath, fmt);
                    RhinoApp.WriteLine($"  EXIF orientation corrected — working copy saved.");
                }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"PhotoPlaneRegistry: cannot read image — {ex.Message}");
                return null;
            }

            double aspectRatio = (double)pw / ph;
            var activeVp = doc.Views.ActiveView.ActiveViewport;

            var pair = new PhotoPlanePair(name, imagePath, aspectRatio, pw, ph, activeVp.Id);
            if (workingImagePath != null)
                pair.WorkingImagePath = workingImagePath;
            _pairs.Add(pair);
            OnChanged();

            RhinoApp.WriteLine($"Photo plane \"{name}\" created — {pw} x {ph} px  |  \"{Path.GetFileName(imagePath)}\"");
            return pair;
        }

        // ------------------------------------------------------------------ //
        //  Remove
        // ------------------------------------------------------------------ //

        public void RemovePair(RhinoDoc doc, PhotoPlanePair pair)
        {
            _pairs.Remove(pair);
            OnChanged();
            RhinoApp.WriteLine($"Photo plane \"{pair.Name}\" removed.");
        }

        // ------------------------------------------------------------------ //
        //  Relink
        // ------------------------------------------------------------------ //

        public void RelinkPhoto(RhinoDoc doc, PhotoPlanePair pair, string newImagePath,
                                PhotoPlaneConduit conduit)
        {
            pair.ImagePath = newImagePath;
            conduit.InvalidateMaterial(pair.Name);
            doc.Views.Redraw();
            OnChanged();
            RhinoApp.WriteLine($"\"{pair.Name}\" relinked to \"{Path.GetFileName(newImagePath)}\".");
        }

        // ------------------------------------------------------------------ //
        //  Helpers
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Adds a pair that was constructed externally (e.g. from a saved session).
        /// Skips image reading — the caller is responsible for supplying correct dimensions.
        /// </summary>
        public void AddRestoredPair(PhotoPlanePair pair)
        {
            _pairs.Add(pair);
            OnChanged();
        }

        /// <summary>
        /// Removes the pair with the given name (case-insensitive) and invalidates its
        /// material cache, if found. No-op if not found.
        /// </summary>
        public void RemoveByName(string name, PhotoPlaneConduit conduit)
        {
            var pair = FindByName(name);
            if (pair == null) return;
            conduit.InvalidateMaterial(pair.Name);
            _pairs.Remove(pair);
            OnChanged();
        }

        public PhotoPlanePair? FindByName(string name) =>
            _pairs.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public string GenerateAutoName()
        {
            for (int i = 1; i < 100; i++)
            {
                string candidate = $"PM_Photo_{i:D2}";
                if (_pairs.All(p => !p.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                    return candidate;
            }
            return $"PM_Photo_{Guid.NewGuid():N}";
        }
    }
}
