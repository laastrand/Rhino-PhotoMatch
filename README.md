# RhinoPhotoMatch

A Rhino 8 plugin for photo-to-model camera matching. Import a photo, calibrate the Rhino camera to match the real-world camera that took it, and composite your 3D model seamlessly over the photo.

**Tech stack:** C# · RhinoCommon (Rhino 8) · OpenCvSharp4 · Math.NET Numerics · Eto.Forms · System.Text.Json

---

## Features

### Photo Plane Management
- **PMImportPhoto** — import a JPG or PNG as a flat mesh with a dedicated material; creates a linked named camera view automatically
- **PMAddPhotoPlane** — add additional photo plane / camera pairs (e.g. a foreground cutout and a background layer)
- **PMRemovePhotoPlane** — remove a pair with a confirmation prompt
- **PMRelinkPhoto** — re-point a plane's texture to a new file path and refresh the viewport
- Alpha-transparent PNG supported for foreground cutouts in rendered viewports
- Each plane gets its own unique material instance; no sharing between planes

### Camera Calibration — PnP (Reference Points)
- **PMSetReferencePoints** — interactively pair 3D model points with their matching 2D pixel locations in the photo
- **PMCalibrate** — solves camera pose via OpenCV `SolvePnP` (EPnP for ≥6 points, iterative refinement for all)
- Requires at least 4 point pairs; shows reprojection error in the command line after solving
- Focal length auto-detected from EXIF (`FocalLengthIn35mmFilm` tag) or estimated via PnP; shown as mm in the UI

### Camera Calibration — Vanishing Points
- **PMSetVanishingLines** / **SolveVanishingPoints** — draw line pairs along parallel edges in the photo; the solver derives camera orientation and FOV from the vanishing point geometry
- Supports one- and two-point perspective

### Overlay & Feedback
- **PMToggleOverlay** — `DisplayConduit` that projects visible model edges over the photo in the calibrated viewport only; skips draw if it takes longer than 50 ms
- **PMLockView** — prevents accidental camera rotation/pan after calibration; disables camera control sliders in the panel

### Session Save & Restore
- **PMSaveSession** / **PMLoadSession** — all calibration data (camera parameters, reference points, photo paths, method used) serialized to JSON and stored in `RhinoDoc.Strings`
- Auto-saves on `BeginSaveDocument` and restores on `EndOpenDocument`
- Warns and prompts to relink if the photo file has moved

### Dockable Panel UI
- **PMPanel** — open via command or the Rhino Panels menu
- **Photo plane list** — thumbnail, name, distance from camera, transparency slider, activate-camera button, relink button, delete button
- **Calibration workflow** — active plane selector → focal length field (mm) → pick reference points → calibrate → fine-tune controls
- **Camera control sliders** — live adjustment of focal length (10–200 mm), roll (±15°), tilt (±30°), and camera height (Z); each with a numeric field and reset button; disabled when view is locked

---

## Project Structure

```
RhinoPhotoMatch/
├── RhinoPhotoMatchPlugin.cs      # Plugin entry point
├── Commands/
│   ├── ImportPhotoCommand.cs
│   ├── AddPhotoPlaneCommand.cs
│   ├── RemovePhotoPlaneCommand.cs
│   ├── RelinkPhotoCommand.cs
│   ├── SetReferencePointsCommand.cs
│   ├── CalibrateCommand.cs
│   ├── AddVanishingLineCommand.cs
│   ├── SolveVanishingPointsCommand.cs
│   ├── SetScaleCommand.cs
│   ├── ScalePhotoPlaneCommand.cs
│   ├── SetPhotoplaneTransparencyCommand.cs
│   ├── ExtractPhotoPlaneCommand.cs
│   ├── SaveSessionCommand.cs
│   ├── LoadSessionCommand.cs
│   └── PanelCommand.cs
├── Core/
│   ├── PhotoPlanePair.cs         # Record: plane ID, named view name, image path, material ID
│   ├── PhotoPlaneRegistry.cs     # Tracks all plane/camera pairs
│   ├── PicturePlaneManager.cs    # Plane mesh + material creation and retrieval
│   ├── CalibrationSolver.cs      # OpenCV SolvePnP wrapper
│   ├── ViewportSync.cs           # Applies solved camera to RhinoViewport
│   ├── VanishingLine.cs
│   ├── VanishingPointSolver.cs
│   ├── PhotoPlaneConduit.cs      # DisplayConduit for overlay
│   └── SessionData.cs            # JSON-serializable calibration state
└── UI/
    └── PhotoMatchPanel.cs        # Eto.Forms dockable panel
```

---

## Requirements

- Rhino 8 for Windows (macOS support planned)
- .NET 7.0 (bundled with Rhino 8)
- NuGet packages (restored automatically on build):
  - `OpenCvSharp4` + `OpenCvSharp4.runtime.win`
  - `MathNet.Numerics`

---

## Building

```
dotnet build RhinoPhotoMatch.sln
```

The build output is a `.rhp` file. Load it in Rhino via **Tools → Options → Plug-ins → Install**.

---

## Workflow

1. Open a Rhino document with your 3D model
2. Run **PMImportPhoto** and select a JPG or PNG — a photo plane and linked named camera are created
3. Open the **PMPanel** dockable panel
4. Confirm or adjust the focal length (mm) — auto-read from EXIF where available
5. Click **Pick Reference Points** and pair at least 4 (ideally 6+) 3D model points with their photo positions
6. Click **Calibrate** — the named camera updates to match the photo perspective
7. Toggle **Overlay** to check alignment; use the camera control sliders to fine-tune
8. Run **PMLockView** to lock the calibrated camera
9. Save the document — calibration data is stored inside the `.3dm` file automatically

For foreground/background compositing, run **PMAddPhotoPlane** a second time with an alpha-masked PNG to add a foreground cutout plane in front of the model.
