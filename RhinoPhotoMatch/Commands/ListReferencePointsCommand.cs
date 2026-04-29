using Rhino;
using Rhino.Commands;
using RhinoPhotoMatch.Core;

namespace RhinoPhotoMatch.Commands
{
    public class ListReferencePointsCommand : Command
    {
        public override string EnglishName => "PMListReferencePoints";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var registry = RhinoPhotoMatchPlugin.Instance.GetRegistry(doc);

            if (registry.Pairs.Count == 0)
            {
                RhinoApp.WriteLine("PMListReferencePoints: no photo planes in registry.");
                return Result.Failure;
            }

            var pair = PickPair(registry);
            if (pair == null) return Result.Cancel;

            if (pair.ReferencePairs.Count == 0)
            {
                RhinoApp.WriteLine($"PMListReferencePoints: no reference points stored for \"{pair.Name}\".");
                return Result.Success;
            }

            RhinoApp.WriteLine($"Reference points for \"{pair.Name}\" ({pair.ReferencePairs.Count} pairs):");
            RhinoApp.WriteLine($"  {"#",-4} {"World X",10} {"World Y",10} {"World Z",10}    {"Image X",10} {"Image Y",10}");
            RhinoApp.WriteLine($"  {new string('-', 60)}");

            for (int i = 0; i < pair.ReferencePairs.Count; i++)
            {
                var (world, image) = pair.ReferencePairs[i];
                RhinoApp.WriteLine($"  {i+1,-4} {world.X,10:F3} {world.Y,10:F3} {world.Z,10:F3}    {image.X,10:F1} {image.Y,10:F1}");
            }

            RhinoApp.WriteLine($"  Photo size: {pair.PixelWidth} × {pair.PixelHeight} px");
            return Result.Success;
        }

        private static PhotoPlanePair? PickPair(PhotoPlaneRegistry registry)
        {
            if (registry.Pairs.Count == 1) return registry.Pairs[0];

            var names = new System.Collections.Generic.List<string>();
            foreach (var p in registry.Pairs) names.Add(p.Name);

            string pick = names[0];
            var res = Rhino.Input.RhinoGet.GetString(
                $"Photo plane to calibrate ({string.Join(", ", names)})", false, ref pick);
            if (res != Result.Success) return null;

            return registry.FindByName(pick);
        }
    }
}
