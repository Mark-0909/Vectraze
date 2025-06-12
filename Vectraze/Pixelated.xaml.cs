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
using Xceed.Wpf.Toolkit; 
using System.Collections.Generic;
using System.Windows.Media.Animation;

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
        private bool isPainting = false;
        private bool hasPushedUndoForDrag = false;

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
                if (int.TryParse(widthTB.Text, out int initialWidth) && int.TryParse(heightTB.Text, out int initialHeight))
                {
                    targetSize = Math.Max(initialWidth, initialHeight);
                }
                RenderPixelatedImage(originalImage, targetSize);
                PushUndoState(); // Initial state
            };
        }

        private void RenderPixelatedImage(BitmapImage image, int size, bool forExport = false)
        {
            targetSize = size;
            int pixelWidth, pixelHeight;

            if (aspectRatio >= 1.0)
            {
                pixelWidth = targetSize;
                pixelHeight = Math.Max(1, (int)Math.Round(targetSize / aspectRatio));
            }
            else
            {
                pixelHeight = targetSize;
                pixelWidth = Math.Max(1, (int)Math.Round(targetSize * aspectRatio));
            }

            if (widthTB.Text != pixelWidth.ToString()) widthTB.Text = pixelWidth.ToString();
            if (heightTB.Text != pixelHeight.ToString()) heightTB.Text = pixelHeight.ToString();

            TransformedBitmap resized = new TransformedBitmap(image, new ScaleTransform(
                pixelWidth / (double)image.PixelWidth,
                pixelHeight / (double)image.PixelHeight));
            resized.Freeze();

            int stride = pixelWidth * 4; // BGRA
            byte[] pixels = new byte[stride * pixelHeight];
            resized.CopyPixels(pixels, stride, 0);

            double canvasViewboxWidth = PixelCanvas.Width;
            double canvasViewboxHeight = PixelCanvas.Height;

            double cellSizeX = canvasViewboxWidth / pixelWidth;
            double cellSizeY = canvasViewboxHeight / pixelHeight;
            double cellSize = Math.Floor(Math.Min(cellSizeX, cellSizeY));
            if (cellSize < 1) cellSize = 1;

            double totalImageWidth = pixelWidth * cellSize;
            double totalImageHeight = pixelHeight * cellSize;

            double offsetX = Math.Floor((canvasViewboxWidth - totalImageWidth) / 2);
            double offsetY = Math.Floor((canvasViewboxHeight - totalImageHeight) / 2);

            PixelCanvas.Children.Clear();

            if (userBackgroundColor.HasValue)
            {
                PixelCanvas.Background = new SolidColorBrush(userBackgroundColor.Value);
            }
            else
            {
                PixelCanvas.Background = Brushes.Transparent;
                if (!forExport)
                {
                    for (int y = 0; y < pixelHeight; y++)
                    {
                        for (int x = 0; x < pixelWidth; x++)
                        {
                            Rectangle bgSquare = new Rectangle
                            {
                                Width = cellSize,
                                Height = cellSize,
                                Fill = new SolidColorBrush((x + y) % 2 == 0 ? Colors.LightGray : Colors.Gainsboro),
                                Tag = "Checkerboard"
                            };
                            Canvas.SetLeft(bgSquare, offsetX + x * cellSize);
                            Canvas.SetTop(bgSquare, offsetY + y * cellSize);
                            PixelCanvas.Children.Add(bgSquare);
                        }
                    }
                }
            }

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
                        Tag = new Point(x, y)
                    };

                    Canvas.SetLeft(rect, offsetX + x * cellSize);
                    Canvas.SetTop(rect, offsetY + y * cellSize);
                    rect.MouseLeftButtonDown += PixelRect_MouseLeftButtonDown;
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
            redoStack.Clear();
            UpdateUndoRedoButtonStates();
        }

        private void RestorePixelState(PixelState state)
        {
            userBackgroundColor = state.BackgroundColor;
            UpdateBgColorPreview();

            widthTB.TextChanged -= WidthTB_TextChange;
            heightTB.TextChanged -= HeightTB_TextChange;

            RenderPixelatedImage(originalImage, targetSize);

            foreach (var rect in GetPixelRectangles())
            {
                if (rect.Tag is Point pt && state.Pixels.TryGetValue(pt, out var color))
                {
                    rect.Fill = new SolidColorBrush(color);
                }
            }

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
                if (!isPainting)
                {
                    isPainting = true;
                    hasPushedUndoForDrag = false;
                    Mouse.Capture(PixelCanvas);
                }

                PaintRectangle(rect);
                e.Handled = true;
            }
        }

        private Rect GetPixelGridBounds()
        {
            int pixelWidth = int.Parse(widthTB.Text);
            int pixelHeight = int.Parse(heightTB.Text);
            double canvasViewboxWidth = PixelCanvas.Width;
            double canvasViewboxHeight = PixelCanvas.Height;
            double cellSizeX = canvasViewboxWidth / pixelWidth;
            double cellSizeY = canvasViewboxHeight / pixelHeight;
            double cellSize = Math.Floor(Math.Min(cellSizeX, cellSizeY));
            if (cellSize < 1) cellSize = 1;
            double totalImageWidth = pixelWidth * cellSize;
            double totalImageHeight = pixelHeight * cellSize;
            double offsetX = Math.Floor((canvasViewboxWidth - totalImageWidth) / 2);
            double offsetY = Math.Floor((canvasViewboxHeight - totalImageHeight) / 2);

            return new Rect(offsetX, offsetY, totalImageWidth, totalImageHeight);
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            // Save the original background and checkerboard rectangles
            Brush originalCanvasBackground = PixelCanvas.Background;
            List<UIElement> checkerboardRects = new List<UIElement>();

            if (userBackgroundColor.HasValue)
            {
                PixelCanvas.Background = new SolidColorBrush(userBackgroundColor.Value);
            }
            else
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
                PixelCanvas.Background = Brushes.Transparent;
            }

            var originalTransform = PixelCanvas.LayoutTransform;
            PixelCanvas.LayoutTransform = Transform.Identity;

            // Get the bounds of the pixel grid
            Rect gridBounds = GetPixelGridBounds();

            // Arrange the canvas as usual
            PixelCanvas.Measure(new Size(PixelCanvas.Width, PixelCanvas.Height));
            PixelCanvas.Arrange(new Rect(0, 0, PixelCanvas.Width, PixelCanvas.Height));

            // Render the whole canvas to a temporary bitmap
            RenderTargetBitmap rtbFull = new RenderTargetBitmap(
                (int)PixelCanvas.Width, (int)PixelCanvas.Height, 96, 96, PixelFormats.Pbgra32);
            rtbFull.Render(PixelCanvas);
            rtbFull.Freeze();

            // Crop to the pixel grid area
            CroppedBitmap cropped = new CroppedBitmap(
                rtbFull,
                new Int32Rect(
                    (int)gridBounds.X,
                    (int)gridBounds.Y,
                    (int)gridBounds.Width,
                    (int)gridBounds.Height
                )
            );

            PixelCanvas.LayoutTransform = originalTransform;
            PixelCanvas.Background = originalCanvasBackground;

            // Restore checkerboard if they were removed
            if (checkerboardRects.Any())
            {
                foreach (var rect in checkerboardRects)
                {
                    PixelCanvas.Children.Insert(0, rect);
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
                        jpegEncoder.QualityLevel = 90;
                        encoder = jpegEncoder;
                        encoder.Frames.Add(BitmapFrame.Create(cropped));
                        break;
                    case ".bmp":
                        encoder = new BmpBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(cropped));
                        break;
                    default:
                        encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(cropped));
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
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                const double zoomStep = 0.1;
                currentZoom += e.Delta > 0 ? zoomStep : -zoomStep;
                currentZoom = Math.Max(0.1, Math.Min(currentZoom, 10.0));

                canvasScaleTransform.ScaleX = currentZoom;
                canvasScaleTransform.ScaleY = currentZoom;

                e.Handled = true;
            }
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            Window currentWindow = Window.GetWindow(this);
            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
            currentWindow?.Close();
        }

        private void PixelCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!isPaintMode && e.ButtonState == MouseButtonState.Pressed)
            {
                lastDragPoint = e.GetPosition(ScrollArea);
                PixelCanvas.CaptureMouse();
                PixelCanvas.Cursor = Cursors.ScrollAll;
            }
        }

        private void PixelCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (isPaintMode && isPainting)
            {
                Point pos = e.GetPosition(PixelCanvas);
                HitTestResult hit = VisualTreeHelper.HitTest(PixelCanvas, pos);
                if (hit != null && hit.VisualHit is Rectangle rect && rect.Tag is Point)
                {
                    PaintRectangle(rect);
                }
            }
            else if (!isPaintMode && lastDragPoint.HasValue && e.LeftButton == MouseButtonState.Pressed)
            {
                // Panning logic
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
            if (isPaintMode)
            {
                isPainting = false;
                hasPushedUndoForDrag = false;
                Mouse.Capture(null);
            }
            else
            {
                PixelCanvas.ReleaseMouseCapture();
                lastDragPoint = null;
                PixelCanvas.Cursor = Cursors.Cross; // Or your default cursor
            }
        }

        private void PixelCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!isPaintMode && PixelCanvas.IsMouseCaptured)
            {
                PixelCanvas.ReleaseMouseCapture();
                lastDragPoint = null;
                PixelCanvas.Cursor = Cursors.Cross; // Or your default cursor
            }
        }


        private void PaintRectangle(Rectangle rect)
        {
            if (rect.Fill is SolidColorBrush brush && brush.Color != selectedColor)
            {
                if (!hasPushedUndoForDrag)
                {
                    PushUndoState();
                    hasPushedUndoForDrag = true;
                }
                rect.Fill = new SolidColorBrush(selectedColor);
            }
        }

        private bool _isUpdatingTextBoxes = false;

        

        private async void ResizeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingTextBoxes) return;

            if (int.TryParse(widthTB.Text, out int newWidth) && int.TryParse(heightTB.Text, out int newHeight) && newWidth > 0 && newHeight > 0)
            {
                ShowLoading("Resizing...");
                await Task.Run(() =>
                {
                    Dispatcher.Invoke(() => PushUndoState());
                    Dispatcher.Invoke(() =>
                    {
                        targetSize = Math.Max(newWidth, newHeight);
                        aspectRatio = (double)newWidth / newHeight;
                        RenderPixelatedImage(originalImage, targetSize);
                    });
                });
                HideLoading();
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
                inlineColorPicker.SelectedColor = selectedColor;
            }
        }

        private void InlineColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e)
        {
            if (e.NewValue.HasValue)
            {
                selectedColor = e.NewValue.Value;
                SelectedColorPreview.Fill = new SolidColorBrush(selectedColor);
            }
        }

        private void PaintModeBtn_Click(object sender, RoutedEventArgs e)
        {
            isPaintMode = !isPaintMode;
            PaintModeBtn.Content = isPaintMode ? "Exit Paint Mode" : "Paint Mode";
            PixelCanvas.Cursor = isPaintMode ? Cursors.Pen : Cursors.Cross;
        }

        private void UndoBtn_Click(object sender, RoutedEventArgs e)
        {
            if (undoStack.Count > 0)
            {
                ShowLoading("Undoing...");
                DoEvents();
                redoStack.Push(new PixelState(GetPixelRectangles(), userBackgroundColor));
                var prevState = undoStack.Pop();
                RestorePixelState(prevState);
                UpdateUndoRedoButtonStates();
                HideLoading();
            }
        }

        private void RedoBtn_Click(object sender, RoutedEventArgs e)
        {
            if (redoStack.Count > 0)
            {
                ShowLoading("Redoing...");
                DoEvents();
                undoStack.Push(new PixelState(GetPixelRectangles(), userBackgroundColor));
                var nextState = redoStack.Pop();
                RestorePixelState(nextState);
                UpdateUndoRedoButtonStates();
                HideLoading();
            }
        }

        private void UpdateBgColorPreview()
        {
            if (userBackgroundColor.HasValue)
            {
                SelectedBgColorPreview.Fill = new SolidColorBrush(userBackgroundColor.Value);
                SelectedBgColorPreview.OpacityMask = null;
            }
            else
            {
                SelectedBgColorPreview.Fill = Brushes.Transparent;
                if (SelectedBgColorPreview.OpacityMask == null)
                {
                    SelectedBgColorPreview.OpacityMask = (VisualBrush)this.Resources["CheckerboardBrushForPreview"];
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
            PushUndoState();

            if (e.NewValue.HasValue && e.NewValue.Value.A > 0)
            {
                userBackgroundColor = e.NewValue.Value;
            }
            else
            {
                userBackgroundColor = null;
            }
            UpdateBgColorPreview();
            RedrawBackgroundOnly();
        }

        private void ApplyFilter(Action<Rectangle, Color> filterAction)
        {
            PushUndoState();
            foreach (var rect in GetPixelRectangles())
            {
                if (rect.Fill is SolidColorBrush brush)
                {
                    var color = brush.Color;
                    if (color.A == 0) continue;
                    filterAction(rect, color);
                }
            }
        }

        private async void GrayscleBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowLoading("Applying Grayscale...");
            await Task.Run(() =>
            {
                Dispatcher.Invoke(() =>
                {
                    PushUndoState();
                    var rectangles = GetPixelRectangles();
                    foreach (var rect in rectangles)
                    {
                        if (rect.Fill is SolidColorBrush brush)
                        {
                            var color = brush.Color;
                            if (color.A == 0) continue;
                            byte gray = (byte)(0.299 * color.R + 0.587 * color.G + 0.114 * color.B);
                            rect.Fill = new SolidColorBrush(Color.FromArgb(color.A, gray, gray, gray));
                        }
                    }
                });
            });
            HideLoading();
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
            Color tintColor = Colors.SteelBlue;
            double tintStrength = 0.3;

            ApplyFilter((rect, color) =>
            {
                byte r = (byte)(color.R * (1 - tintStrength) + tintColor.R * tintStrength);
                byte g = (byte)(color.G * (1 - tintStrength) + tintColor.G * tintStrength);
                byte b = (byte)(color.B * (1 - tintStrength) + tintColor.B * tintStrength);
                rect.Fill = new SolidColorBrush(Color.FromArgb(color.A, r, g, b));
            });
        }

        private async void RemoveBgBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowLoading("Removing Background...");
            await Task.Run(() =>
            {
                Dispatcher.Invoke(() => PushUndoState());
                // ... rest of your remove background logic, using Dispatcher.Invoke for UI updates ...
            });
            HideLoading();
        }

        private void RedrawBackgroundOnly()
        {
            var bgRects = PixelCanvas.Children
                .OfType<Rectangle>()
                .Where(r => r.Tag as string == "Checkerboard")
                .ToList();
            foreach (var rect in bgRects)
                PixelCanvas.Children.Remove(rect);

            int pixelWidth = int.Parse(widthTB.Text);
            int pixelHeight = int.Parse(heightTB.Text);
            double canvasViewboxWidth = PixelCanvas.Width;
            double canvasViewboxHeight = PixelCanvas.Height;
            double cellSizeX = canvasViewboxWidth / pixelWidth;
            double cellSizeY = canvasViewboxHeight / pixelHeight;
            double cellSize = Math.Floor(Math.Min(cellSizeX, cellSizeY));
            if (cellSize < 1) cellSize = 1;
            double totalImageWidth = pixelWidth * cellSize;
            double totalImageHeight = pixelHeight * cellSize;
            double offsetX = Math.Floor((canvasViewboxWidth - totalImageWidth) / 2);
            double offsetY = Math.Floor((canvasViewboxHeight - totalImageHeight) / 2);

            if (userBackgroundColor.HasValue)
            {
                PixelCanvas.Background = new SolidColorBrush(userBackgroundColor.Value);
            }
            else
            {
                PixelCanvas.Background = Brushes.Transparent;
                for (int y = 0; y < pixelHeight; y++)
                {
                    for (int x = 0; x < pixelWidth; x++)
                    {
                        Rectangle bgSquare = new Rectangle
                        {
                            Width = cellSize,
                            Height = cellSize,
                            Fill = new SolidColorBrush((x + y) % 2 == 0 ? Colors.LightGray : Colors.Gainsboro),
                            Tag = "Checkerboard"
                        };
                        Canvas.SetLeft(bgSquare, offsetX + x * cellSize);
                        Canvas.SetTop(bgSquare, offsetY + y * cellSize);
                        PixelCanvas.Children.Insert(0, bgSquare);
                    }
                }
            }
        }

        private static bool IsColorMatch(Color a, Color b, int tolerance)
        {
            return Math.Abs(a.R - b.R) <= tolerance &&
                   Math.Abs(a.G - b.G) <= tolerance &&
                   Math.Abs(a.B - b.B) <= tolerance;
        }

        private void SaturateBtn_Click(object sender, RoutedEventArgs e)
        {
            PushUndoState();
            double saturateAmount = 0.3;

            foreach (var rect in GetPixelRectangles())
            {
                if (rect.Fill is SolidColorBrush brush)
                {
                    var color = brush.Color;
                    if (color.A == 0) continue;

                    ColorToHsl(color, out double h, out double s, out double l);
                    s = Math.Min(1.0, s + saturateAmount * (1.0 - s));
                    var saturatedColor = HslToColor(h, s, l, color.A);
                    rect.Fill = new SolidColorBrush(saturatedColor);
                }
            }
        }

        private static void ColorToHsl(Color color, out double h, out double s, out double l)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            h = s = l = (max + min) / 2.0;

            if (max == min)
            {
                h = s = 0;
            }
            else
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

                if (max == r)
                    h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g)
                    h = (b - r) / d + 2;
                else
                    h = (r - g) / d + 4;

                h /= 6.0;
            }
        }

        private static Color HslToColor(double h, double s, double l, byte alpha)
        {
            double r, g, b;

            if (s == 0)
            {
                r = g = b = l;
            }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                r = HueToRgb(p, q, h + 1.0 / 3.0);
                g = HueToRgb(p, q, h);
                b = HueToRgb(p, q, h - 1.0 / 3.0);
            }

            return Color.FromArgb(alpha, (byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }
        private void ShowLoading(string message = "Processing...")
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingMessage.Text = message;
            DoEvents();
        }

        private void HideLoading()
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }

        // Helper to force UI update
        private void DoEvents()
        {
            Application.Current.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(delegate { }));
        }

        
        
    }
}
