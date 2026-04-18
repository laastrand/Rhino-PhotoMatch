using System;
using System.Collections.Generic;
using System.IO;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.Display;
using RhinoPhotoMatch.Core;

namespace RhinoPhotoMatch.UI
{
    /// <summary>
    /// Dockable panel showing all registered photo plane pairs.
    /// Open with the PMPanel command or from Rhino's panel menu.
    /// </summary>
    [System.Runtime.InteropServices.Guid("B3F2A1E4-C7D8-4B6F-A9E2-D3C5F8B1E7A2")]
    public class PhotoMatchPanel : Panel
    {
        public static Guid PanelId => typeof(PhotoMatchPanel).GUID;

        private readonly uint _documentSerialNumber;
        private readonly StackLayout _listStack;
        private readonly List<(Label Label, PhotoPlanePair Pair)> _distanceLabels = new();

        public PhotoMatchPanel(uint documentSerialNumber)
        {
            _documentSerialNumber = documentSerialNumber;

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
            refreshBtn.Click += (_, _) => UpdateDistances();

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

            var placeholderLabel = new Label
            {
                Text = "Calibration workflow \u2014 coming soon",
                TextColor = Colors.Gray,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            Content = new TableLayout
            {
                Rows =
                {
                    new TableRow(headerRow),
                    new TableRow(scrollable) { ScaleHeight = true },
                    new TableRow(new Panel { Content = placeholderLabel, Height = 36 }),
                },
                Padding = new Padding(6),
                Spacing = new Size(0, 4),
            };

            // Subscribe to events
            RhinoPhotoMatchPlugin.Instance.Registry.Changed += OnRegistryChanged;
            RhinoView.Modified                           += OnViewModified;
            RhinoDoc.EndOpenDocument                        += OnEndOpenDocument;

            RebuildList();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                var registry = RhinoPhotoMatchPlugin.Instance?.Registry;
                if (registry != null) registry.Changed -= OnRegistryChanged;
                RhinoView.Modified    -= OnViewModified;
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
            Application.Instance.Invoke(RebuildList);
        }

        private void OnViewModified(object? sender, ViewEventArgs e)
        {
            Application.Instance.Invoke(UpdateDistances);
        }

        private void OnEndOpenDocument(object? sender, DocumentOpenEventArgs e)
        {
            Application.Instance.Invoke(UpdateDistances);
        }

        // ------------------------------------------------------------------ //
        //  List building
        // ------------------------------------------------------------------ //

        private void RebuildList()
        {
            _distanceLabels.Clear();
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

            // Distance label — tagged so UpdateDistances() can reach it
            var distLabel = new Label
            {
                Text              = FormatDistance(pair),
                Width             = 52,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _distanceLabels.Add((distLabel, pair));

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
                        new TableCell(distLabel,  false),
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

        private static string FormatDistance(PhotoPlanePair pair)
        {
            return $"{pair.Distance:F1}";
        }

        private void UpdateDistances()
        {
            var doc = Doc;
            foreach (var (label, pair) in _distanceLabels)
                label.Text = FormatDistance(pair);
        }

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
            // Registry.Changed fires inside RemovePair → triggers RebuildList
        }
    }
}
