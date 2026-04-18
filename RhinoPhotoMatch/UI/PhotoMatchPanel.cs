using System;
using System.Collections.Generic;
using System.IO;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using RhinoPhotoMatch.Core;

namespace RhinoPhotoMatch.UI
{
    /// <summary>
    /// Dockable panel showing all registered photo plane pairs and the calibration workflow.
    /// Open with the PMPanel command or from Rhino's panel menu.
    /// </summary>
    [System.Runtime.InteropServices.Guid("B3F2A1E4-C7D8-4B6F-A9E2-D3C5F8B1E7A2")]
    public class PhotoMatchPanel : Panel
    {
        public static Guid PanelId => typeof(PhotoMatchPanel).GUID;

        private readonly uint _documentSerialNumber;

        // ---- List section state ----
        private readonly StackLayout _listStack;

        // ---- Workflow section state ----
        private PhotoPlanePair? _activePair;
        private DropDown      _pairDropDown   = null!;
        private NumericStepper _focalStepper  = null!;
        private Label         _focalHintLabel = null!;
        private Label         _refCountLabel  = null!;
        private Label         _reprErrorLabel = null!;
        private Panel         _fineTunePanel  = null!;
        private Label         _lensLabel      = null!;
        private double        _stepSize       = 1.0;
        private bool          _suppressDropDownChange;

        public PhotoMatchPanel(uint documentSerialNumber)
        {
            _documentSerialNumber = documentSerialNumber;

            // ----------------------------------------------------------------
            //  List section (unchanged)
            // ----------------------------------------------------------------

            _listStack = new StackLayout
            {
                Orientation = Orientation.Vertical,
                Spacing = 1,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Padding(0),
            };

            var scrollable = new Scrollable
            {
                Content = _listStack,
                Border = BorderType.None,
                ExpandContentWidth = true,
            };

            var addBtn = new Button { Text = "+ Add Photo Plane", Width = 130 };
            addBtn.Click += (_, _) => RhinoApp.RunScript("PMAddPhotoPlane", false);

            var refreshBtn = new Button { Text = "Refresh", Width = 70 };
            refreshBtn.Click += (_, _) => Doc?.Views.Redraw();

            var headerRow = new TableLayout
            {
                Rows =
                {
                    new TableRow(
                        new TableCell(new Label
                        {
                            Text = "Photo Planes",
                            Font = new Font(SystemFont.Bold, 11),
                            VerticalAlignment = VerticalAlignment.Center,
                        }, true),
                        new TableCell(addBtn, false),
                        new TableCell(refreshBtn, false)
                    )
                },
                Spacing = new Size(4, 0),
            };

            // ----------------------------------------------------------------
            //  Workflow section
            // ----------------------------------------------------------------

            var workflowSection = BuildWorkflowSection();

            // ----------------------------------------------------------------
            //  Full layout
            // ----------------------------------------------------------------

            Content = new TableLayout
            {
                Rows =
                {
                    new TableRow(headerRow),
                    new TableRow(scrollable) { ScaleHeight = true },
                    new TableRow(workflowSection),
                },
                Padding = new Padding(6),
                Spacing = new Size(0, 4),
            };

            // Subscribe to events
            RhinoPhotoMatchPlugin.Instance.Registry.Changed += OnRegistryChanged;
            RhinoView.Modified                              += OnViewModified;
            RhinoDoc.EndOpenDocument                        += OnEndOpenDocument;

            RebuildList();
            PopulateDropDown();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                var registry = RhinoPhotoMatchPlugin.Instance?.Registry;
                if (registry != null) registry.Changed -= OnRegistryChanged;
                RhinoView.Modified       -= OnViewModified;
                RhinoDoc.EndOpenDocument -= OnEndOpenDocument;
            }
            base.Dispose(disposing);
        }

        private RhinoDoc? Doc => RhinoDoc.FromRuntimeSerialNumber(_documentSerialNumber);

        // ------------------------------------------------------------------ //
        //  Event handlers
        // ------------------------------------------------------------------ //

        private void OnRegistryChanged(object? sender, EventArgs e)
        {
            Application.Instance.Invoke(() =>
            {
                RebuildList();
                PopulateDropDown();
            });
        }

        private void OnViewModified(object? sender, ViewEventArgs e)
        {
            if (_fineTunePanel.Visible)
                Application.Instance.Invoke(UpdateLensLabel);
        }

        private void OnEndOpenDocument(object? sender, DocumentOpenEventArgs e)
        {
        }

        // ------------------------------------------------------------------ //
        //  List section — unchanged
        // ------------------------------------------------------------------ //

        private void RebuildList()
        {
            _listStack.Items.Clear();

            var registry = RhinoPhotoMatchPlugin.Instance.Registry;

            foreach (var pair in registry.Pairs)
                _listStack.Items.Add(BuildRow(pair));
        }

        private Control BuildRow(PhotoPlanePair pair)
        {
            // Thumbnail — load once, cache on pair
            var imgView = new ImageView
            {
                Size  = new Size(48, 48),
                Image = GetOrLoadThumbnail(pair),
            };

            // Name label
            var nameLabel = new Label
            {
                Text               = pair.Name,
                Width              = 120,
                VerticalAlignment  = VerticalAlignment.Center,
            };

            // Distance stepper — lets the user adjust how far the plane sits from the camera
            var distStepper = new NumericStepper
            {
                MinValue = 0.1, MaxValue = 1000000, Increment = 1,
                DecimalPlaces = 2, Width = 75, Value = pair.Distance,
            };
            distStepper.ValueChanged += (_, _) =>
            {
                pair.Distance = distStepper.Value;
                Doc?.Views.Redraw();
            };

            // Transparency slider
            var slider = new Slider
            {
                MinValue = 0,
                MaxValue = 100,
                Value    = (int)Math.Round(pair.Transparency * 100),
                Width    = 100,
            };
            slider.ValueChanged += (_, _) =>
            {
                pair.Transparency = slider.Value / 100.0;
                RhinoPhotoMatchPlugin.Instance.Conduit.InvalidateMaterial(pair.Name);
                Doc?.Views.Redraw();
            };

            // View button — activates the linked viewport
            var viewBtn = new Button { Text = "View", Width = 42 };
            viewBtn.Click += (_, _) =>
            {
                var d = Doc;
                if (d == null) return;
                foreach (var view in d.Views)
                    if (view.ActiveViewport.Id == pair.ActiveViewportId)
                    { d.Views.ActiveView = view; break; }
            };

            // Relink button
            var relinkBtn = new Button { Text = "Relink", Width = 55 };
            relinkBtn.Click += (_, _) => DoRelink(pair, imgView);

            // Delete button
            var deleteBtn = new Button { Text = "\u2715", Width = 30 };
            deleteBtn.Click += (_, _) => DoDelete(pair);

            var row = new TableLayout
            {
                Rows =
                {
                    new TableRow(
                        new TableCell(imgView,    false),
                        new TableCell(nameLabel,  false),
                        new TableCell(distStepper, false),
                        new TableCell(slider,     false),
                        new TableCell(viewBtn,    false),
                        new TableCell(relinkBtn,  false),
                        new TableCell(deleteBtn,  false),
                        new TableCell(null, true)          // trailing spacer
                    )
                },
                Spacing = new Size(4, 0),
                Padding = new Padding(2),
            };

            return row;
        }

        // ------------------------------------------------------------------ //
        //  Thumbnail helpers
        // ------------------------------------------------------------------ //

        private static Bitmap GetOrLoadThumbnail(PhotoPlanePair pair)
        {
            if (pair.ThumbnailBitmap != null)
                return pair.ThumbnailBitmap;

            pair.ThumbnailBitmap = LoadThumbnail(pair.ImagePath);
            return pair.ThumbnailBitmap;
        }

        private static Bitmap LoadThumbnail(string imagePath)
        {
            const int Size = 48;
            try
            {
                using var src = System.Drawing.Image.FromFile(imagePath);
                var sysBmp = new System.Drawing.Bitmap(Size, Size);
                using (var g = System.Drawing.Graphics.FromImage(sysBmp))
                {
                    g.InterpolationMode =
                        System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(src, 0, 0, Size, Size);
                }
                using var ms = new MemoryStream();
                sysBmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                sysBmp.Dispose();
                ms.Position = 0;
                return new Bitmap(ms);
            }
            catch
            {
                return MakeGreyPlaceholder(Size);
            }
        }

        private static Bitmap MakeGreyPlaceholder(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppRgba);
            using var g = new Graphics(bmp);
            g.Clear(new Color(0.5f, 0.5f, 0.5f));
            return bmp;
        }

        // ------------------------------------------------------------------ //
        //  Distance helpers
        // ------------------------------------------------------------------ //

        // ------------------------------------------------------------------ //
        //  Relink
        // ------------------------------------------------------------------ //

        private void DoRelink(PhotoPlanePair pair, ImageView imgView)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Relink Photo (JPG or PNG)",
                Filters =
                {
                    new FileFilter("Image files", ".jpg", ".jpeg", ".png"),
                    new FileFilter("All files", ".*"),
                },
                CurrentFilterIndex = 0,
            };

            if (dialog.ShowDialog(this) != DialogResult.Ok) return;

            string newPath = dialog.FileName;
            string ext = Path.GetExtension(newPath).ToLowerInvariant();
            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png")
            {
                MessageBox.Show("Only JPG and PNG files are supported.", MessageBoxType.Error);
                return;
            }

            // Dispose cached thumbnail so it is reloaded from the new path
            pair.ThumbnailBitmap?.Dispose();
            pair.ThumbnailBitmap = null;

            // RelinkPhoto updates pair.ImagePath, invalidates material, redraws, fires Changed.
            // Changed → RebuildList → GetOrLoadThumbnail loads the new thumbnail.
            var doc = Doc ?? RhinoDoc.ActiveDoc;
            if (doc != null)
                RhinoPhotoMatchPlugin.Instance.Registry.RelinkPhoto(
                    doc, pair, newPath, RhinoPhotoMatchPlugin.Instance.Conduit);
        }

        // ------------------------------------------------------------------ //
        //  Delete
        // ------------------------------------------------------------------ //

        private void DoDelete(PhotoPlanePair pair)
        {
            var result = MessageBox.Show(
                $"Delete photo plane \"{pair.Name}\"?",
                "Confirm Delete",
                MessageBoxButtons.YesNo,
                MessageBoxType.Question);

            if (result != DialogResult.Yes) return;

            var doc = Doc ?? RhinoDoc.ActiveDoc;
            RhinoPhotoMatchPlugin.Instance.Conduit.InvalidateMaterial(pair.Name);
            RhinoPhotoMatchPlugin.Instance.Registry.RemovePair(
                doc ?? RhinoDoc.ActiveDoc!, pair);
            doc?.Views.Redraw();
            // Registry.Changed fires inside RemovePair → triggers RebuildList + PopulateDropDown
        }

        // ================================================================== //
        //  WORKFLOW SECTION
        // ================================================================== //

        private Control BuildWorkflowSection()
        {
            // ---- Active plane selector ----
            _pairDropDown = new DropDown();
            _pairDropDown.SelectedIndexChanged += OnActivePairChanged;

            var pairRow = new TableLayout
            {
                Rows =
                {
                    new TableRow(
                        new TableCell(new Label
                        {
                            Text = "Active plane",
                            VerticalAlignment = VerticalAlignment.Center,
                            Width = 100,
                        }, false),
                        new TableCell(_pairDropDown, true)
                    )
                },
                Spacing = new Size(4, 0),
            };

            // ---- Focal length ----
            _focalStepper = new NumericStepper
            {
                MinValue = 10, MaxValue = 300, Increment = 1,
                DecimalPlaces = 0, Width = 70, Value = 50,
            };
            _focalStepper.ValueChanged += (_, _) =>
            {
                if (_activePair != null)
                    _activePair.FocalLengthMm = _focalStepper.Value;
            };

            _focalHintLabel = new Label
            {
                Text = "default",
                TextColor = Colors.DarkGray,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var focalRow = new TableLayout
            {
                Rows =
                {
                    new TableRow(
                        new TableCell(new Label
                        {
                            Text = "Focal length (mm)",
                            VerticalAlignment = VerticalAlignment.Center,
                            Width = 100,
                        }, false),
                        new TableCell(_focalStepper, false),
                        new TableCell(_focalHintLabel, true)
                    )
                },
                Spacing = new Size(4, 0),
            };

            // ---- Reference points ----
            var pickBtn = new Button { Text = "Pick Reference Points" };
            pickBtn.Click += OnPickReferencePoints;

            _refCountLabel = new Label
            {
                Text = "0 pair(s)",
                VerticalAlignment = VerticalAlignment.Center,
                TextColor = Colors.DarkGray,
            };

            var refRow = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalContentAlignment = VerticalAlignment.Center,
                Items = { pickBtn, _refCountLabel },
            };

            // ---- Calibrate ----
            var calibrateBtn = new Button { Text = "Calibrate" };
            calibrateBtn.Click += OnCalibrate;

            _reprErrorLabel = new Label
            {
                Text = "",
                VerticalAlignment = VerticalAlignment.Center,
                TextColor = Colors.DarkGray,
            };

            var calibRow = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalContentAlignment = VerticalAlignment.Center,
                Items = { calibrateBtn, _reprErrorLabel },
            };

            // ---- Fine-tune (hidden until first successful calibration) ----
            _fineTunePanel = BuildFineTuneSection();
            _fineTunePanel.Visible = false;

            // ---- Separator + label ----
            var sectionLabel = new Label
            {
                Text = "Calibration",
                Font = new Font(SystemFont.Bold, 10),
                VerticalAlignment = VerticalAlignment.Center,
            };

            return new StackLayout
            {
                Orientation = Orientation.Vertical,
                Spacing = 5,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Padding(0, 6, 0, 0),
                Items =
                {
                    sectionLabel,
                    pairRow,
                    focalRow,
                    refRow,
                    calibRow,
                    _fineTunePanel,
                },
            };
        }

        // ------------------------------------------------------------------ //
        //  Fine-tune section
        // ------------------------------------------------------------------ //

        private Panel BuildFineTuneSection()
        {
            // Step size free-entry field
            var stepStepper = new NumericStepper
            {
                MinValue = 0.001, MaxValue = 10000, Increment = 0.1,
                DecimalPlaces = 3, Width = 80, Value = _stepSize,
            };
            stepStepper.ValueChanged += (_, _) =>
            {
                if (stepStepper.Value > 0) _stepSize = stepStepper.Value;
            };

            var stepRow = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalContentAlignment = VerticalAlignment.Center,
                Items =
                {
                    new Label { Text = "Step:", VerticalAlignment = VerticalAlignment.Center },
                    stepStepper,
                    new Label { Text = "units", VerticalAlignment = VerticalAlignment.Center },
                },
            };

            // Nudge buttons — 2-column grid
            static Button Btn(string t) => new Button { Text = t, Width = 80 };

            var fwdBtn  = Btn("Forward");
            var backBtn = Btn("Back");
            var leftBtn = Btn("Left");
            var rgtBtn  = Btn("Right");
            var upBtn   = Btn("Up");
            var dwnBtn  = Btn("Down");

            fwdBtn.Click  += (_, _) => NudgeCamera( 1,  0,  0);
            backBtn.Click += (_, _) => NudgeCamera(-1,  0,  0);
            leftBtn.Click += (_, _) => NudgeCamera( 0, -1,  0);
            rgtBtn.Click  += (_, _) => NudgeCamera( 0,  1,  0);
            upBtn.Click   += (_, _) => NudgeCamera( 0,  0,  1);
            dwnBtn.Click  += (_, _) => NudgeCamera( 0,  0, -1);

            var nudgeGrid = new TableLayout
            {
                Rows =
                {
                    new TableRow(fwdBtn,  backBtn),
                    new TableRow(leftBtn, rgtBtn),
                    new TableRow(upBtn,   dwnBtn),
                },
                Spacing = new Size(4, 4),
            };

            // Lens controls
            _lensLabel = new Label
            {
                Text = "\u2014 mm",
                Width = 55,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var lensMinusBtn = new Button { Text = "-1mm", Width = 50 };
            var lensPlusBtn  = new Button { Text = "+1mm", Width = 50 };
            lensMinusBtn.Click += (_, _) => AdjustLens(-1);
            lensPlusBtn.Click  += (_, _) => AdjustLens( 1);

            var lensRow = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                VerticalContentAlignment = VerticalAlignment.Center,
                Items =
                {
                    new Label { Text = "Lens:", VerticalAlignment = VerticalAlignment.Center },
                    lensMinusBtn, _lensLabel, lensPlusBtn,
                },
            };

            var content = new StackLayout
            {
                Orientation = Orientation.Vertical,
                Spacing = 5,
                Padding = new Padding(0, 4, 0, 0),
                Items = { stepRow, nudgeGrid, lensRow },
            };

            return new Panel { Content = content };
        }

        // ------------------------------------------------------------------ //
        //  Workflow helpers
        // ------------------------------------------------------------------ //

        private void PopulateDropDown()
        {
            _suppressDropDownChange = true;
            string? prevName = _activePair?.Name;

            _pairDropDown.Items.Clear();
            var registry = RhinoPhotoMatchPlugin.Instance.Registry;
            foreach (var pair in registry.Pairs)
                _pairDropDown.Items.Add(pair.Name);

            int idx = -1;
            if (prevName != null)
                for (int i = 0; i < registry.Pairs.Count; i++)
                    if (registry.Pairs[i].Name == prevName) { idx = i; break; }
            if (idx < 0 && registry.Pairs.Count > 0) idx = 0;

            _suppressDropDownChange = false;
            _pairDropDown.SelectedIndex = idx;  // fires OnActivePairChanged

            if (idx < 0)
            {
                _activePair = null;
                UpdateWorkflowState();
            }
        }

        private void UpdateWorkflowState()
        {
            bool hasPair = _activePair != null;
            _focalStepper.Enabled = hasPair;

            if (!hasPair)
            {
                _focalHintLabel.Text  = "";
                _refCountLabel.Text   = "";
                _reprErrorLabel.Text  = "";
                _fineTunePanel.Visible = false;
                return;
            }

            _focalStepper.Value   = _activePair!.FocalLengthMm;
            _focalHintLabel.Text  = _activePair.FocalLengthHint;
            _refCountLabel.Text   = $"{_activePair.ReferencePairs.Count} pair(s)";

            var result = _activePair.LastCalibrationResult;
            if (result != null)
            {
                _reprErrorLabel.Text   = $"Error: {result.ReprojectionError:F2} px";
                _fineTunePanel.Visible = true;
                UpdateLensLabel();
            }
            else
            {
                _reprErrorLabel.Text   = "";
                _fineTunePanel.Visible = false;
            }
        }

        private void UpdateLensLabel()
        {
            if (_activePair == null) return;
            var doc = Doc ?? RhinoDoc.ActiveDoc;
            if (doc == null) { _lensLabel.Text = "\u2014 mm"; return; }
            var vp = PicturePlaneManager.FindViewport(doc, _activePair.ActiveViewportId);
            _lensLabel.Text = vp != null ? $"{vp.Camera35mmLensLength:F0} mm" : "\u2014 mm";
        }

        // ------------------------------------------------------------------ //
        //  Workflow event handlers
        // ------------------------------------------------------------------ //

        private void OnActivePairChanged(object? sender, EventArgs e)
        {
            if (_suppressDropDownChange) return;
            var registry = RhinoPhotoMatchPlugin.Instance.Registry;
            int idx = _pairDropDown.SelectedIndex;
            _activePair = (idx >= 0 && idx < registry.Pairs.Count) ? registry.Pairs[idx] : null;
            UpdateWorkflowState();
        }

        private void OnPickReferencePoints(object? sender, EventArgs e)
        {
            if (_activePair == null) return;
            RhinoApp.RunScript("PMSetReferencePoints", false);
            _refCountLabel.Text = $"{_activePair.ReferencePairs.Count} pair(s)";
        }

        private void OnCalibrate(object? sender, EventArgs e)
        {
            if (_activePair == null) return;
            // Push the panel's focal length to the pair so PMCalibrate uses it as the default.
            _activePair.FocalLengthMm = _focalStepper.Value;
            RhinoApp.RunScript("PMCalibrate", false);
            // PMCalibrate stores its result in pair.LastCalibrationResult — read it back.
            UpdateWorkflowState();
        }

        // ------------------------------------------------------------------ //
        //  Camera fine-tune actions
        // ------------------------------------------------------------------ //

        private void NudgeCamera(int fwdSign, int rightSign, int upSign)
        {
            if (_activePair == null) return;
            var doc = Doc ?? RhinoDoc.ActiveDoc;
            if (doc == null) return;
            var vp = PicturePlaneManager.FindViewport(doc, _activePair.ActiveViewportId);
            if (vp == null) return;

            var loc = vp.CameraLocation;

            if (fwdSign != 0)
            {
                var dir = vp.CameraDirection;
                dir.Unitize();
                loc += dir * (fwdSign * _stepSize);
            }
            if (rightSign != 0)
            {
                var right = Vector3d.CrossProduct(vp.CameraDirection, vp.CameraUp);
                right.Unitize();
                loc += right * (rightSign * _stepSize);
            }
            if (upSign != 0)
            {
                loc = new Point3d(loc.X, loc.Y, loc.Z + upSign * _stepSize);
            }

            vp.SetCameraLocation(loc, true);
            doc.Views.Redraw();
        }

        private void AdjustLens(double deltaMm)
        {
            if (_activePair == null) return;
            var doc = Doc ?? RhinoDoc.ActiveDoc;
            if (doc == null) return;
            var vp = PicturePlaneManager.FindViewport(doc, _activePair.ActiveViewportId);
            if (vp == null) return;

            vp.Camera35mmLensLength = Math.Max(10, Math.Min(300, vp.Camera35mmLensLength + deltaMm));
            _lensLabel.Text = $"{vp.Camera35mmLensLength:F0} mm";
            doc.Views.Redraw();
        }
    }
}
