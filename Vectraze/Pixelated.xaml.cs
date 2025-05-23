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

        public Pixelated(BitmapImage bitmapImage)
        {
            InitializeComponent();
            originalImage = bitmapImage;
            aspectRatio = bitmapImage.PixelWidth / (double)bitmapImage.PixelHeight;

            PixelCanvas.Loaded += (s, e) => RenderPixelatedImage(originalImage, targetSize);

            widthTB.TextChanged += WidthTB_TextChange;
            heightTB.TextChanged += HeightTB_TextChange;
        }

        private void RenderPixelatedImage(BitmapImage image, int size)
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

            // Draw checkerboard background
            for (int y = 0; y < pixelHeight; y++)
            {
                for (int x = 0; x < pixelWidth; x++)
                {
                    Rectangle bgSquare = new Rectangle
                    {
                        Width = cellSize,
                        Height = cellSize,
                        Fill = new SolidColorBrush((x + y) % 2 == 0 ? Colors.LightGray : Colors.Gray)
                    };

                    Canvas.SetLeft(bgSquare, offsetX + x * cellSize);
                    Canvas.SetTop(bgSquare, offsetY + y * cellSize);
                    PixelCanvas.Children.Add(bgSquare);
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

        private void PixelRect_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (isPaintMode && sender is Rectangle rect)
            {
                rect.Fill = new SolidColorBrush(selectedColor);
                e.Handled = true;
            }
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            var originalTransform = PixelCanvas.LayoutTransform;
            PixelCanvas.LayoutTransform = Transform.Identity;

            Size size = new Size(PixelCanvas.ActualWidth, PixelCanvas.ActualHeight);
            PixelCanvas.Measure(size);
            PixelCanvas.Arrange(new Rect(size));

            RenderTargetBitmap rtb = new RenderTargetBitmap(
                (int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(PixelCanvas);

            PixelCanvas.LayoutTransform = originalTransform;

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
    }
}
