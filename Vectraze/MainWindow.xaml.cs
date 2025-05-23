using Microsoft.Win32;
using System; // Added for Uri
using System.Linq; // Added for Contains
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input; // Added for MouseButtonEventArgs
using System.Windows.Media;
using System.Windows.Media.Imaging;


namespace Vectraze
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        // Event handler for when the logo video ends
        private void LogoVideo_MediaEnded(object sender, RoutedEventArgs e)
        {
            // To loop the video, set its position back to the beginning and play again
            if (sender is MediaElement me)
            {
                me.Position = TimeSpan.Zero;
                me.Play();
            }
        }


        private void HandleImageLoad(string filePath)
        {
            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // Cache immediately
                bitmap.EndInit();
                bitmap.Freeze(); // Freeze for performance and cross-thread access if needed

                ImagePreview.Source = bitmap;
                ImagePreview.Visibility = Visibility.Visible;
                PlaceholderContent.Visibility = Visibility.Collapsed;
                RasterizedBtn.IsEnabled = true; // Enable button after image is loaded
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading image: {ex.Message}", "Image Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ClearImagePreview();
            }
        }

        private void ClearImagePreview()
        {
            ImagePreview.Source = null;
            ImagePreview.Visibility = Visibility.Collapsed;
            PlaceholderContent.Visibility = Visibility.Visible;
            RasterizedBtn.IsEnabled = false;
        }

        // This method replaces the old AddImageBtn_Click
        private void OpenImageDialog()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = "Open Image File";
            openFileDialog.Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.svg"; // Added SVG as per original code

            if (openFileDialog.ShowDialog() == true)
            {
                HandleImageLoad(openFileDialog.FileName);
            }
        }

        // Event handler for clicking the drop border
        private void DropBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            OpenImageDialog();
        }

        private void DropBorder_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void DropBorder_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

                if (files.Length > 0)
                {
                    string filePath = files[0];
                    // Updated extension check to be case-insensitive and match dialog filter
                    string extension = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
                    string[] allowedExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".svg" };

                    if (allowedExtensions.Contains(extension))
                    {
                        HandleImageLoad(filePath);
                    }
                    else
                    {
                        MessageBox.Show("Unsupported file format. Please drop a PNG, JPG, JPEG, BMP, GIF, or SVG file.", "Unsupported File", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            e.Handled = true;
        }

        private void RasterizedBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ImagePreview.Source is BitmapImage bitmapImage)
            {
                Pixelated PixelUserControl = new Pixelated(bitmapImage);
                this.Content = PixelUserControl;
            }
            else
            {
                MessageBox.Show("Please load an image first.", "No Image", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // Example method for how AddImageBtn_Click would be if it were still a button
        // This is effectively replaced by DropBorder_MouseLeftButtonUp calling OpenImageDialog()
        // public void AddImageBtn_Click(object sender, RoutedEventArgs e)
        // {
        //     OpenImageDialog();
        // }
    }
}