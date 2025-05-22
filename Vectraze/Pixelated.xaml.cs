using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace Vectraze
{
    public partial class Pixelated : UserControl
    {
        private double currentZoom = 1.0;
        public Pixelated(BitmapImage bitmapImage)
        {
            InitializeComponent();
            PixelCanvas.Loaded += (s, e) => RenderPixelatedImage(bitmapImage);
        }

        private void RenderPixelatedImage(BitmapImage image)
        {
            int targetSize = 32;
            int pixelWidth, pixelHeight;

            double aspectRatio = image.PixelWidth / (double)image.PixelHeight;
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

            // Draw pixelated image
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
                        Fill = new SolidColorBrush(Color.FromArgb(a, r, g, b))
                    };

                    Canvas.SetLeft(rect, offsetX + x * cellSize);
                    Canvas.SetTop(rect, offsetY + y * cellSize);
                    PixelCanvas.Children.Add(rect);
                }
            }
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            double width = PixelCanvas.ActualWidth;
            double height = PixelCanvas.ActualHeight;

            if (width == 0 || height == 0)
            {
                MessageBox.Show("Canvas is empty or has no size.");
                return;
            }

            RenderTargetBitmap rtb = new RenderTargetBitmap(
                (int)width, (int)height, 96, 96, PixelFormats.Pbgra32);

            rtb.Render(PixelCanvas);

            PngBitmapEncoder pngEncoder = new PngBitmapEncoder();
            pngEncoder.Frames.Add(BitmapFrame.Create(rtb));

            SaveFileDialog dialog = new SaveFileDialog
            {
                FileName = "pixelated_image",
                DefaultExt = ".png",
                Filter = "PNG Image|*.png"
            };

            if (dialog.ShowDialog() == true)
            {
                using (var fs = new FileStream(dialog.FileName, FileMode.Create))
                {
                    pngEncoder.Save(fs);
                }

                MessageBox.Show("Image saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        private void ScrollArea_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            const double zoomStep = 0.1;
            if (e.Delta > 0)
                currentZoom += zoomStep;
            else
                currentZoom = Math.Max(0.1, currentZoom - zoomStep); // Prevent zooming out too far

            canvasScaleTransform.ScaleX = currentZoom;
            canvasScaleTransform.ScaleY = currentZoom;

            e.Handled = true;
        }
        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            // Find the parent Window
            Window currentWindow = Window.GetWindow(this);

            // Create and show a new MainWindow
            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();

            // Close the current window
            currentWindow?.Close();
        }



    }
}
