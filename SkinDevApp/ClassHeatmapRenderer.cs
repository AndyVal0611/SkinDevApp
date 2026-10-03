// ============================================================================
// ClassHeatmapRenderer.cs  (NEW)  -  namespace SkinDevApp.Imaging
//
// Class-coloured Grad-CAM++ overlay (replaces the JET look for the kiosk).
//
//   per class k:  a_k(x,y) = AlphaMax * heat_k(x,y)^Gamma * StrengthFactor(rel_k)
//   layers are composited with the standard "over" operator, weakest first.
//
// To keep 30 fps video cheap the heatmap is composited ONCE, at heatmap
// resolution, into a premultiplied colour plane + an alpha plane
// (ClassOverlayLayer). Each video frame then only resizes those two planes
// (cached per frame size) and blends:
//
//     out = frame * (1 - A * f) + P * f          f = age/motion factor 0..1
//
// Heatmap.exe-style JET (HeatmapRenderer.cs) is left untouched and unused here.
// ============================================================================

using System;
using System.Collections.Generic;
using OpenCvSharp;
using SkinDevApp.Explainability;

namespace SkinDevApp.Imaging
{
    /// <summary>Pre-composited class overlay at heatmap resolution.</summary>
    public sealed class ClassOverlayLayer : IDisposable
    {
        private readonly object _lock = new object();

        private Mat _premult;     // CV_32FC3, colour * alpha
        private Mat _alpha;       // CV_32FC1

        private int _cw, _ch;
        private Mat _cachedP, _cachedA;

        internal ClassOverlayLayer(Mat premult, Mat alpha)
        {
            _premult = premult;
            _alpha = alpha;
        }

        /// <summary>Alpha-blend this layer over frameBgr. Caller disposes result.</summary>
        public Mat Blend(Mat frameBgr, double factor)
        {
            if (frameBgr == null || frameBgr.Empty())
                throw new ArgumentException("Empty frame.", nameof(frameBgr));

            if (factor <= 0.001 || _premult == null)
                return frameBgr.Clone();

            if (factor > 1.0) factor = 1.0;

            Mat p, a;

            lock (_lock)
            {
                if (_cachedP == null || _cw != frameBgr.Width || _ch != frameBgr.Height)
                {
                    _cachedP?.Dispose();
                    _cachedA?.Dispose();
                    _cachedP = new Mat();
                    _cachedA = new Mat();

                    Size target = new Size(frameBgr.Width, frameBgr.Height);
                    Cv2.Resize(_premult, _cachedP, target, interpolation: InterpolationFlags.Linear);
                    Cv2.Resize(_alpha, _cachedA, target, interpolation: InterpolationFlags.Linear);
                    _cw = frameBgr.Width;
                    _ch = frameBgr.Height;
                }

                p = _cachedP.Clone();
                a = _cachedA.Clone();
            }

            using (p)
            using (a)
            using (Mat a3 = new Mat())
            using (Mat inv3 = new Mat())
            using (Mat frameF = new Mat())
            using (Mat sum = new Mat())
            {
                if (factor < 0.999)
                {
                    Cv2.Multiply(p, new Scalar(factor, factor, factor), p);
                    Cv2.Multiply(a, new Scalar(factor), a);
                }

                Cv2.Merge(new[] { a, a, a }, a3);
                Cv2.Subtract(new Scalar(1.0, 1.0, 1.0), a3, inv3);

                frameBgr.ConvertTo(frameF, MatType.CV_32FC3);
                Cv2.Multiply(frameF, inv3, frameF);
                Cv2.Add(frameF, p, sum);

                Mat result = new Mat();
                sum.ConvertTo(result, MatType.CV_8UC3);
                return result;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _premult?.Dispose(); _premult = null;
                _alpha?.Dispose(); _alpha = null;
                _cachedP?.Dispose(); _cachedP = null;
                _cachedA?.Dispose(); _cachedA = null;
            }
        }
    }

    public static class ClassHeatmapRenderer
    {
        /// <summary>
        /// Composite the given class indices (draw order = list order) into one
        /// layer. Returns null if nothing can be drawn.
        /// </summary>
        public static ClassOverlayLayer BuildLayer(ClassHeatmapSet set, IList<int> classIndices, bool useRelativeStrength = true)
        {
            if (set == null || classIndices == null || classIndices.Count == 0)
                return null;

            Mat premult = null;
            Mat alphaTot = null;

            try
            {
                foreach (int k in classIndices)
                {
                    ClassMapInfo map = set[k];
                    if (map == null || map.Heat8U == null || map.Heat8U.Empty()) continue;

                    int w = map.Heat8U.Width, h = map.Heat8U.Height;

                    if (premult == null)
                    {
                        premult = new Mat(h, w, MatType.CV_32FC3, Scalar.All(0));
                        alphaTot = new Mat(h, w, MatType.CV_32FC1, Scalar.All(0));
                    }
                    else if (premult.Width != w || premult.Height != h)
                    {
                        continue;   // all maps from one response share a size
                    }

                    // useRelativeStrength=false: every class is drawn at the same opacity scale. The relative
                    // strength is a raw-peak ratio between classes, not evidence, so comparison images do not use it.
                    double scale = ClassPalette.AlphaMax *
                        (useRelativeStrength ? ClassPalette.StrengthFactor(map.RelativeStrength) : 1.0);

                    using (Mat heatF = new Mat())
                    using (Mat a = new Mat())
                    using (Mat inv1 = new Mat())
                    using (Mat a3 = new Mat())
                    using (Mat inv3 = new Mat())
                    using (Mat color = new Mat(h, w, MatType.CV_32FC3, ClassPalette.ColorsBgr[k]))
                    using (Mat colorA = new Mat())
                    using (Mat oldP = new Mat())
                    using (Mat oldA = new Mat())
                    {
                        map.Heat8U.ConvertTo(heatF, MatType.CV_32FC1, 1.0 / 255.0);
                        Cv2.Pow(heatF, ClassPalette.Gamma, a);
                        Cv2.Multiply(a, new Scalar(scale), a);

                        Cv2.Subtract(new Scalar(1.0), a, inv1);
                        Cv2.Merge(new[] { a, a, a }, a3);
                        Cv2.Merge(new[] { inv1, inv1, inv1 }, inv3);

                        // premult = color * a + premult * (1 - a)
                        Cv2.Multiply(color, a3, colorA);
                        Cv2.Multiply(premult, inv3, oldP);
                        Cv2.Add(colorA, oldP, premult);

                        // alpha = a + alpha * (1 - a)
                        Cv2.Multiply(alphaTot, inv1, oldA);
                        Cv2.Add(a, oldA, alphaTot);
                    }
                }

                if (premult == null) return null;

                var layer = new ClassOverlayLayer(premult, alphaTot);
                premult = null;
                alphaTot = null;
                return layer;
            }
            finally
            {
                premult?.Dispose();
                alphaTot?.Dispose();
            }
        }

        /// <summary>Convenience for one-off renders (result view, saved files).</summary>
        public static Mat RenderOverlay(
            Mat frameBgr, ClassHeatmapSet set, OverlayView view, int displayIndex, double factor = 1.0)
        {
            using (ClassOverlayLayer layer = BuildLayer(set, set.IndicesForView(view, displayIndex)))
            {
                if (layer == null) return frameBgr.Clone();
                return layer.Blend(frameBgr, factor);
            }
        }

        /// <summary>Overlay of ONE class, for the per-class files in the capture archive.</summary>
        public static Mat RenderSingleClass(Mat frameBgr, ClassHeatmapSet set, int classIndex)
        {
            using (ClassOverlayLayer layer = BuildLayer(set, new List<int> { classIndex }, useRelativeStrength: false))
            {
                if (layer == null) return frameBgr.Clone();
                return layer.Blend(frameBgr, 1.0);
            }
        }
    }
}
