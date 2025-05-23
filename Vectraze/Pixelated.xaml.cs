using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Xceed.Wpf.Toolkit; // Keep this for ColorPicker if still using Xceed
using System.Collections.Generic;

namespace Vectraze
{
    public partial class Pixelated : UserControl
    {
        private double currentZoom = 1.0;
        private Point? lastDragPoint;
        private BitmapImage originalImage;
        private double aspectRatio;
        public int targetSize = 32; // Default or initial target size
        private bool isPaintMode = false;
        private Color selectedColor = Colors.Black; // Default paint color
        private Color? userBackgroundColor = null;

        private class PixelState
        {
            public Dictionary<Point, Color> Pixels { get; } = new();
            public Color? BackgroundColor { get; } // Store background state too
            public PixelState(IEnumerable<Rectangle> rects, Color? bgColor)
            {
                foreach (var rect in rects)
                {
                    if (rect.Tag is Point pt && rect.Fill is SolidColorBrush brush)
                        Pixels[pt] = brush.Color;
                }
                BackgroundColor = bgColor;
            }
        }
        private Stack<PixelState> undoStack = new();
        private Stack<PixelState> redoStack = new();

        public Pixelated(BitmapImage bitmapImage)
        {
            InitializeComponent();
            originalImage = bitmapImage;
            aspectRatio = bitmapImage.PixelWidth / (double)bitmapImage.PixelHeight;

            // Set initial preview colors
            SelectedColorPreview.Fill = new SolidColorBrush(selectedColor);
            UpdateBgColorPreview();


            PixelCanvas.Loaded += (s, e) =>
            {
                // Ensure targetSize is based on initial TextBox values if they are different
                if (int.TryParse(widthTB.Text, out int initialWidth) && int.TryParse(heightTB.Text, out int initialHeight))
                {
                    targetSize = Math.Max(initialWidth, initialHeight); // Or choose a different logic
                }
                RenderPixelatedImage(originalImage, targetSize);
                PushUndoState(); // Initial state
            };

            // No need for these explicit event handlers if they only call RenderPixelatedImage
            // widthTB.TextChanged += WidthTB_TextChange;
            // heightTB.TextChanged += HeightTB_TextChange;
        }

        private void RenderPixelatedImage(BitmapImage image, int size, bool forExport = false)
        {
            targetSize = size; // Update the class field
            int pixelWidth, pixelHeight;

            if (aspectRatio >= 1.0) // Landscape or square
            {
                pixelWidth = targetSize;
                pixelHeight = Math.Max(1, (int)Math.Round(targetSize / aspectRatio));
            }
            else // Portrait
            {
                pixelHeight = targetSize;
                pixelWidth = Math.Max(1, (int)Math.Round(targetSize * aspectRatio));
            }

            // Update TextBoxes without triggering recursive calls if Render is called from TextChanged
            // This is safer done directly if RenderPixelatedImage is not called from TextChanged
            if (widthTB.Text != pixelWidth.ToString()) widthTB.Text = pixelWidth.ToString();
            if (heightTB.Text != pixelHeight.ToString()) heightTB.Text = pixelHeight.ToString();


            TransformedBitmap resized = new TransformedBitmap(image, new ScaleTransform(
                pixelWidth / (double)image.PixelWidth,
                pixelHeight / (double)image.PixelHeight));
            resized.Freeze();


            int stride = pixelWidth * 4; // BGRA
            byte[] pixels = new byte[stride * pixelHeight];
            resized.CopyPixels(pixels, stride, 0);

            // Use the actual allocated size of the canvas for calculations
            double canvasViewboxWidth = PixelCanvas.Width;  // The size defined in XAML or set programmatically for Viewbox scaling
            double canvasViewboxHeight = PixelCanvas.Height;


            double cellSizeX = canvasViewboxWidth / pixelWidth;
            double cellSizeY = canvasViewboxHeight / pixelHeight;
            double cellSize = Math.Floor(Math.Min(cellSizeX, cellSizeY));
            if (cellSize < 1) cellSize = 1; // Ensure at least 1x1 pixel cells

            double totalImageWidth = pixelWidth * cellSize;
            double totalImageHeight = pixelHeight * cellSize;

            double offsetX = Math.Floor((canvasViewboxWidth - totalImageWidth) / 2);
            double offsetY = Math.Floor((canvasViewboxHeight - totalImageHeight) / 2);

            PixelCanvas.Children.Clear(); // Clear previous drawing

            if (userBackgroundColor.HasValue)
            {
                PixelCanvas.Background = new SolidColorBrush(userBackgroundColor.Value);
            }
            else
            {
                PixelCanvas.Background = Brushes.Transparent;
                if (!forExport) // Only draw checkerboard if not exporting and no user background
                {
                    for (int y = 0; y < pixelHeight; y++)
                    {
                        for (int x = 0; x < pixelWidth; x++)
                        {
                            Rectangle bgSquare = new Rectangle
                            {
                                Width = cellSize,
                                Height = cellSize,
                                Fill = new SolidColorBrush((x + y) % 2 == 0 ? Colors.LightGray : Colors.Gainsboro), // Lighter checkerboard
                                Tag = "Checkerboard" // Tag for easy removal later
                            };
                            Canvas.SetLeft(bgSquare, offsetX + x * cellSize);
                            Canvas.SetTop(bgSquare, offsetY + y * cellSize);
                            PixelCanvas.Children.Add(bgSquare);
                        }
                    }
                }
            }

            // Draw image pixels as rectangles
            for (int y = 0; y < pixelHeight; y++)
            {
                for (int x = 0; x < pixelWidth; x++)
                {
                    int index = (y * stride) + (x * 4); // BGRA
                    byte b = pixels[index];
                    byte g = pixels[index + 1];
                    byte r = pixels[index + 2];
                    byte a = pixels[index + 3];

                    Rectangle rect = new Rectangle
                    {
                        Width = cellSize,
                        Height = cellSize,
                        Fill = new SolidColorBrush(Color.FromArgb(a, r, g, b)),
                        Tag = new Point(x, y) // Store logical pixel position
                    };

                    Canvas.SetLeft(rect, offsetX + x * cellSize);
                    Canvas.SetTop(rect, offsetY + y * cellSize);
                    rect.MouseLeftButtonDown += PixelRect_MouseLeftButtonDown; // For paint mode
                    PixelCanvas.Children.Add(rect);
                }
            }
        }


        private IEnumerable<Rectangle> GetPixelRectangles()
        {
            return PixelCanvas.Children.OfType<Rectangle>().Where(r => r.Tag is Point);
        }

        private void PushUndoState()
        {
            undoStack.Push(new PixelState(GetPixelRectangles(), userBackgroundColor));
            redoStack.Clear(); // Clear redo stack when a new action is performed
            UpdateUndoRedoButtonStates();
        }

        private void RestorePixelState(PixelState state)
        {
            userBackgroundColor = state.BackgroundColor; // Restore background
            UpdateBgColorPreview(); // Update UI for background color

            // Temporarily detach event handlers from TextBoxes to prevent Render loop during restore
            widthTB.TextChanged -= WidthTB_TextChange;
            heightTB.TextChanged -= HeightTB_TextChange;

            // Re-render based on the state's first pixel to get dimensions, or store dimensions in state
            // For simplicity, we'll re-render using originalImage and a size derived from the state.
            // This requires storing pixelWidth/Height or targetSize in PixelState or recalculating.
            // Let's assume the targetSize that led to this state is implicitly handled by re-rendering.
            // More robustly, PixelState should store the dimensions (pixelWidth, pixelHeight).

            // For now, let's just update fills and re-render if background changed
            // A full re-render ensures cell sizes are correct if dimensions changed.
            // The challenge: PixelState stores colors for Point(x,y), but if canvas size changed, RenderPixelatedImage is needed.
            // For simplicity, if background changed, or if pixel count differs, re-render.
            // The current RenderPixelatedImage uses 'targetSize'. If undo/redo involves size changes, 'targetSize' also needs to be in PixelState.

            // Simplified Restore:
            // We need to re-create the visual rectangles based on the stored state.
            // This means RenderPixelatedImage needs to be able to take a PixelState or raw pixel data.
            // For now, let's assume RenderPixelatedImage correctly sets up the grid, and we just change colors.

            // Find targetSize from the state if possible (e.g., by max X or Y in state.Pixels)
            // For now, let's assume the dimensions are consistent with the current targetSize
            // or that RenderPixelatedImage is called appropriately if dimensions change.

            RenderPixelatedImage(originalImage, targetSize); // Re-render to set up structure and background

            // Now apply the stored pixel colors
            foreach (var rect in GetPixelRectangles())
            {
                if (rect.Tag is Point pt && state.Pixels.TryGetValue(pt, out var color))
                {
                    rect.Fill = new SolidColorBrush(color);
                }
                // Else, if a pixel from state is not on canvas (e.g. resize smaller), it's ignored.
                // If canvas has new pixels not in state (e.g. resize larger), they keep their RenderPixelatedImage color.
            }

            // Re-attach event handlers
            widthTB.TextChanged += WidthTB_TextChange;
            heightTB.TextChanged += HeightTB_TextChange;
            UpdateUndoRedoButtonStates();
        }

        private void UpdateUndoRedoButtonStates()
        {
            UndoBtn.IsEnabled = undoStack.Count > 0;
            RedoBtn.IsEnabled = redoStack.Count > 0;
        }


        private void PixelRect_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (isPaintMode && sender is Rectangle rect)
            {
                if (rect.Fill is SolidColorBrush oldBrush && oldBrush.Color == selectedColor)
                {
                    // No change, do nothing
                }
                else
                {
                    PushUndoState();
                    rect.Fill = new SolidColorBrush(selectedColor);
                }
                e.Handled = true; // Prevent canvas drag
            }
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            // Temporarily set canvas background for export if userBackgroundColor is set
            Brush originalCanvasBackground = PixelCanvas.Background;
            List<UIElement> checkerboardRects = new List<UIElement>();

            if (userBackgroundColor.HasValue)
            {
                PixelCanvas.Background = new SolidColorBrush(userBackgroundColor.Value);
            }
            else // If transparent background desired for PNG, remove checkerboard
            {
                checkerboardRects = PixelCanvas.Children
                                       .OfType<Rectangle>()
                                       .Where(r => r.Tag as string == "Checkerboard")
                                       .Cast<UIElement>()
                                       .ToList();
                foreach (var rect in checkerboardRects)
                {
                    PixelCanvas.Children.Remove(rect);
                }
                PixelCanvas.Background = Brushes.Transparent; // Ensure transparent for PNG
            }


            var originalTransform = PixelCanvas.LayoutTransform;
            PixelCanvas.LayoutTransform = Transform.Identity; // Reset zoom for rendering

            // Use the actual dimensions derived from pixelWidth/Height and cellSize
            if (!int.TryParse(widthTB.Text, out int pixelWidth) || !int.TryParse(heightTB.Text, out int pixelHeight))
            {
                System.Windows.MessageBox.Show("Invalid canvas dimensions.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            double tempCellSizeX = PixelCanvas.Width / pixelWidth;
            double tempCellSizeY = PixelCanvas.Height / pixelHeight;
            double tempCellSize = Math.Floor(Math.Min(tempCellSizeX, tempCellSizeY));
            if (tempCellSize < 1) tempCellSize = 1;

            Size renderSize = new Size(pixelWidth * tempCellSize, pixelHeight * tempCellSize);


            PixelCanvas.Measure(renderSize);
            PixelCanvas.Arrange(new Rect(renderSize));

            RenderTargetBitmap rtb = new RenderTargetBitmap(
                (int)renderSize.Width, (int)renderSize.Height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(PixelCanvas);
            rtb.Freeze();

            PixelCanvas.LayoutTransform = originalTransform; // Restore zoom
            PixelCanvas.Background = originalCanvasBackground; // Restore original live background

            // Restore checkerboard if they were removed
            if (checkerboardRects.Any())
            {
                foreach (var rect in checkerboardRects)
                {
                    PixelCanvas.Children.Insert(0, rect); // Insert at bottom
                }
            }


            SaveFileDialog dialog = new SaveFileDialog
            {
                FileName = "pixelated_image",
                Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg;*.jpeg)|*.jpg;*.jpeg|Bitmap Image (*.bmp)|*.bmp",
                DefaultExt = ".png"
            };

            if (dialog.ShowDialog() == true)
            {
                BitmapEncoder encoder;
                string ext = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();

                switch (ext)
                {
                    case ".jpg":
                    case ".jpeg":
                        var jpegEncoder = new JpegBitmapEncoder();
                        jpegEncoder.QualityLevel = 90; // Set quality for JPEG
                        encoder = jpegEncoder;
                        // For JPG, if original was transparent, you might want to fill with white explicitly
                        // This is complex if rendering a transparent canvas to JPG.
                        // The RenderTargetBitmap with Pbgra32 will handle alpha. JPG will likely make transparent areas black or white.
                        // To ensure white background for JPG:
                        // Create a new Visual with white background, draw rtb on top, then render that.
                        // Or, if saving to JPG and originalCanvasBackground was transparent, ensure it was white during rtb.Render
                        if (PixelCanvas.Background == Brushes.Transparent && (originalCanvasBackground == Brushes.Transparent || originalCanvasBackground == null))
                        {
                            // Create a temporary visual with white background
                            DrawingVisual dv = new DrawingVisual();
                            using (DrawingContext dc = dv.RenderOpen())
                            {
                                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, rtb.Width, rtb.Height));
                                dc.DrawImage(rtb, new Rect(0, 0, rtb.Width, rtb.Height));
                            }
                            RenderTargetBitmap jpgRtb = new RenderTargetBitmap((int)rtb.PixelWidth, (int)rtb.PixelHeight, 96, 96, PixelFormats.Pbgra32);
                            jpgRtb.Render(dv);
                            jpgRtb.Freeze();
                            encoder.Frames.Add(BitmapFrame.Create(jpgRtb));
                        }
                        else
                        {
                            encoder.Frames.Add(BitmapFrame.Create(rtb));
                        }
                        break;
                    case ".bmp":
                        encoder = new BmpBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(rtb));
                        break;
                    default: // PNG
                        encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(rtb));
                        break;
                }


                try
                {
                    using (var fs = new FileStream(dialog.FileName, FileMode.Create))
                    {
                        encoder.Save(fs);
                    }
                    System.Windows.MessageBox.Show("Image saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error saving image: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }


        private void ScrollArea_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control) // Zoom only with Ctrl + Wheel
            {
                const double zoomStep = 0.1;
                currentZoom += e.Delta > 0 ? zoomStep : -zoomStep;
                currentZoom = Math.Max(0.1, Math.Min(currentZoom, 10.0)); // Clamp zoom

                canvasScaleTransform.ScaleX = currentZoom;
                canvasScaleTransform.ScaleY = currentZoom;

                e.Handled = true;
            }
            // else, let ScrollViewer handle normal scrolling if content is larger than view
        }


        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            Window currentWindow = Window.GetWindow(this);
            MainWindow mainWindow = new MainWindow(); // Create a new instance of MainWindow
            mainWindow.Show();
            currentWindow?.Close(); // Close the current window (which hosts Pixelated UserControl)
        }

        private void PixelCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!isPaintMode && e.ButtonState == MouseButtonState.Pressed)
            {
                lastDragPoint = e.GetPosition(ScrollArea); // Pan relative to ScrollArea
                PixelCanvas.CaptureMouse();
                PixelCanvas.Cursor = Cursors.ScrollAll;
            }
        }

        private void PixelCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isPaintMode && lastDragPoint.HasValue && e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPos = e.GetPosition(ScrollArea);
                double dX = currentPos.X - lastDragPoint.Value.X;
                double dY = currentPos.Y - lastDragPoint.Value.Y;

                ScrollArea.ScrollToHorizontalOffset(ScrollArea.HorizontalOffset - dX);
                ScrollArea.ScrollToVerticalOffset(ScrollArea.VerticalOffset - dY);

                lastDragPoint = currentPos;
            }
        }

        private void PixelCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!isPaintMode)
            {
                PixelCanvas.ReleaseMouseCapture();
                lastDragPoint = null;
                PixelCanvas.Cursor = Cursors.Cross; // Or your default paint cursor
            }
        }

        private bool _isUpdatingTextBoxes = false; // Flag to prevent event recursion

        private void ResizeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingTextBoxes) return;

            if (int.TryParse(widthTB.Text, out int newWidth) && int.TryParse(heightTB.Text, out int newHeight) && newWidth > 0 && newHeight > 0)
            {
                PushUndoState(); // Save state before resize

                // Determine the 'master' dimension for targetSize based on what changed or a preferred logic
                // For simplicity, let's use the larger of the two as the new targetSize base
                targetSize = Math.Max(newWidth, newHeight);
                aspectRatio = (double)newWidth / newHeight; // Update aspect ratio based on new inputs

                RenderPixelatedImage(originalImage, targetSize);
            }
            else
            {
                System.Windows.MessageBox.Show("Please enter valid positive integer values for width and height.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void HeightTB_TextChange(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingTextBoxes || !heightTB.IsFocused) return;

            _isUpdatingTextBoxes = true;
            ValidateIntegerInput(heightTB);
            if (int.TryParse(heightTB.Text, out int newHeight) && newHeight > 0)
            {
                int newWidth = Math.Max(1, (int)Math.Round(newHeight * aspectRatio));
                widthTB.Text = newWidth.ToString();
            }
            _isUpdatingTextBoxes = false;
        }

        private void WidthTB_TextChange(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingTextBoxes || !widthTB.IsFocused) return;

            _isUpdatingTextBoxes = true;
            ValidateIntegerInput(widthTB);
            if (int.TryParse(widthTB.Text, out int newWidth) && newWidth > 0)
            {
                int newHeight = Math.Max(1, (int)Math.Round(newWidth / aspectRatio));
                heightTB.Text = newHeight.ToString();
            }
            _isUpdatingTextBoxes = false;
        }


        private void ValidateIntegerInput(TextBox textBox)
        {
            string currentText = textBox.Text;
            string filteredText = new string(currentText.Where(char.IsDigit).ToArray());

            if (filteredText != currentText)
            {
                int caret = textBox.CaretIndex;
                textBox.Text = filteredText;
                textBox.CaretIndex = Math.Min(caret, filteredText.Length);
            }
        }

        private void ColorPickerBtn_Click(object sender, RoutedEventArgs e)
        {
            ColorPickerPopup.IsOpen = !ColorPickerPopup.IsOpen;
            if (ColorPickerPopup.IsOpen)
            {
                inlineColorPicker.SelectedColor = selectedColor; // Initialize with current color
            }
        }

        private void InlineColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e)
        {
            if (e.NewValue.HasValue)
            {
                selectedColor = e.NewValue.Value;
                SelectedColorPreview.Fill = new SolidColorBrush(selectedColor);
            }
            // ColorPickerPopup.IsOpen = false; // Optionally close after selection
        }

        private void PaintModeBtn_Click(object sender, RoutedEventArgs e)
        {
            isPaintMode = !isPaintMode;
            PaintModeBtn.Content = isPaintMode ? "Exit Paint Mode" : "Paint Mode";
            PixelCanvas.Cursor = isPaintMode ? Cursors.Pen : Cursors.Cross; // Or Cursors.ScrollAll if not in paint mode
        }

        private void UndoBtn_Click(object sender, RoutedEventArgs e)
        {
            if (undoStack.Count > 0)
            {
                // Current state becomes a redo state
                redoStack.Push(new PixelState(GetPixelRectangles(), userBackgroundColor));
                var prevState = undoStack.Pop();
                RestorePixelState(prevState);
                UpdateUndoRedoButtonStates();
            }
        }

        private void RedoBtn_Click(object sender, RoutedEventArgs e)
        {
            if (redoStack.Count > 0)
            {
                // Current state becomes an undo state
                undoStack.Push(new PixelState(GetPixelRectangles(), userBackgroundColor));
                var nextState = redoStack.Pop();
                RestorePixelState(nextState);
                UpdateUndoRedoButtonStates();
            }
        }

        private void UpdateBgColorPreview()
        {
            if (userBackgroundColor.HasValue)
            {
                SelectedBgColorPreview.Fill = new SolidColorBrush(userBackgroundColor.Value);
                SelectedBgColorPreview.OpacityMask = null; // Show solid color
            }
            else
            {
                SelectedBgColorPreview.Fill = Brushes.Transparent; // Will show through to checkerboard pattern
                // Re-apply opacity mask if it was removed
                if (SelectedBgColorPreview.OpacityMask == null)
                {
                    SelectedBgColorPreview.OpacityMask = (VisualBrush)this.Resources["CheckerboardBrushForPreview"];
                    // If you defined it directly in XAML, you might need to find it or re-create it.
                    // For simplicity, the XAML for SelectedBgColorPreview already has the VisualBrush.
                }
            }
        }

        private void BgColorPickerBtn_Click(object sender, RoutedEventArgs e)
        {
            BgColorPickerPopup.IsOpen = !BgColorPickerPopup.IsOpen;
            if (BgColorPickerPopup.IsOpen)
            {
                bgInlineColorPicker.SelectedColor = userBackgroundColor;
            }
        }

        private void BgInlineColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e)
        {
            PushUndoState(); // Save state before changing background

            // If user selects "No Color" or clears it, e.NewValue might be null or transparent
            if (e.NewValue.HasValue && e.NewValue.Value.A > 0) // Consider fully transparent as "no background"
            {
                userBackgroundColor = e.NewValue.Value;
            }
            else
            {
                userBackgroundColor = null; // No background color / transparent
            }
            UpdateBgColorPreview();
            RenderPixelatedImage(originalImage, targetSize); // Re-render with new background
            // BgColorPickerPopup.IsOpen = false; // Optionally close
        }

        private void ApplyFilter(Action<Rectangle, Color> filterAction)
        {
            PushUndoState();
            foreach (var rect in GetPixelRectangles())
            {
                if (rect.Fill is SolidColorBrush brush)
                {
                    var color = brush.Color;
                    if (color.A == 0) continue; // Skip fully transparent pixels for most filters
                    filterAction(rect, color);
                }
            }
        }

        private void GrayscleBtn_Click(object sender, RoutedEventArgs e)
        {
            ApplyFilter((rect, color) =>
            {
                byte gray = (byte)(0.299 * color.R + 0.587 * color.G + 0.114 * color.B);
                rect.Fill = new SolidColorBrush(Color.FromArgb(color.A, gray, gray, gray));
            });
        }

        private void SeppiaBtn_Click(object sender, RoutedEventArgs e)
        {
            ApplyFilter((rect, color) =>
            {
                int tr = (int)(0.393 * color.R + 0.769 * color.G + 0.189 * color.B);
                int tg = (int)(0.349 * color.R + 0.686 * color.G + 0.168 * color.B);
                int tb = (int)(0.272 * color.R + 0.534 * color.G + 0.131 * color.B);
                rect.Fill = new SolidColorBrush(Color.FromArgb(color.A,
                    (byte)Math.Min(255, tr), (byte)Math.Min(255, tg), (byte)Math.Min(255, tb)));
            });
        }

        private void InvertBtn_Click(object sender, RoutedEventArgs e)
        {
            ApplyFilter((rect, color) =>
            {
                rect.Fill = new SolidColorBrush(Color.FromArgb(color.A,
                    (byte)(255 - color.R), (byte)(255 - color.G), (byte)(255 - color.B)));
            });
        }

        private void TintBtn_Click(object sender, RoutedEventArgs e)
        {
            // You might want a way for the user to pick the tint color and strength
            Color tintColor = Colors.SteelBlue; // Example tint color
            double tintStrength = 0.3; // 0.0 (no tint) to 1.0 (full color overlay)

            ApplyFilter((rect, color) =>
            {
                byte r = (byte)(color.R * (1 - tintStrength) + tintColor.R * tintStrength);
                byte g = (byte)(color.G * (1 - tintStrength) + tintColor.G * tintStrength);
                byte b = (byte)(color.B * (1 - tintStrength) + tintColor.B * tintStrength);
                rect.Fill = new SolidColorBrush(Color.FromArgb(color.A, r, g, b));
            });
        }
    }
}