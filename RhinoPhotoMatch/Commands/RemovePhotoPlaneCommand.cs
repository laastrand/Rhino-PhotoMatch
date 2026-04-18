using Rhino;
using Rhino.Commands;
using Rhino.Input;
using Rhino.Input.Custom;
using RhinoPhotoMatch.Core;

namespace RhinoPhotoMatch.Commands
{
    /// <summary>
    /// PMRemovePhotoPlane — lets the user pick a photo plane by name and deletes both
    /// the mesh and its linked named camera after a confirmation prompt.
    /// </summary>
    public class RemovePhotoPlaneCommand : Rhino.Commands.Command
    {
        public override string EnglishName => "PMRemovePhotoPlane";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var registry = RhinoPhotoMatchPlugin.Instance.Registry;

            if (registry.Pairs.Count == 0)
            {
                RhinoApp.WriteLine("PMRemovePhotoPlane: no photo planes in this document.");
                return Result.Nothing;
            }

            // Build option list from registered pair names
            var go = new GetOption();
            go.SetCommandPrompt("Select photo plane to remove");

            foreach (var pair in registry.Pairs)
                go.AddOption(pair.Name.Replace(" ", "_")); // option tokens can't have spaces

            go.Get();
            if (go.CommandResult() != Result.Success)
                return go.CommandResult();

            int idx = go.Option().Index - 1; // GetOption indices are 1-based
            if (idx < 0 || idx >= registry.Pairs.Count)
                return Result.Failure;

            var target = registry.Pairs[idx];

            // Confirmation
            string confirm = "No";
            RhinoApp.WriteLine($"This will permanently delete \"{target.Name}\" and its linked camera.");
            if (RhinoGet.GetString("Continue? (Yes/No)", false, ref confirm) != Result.Success)
                return Result.Cancel;

            if (!confirm.StartsWith("Y", System.StringComparison.OrdinalIgnoreCase))
            {
                RhinoApp.WriteLine("PMRemovePhotoPlane: cancelled.");
                return Result.Cancel;
            }

            RhinoPhotoMatchPlugin.Instance.Conduit.InvalidateMaterial(target.Name);
            registry.RemovePair(doc, target);
            doc.Views.Redraw();
            return Result.Success;
        }
    }
}
