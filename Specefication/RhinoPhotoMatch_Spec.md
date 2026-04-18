# RhinoPhotoMatch

> C# | RhinoCommon (Rhino 8) | Windows (primary), macOS (secondary)

**Tech stack:**
- Computer Vision: OpenCvSharp4 (NuGet)
- Linear Algebra: Math.NET Numerics (NuGet)
- UI: Eto.Forms (ships with Rhino)
- Serialization: System.Text.Json

---

# Plugin Scaffold

## Why
Claude Code needs a working Rhino plugin project before any features can be built.

## What
A minimal Rhino 8 C# plugin that loads successfully in Rhino with no functionality yet — just the correct structure and entry point.

## Constraints
### Must Not
- Not reference any external NuGet packages yet

### Out of Scope
- Any user-facing commands or UI

## Tasks
- Create a new RhinoCommon plugin project using the Rhino Visual Studio Extension template
- Verify the plugin loads in Rhino 8 without errors
- Set up the folder structure:
```
RhinoPhotoMatch/
├── RhinoPhotoMatchPlugin.cs
├── Commands/
├── Core/
└── UI/
```
- Add NuGet references: OpenCvSharp4, Math.NET Numerics
- Confirm OpenCV native DLLs are included in the build output

---

# Photo Import & Picture Plane

## Why
The user needs to bring a real-world photo into Rhino and place it as a reference object before any calibration can happen.

## What
A command `PMImportPhoto` that lets the user pick an image file, then places it as a Rhino picture frame object in the scene. The picture frame is stored by object ID for later use by other commands.

## Constraints
### Must Not
- Must not distort the photo aspect ratio when placing the picture frame
- Must not allow importing unsupported formats silently — show an error for anything other than JPG/PNG

### Out of Scope
- RAW, TIFF, or HDR image formats
- Automatic placement based on model extents

## Tasks
- Implement `ImportPhotoCommand.cs` — file picker dialog (JPG, PNG only)
- Place image as a `Rhino.DocObjects.PictureFrame` at the world origin, scaled to match aspect ratio
- Store the picture frame object ID in plugin state (`RhinoPhotoMatchPlugin` instance)
- Implement `PicturePlaneManager.cs` to encapsulate picture frame creation and retrieval
- Show a confirmation message in the Rhino command line with the photo dimensions

---

# Camera-Linked Picture Planes

## Why
A photo taken from a specific position in the real world corresponds to a specific camera viewpoint. The picture plane must be linked to that camera so that when the user looks through that camera, the photo sits correctly in the scene. Multiple photos from different viewpoints should each have their own camera and picture plane pair — for example one photo for the background and one for a foreground cutout. This also enables a compositing workflow where the user can edit the photo externally (e.g. in Photoshop) to separate foreground and background layers, then use those layers as physical planes in the Rhino scene — one behind the model (background) and one in front (foreground/cutout).

## What
When the user runs `PMImportPhoto`, the plugin creates a **named camera** (a named view in Rhino) and a **picture plane** as a pair. The picture plane is a flat mesh or surface placed in 3D space, with a dedicated Rhino material applied to it that uses the photo as a diffuse texture (alpha-transparent PNG supported for foreground cutouts). Multiple picture plane / camera pairs can exist simultaneously in the same document. Each pair is managed independently — they can be calibrated, renamed, and toggled independently.

The plugin tracks all pairs in a `PhotoPlaneRegistry` so commands always know which camera belongs to which plane.

## Constraints
### Must Not
- Must not share materials between picture planes — each plane gets its own unique material instance
- Must not use Rhino's built-in PictureFrame object type for this feature — use a flat mesh/surface with a custom material so alpha transparency works correctly in rendered viewports
- Must not delete the camera if the picture plane is deleted without warning the user, and vice versa

### Out of Scope
- Animated or video textures
- HDR / EXR image formats
- Automatic layering order beyond what the user sets by positioning planes in Z

## Tasks
- Update `ImportPhotoCommand.cs`:
  - After placing the picture plane, create a matching named view (camera) in `RhinoDoc.NamedViews`
  - Name both with a shared identifier, e.g. `PM_Photo_01`, `PM_Photo_02`, incrementing automatically
  - Prompt the user to optionally rename the pair after creation
- Implement `PhotoPlaneRegistry.cs`:
  - Tracks all picture plane / camera pairs as a list of `PhotoPlanePair` records
  - Each record holds: picture plane object ID, named view name, source image path, material ID
  - Persisted as part of session data (see Session Save & Restore feature)
- Implement material creation in `PicturePlaneManager.cs`:
  - Create a new `Rhino.DocObjects.Material` for each picture plane
  - Set the photo as the diffuse texture: `material.SetBitmapTexture(imagePath)`
  - For PNG files, also set the texture as the transparency/alpha map to support foreground cutouts
  - Assign the material to the picture plane object
  - Set material to be self-illuminated (no shading) so it displays the photo faithfully in rendered mode
- Implement `PMAddPhotoPlane` command — adds a new picture plane / camera pair (can be called multiple times)
- Implement `PMRemovePhotoPlane` command — removes a pair, warns user before deleting both objects
- Implement `PMRelinkPhoto` command — re-points the material texture to a new file path (for when the user has edited the photo externally)
  - Automatically refreshes the texture in the Rhino viewport after relinking
- Add a picture plane list to the dockable panel UI showing all registered pairs with: name, thumbnail, camera button (activates named view), relink button, delete button

---

# Focal Length Detection

## Why
The calibration solver needs a camera intrinsic matrix which requires a focal length. Asking the user to enter FOV in degrees is unintuitive and error-prone. Focal length in mm is what photographers understand, and most digital photos already contain this data in their EXIF metadata. The plugin should find the focal length automatically where possible and only ask the user to confirm or override it.

## What
When a photo is imported, the plugin reads its EXIF metadata to extract the 35mm-equivalent focal length. If EXIF is unavailable, the plugin estimates focal length by running `SolvePnPRefineLM` with a 50mm starting guess after reference points are collected. Either way, the result is shown to the user as a pre-filled focal length field (in mm) in the panel before calibration runs. The user can accept it or type their own value.

The field always shows mm, never FOV degrees. FOV is an internal detail only.

## Constraints
### Must Not
- Must not ask the user for FOV in degrees at any point — focal length in mm only
- Must not block photo import if EXIF is missing — silently fall back to estimation
- Must not run `SolvePnPRefineLM` estimation until at least 6 reference point pairs exist

### Out of Scope
- Reading other EXIF data (aperture, shutter speed, GPS, etc.)
- Lens distortion coefficients from EXIF
- Supporting cameras with non-35mm crop factors (assume 35mm equivalent is always used)

## Tasks
- Add `MetadataExtractor` NuGet package (lightweight, no native dependencies)
- Implement `FocalLengthDetector.cs` in `Core/`:
  - `TryReadExif(string imagePath, out double focalLength35mm)` — reads `TagFocalLengthIn35mmFilm` from EXIF, returns false if not found
  - `EstimateFromPoints(objMat, imgMat, imageWidth, imageHeight, out double focalLength35mm)` — runs EPnP then `SolvePnPRefineLM` with 50mm starting guess, reads back solved `fx`, converts to 35mm equivalent: `focalLength = (fx / imageWidth) * 36.0`
- In `ImportPhotoCommand.cs`: call `TryReadExif` after loading the photo, store result on `PhotoPlanePair` as `DetectedFocalLengthMm` and `FocalLengthSource` (enum: `Exif`, `Estimated`, `UserProvided`)
- In the dockable panel, show a focal length field before the Calibrate button:
  - Label: `"Focal length (mm)"` 
  - Pre-filled with detected value and source hint: `"28 mm  (from EXIF)"` or `"50 mm  (estimated)"`
  - User can edit the value freely
  - If fewer than 6 pairs exist and no EXIF, show `"50 mm  (default — add more points for estimation)"`
- Pass the final mm value from the panel into `CalibrationSolver.SolvePnP()` converted to FOV: `fovRad = 2.0 * Math.Atan(imageWidth / (2.0 * (focalLength35mm / 36.0 * imageWidth)))`
- Remove the `horizontalFovDegrees` parameter from `CalibrationSolver.SolvePnP()` — replace with `focalLength35mm` parameter

---

# Camera Calibration — Reference Points (PnP)

## Why
To match Rhino's camera to the photo perspective, the user needs to be able to pair known 3D points on the model with their corresponding 2D locations in the photo.

## What
A command `PMSetReferencePoints` that guides the user through picking 3D model points and marking the matching 2D positions on the photo. The pairs are stored and later passed to the calibration solver.

A command `PMCalibrate` (method: PnP) that passes the stored point pairs to `OpenCvSharp.Cv2.SolvePnP()` and returns the solved camera pose (rotation + translation).

## Constraints
### Must Not
- Must not proceed with fewer than 4 point pairs — show an error
- Must not modify the Rhino viewport until the user explicitly confirms the calibration result

### Out of Scope
- Automatic feature detection or image-based point suggestions
- Lens distortion modelling

## Tasks
- Implement `SetReferencePointsCommand.cs`:
  - Loop: user picks a 3D point in viewport, then clicks the matching location on the photo
  - Draw numbered markers at each picked point using a `DisplayConduit`
  - Store pairs as `List<(Point3d worldPoint, Point2d imagePoint)>` in plugin state
- Implement `CalibrationSolver.cs`:
  - Accept `focalLength35mm` (not FOV degrees) and derive `fx` from it: `fx = (focalLength35mm / 36.0) * imageWidth`
  - Call `Cv2.SolvePnP()` using EPnP for initial estimate, then `SOLVEPNP_ITERATIVE` to refine
  - Return rotation vector, translation vector, and reprojection error
- Implement `ViewportSync.cs`:
  - Apply solved camera to viewport via `SetCameraLocation()`, `SetCameraDirection()`, `CameraUp`, `Camera35mmLensLength`
- Show reprojection error and solved focal length in command line after calibration

---

# Camera Calibration — Vanishing Points

## Why
When the user does not have known 3D coordinates to use as reference points, vanishing lines drawn along parallel features in the photo can be used to derive the camera orientation and FOV.

## What
A command `PMSetVanishingLines` that lets the user draw line pairs along parallel edges in the photo. The solver computes the vanishing points, horizon line, and derives camera parameters from these.

## Constraints
### Must Not
- Must not allow fewer than 2 line pairs per vanishing point — show an error
- Must not overwrite an existing PnP calibration without user confirmation

### Out of Scope
- Automatic edge detection to suggest vanishing lines
- Three-point perspective (only one- and two-point perspective supported in v1.0)

## Tasks
- Implement `SetVanishingLinesCommand.cs`:
  - User draws line segments on the photo in the viewport
  - Lines are grouped into sets representing the same vanishing direction
  - Store line sets in plugin state
- Extend `CalibrationSolver.cs` with a vanishing point solver:
  - Compute intersection of each line set to find vanishing points
  - Derive horizon line from two vanishing points
  - Compute camera FOV and orientation from vanishing point geometry
- Pass result through `ViewportSync.cs` same as PnP method

---

# Overlay & Alignment Feedback

## Why
After calibration, the user needs visual feedback to see how well the 3D model aligns with the photo. A wireframe overlay projected onto the picture plane makes misalignment immediately visible.

## What
A `DisplayConduit` that draws projected model edges on top of the picture plane in the calibrated viewport. A command `PMToggleOverlay` turns it on and off.

## Constraints
### Must Not
- Must not affect other viewports — overlay must only draw in the calibrated viewport
- Must not cause viewport lag — skip overlay draw if it takes longer than 50ms

### Out of Scope
- Shaded or rendered overlay modes
- Per-object overlay visibility

## Tasks
- Implement `OverlayConduit.cs` deriving from `Rhino.Display.DisplayConduit`
  - Override `DrawOverlay` to project visible model edges into 2D and draw over the picture plane
  - Only activate in the named calibrated viewport
- Implement `ToggleOverlayCommand.cs` — enables/disables the conduit
- Wire conduit enable/disable into the dockable panel UI

---

# View Lock

## Why
Once calibrated, the user needs to prevent accidentally rotating or panning the viewport and losing the matched camera position.

## What
A command `PMLockView` that toggles a locked state on the calibrated viewport, preventing camera manipulation.

## Constraints
### Must Not
- Must not lock object selection or editing — only camera movement
- Must not lock viewports other than the calibrated one

### Out of Scope
- Per-axis lock (e.g. allow pan but not rotate)

## Tasks
- Implement `LockViewCommand.cs`
- Use `RhinoViewport` camera lock or intercept `MouseMove` events on the viewport to suppress camera changes when locked
- Show lock state in the dockable panel (locked / unlocked indicator)

---

# Session Save & Restore

## Why
The user needs to be able to close and reopen a Rhino document and continue working with the same calibration without redoing the setup.

## What
Calibration data (camera parameters, reference points, photo path, method used) is serialized to JSON and stored in Rhino document properties. On document open the plugin restores the session automatically.

## Constraints
### Must Not
- Must not silently fail if the photo file has moved — show a warning and let the user relink it

### Out of Scope
- Cloud sync or external session storage
- Multiple saved calibrations per document (v1.0 supports one calibration per document)

## Tasks
- Implement `SessionData.cs` — serializable record containing all calibration state
- Implement `SaveSessionCommand.cs` — serialize to JSON, store in `RhinoDoc.Strings`
- Implement `LoadSessionCommand.cs` — deserialize and restore plugin state, relink photo if needed
- Hook into `RhinoDoc.BeginSaveDocument` and `RhinoDoc.EndOpenDocument` events for automatic save/restore

---

# Dockable Panel UI

## Why
The individual commands need a unified, guided UI so the workflow is clear and discoverable without requiring the user to memorise command names. The panel also needs to show all loaded photo planes at a glance so the user can manage them, adjust transparency, and switch between cameras without using the command line.

## What
A single dockable Eto.Forms panel with two sections:

**Top section — Photo Plane List:** shows all registered photo planes, one row per plane, with thumbnail, name, distance from camera, transparency slider, and action buttons.

**Bottom section — Calibration Workflow:** guides the user through calibration for whichever plane is currently active.

## Constraints
### Must Not
- Must not use WinForms or WPF — use Eto.Forms only (required for future macOS support)
- Must not allow the user to proceed to a later step if an earlier required step is incomplete
- Must not recompute distance on every UI tick — update distance only on viewport change events or when the panel is explicitly refreshed

### Out of Scope
- Multiple simultaneous calibration sessions in the panel
- Theming or custom styling beyond Rhino's default Eto appearance
- Drag to reorder photo planes

## Tasks

**Photo Plane List (implement first)**

Each row in the list shows:
- **Thumbnail** — 48×48px, cropped from the source image file using `System.Drawing`. Load once and cache on `PhotoPlanePair`. Show a grey placeholder if the image cannot be read
- **Name** — the pair name (e.g. `PM_Photo_01`), non-editable label
- **Distance** — distance in model units from the current camera location to `pair.PlaneCenter`, formatted as `"12.3 m"`. Computed as `(vp.CameraLocation - pair.PlaneCenter).Length`. Updated when the active viewport changes via `RhinoView.SetActiveView` event or a Refresh button
- **Transparency slider** — Eto `Slider` 0–100, default 0 (fully opaque). On change, update the material transparency on the plane object: `material.Transparency = sliderValue / 100.0` then `doc.Materials.Modify(material, index, true)` and `doc.Views.Redraw()`
- **Activate camera button** — small button labelled `"📷"` or `"View"`. Activates the named view associated with the pair: find the named view by name in `doc.NamedViews` and restore it to the active viewport
- **Relink photo button** — small button labelled `"Relink"`. Opens a file picker (JPG/PNG), updates `pair.ImagePath`, calls `doc.Materials.Modify` to update the texture path, regenerates the thumbnail, redraws
- **Delete button** — small button labelled `"✕"`. Warns user with a confirmation dialog, then deletes the plane mesh object from the doc, removes the named view, removes from registry, removes the row from the list

Implement the list as a `Eto.Forms.StackLayout` of rows inside a `Scrollable`, rebuilt whenever the registry changes. Do not use `GridView` — it does not support mixed controls per cell in Eto.

**Calibration Workflow (implement second, below the list)**

- Step 1: Active plane selector — shows which plane is being calibrated (dropdown if multiple)
- Step 2: Focal length field (mm) — pre-filled from EXIF or estimate, user can override
- Step 3: Pick reference points button — triggers `PMSetReferencePoints`, shows count of pairs
- Step 4: Calibrate button — triggers `PMCalibrate`, shows reprojection error after
- Step 5: Fine-tune controls — nudge buttons (Forward/Back, Left/Right, Up/Down), step size selector (0.1 / 1.0 / 10.0), lens length +/- buttons

**Registration**

- Register panel with Rhino using `Rhino.UI.Panels.RegisterPanel()`
- Panel should be openable via `PMPanel` command and from the Rhino Panels menu

---

# fSpy Import

## Why
fSpy is a free, open source, cross-platform desktop app specifically designed for still image camera matching using vanishing points. It has a polished UI that is faster and more precise than doing the same workflow inside Rhino. There is currently no fSpy importer for Rhino despite repeated user requests on the McNeel forum since 2019. Adding fSpy import to RhinoPhotoMatch fills this gap and gives users a best-in-class calibration workflow: do the vanishing point work in fSpy, import the result directly into Rhino with one command.

## What
A command `PMImportFSpy` that reads an fSpy JSON export file and applies the solved camera parameters to the active photo plane's named view in Rhino. The fSpy JSON contains a ready-made 4x4 camera transform matrix plus image dimensions and focal length — no further solving is needed.

fSpy exports camera parameters via **File → Export → Camera parameters as JSON**. The resulting JSON contains:
- `cameraTransform.rows` — a 4x4 row-major camera-to-world transform matrix
- `horizontalFieldOfView` — camera FOV in radians
- `imageWidth` / `imageHeight` — pixel dimensions of the source image

fSpy uses a **right-handed Y-up coordinate system** — the same as Rhino — so no axis flip is required. This makes the conversion significantly simpler and more reliable than the OpenCV PnP approach.

## Constraints
### Must Not
- Must not require the user to manually copy or type any values — the entire import must be driven from the JSON file
- Must not apply the camera if the image dimensions in the JSON don't match the currently loaded photo — show a warning instead
- Must not overwrite an existing calibration without user confirmation

### Out of Scope
- Reading the binary `.fspy` project file format directly — use the JSON export only
- Importing fSpy's vanishing line data or reconstructing the calibration state from fSpy
- Batch import of multiple fSpy files

## Tasks
- Implement `ImportFSpyCommand.cs` — file picker for `.json` files
- Implement `FSpyImporter.cs` in `Core/`:
  - Deserialize the JSON using `System.Text.Json` into a typed `FSpyCameraData` record
  - Extract camera location from column 3 of the 4x4 matrix (rows[0][3], rows[1][3], rows[2][3])
  - Extract camera forward direction from the negated third column (rows[0][2], rows[1][2], rows[2][2])
  - Extract camera up vector from the second column (rows[0][1], rows[1][1], rows[2][1])
  - Convert `horizontalFieldOfView` (radians) to lens length in mm: `lensMM = 18.0 / tan(hFov / 2)`
  - Apply all values to the named view via `ViewportInfo`
- Validate image dimensions match loaded photo before applying — warn and abort if mismatch
- Show a summary in the command line after import: camera location, lens length, and confirmation that the named view was updated
- Add an **Import from fSpy** button to the dockable panel as an alternative calibration path alongside the vanishing lines method

---

# Camera Control Widget

## Why
After calibration — whether from vanishing points, fSpy import, or PnP — the result is rarely perfect on the first attempt. The user needs a way to fine-tune individual camera parameters interactively without re-running the full solver. Direct sliders for focal length, camera roll, and tilt give immediate visual feedback and allow precise adjustments in seconds.

## What
A panel section in the dockable UI with sliders and numeric input fields for the key camera parameters that need manual tweaking. Each control updates the Rhino viewport live as the value changes. The controls are only active when a calibrated named view is selected.

Parameters exposed:
- **Focal length** (mm) — controls FOV / perspective compression. Range 10–200mm, step 0.5mm
- **Camera roll** — rotation around the camera's own forward axis. Range ±15°, step 0.1°. Corrects horizon tilt
- **Camera tilt** — pitch up/down. Range ±30°, step 0.1°. Adjusts horizon height in the frame
- **Camera height** — vertical position in world space (Z). Range dependent on model scale, step 0.01m. Useful when the real-world camera height is known (e.g. eye level = 1.6m)

Each parameter has:
- A labeled slider
- A numeric text field showing the current value, editable directly
- A reset button (↺) that returns that parameter to the last solved value

## Constraints
### Must Not
- Must not use WinForms or WPF — Eto.Forms only
- Must not update the viewport on every slider tick if the update takes longer than ~16ms — debounce or throttle to maintain smooth interaction
- Must not change the camera if the view is locked — show a visual indicator that the view is locked and disable the sliders

### Out of Scope
- Animating between camera states
- Saving multiple named presets of camera parameters
- Controlling camera position in X/Y (only Z / height is exposed — X/Y position is set by the calibration solver)

## Tasks
- Implement `CameraControlPanel.cs` as a section within `PhotoMatchPanel.cs`
- Focal length slider:
  - Read current lens length from `ViewportInfo` on panel activation
  - On change: call `vpInfo.SetCameraAngle()` with the converted FOV and trigger viewport redraw
- Roll slider:
  - Read current up vector from `ViewportInfo`
  - On change: rotate the up vector around the camera direction vector by the roll delta using `Transform.Rotation(rollRadians, cameraDirection, Point3d.Origin)`, apply via `vpInfo.SetCameraUp()`
- Tilt slider:
  - On change: rotate both camera direction and up vector around the camera's right axis by the tilt delta
  - Camera right axis = `Vector3d.CrossProduct(cameraDirection, cameraUp)`
- Camera height slider:
  - On change: move camera location vertically — only modify the Z component of `vpInfo.CameraLocation`
  - Also move camera target by the same Z delta to preserve the viewing direction
- Store the "last solved" values when calibration completes so the reset buttons have a reference point
- Disable all sliders and show a "View locked" label when `PMLockView` is active

---

# Reference Links

- RhinoCommon SDK: https://developer.rhino3d.com/guides/rhinocommon/
- Your First Plugin (Windows): https://developer.rhino3d.com/guides/rhinocommon/your-first-plugin-windows/
- OpenCvSharp: https://github.com/shimat/opencvsharp
- OpenCV solvePnP: https://docs.opencv.org/4.x/d9/d0c/group__calib3d.html
- Math.NET Numerics: https://numerics.mathdotnet.com/
- Eto.Forms: https://github.com/picoe/Eto
- fSpy: https://fspy.io/
- fSpy Blender importer (reference for matrix conversion): https://github.com/stuffmatic/fSpy-Blender
- fSpy file format: https://github.com/stuffmatic/fSpy/blob/develop/project_file_format.md