using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace SkinDevApp
{
    /// <summary>
    /// Conversions between WPF's BitmapSource, System.Drawing.Bitmap and
    /// OpenCvSharp's Mat. Needed because the camera/UI layer speaks WPF,
    /// while ONNX Runtime and the Grad-CAM++ service speak OpenCV/BGR bytes.
    /// </summary>
    public static class ImageInterop
    {
        /// <summary>
        /// WPF BitmapSource -> OpenCV Mat (BGR, uint8).
        ///
        /// Works identically whether the source came from the live webcam
        /// (via AForge -> Bitmap -> BitmapSource) or from an uploaded file
        /// (via OpenFileDialog -> BitmapImage), because it round-trips
        /// through a PNG encode rather than assuming a specific pixel format.
        /// </summary>
        public static Mat BitmapSourceToMat(BitmapSource source)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));

            using var ms = new MemoryStream();
            encoder.Save(ms);

            byte[] bytes = ms.ToArray();
            Mat mat = Cv2.ImDecode(bytes, ImreadModes.Color); // BGR

            if (mat.Empty())
                throw new InvalidOperationException(
                    "Failed to decode the current image for analysis.");

            return mat;
        }

        /// <summary>
        /// System.Drawing.Bitmap (e.g. the Grad-CAM overlay returned by the
        /// Python service) -> a frozen, cross-thread-safe WPF BitmapSource
        /// suitable for assigning to an Image control's Source property.
        /// </summary>
        public static BitmapSource ToBitmapSource(Bitmap bitmap)
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            ms.Position = 0;

            var result = new BitmapImage();
            result.BeginInit();
            result.CacheOption = BitmapCacheOption.OnLoad;
            result.StreamSource = ms;
            result.EndInit();
            result.Freeze();

            return result;
        }
    }
}