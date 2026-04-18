using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rhino;
using Rhino.Geometry;

namespace RhinoPhotoMatch.Core
{
    // ------------------------------------------------------------------ //
    //  DTOs — plain data classes that serialize cleanly to/from JSON
    // ------------------------------------------------------------------ //

    public class SessionReferencePairDto
    {
        public double WorldX { get; set; }
        public double WorldY { get; set; }
        public double WorldZ { get; set; }
        public double ImageX { get; set; }
        public double ImageY { get; set; }
    }

    public class SessionVanishingLineDto
    {
        public double PixAX { get; set; }
        public double PixAY { get; set; }
        public double PixBX { get; set; }
        public double PixBY { get; set; }
        public string Axis  { get; set; } = "X";   // "X" | "Y" | "Z"
    }

    public class SessionVanishingResultDto
    {
        public double VpXx  { get; set; }
        public double VpXy  { get; set; }
        public double VpYx  { get; set; }
        public double VpYy  { get; set; }
        public bool   HasVpZ { get; set; }
        public double VpZx  { get; set; }
        public double VpZy  { get; set; }
        public double FocalLengthPixels  { get; set; }
        public double LensLengthMm       { get; set; }
        public double HorizonY           { get; set; }
        public double HorizonAngle       { get; set; }
        public double CameraTiltDegrees  { get; set; }
    }

    public class SessionPairDto
    {
        public string Name             { get; set; } = "";
        public string ImagePath        { get; set; } = "";
        public int    PixelWidth       { get; set; }
        public int    PixelHeight      { get; set; }
        public double Distance         { get; set; }
        public double Scale            { get; set; }
        public double Transparency     { get; set; }
        public string ViewportName     { get; set; } = "";   // human-readable; used to re-find the viewport

        public List<SessionReferencePairDto>  ReferencePairs  { get; set; } = new();
        public List<SessionVanishingLineDto>  VanishingLines  { get; set; } = new();
        public SessionVanishingResultDto?     VanishingResult { get; set; }

        // Baked plane frame
        public bool   FrameBaked    { get; set; }
        public double PlaneCenterX  { get; set; }
        public double PlaneCenterY  { get; set; }
        public double PlaneCenterZ  { get; set; }
        public double PlaneRightX   { get; set; }
        public double PlaneRightY   { get; set; }
        public double PlaneRightZ   { get; set; }
        public double PlaneUpX      { get; set; }
        public double PlaneUpY      { get; set; }
        public double PlaneUpZ      { get; set; }
        public double PlaneWorldW   { get; set; }
        public double PlaneWorldH   { get; set; }
    }

    public class SessionDto
    {
        public int                    Version { get; set; } = 1;
        public List<SessionPairDto>   Pairs   { get; set; } = new();
    }

    // ------------------------------------------------------------------ //
    //  Serializer — converts between registry and RhinoDoc.Strings
    // ------------------------------------------------------------------ //

    public static class SessionSerializer
    {
        private const string DocStringKey = "RhinoPhotoMatch.Session";

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented            = false,
            DefaultIgnoreCondition   = JsonIgnoreCondition.Never,
        };

        // ---- Save -------------------------------------------------------

        public static void Save(RhinoDoc doc, PhotoPlaneRegistry registry)
        {
            var dto = new SessionDto();

            foreach (var pair in registry.Pairs)
            {
                // Resolve viewport name so we can find it again after reload
                string vpName = "";
                foreach (var view in doc.Views)
                    if (view.ActiveViewport.Id == pair.ActiveViewportId)
                    { vpName = view.ActiveViewport.Name; break; }

                var pairDto = new SessionPairDto
                {
                    Name         = pair.Name,
                    ImagePath    = pair.ImagePath,
                    PixelWidth   = pair.PixelWidth,
                    PixelHeight  = pair.PixelHeight,
                    Distance     = pair.Distance,
                    Scale        = pair.Scale,
                    Transparency = pair.Transparency,
                    ViewportName = vpName,
                    FrameBaked   = pair.FrameBaked,
                    PlaneCenterX = pair.PlaneCenter.X,
                    PlaneCenterY = pair.PlaneCenter.Y,
                    PlaneCenterZ = pair.PlaneCenter.Z,
                    PlaneRightX  = pair.PlaneRight.X,
                    PlaneRightY  = pair.PlaneRight.Y,
                    PlaneRightZ  = pair.PlaneRight.Z,
                    PlaneUpX     = pair.PlaneUp.X,
                    PlaneUpY     = pair.PlaneUp.Y,
                    PlaneUpZ     = pair.PlaneUp.Z,
                    PlaneWorldW  = pair.PlaneWorldW,
                    PlaneWorldH  = pair.PlaneWorldH,
                };

                foreach (var rp in pair.ReferencePairs)
                    pairDto.ReferencePairs.Add(new SessionReferencePairDto
                    {
                        WorldX = rp.WorldPoint.X, WorldY = rp.WorldPoint.Y, WorldZ = rp.WorldPoint.Z,
                        ImageX = rp.ImagePoint.X, ImageY = rp.ImagePoint.Y,
                    });

                foreach (var vl in pair.VanishingLines)
                    pairDto.VanishingLines.Add(new SessionVanishingLineDto
                    {
                        PixAX = vl.PixelA.X, PixAY = vl.PixelA.Y,
                        PixBX = vl.PixelB.X, PixBY = vl.PixelB.Y,
                        Axis  = vl.Axis.ToString(),
                    });

                if (pair.LastVanishingResult != null)
                {
                    var r = pair.LastVanishingResult;
                    pairDto.VanishingResult = new SessionVanishingResultDto
                    {
                        VpXx  = r.VpX.X, VpXy  = r.VpX.Y,
                        VpYx  = r.VpY.X, VpYy  = r.VpY.Y,
                        HasVpZ = r.VpZ.HasValue,
                        VpZx  = r.VpZ.HasValue ? r.VpZ.Value.X : 0,
                        VpZy  = r.VpZ.HasValue ? r.VpZ.Value.Y : 0,
                        FocalLengthPixels = r.FocalLengthPixels,
                        LensLengthMm      = r.LensLengthMm,
                        HorizonY          = r.HorizonY,
                        HorizonAngle      = r.HorizonAngle,
                        CameraTiltDegrees = r.CameraTiltDegrees,
                    };
                }

                dto.Pairs.Add(pairDto);
            }

            string json = JsonSerializer.Serialize(dto, _jsonOptions);
            doc.Strings.SetString(DocStringKey, json);
        }

        // ---- Load -------------------------------------------------------

        /// <summary>
        /// Restores all pairs from the JSON stored in <see cref="RhinoDoc.Strings"/>.
        /// Returns the number of pairs restored, or -1 if no session data was found.
        /// Emits warnings to the command line for missing image files.
        /// Existing pairs with the same name are replaced so reloading the same
        /// document in the same Rhino session always reflects the saved state.
        /// </summary>
        public static int Load(RhinoDoc doc, PhotoPlaneRegistry registry,
                               PhotoPlaneConduit conduit)
        {
            string? json = doc.Strings.GetValue(DocStringKey);
            if (string.IsNullOrEmpty(json)) return -1;

            RhinoApp.WriteLine($"RhinoPhotoMatch: found session data, restoring…");

            SessionDto dto;
            try
            {
                dto = JsonSerializer.Deserialize<SessionDto>(json, _jsonOptions)
                      ?? new SessionDto();
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"RhinoPhotoMatch: session data could not be parsed — {ex.Message}");
                return 0;
            }

            int restored = 0;

            foreach (var pairDto in dto.Pairs)
            {
                // Warn if image is missing, but still restore the pair
                if (!File.Exists(pairDto.ImagePath))
                    RhinoApp.WriteLine($"RhinoPhotoMatch: image not found — \"{pairDto.ImagePath}\".  " +
                                       "Use PMRelinkPhoto to relink.");

                // Find the viewport by name
                Guid vpId = Guid.Empty;
                foreach (var view in doc.Views)
                    if (view.ActiveViewport.Name.Equals(pairDto.ViewportName,
                            StringComparison.OrdinalIgnoreCase))
                    { vpId = view.ActiveViewport.Id; break; }

                if (vpId == Guid.Empty)
                {
                    // Fall back to the active viewport so the pair is at least visible
                    vpId = doc.Views.ActiveView?.ActiveViewport.Id ?? Guid.Empty;
                    RhinoApp.WriteLine($"RhinoPhotoMatch: viewport \"{pairDto.ViewportName}\" not found " +
                                       $"for pair \"{pairDto.Name}\" — using active viewport.");
                }

                // Replace any existing pair with the same name so re-opening the same
                // document in the same Rhino session works correctly.
                registry.RemoveByName(pairDto.Name, conduit);

                double aspect = pairDto.PixelWidth > 0 && pairDto.PixelHeight > 0
                    ? (double)pairDto.PixelWidth / pairDto.PixelHeight
                    : 1.0;

                var pair = new PhotoPlanePair(
                    pairDto.Name,
                    pairDto.ImagePath,
                    aspect,
                    pairDto.PixelWidth,
                    pairDto.PixelHeight,
                    vpId)
                {
                    Distance     = pairDto.Distance,
                    Scale        = pairDto.Scale,
                    Transparency = pairDto.Transparency,
                    FrameBaked   = pairDto.FrameBaked,
                    PlaneCenter  = new Point3d(pairDto.PlaneCenterX, pairDto.PlaneCenterY, pairDto.PlaneCenterZ),
                    PlaneRight   = new Vector3d(pairDto.PlaneRightX,  pairDto.PlaneRightY,  pairDto.PlaneRightZ),
                    PlaneUp      = new Vector3d(pairDto.PlaneUpX,     pairDto.PlaneUpY,     pairDto.PlaneUpZ),
                    PlaneWorldW  = pairDto.PlaneWorldW,
                    PlaneWorldH  = pairDto.PlaneWorldH,
                };

                foreach (var rp in pairDto.ReferencePairs)
                    pair.ReferencePairs.Add((
                        new Point3d(rp.WorldX, rp.WorldY, rp.WorldZ),
                        new Point2d(rp.ImageX, rp.ImageY)));

                foreach (var vl in pairDto.VanishingLines)
                {
                    var axis = Enum.TryParse<VanishingAxis>(vl.Axis, out var a) ? a : VanishingAxis.X;
                    pair.VanishingLines.Add(new VanishingLine(
                        new Point2d(vl.PixAX, vl.PixAY),
                        new Point2d(vl.PixBX, vl.PixBY),
                        axis));
                }

                if (pairDto.VanishingResult != null)
                {
                    var r = pairDto.VanishingResult;
                    pair.LastVanishingResult = new VanishingPointResult
                    {
                        VpX               = new Point2d(r.VpXx, r.VpXy),
                        VpY               = new Point2d(r.VpYx, r.VpYy),
                        VpZ               = r.HasVpZ ? new Point2d(r.VpZx, r.VpZy) : (Point2d?)null,
                        FocalLengthPixels = r.FocalLengthPixels,
                        LensLengthMm      = r.LensLengthMm,
                        HorizonY          = r.HorizonY,
                        HorizonAngle      = r.HorizonAngle,
                        CameraTiltDegrees = r.CameraTiltDegrees,
                    };
                }

                registry.AddRestoredPair(pair);
                conduit.InvalidateMaterial(pair.Name);
                restored++;
            }

            return restored;
        }
    }
}
