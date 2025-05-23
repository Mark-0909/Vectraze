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

namespace Vectraze
{
    public partial class Pixelated : UserControl
    {
        private double currentZoom = 1.0;
        private Point? lastDragPoint;
        private BitmapImage originalImage;
        private double aspectRatio;
        public int targetSize = 32;
        private bool isPaintMode = false;
        private Color selectedColor = Colors.Transparent;
        private Color? userBackgroundColor = null;

        // Snapshot-based undo/redo
        private class PixelState
        {
            public Dictionary<Point, Color> Pixels { get; } = new();
            public PixelState(IEnumerable<Rectangle> rects)
            {
                foreach (var rect in rects)
                {
                    if (rect.Tag is Point pt && rect.Fill is SolidColorBrush brush)
                        Pixels[pt] = brush.Color;
                }
            }
        }
        private Stack<PixelState> undoStack = new();
        private Stack<PixelState> redoStack = new();

        public Pixelated(BitmapImage bitmapImage)
        {
            InitializeComponent();
            originalImage = bitmapImage;
            aspectRatio = bitmapImage.PixelWidth / (double)bitmapImage.PixelHeight;

            PixelCanvas.Loaded += (s, e) => RenderPixelatedImage(originalImage, targetSize);

            widthTB.TextChanged += WidthTB_TextChange;
            heightTB.TextChanged += HeightTB_TextChange;
        }

        private void RenderPixelatedImage(BitmapImage image, int size, bool forExport = false)
        {
            targetSize = size;
            int pixelWidth, pixelHeight;

            if (aspectRatio >= 1.0)
            {
                pixelWidth = targetSize;
                pixelHeight = (int)(targetSize / aspectRatio);
            }
            else
            {
                pixelHeight = targetSize;
                pixelWidth = (int)(targetSize * aspectRatio);
            }

            widthTB.Text = pixelWidth.ToString();
            heightTB.Text = pixelHeight.ToString();

            TransformedBitmap resized = new TransformedBitmap(image, new ScaleTransform(
                pixelWidth / (double)image.PixelWidth,
                pixelHeight / (double)image.PixelHeight));

            int stride = pixelWidth * 4;
            byte[] pixels = new byte[stride * pixelHeight];
            resized.CopyPixels(pixels, stride, 0);

            double canvasWidth = PixelCanvas.ActualWidth;
            double canvasHeight = PixelCanvas.ActualHeight;

            if (canvasWidth == 0 || canvasHeight == 0)
            {
                canvasWidth = 512;
                canvasHeight = 512;
            }

            double cellSizeX = canvasWidth / pixelWidth;
            double cellSizeY = canvasHeight / pixelHeight;
            double cellSize = Math.Floor(Math.Min(cellSizeX, cellSizeY));

            double totalImageWidth = pixelWidth * cellSize;
            double totalImageHeight = pixelHeight * cellSize;

            double offsetX = Math.Floor((canvasWidth - totalImageWidth) / 2);
            double offsetY = Math.Floor((canvasHeight - totalImageHeight) / 2);

            PixelCanvas.Children.Clear();

            // Always keep the canvas background transparent
            PixelCanvas.Background = Brushes.Transparent;

            if (!userBackgroundColor.HasValue && !forExport)
            {
                // Draw checkerboard
                for (int y = 0; y < pixelHeight; y++)
                {
                    for (int x = 0; x < pixelWidth; x++)
                    {
                        Rectangle bgSquare = new Rectangle
                        {
                            Width = cellSize,
                            Height = cellSize,
                            Fill = new SolidColorBrush((x + y) % 2 == 0 ? Colors.LightGray : Colors.Gray),
                            Tag = "Checkerboard"
                        };

                        Canvas.SetLeft(bgSquare, offsetX + x * cellSize);
                        Canvas.SetTop(bgSquare, offsetY + y * cellSize);
                        PixelCanvas.Children.Add(bgSquare);
                    }
                }
            }

            // Draw image pixels as rectangles
            for (int y = 0; y < pixelHeight; y++)
            {
                for (int x = 0; x < pixelWidth; x++)
                {
                    int index = (y * stride) + (x * 4);
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

                    // Attach mouse event for painting
                    rect.MouseLeftButtonDown += PixelRect_MouseLeftButtonDown;

                    PixelCanvas.Children.Add(rect);
                }
            }
        }

        private IEnumerable<Rectangle> GetPixelRectangles()
        {
            // Pixel rectangles have Tag of type Point
            return PixelCanvas.Children
                .OfType<Rectangle>()
                .Where(r => r.Tag is Point);
        }

        private void PushUndoState()
        {
            undoStack.Push(new PixelState(GetPixelRectangles()));
            redoStack.Clear();
        }

        private void RestorePixelState(PixelState state)
        {
            foreach (var rect in GetPixelRectangles())
            {
                if (rect.Tag is Point pt && state.Pixels.TryGetValue(pt, out var color))
                    rect.Fill = new SolidColorBrush(color);
            }
        }

        private void PixelRect_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (isPaintMode && sender is Rectangle rect)
            {
                var oldBrush = rect.Fill as SolidColorBrush;
                var oldColor = oldBrush != null ? oldBrush.Color : Colors.Transparent;

                if (oldColor != selectedColor)
                {
                    PushUndoState();
                    rect.Fill = new SolidColorBrush(selectedColor);
                }
                e.Handled = true;
            }
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            // Hide checkerboard rectangles for export
            var checkerboardRects = PixelCanvas.Children
                .OfType<Rectangle>()
                .Where(r => r.Tag as string == "Checkerboard")
                .ToList();

            foreach (var rect in checkerboardRects)
                PixelCanvas.Children.Remove(rect);

            var originalTransform = PixelCanvas.LayoutTransform;
            PixelCanvas.LayoutTransform = Transform.Identity;

            Size size = new Size(PixelCanvas.ActualWidth, PixelCanvas.ActualHeight);
            PixelCanvas.Measure(size);
            PixelCanvas.Arrange(new Rect(size));

            RenderTargetBitmap rtb = new RenderTargetBitmap(
                (int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(PixelCanvas);

            PixelCanvas.LayoutTransform = originalTransform;

            // Restore checkerboard rectangles only
            RestoreCheckerboardOnly();

            SaveFileDialog dialog = new SaveFileDialog
            {
                FileName = "pixelated_image",
                Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg;*.jpeg)|*.jpg;*.jpeg|Bitmap Image (*.bmp)|*.bmp",
                DefaultExt = ".png"
            };

            if (dialog.ShowDialog() == true)
            {
                BitmapEncoder encoder;

                string ext = System.IO.Path.GetExtension(dialog.FileName).ToLower();
                switch (ext)
                {
                    case ".jpg":
                    case ".jpeg":
                        encoder = new JpegBitmapEncoder();
                        break;
                    case ".bmp":
                        encoder = new BmpBitmapEncoder();
                        break;
                    default:
                        encoder = new PngBitmapEncoder();
                        break;
                }

                encoder.Frames.Add(BitmapFrame.Create(rtb));

                using (var fs = new FileStream(dialog.FileName, FileMode.Create))
                {
                    encoder.Save(fs);
                }

                System.Windows.MessageBox.Show("Image saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void RestoreCheckerboardOnly()
        {
            // Get current image area and cell size
            double canvasWidth = PixelCanvas.ActualWidth;
            double canvasHeight = PixelCanvas.ActualHeight;

            int pixelWidth = int.Parse(widthTB.Text);
            int pixelHeight = int.Parse(heightTB.Text);

            double cellSizeX = canvasWidth / pixelWidth;
            double cellSizeY = canvasHeight / pixelHeight;
            double cellSize = Math.Floor(Math.Min(cellSizeX, cellSizeY));

            double totalImageWidth = pixelWidth * cellSize;
            double totalImageHeight = pixelHeight * cellSize;

            double offsetX = Math.Floor((canvasWidth - totalImageWidth) / 2);
            double offsetY = Math.Floor((canvasHeight - totalImageHeight) / 2);

            // Only add checkerboard if no background color
            if (!userBackgroundColor.HasValue)
            {
                for (int y = 0; y < pixelHeight; y++)
                {
                    for (int x = 0; x < pixelWidth; x++)
                    {
                        Rectangle bgSquare = new Rectangle
                        {
                            Width = cellSize,
                            Height = cellSize,
                            Fill = new SolidColorBrush((x + y) % 2 == 0 ? Colors.LightGray : Colors.Gray),
                            Tag = "Checkerboard"
                        };

                        Canvas.SetLeft(bgSquare, offsetX + x * cellSize);
                        Canvas.SetTop(bgSquare, offsetY + y * cellSize);
                        // Insert at the bottom so it doesn't cover pixel rectangles
                        PixelCanvas.Children.Insert(0, bgSquare);
                    }
                }
            }
        }

        private void ScrollArea_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            const double zoomStep = 0.1;
            currentZoom += e.Delta > 0 ? zoomStep : -zoomStep;
            currentZoom = Math.Max(0.1, currentZoom);

            canvasScaleTransform.ScaleX = currentZoom;
            canvasScaleTransform.ScaleY = currentZoom;

            e.Handled = true;
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
            if (!isPaintMode)
            {
                lastDragPoint = e.GetPosition(ScrollArea);
                PixelCanvas.CaptureMouse();
            }
            // In paint mode, handled by PixelRect_MouseLeftButtonDown
        }

        private void PixelCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (lastDragPoint.HasValue && e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPos = e.GetPosition(ScrollArea);
                double offsetX = currentPos.X - lastDragPoint.Value.X;
                double offsetY = currentPos.Y - lastDragPoint.Value.Y;

                ScrollArea.ScrollToHorizontalOffset(ScrollArea.HorizontalOffset - offsetX);
                ScrollArea.ScrollToVerticalOffset(ScrollArea.VerticalOffset - offsetY);

                lastDragPoint = currentPos;
            }
        }

        private void PixelCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            PixelCanvas.ReleaseMouseCapture();
            lastDragPoint = null;
        }

        private void ResizeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(widthTB.Text, out int newWidth) && int.TryParse(heightTB.Text, out int newHeight))
            {
                PixelCanvas.Children.Clear();

                // Compute new aspect ratio if needed
                aspectRatio = newWidth / (double)newHeight;

                // Re-render the pixelated image with new dimensions
                RenderPixelatedImage(originalImage, Math.Max(newWidth, newHeight));
            }
            else
            {
                System.Windows.MessageBox.Show("Please enter valid integer values for width and height.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void HeightTB_TextChange(object sender, TextChangedEventArgs e)
        {
            if (!heightTB.IsFocused) return;

            ValidateIntegerInput(heightTB);
            if (double.TryParse(heightTB.Text, out double newHeight))
            {
                int newWidth = (int)Math.Round(newHeight * aspectRatio);
                widthTB.Text = newWidth.ToString();
            }
        }

        private void WidthTB_TextChange(object sender, TextChangedEventArgs e)
        {
            if (!widthTB.IsFocused) return;

            ValidateIntegerInput(widthTB);
            if (double.TryParse(widthTB.Text, out double newWidth))
            {
                int newHeight = (int)Math.Round(newWidth / aspectRatio);
                heightTB.Text = newHeight.ToString();
            }
        }

        private void ValidateIntegerInput(TextBox textBox)
        {
            string input = textBox.Text;
            string filtered = new string(input.Where(char.IsDigit).ToArray());

            if (filtered != input)
            {
                int caretIndex = textBox.CaretIndex;
                textBox.Text = filtered;
                textBox.CaretIndex = Math.Min(caretIndex, filtered.Length);
            }
        }

        private void ColorPickerBtn_Click(object sender, RoutedEventArgs e)
        {
            // Toggle visibility of the color picker
            if (inlineColorPicker.Visibility == Visibility.Visible)
                inlineColorPicker.Visibility = Visibility.Collapsed;
            else
                inlineColorPicker.Visibility = Visibility.Visible;
        }

        private void InlineColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e)
        {
            if (e.NewValue.HasValue)
            {
                selectedColor = e.NewValue.Value;
                colorPickerBtn.Background = new SolidColorBrush(selectedColor);
            }
        }

        private void PaintModeBtn_Click(object sender, RoutedEventArgs e)
        {
            isPaintMode = !isPaintMode;
            PaintModeBtn.Content = isPaintMode ? "Exit Paint Mode" : "Paint Mode";
        }

        private void UndoBtn_Click(object sender, RoutedEventArgs e)
        {
            if (undoStack.Count > 0)
            {
                redoStack.Push(new PixelState(GetPixelRectangles()));
                var prev = undoStack.Pop();
                RestorePixelState(prev);
            }
        }

        private void RedoBtn_Click(object sender, RoutedEventArgs e)
        {
            if (redoStack.Count > 0)
            {
                undoStack.Push(new PixelState(GetPixelRectangles()));
                var next = redoStack.Pop();
                RestorePixelState(next);
            }
        }

        private void BgColorPickerBtn_Click(object sender, RoutedEventArgs e)
        {
            // Toggle visibility of the background color picker
            if (bgInlineColorPicker.Visibility == Visibility.Visible)
                bgInlineColorPicker.Visibility = Visibility.Collapsed;
            else
                bgInlineColorPicker.Visibility = Visibility.Visible;
        }

        private void BgInlineColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e)
        {
            PushUndoState();
            if (e.NewValue.HasValue && e.NewValue.Value.A > 0)
            {
                userBackgroundColor = e.NewValue.Value;
                bgColorPickerBtn.Background = new SolidColorBrush(userBackgroundColor.Value);
            }
            else
            {
                userBackgroundColor = null;
                bgColorPickerBtn.Background = Brushes.Transparent;
            }
            RenderPixelatedImage(originalImage, targetSize);
        }

        private void GrayscleBtn_Click(object sender, RoutedEventArgs e)
        {
            PushUndoState();
            foreach (var rect in GetPixelRectangles())
            {
                if (rect.Fill is SolidColorBrush brush)
                {
                    var color = brush.Color;
                    if (color.A == 0) continue; // Skip transparent

                    byte gray = (byte)(0.299 * color.R + 0.587 * color.G + 0.114 * color.B);
                    var grayColor = Color.FromArgb(color.A, gray, gray, gray);
                    rect.Fill = new SolidColorBrush(grayColor);
                }
            }
        }

        private void SeppiaBtn_Click(object sender, RoutedEventArgs e)
        {
            PushUndoState();
            foreach (var rect in GetPixelRectangles())
            {
                if (rect.Fill is SolidColorBrush brush)
                {
                    var color = brush.Color;
                    if (color.A == 0) continue; // Skip transparent

                    int tr = (int)(0.393 * color.R + 0.769 * color.G + 0.189 * color.B);
                    int tg = (int)(0.349 * color.R + 0.686 * color.G + 0.168 * color.B);
                    int tb = (int)(0.272 * color.R + 0.534 * color.G + 0.131 * color.B);

                    byte r = (byte)Math.Min(255, tr);
                    byte g = (byte)Math.Min(255, tg);
                    byte b = (byte)Math.Min(255, tb);

                    var sepiaColor = Color.FromArgb(color.A, r, g, b);
                    rect.Fill = new SolidColorBrush(sepiaColor);
                }
            }
        }

        private void InvertBtn_Click(object sender, RoutedEventArgs e)
        {
            PushUndoState();
            foreach (var rect in GetPixelRectangles())
            {
                if (rect.Fill is SolidColorBrush brush)
                {
                    var color = brush.Color;
                    if (color.A == 0) continue; // Skip transparent

                    var invertedColor = Color.FromArgb(color.A, (byte)(255 - color.R), (byte)(255 - color.G), (byte)(255 - color.B));
                    rect.Fill = new SolidColorBrush(invertedColor);
                }
            }
        }

        private void TintBtn_Click(object sender, RoutedEventArgs e)
        {
            PushUndoState();
            Color tint = Colors.Blue; // Change to any color you want
            double tintStrength = 0.3; // 0 = no tint, 1 = full tint

            foreach (var rect in GetPixelRectangles())
            {
                if (rect.Fill is SolidColorBrush brush)
                {
                    var color = brush.Color;
                    if (color.A == 0) continue; // Skip transparent

                    byte r = (byte)(color.R * (1 - tintStrength) + tint.R * tintStrength);
                    byte g = (byte)(color.G * (1 - tintStrength) + tint.G * tintStrength);
                    byte b = (byte)(color.B * (1 - tintStrength) + tint.B * tintStrength);

                    var tintedColor = Color.FromArgb(color.A, r, g, b);
                    rect.Fill = new SolidColorBrush(tintedColor);
                }
            }
        }

        private void SaturateBtn_Click(object sender, RoutedEventArgs e)
        {
            PushUndoState();
            double saturateAmount = 0.3; // Increase by 30% (can adjust)

            foreach (var rect in GetPixelRectangles())
            {
                if (rect.Fill is SolidColorBrush brush)
                {
                    var color = brush.Color;
                    if (color.A == 0) continue; // Skip transparent

                    ColorToHsl(color, out double h, out double s, out double l);
                    s = Math.Min(1.0, s + saturateAmount * (1.0 - s)); // Increase saturation, clamp to 1.0
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
                h = s = 0; // achromatic
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
                r = g = b = l; // achromatic
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

    }
}
