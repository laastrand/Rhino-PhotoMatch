using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace RhinoPhotoMatch.Core
{
    /// <summary>
    /// Data for one picture plane / named camera pair.
    /// There is no document mesh — the plane is drawn each frame by PhotoPlaneConduit
    /// at a position computed from the viewport's current camera.
    /// </summary>
    public class PhotoPlanePair
    {
        public PhotoPlanePair(string name, string imagePath, double aspectRatio,
                              int pixelWidth, int pixelHeight, Guid activeViewportId)
        {
            Name             = name;
            ImagePath        = imagePath;
            AspectRatio      = aspectRatio;
            PixelWidth       = pixelWidth;
            PixelHeight      = pixelHeight;
            ActiveViewportId = activeViewportId;
        }

        /// <summary>Identifier for the plane, e.g. PM_Photo_01.</summary>
        public string Name { get; set; }

        /// <summary>Absolute path of the source image file.</summary>
        public string ImagePath { get; set; }

        /// <summary>Image width / height.</summary>
        public double AspectRatio { get; private set; }

        /// <summary>Source image pixel dimensions.</summary>
        public int PixelWidth  { get; private set; }
        public int PixelHeight { get; private set; }

        /// <summary>3D world point ↔ 2D image pixel coordinate pairs used for PnP calibration.</summary>
        public List<(Point3d WorldPoint, Point2d ImagePoint)> ReferencePairs { get; } = new();

        /// <summary>
        /// Distance from the camera to the plane in model units.
        /// Increase to push the plane further into the scene (useful for depth ordering).
        /// </summary>
        public double Distance { get; set; } = 2.0;

        /// <summary>
        /// Uniform scale applied on top of the FOV-fitting size.
        /// 1.0 = fills the camera FOV exactly at the current Distance.
        /// </summary>
        public double Scale { get; set; } = 1.0;

        /// <summary>The viewport that "owns" this pair and where the plane is rendered.</summary>
        public Guid ActiveViewportId { get; set; }

        /// <summary>Display transparency: 0.0 = fully opaque, 1.0 = fully transparent.</summary>
        public double Transparency { get; set; } = 0.0;

        /// <summary>Vanishing lines drawn on this photo for vanishing-point calibration.</summary>
        public List<VanishingLine> VanishingLines { get; } = new();

        /// <summary>Most recently solved vanishing-point result. Null until PMSolveVanishingPoints runs.</summary>
        public VanishingPointResult? LastVanishingResult { get; set; }

        // Baked world-space frame — set once when the plane is created, never changed
        public Point3d  PlaneCenter   { get; set; }
        public Vector3d PlaneRight    { get; set; }  // unit vector, world space
        public Vector3d PlaneUp       { get; set; }  // unit vector, world space
        public double   PlaneWorldW   { get; set; }  // width in model units
        public double   PlaneWorldH   { get; set; }  // height in model units
        public bool     FrameBaked    { get; set; } = false;

        /// <summary>
        /// Cached 48×48 thumbnail bitmap for the panel UI.
        /// Loaded once on first panel display; cleared and reloaded on relink.
        /// Not serialized — purely runtime state.
        /// </summary>
        public Eto.Drawing.Bitmap? ThumbnailBitmap { get; set; }
    }
}
