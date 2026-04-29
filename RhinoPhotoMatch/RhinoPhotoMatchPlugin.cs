using System;
using System.Collections.Generic;
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

        // ------------------------------------------------------------------ //
        //  Per-document state — one registry + conduit per open document
        // ------------------------------------------------------------------ //

        private readonly Dictionary<uint, (PhotoPlaneRegistry Registry, PhotoPlaneConduit Conduit)>
            _states = new();

        public PhotoPlaneRegistry GetRegistry(RhinoDoc doc)  => GetOrCreate(doc.RuntimeSerialNumber).Registry;
        public PhotoPlaneConduit  GetConduit(RhinoDoc doc)   => GetOrCreate(doc.RuntimeSerialNumber).Conduit;
        public PhotoPlaneRegistry GetRegistry(uint sn)       => GetOrCreate(sn).Registry;
        public PhotoPlaneConduit  GetConduit(uint sn)        => GetOrCreate(sn).Conduit;

        private (PhotoPlaneRegistry Registry, PhotoPlaneConduit Conduit) GetOrCreate(uint sn)
        {
            if (!_states.TryGetValue(sn, out var state))
            {
                var registry = new PhotoPlaneRegistry();
                var conduit  = new PhotoPlaneConduit(registry);
                conduit.Enabled = true;
                state = (registry, conduit);
                _states[sn] = state;
            }
            return state;
        }

        // ------------------------------------------------------------------ //
        //  Plugin lifecycle
        // ------------------------------------------------------------------ //

        protected override LoadReturnCode OnLoad(ref string errorMessage)
        {
            // Make NuGet dependency DLLs findable from the plugin's output directory.
            var pluginDir = Path.GetDirectoryName(GetType().Assembly.Location) ?? "";

            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                var name = new AssemblyName(args.Name).Name;
                var dll  = Path.Combine(pluginDir, name + ".dll");
                return File.Exists(dll) ? Assembly.LoadFrom(dll) : null;
            };

            var openCvManagedPath = Path.Combine(pluginDir, "OpenCvSharp.dll");
            if (File.Exists(openCvManagedPath))
            {
                var openCvAssembly = Assembly.LoadFrom(openCvManagedPath);
                NativeLibrary.SetDllImportResolver(openCvAssembly, (libName, _, _) =>
                {
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

            Rhino.UI.Panels.RegisterPanel(
                this,
                typeof(UI.PhotoMatchPanel),
                "Photo Match",
                System.Drawing.SystemIcons.Information);

            RhinoDoc.BeginSaveDocument += OnBeginSaveDocument;
            RhinoDoc.EndOpenDocument   += OnEndOpenDocument;
            RhinoDoc.CloseDocument     += OnCloseDocument;

            RhinoApp.WriteLine("RhinoPhotoMatch loaded.");
            return LoadReturnCode.Success;
        }

        protected override void OnShutdown()
        {
            RhinoDoc.BeginSaveDocument -= OnBeginSaveDocument;
            RhinoDoc.EndOpenDocument   -= OnEndOpenDocument;
            RhinoDoc.CloseDocument     -= OnCloseDocument;

            foreach (var state in _states.Values)
                state.Conduit.Enabled = false;
            _states.Clear();
        }

        // ------------------------------------------------------------------ //
        //  Document event handlers
        // ------------------------------------------------------------------ //

        private void OnCloseDocument(object? sender, DocumentEventArgs e)
        {
            if (e.Document == null) return;
            uint sn = e.Document.RuntimeSerialNumber;
            if (_states.TryGetValue(sn, out var state))
            {
                state.Conduit.Enabled = false;
                _states.Remove(sn);
            }
        }

        private void OnBeginSaveDocument(object? sender, DocumentSaveEventArgs e)
        {
            var doc = e.Document;
            if (doc == null) return;
            var registry = GetRegistry(doc);
            if (registry.Pairs.Count == 0) return;
            Core.SessionSerializer.Save(doc, registry);
        }

        private void OnEndOpenDocument(object? sender, DocumentOpenEventArgs e)
        {
            var doc = e.Document;
            if (doc == null) return;
            _pendingRestoreDocs.Enqueue(doc);
            RhinoApp.Idle += OnIdleRestoreOnce;
        }

        private readonly Queue<RhinoDoc> _pendingRestoreDocs = new();

        private void OnIdleRestoreOnce(object? sender, EventArgs e)
        {
            RhinoApp.Idle -= OnIdleRestoreOnce;

            while (_pendingRestoreDocs.TryDequeue(out var doc))
            {
                var (registry, conduit) = GetOrCreate(doc.RuntimeSerialNumber);
                int n = Core.SessionSerializer.Load(doc, registry, conduit);
                if (n > 0)
                {
                    doc.Views.Redraw();
                    RhinoApp.WriteLine($"RhinoPhotoMatch: {n} photo plane(s) restored from document.");
                }
            }
        }
    }
}
