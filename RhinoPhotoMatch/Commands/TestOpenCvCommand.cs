using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Rhino;
using Rhino.Commands;

namespace RhinoPhotoMatch.Commands
{
    /// <summary>
    /// PMTestOpenCV — diagnostic command that checks each step of OpenCV loading.
    /// Run this after PMCalibrate fails to pinpoint the exact failure.
    /// </summary>
    public class TestOpenCvCommand : Command
    {
        public override string EnglishName => "PMTestOpenCV";

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            var pluginDir = Path.GetDirectoryName(
                RhinoPhotoMatchPlugin.Instance.GetType().Assembly.Location) ?? "";

            RhinoApp.WriteLine("=== PMTestOpenCV diagnostic ===");
            RhinoApp.WriteLine($"  Plugin dir : {pluginDir}");

            // ---- 1. File presence ----
            Check("OpenCvSharp.dll present",
                File.Exists(Path.Combine(pluginDir, "OpenCvSharp.dll")));
            Check("OpenCvSharpExtern.dll present",
                File.Exists(Path.Combine(pluginDir, "OpenCvSharpExtern.dll")));
            Check("opencv_videoio_ffmpeg490_64.dll present",
                File.Exists(Path.Combine(pluginDir, "opencv_videoio_ffmpeg490_64.dll")));

            // ---- 2. Managed assembly already loaded? ----
            Assembly? openCvAsm = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name == "OpenCvSharp") { openCvAsm = a; break; }
            }
            Check("OpenCvSharp assembly in AppDomain", openCvAsm != null);
            if (openCvAsm != null)
                RhinoApp.WriteLine($"    Location : {openCvAsm.Location}");

            // ---- 3. Load managed assembly if not yet loaded ----
            if (openCvAsm == null)
            {
                var path = Path.Combine(pluginDir, "OpenCvSharp.dll");
                if (File.Exists(path))
                {
                    try
                    {
                        openCvAsm = Assembly.LoadFrom(path);
                        RhinoApp.WriteLine($"  [OK] Loaded OpenCvSharp.dll from {path}");
                    }
                    catch (Exception ex)
                    {
                        RhinoApp.WriteLine($"  [FAIL] LoadFrom OpenCvSharp.dll: {ex.Message}");
                    }
                }
            }

            // ---- 4. Native library load ----
            var nativePath = Path.Combine(pluginDir, "OpenCvSharpExtern.dll");
            if (File.Exists(nativePath))
            {
                try
                {
                    var handle = NativeLibrary.Load(nativePath);
                    Check("NativeLibrary.Load(OpenCvSharpExtern.dll)", handle != IntPtr.Zero);
                    if (handle != IntPtr.Zero)
                        NativeLibrary.Free(handle);   // release our extra reference
                }
                catch (Exception ex)
                {
                    RhinoApp.WriteLine($"  [FAIL] NativeLibrary.Load: {ex.GetType().Name}: {ex.Message}");
                    if (ex.InnerException != null)
                        RhinoApp.WriteLine($"    Inner: {ex.InnerException.Message}");
                }
            }
            else
            {
                RhinoApp.WriteLine("  [SKIP] OpenCvSharpExtern.dll not found — cannot test native load");
            }

            // ---- 5. Smoke-test: create a Mat ----
            RhinoApp.WriteLine("  Smoke test: new Mat()…");
            try
            {
                using var m = new OpenCvSharp.Mat(4, 4, OpenCvSharp.MatType.CV_64FC1);
                Check("new Mat(4,4,CV_64FC1)", !m.Empty());
                RhinoApp.WriteLine($"    Mat: {m.Rows}x{m.Cols} type={m.Type()}");
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"  [FAIL] new Mat(): {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    RhinoApp.WriteLine($"    Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }

            // ---- 6. Smoke-test: SolvePnP ----
            RhinoApp.WriteLine("  Smoke test: Cv2.SolvePnP (4 points)…");
            try
            {
                var objData = new float[4, 3]
                {
                    { 0, 0, 0 }, { 100, 0, 0 }, { 100, 100, 0 }, { 0, 100, 0 }
                };
                var imgData = new float[4, 2]
                {
                    { 320, 240 }, { 640, 240 }, { 640, 480 }, { 320, 480 }
                };
                double[,] camData = { { 800, 0, 640 }, { 0, 800, 360 }, { 0, 0, 1 } };
                using var objMat  = OpenCvSharp.Mat.FromArray(objData);
                using var imgMat  = OpenCvSharp.Mat.FromArray(imgData);
                using var camMat  = OpenCvSharp.Mat.FromArray(camData);
                using var distMat = OpenCvSharp.Mat.Zeros(1, 4, OpenCvSharp.MatType.CV_64FC1);
                using var rvec    = new OpenCvSharp.Mat();
                using var tvec    = new OpenCvSharp.Mat();

                OpenCvSharp.Cv2.SolvePnP(objMat, imgMat, camMat, distMat, rvec, tvec,
                    useExtrinsicGuess: false,
                    flags: (OpenCvSharp.SolvePnPFlags)1); // EPnP

                Check("Cv2.SolvePnP returned", !rvec.Empty() && !tvec.Empty());
                RhinoApp.WriteLine($"    rvec: {rvec.Rows}x{rvec.Cols}  tvec: {tvec.Rows}x{tvec.Cols}");
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"  [FAIL] Cv2.SolvePnP: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                    RhinoApp.WriteLine($"    Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }

            RhinoApp.WriteLine("=== PMTestOpenCV done ===");
            return Result.Success;
        }

        private static void Check(string label, bool ok) =>
            RhinoApp.WriteLine($"  [{(ok ? "OK  " : "FAIL")}] {label}");
    }
}
