using Rhino;
using Rhino.Commands;
using Rhino.Input;
using RhinoPhotoMatch.Core;

namespace RhinoPhotoMatch.Commands
{
    /// <summary>
    /// PMSetPhotoplaneTransparency — sets the display transparency of a photo plane.
    /// 0 % = fully opaque (default), 100 % = fully transparent.
    /// </summary>
    public class SetPhotoplaneTransparencyCommand : Rhino.Commands.Command
    {
        public override string EnglishName => "PMSetPhotoplaneTransparency";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var plugin   = RhinoPhotoMatchPlugin.Instance;
            var registry = plugin.Registry;

            if (registry.Pairs.Count == 0)
            {
                RhinoApp.WriteLine("PMSetPhotoplaneTransparency: no photo planes in the registry.");
                return Result.Failure;
            }

            // Pick pair
            PhotoPlanePair? pair;
            if (registry.Pairs.Count == 1)
            {
                pair = registry.Pairs[0];
            }
            else
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (var p in registry.Pairs) names.Add(p.Name);

                string pick = names[0];
                var res = RhinoGet.GetString(
                    $"Photo plane ({string.Join(", ", names)})", false, ref pick);
                if (res != Result.Success) return res;

                pair = registry.FindByName(pick);
                if (pair == null)
                {
                    RhinoApp.WriteLine($"PMSetPhotoplaneTransparency: no plane named \"{pick}\".");
                    return Result.Failure;
                }
            }

            // Prompt for percentage
            double current = pair.Transparency * 100.0;
            double pct = current;
            var getResult = RhinoGet.GetNumber(
                $"Transparency % for \"{pair.Name}\" <{current:F0}>",
                true, ref pct, 0.0, 100.0);

            if (getResult == Result.Cancel) return Result.Cancel;
            if (getResult == Result.Nothing) pct = current; // Enter = keep current

            pair.Transparency = pct / 100.0;

            // Invalidate cached material so it rebuilds with new transparency
            plugin.Conduit.InvalidateMaterial(pair.Name);

            doc.Views.Redraw();
            RhinoApp.WriteLine($"\"{pair.Name}\" transparency set to {pct:F0} %.");
            return Result.Success;
        }
    }
}
