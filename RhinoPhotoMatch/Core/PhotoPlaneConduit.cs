using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace RhinoPhotoMatch.Core
{
    /// <summary>
    /// Draws each photo plane as a world-space textured quad positioned relative to
    /// its linked camera. The plane is visible from ALL viewports:
    ///   - Linked viewport  : photo fills the view (background reference)
    ///   - Other viewports  : plane + frustum pyramid are drawn in 3D world space
    /// </summary>
    public sealed class PhotoPlaneConduit : DisplayConduit
    {
        private readonly PhotoPlaneRegistry _registry;
        private readonly Dictionary<string, DisplayMaterial> _materialCache = new();

        public PhotoPlaneConduit(PhotoPlaneRegistry registry)
        {
            _registry = registry;
        }

        public void InvalidateMaterial(string pairName) => _materialCache.Remove(pairName);

        // ------------------------------------------------------------------ //
        //  Bounding-box contribution (keeps near/far clipping from eating planes)
        // ------------------------------------------------------------------ //

        protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
        {
            if (_registry.Pairs.Count == 0) return;
            var doc = RhinoDoc.ActiveDoc;
            if (doc == null) return;

            foreach (var pair in _registry.Pairs)
            {
                // Compute the plane in the context of its own linked viewport so the
                // bounding box reflects the actual world-space position of the quad.
                var linkedVp = PicturePlaneManager.FindViewport(doc, pair.ActiveViewportId);
                if (linkedVp == null) continue;

                var mesh = PicturePlaneManager.ComputePlaneMesh(linkedVp, pair);
                if (mesh == null) continue;

                e.IncludeBoundingBox(mesh.GetBoundingBox(false));
            }
        }

        // ------------------------------------------------------------------ //
        //  Draw background (linked viewport only — fires before geometry)
        // ------------------------------------------------------------------ //

        protected override void PreDrawObjects(DrawEventArgs e)
        {
            if (_registry.Pairs.Count == 0) return;
            var doc = RhinoDoc.ActiveDoc;
            if (doc == null) return;

            foreach (var pair in _registry.Pairs)
            {
                // Only draw here when fully opaque — transparent planes are drawn in
                // PostDrawObjects so they composite correctly over rendered geometry.
                if (e.Viewport.Id != pair.ActiveViewportId) continue;
                if (pair.Transparency > 0.0) continue;

                var linkedVp = PicturePlaneManager.FindViewport(doc, pair.ActiveViewportId);
                if (linkedVp == null) continue;

                var mesh = PicturePlaneManager.ComputePlaneMesh(linkedVp, pair);
                if (mesh == null) continue;

                // Draw as background — geometry will render on top
                var material = GetMaterial(pair);
                e.Display.DrawMeshShaded(mesh, material ?? new DisplayMaterial(Color.White));
            }
        }

        // ------------------------------------------------------------------ //
        //  Draw 3D plane + frustum in all OTHER viewports;
        //  also draw transparent linked-viewport planes here so they blend
        //  correctly over already-rendered scene geometry.
        // ------------------------------------------------------------------ //

        protected override void PostDrawObjects(DrawEventArgs e)
        {
            if (_registry.Pairs.Count == 0) return;
            var doc = RhinoDoc.ActiveDoc;
            if (doc == null) return;

            foreach (var pair in _registry.Pairs)
            {
                bool isLinked = e.Viewport.Id == pair.ActiveViewportId;

                // Opaque linked-viewport plane is handled in PreDrawObjects
                if (isLinked && pair.Transparency == 0.0) continue;

                var linkedVp = PicturePlaneManager.FindViewport(doc, pair.ActiveViewportId);
                if (linkedVp == null) continue;

                var mesh = PicturePlaneManager.ComputePlaneMesh(linkedVp, pair);
                if (mesh == null) continue;

                if (!isLinked)
                {
                    // Frustum pyramid + dot — only visible from external viewports
                    var camLoc      = linkedVp.CameraLocation;
                    var frustumColor = Color.FromArgb(140, 200, 200, 200);
                    for (int i = 0; i < mesh.Vertices.Count; i++)
                        e.Display.DrawLine(camLoc, mesh.Vertices[i], frustumColor, 1);
                    e.Display.DrawPoint(camLoc, PointStyle.Circle, 5, Color.White);
                    e.Display.DrawMeshWires(mesh, Color.FromArgb(200, 255, 255, 255));
                }

                // Textured plane (transparent linked viewport, or all external viewports)
                var material = GetMaterial(pair);
                e.Display.DrawMeshShaded(mesh, material ?? new DisplayMaterial(Color.White));

                // Vanishing lines + VP markers — only in the linked viewport
                if (isLinked)
                    DrawVanishingOverlay(e, pair, linkedVp);
            }
        }

        // ------------------------------------------------------------------ //
        //  Vanishing-line overlay
        // ------------------------------------------------------------------ //

        private static void DrawVanishingOverlay(DrawEventArgs e, PhotoPlanePair pair, RhinoViewport linkedVp)
        {
            if (pair.VanishingLines.Count == 0 && pair.LastVanishingResult == null) return;

            if (!PicturePlaneManager.ComputePlaneFrame(linkedVp, pair,
                    out var center, out var right, out var up, out var planeW, out var planeH))
                return;

            double extendBy = Math.Max(planeW, planeH) * 4.0; // extension beyond endpoints

            foreach (var vl in pair.VanishingLines)
            {
                var col = AxisColor(vl.Axis);
                var ptA = PixelToWorld(vl.PixelA, center, right, up, planeW, planeH, pair.PixelWidth, pair.PixelHeight);
                var ptB = PixelToWorld(vl.PixelB, center, right, up, planeW, planeH, pair.PixelWidth, pair.PixelHeight);

                // Draw the user-placed segment (solid, thick)
                e.Display.DrawLine(ptA, ptB, col, 2);

                // Extend in both directions to show convergence (faded)
                var segDir = ptB - ptA;
                if (segDir.Length > 1e-10)
                {
                    segDir.Unitize();
                    var faded = Color.FromArgb(70, col.R, col.G, col.B);
                    e.Display.DrawLine(ptA - segDir * extendBy, ptA, faded, 1);
                    e.Display.DrawLine(ptB, ptB + segDir * extendBy, faded, 1);
                }
            }

            // Vanishing-point markers (only after a solve)
            if (pair.LastVanishingResult == null) return;
            var res = pair.LastVanishingResult;

            double crossSize = Math.Min(planeW, planeH) * 0.06;
            double maxExtent = Math.Max(pair.PixelWidth, pair.PixelHeight) * 3.0; // skip markers far off-screen

            DrawVpMarker(e, res.VpX, pair, center, right, up, planeW, planeH, crossSize, maxExtent, Color.Red);
            DrawVpMarker(e, res.VpY, pair, center, right, up, planeW, planeH, crossSize, maxExtent, Color.Lime);
            if (res.VpZ.HasValue)
                DrawVpMarker(e, res.VpZ.Value, pair, center, right, up, planeW, planeH, crossSize, maxExtent, Color.DodgerBlue);
        }

        private static void DrawVpMarker(
            DrawEventArgs e, Point2d vpIc, PhotoPlanePair pair,
            Point3d center, Vector3d right, Vector3d up,
            double planeW, double planeH, double crossSize, double maxExtent,
            Color col)
        {
            if (Math.Abs(vpIc.X) > maxExtent || Math.Abs(vpIc.Y) > maxExtent) return;

            // Convert image-centre coords → pixel coords → world 3D
            var pxPt = new Point2d(vpIc.X + pair.PixelWidth / 2.0, -vpIc.Y + pair.PixelHeight / 2.0);
            var pt   = PixelToWorld(pxPt, center, right, up, planeW, planeH, pair.PixelWidth, pair.PixelHeight);

            e.Display.DrawLine(pt - right * crossSize, pt + right * crossSize, col, 2);
            e.Display.DrawLine(pt - up    * crossSize, pt + up    * crossSize, col, 2);
            e.Display.DrawPoint(pt, PointStyle.Circle, 6, col);
        }

        private static Point3d PixelToWorld(
            Point2d pixel,
            Point3d center, Vector3d right, Vector3d up,
            double planeW, double planeH, int pixW, int pixH)
        {
            double u = pixel.X / pixW;
            double v = 1.0 - pixel.Y / pixH;  // flip Y: pixel Y↓ → world Y↑
            return center + right * (u - 0.5) * planeW + up * (v - 0.5) * planeH;
        }

        private static Color AxisColor(VanishingAxis axis)
        {
            if (axis == VanishingAxis.X) return Color.Red;
            if (axis == VanishingAxis.Y) return Color.Lime;
            return Color.DodgerBlue;
        }

        // ------------------------------------------------------------------ //
        //  Helpers
        // ------------------------------------------------------------------ //

        private DisplayMaterial GetMaterial(PhotoPlanePair pair)
        {
            if (_materialCache.TryGetValue(pair.Name, out var cached))
                return cached;

            var docMat = new Rhino.DocObjects.Material
            {
                Name          = pair.Name,
                DiffuseColor  = Color.White,
                EmissionColor = Color.Black,   // no emission — let texture provide color
                Reflectivity  = 0,
                Shine         = 0,
                Transparency  = 0
            };
            docMat.SetBitmapTexture(pair.ImagePath);

            var displayMat = new DisplayMaterial(docMat)
            {
                Transparency = pair.Transparency,
                IsTwoSided   = true
            };
            _materialCache[pair.Name] = displayMat;
            return displayMat;
        }
    }
}
