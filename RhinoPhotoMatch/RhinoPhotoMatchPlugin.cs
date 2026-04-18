using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Rhino;
using Rhino.PlugIns;
using RhinoPhotoMatch.Core;

[assembly: System.Runtime.InteropServices.Guid("A8E3F2C1-D4B7-4A5E-9C3F-B2D8E1A6C9F4")]

namespace RhinoPhotoMatch
{
    public class RhinoPhotoMatchPlugin : PlugIn
    {
        public RhinoPhotoMatchPlugin()
        {
            Instance = this;
        }

        public static RhinoPhotoMatchPlugin Instance { get; private set; } = null!;

        public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

        // --- Feature: Camera-Linked Picture Planes ---

        /// <summary>Registry of all photo plane / named camera pairs for this session.</summary>
        public PhotoPlaneRegistry Registry { get; } = new PhotoPlaneRegistry();

        /// <summary>DisplayConduit that draws photo planes each frame.</summary>
        public PhotoPlaneConduit Conduit { get; private set; } = null!;

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            // Make NuGet dependency DLLs findable from the plugin's output directory.
            // Rhino does not automatically search there, so we hook AssemblyResolve.
            var pluginDir = Path.GetDirectoryName(GetType().Assembly.Location) ?? "";

            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                var name = new AssemblyName(args.Name).Name;
                var dll  = Path.Combine(pluginDir, name + ".dll");
                return File.Exists(dll) ? Assembly.LoadFrom(dll) : null;
            };

            // Load OpenCvSharp.dll explicitly now so we can install a P/Invoke resolver on it
            // before any of its types are touched. This intercepts the DllImport("OpenCvSharpExtern")
            // calls and redirects them to the full path in the plugin directory.
            var openCvManagedPath = Path.Combine(pluginDir, "OpenCvSharp.dll");
            if (File.Exists(openCvManagedPath))
            {
                var openCvAssembly = Assembly.LoadFrom(openCvManagedPath);
                NativeLibrary.SetDllImportResolver(openCvAssembly, (libName, _, _) =>
                {
                    // Check the plugin dir first, then the runtimes subdirectory where
                    // NuGet places native DLLs when not publishing a self-contained app.
                    var candidates = new[]
                    {
                        Path.Combine(pluginDir, libName + ".dll"),
                        Path.Combine(pluginDir, "runtimes", "win-x64", "native", libName + ".dll"),
                    };
                    foreach (var candidate in candidates)
                        if (NativeLibrary.TryLoad(candidate, out var handle))
                            return handle;
                    return IntPtr.Zero;
                });
            }

            Conduit = new PhotoPlaneConduit(Registry);
            Conduit.Enabled = true;

            // Register the dockable panel
            Rhino.UI.Panels.RegisterPanel(
                this,
                typeof(UI.PhotoMatchPanel),
                "Photo Match",
                System.Drawing.SystemIcons.Information);

            // Auto-save session data whenever the document is about to be written
            RhinoDoc.BeginSaveDocument += OnBeginSaveDocument;

            // Auto-restore session data after a document has finished loading
            RhinoDoc.EndOpenDocument += OnEndOpenDocument;

            RhinoApp.WriteLine("RhinoPhotoMatch loaded.");
            return LoadReturnCode.Success;
        }

        protected override void OnShutdown()
        {
            RhinoDoc.BeginSaveDocument -= OnBeginSaveDocument;
            RhinoDoc.EndOpenDocument   -= OnEndOpenDocument;

            if (Conduit != null)
                Conduit.Enabled = false;
        }

        private void OnBeginSaveDocument(object? sender, DocumentSaveEventArgs e)
        {
            // Only auto-save to the document being saved (not autosave backups)
            var doc = e.Document;
            if (doc == null || Registry.Pairs.Count == 0) return;
            Core.SessionSerializer.Save(doc, Registry);
        }

        private void OnEndOpenDocument(object? sender, DocumentOpenEventArgs e)
        {
            var doc = e.Document;
            if (doc == null) return;

            // Defer one idle tick so Rhino has finished building the views collection
            // before we try to look up viewports by name.
            RhinoApp.Idle += OnIdleRestoreOnce;
            _pendingRestoreDoc = doc;
        }

        private RhinoDoc? _pendingRestoreDoc;

        private void OnIdleRestoreOnce(object? sender, EventArgs e)
        {
            RhinoApp.Idle -= OnIdleRestoreOnce;   // fire only once

            var doc = _pendingRestoreDoc;
            _pendingRestoreDoc = null;
            if (doc == null) return;

            int n = Core.SessionSerializer.Load(doc, Registry, Conduit);
            if (n > 0)
            {
                doc.Views.Redraw();
                RhinoApp.WriteLine($"RhinoPhotoMatch: {n} photo plane(s) restored from document.");
            }
        }
    }
}
