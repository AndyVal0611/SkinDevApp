// ============================================================================
// ClassHeatmapSet.cs  (NEW)  -  namespace SkinDevApp.Explainability
//
// One Grad-CAM++ result for ONE frame: a heatmap for each of the four classes
// plus everything needed to draw and describe them honestly.
//
// Per class map:
//   Heat8U            8-bit map, normalised PER CLASS (compare SHAPE, not
//                     brightness, across classes)
//   RelativeStrength  raw CAM peak / max over the 4 classes (0..1). Used for
//                     opacity so a weak class is drawn fainter. NOT a probability.
//   Diffuse           the map is spread out (no focal area)
//   Region            approximate facial zone (or diffuse)
// ============================================================================

using System;
using System.Collections.Generic;
using OpenCvSharp;
using SkinDevApp.Imaging;

namespace SkinDevApp.Explainability
{
    public sealed class ClassMapInfo : IDisposable
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public float Probability { get; set; }
        public double RawPeak { get; set; }
        public double RelativeStrength { get; set; }
        public double Top10Mass { get; set; }
        public double CentroidX { get; set; }
        public double CentroidY { get; set; }
        public bool Diffuse { get; set; }

        /// <summary>8-bit, 0..255, per-class normalised.</summary>
        public Mat Heat8U { get; set; }

        public RegionResult Region { get; set; } = new RegionResult();

        /// <summary>True when this map is focal AND a region was found.</summary>
        public bool HasFocalRegion => !Diffuse && Region != null && !Region.Diffuse;

        public void Dispose()
        {
            Heat8U?.Dispose();
            Heat8U = null;
        }
    }

    public sealed class ClassHeatmapSet : IDisposable
    {
        public long FrameId { get; set; }
        public DateTime ComputedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Grad-CAM service's own prediction for this frame.</summary>
        public int PredictedIndex { get; set; } = -1;
        public float Confidence { get; set; }
        public float[] Probabilities { get; set; } = new float[ClassPalette.ClassCount];

        public string Method { get; set; } = "";
        public string ExecMode { get; set; } = "";
        public string ServiceVersion { get; set; } = "";
        public double ServiceLatencyMs { get; set; }
        public double RoundTripMs { get; set; }

        /// <summary>Size of the frame these maps were computed from.</summary>
        public Size SourceSize { get; set; }

        /// <summary>Face box (reference frame only) in SOURCE frame pixels, if found.</summary>
        public Rect? FaceBoxSource { get; set; }

        /// <summary>Small grey copy of the exact source frame (for motion checks).</summary>
        public Mat SourceThumb { get; set; }

        public ClassMapInfo[] Maps { get; set; } = new ClassMapInfo[0];

        public ClassMapInfo this[int classIndex]
        {
            get
            {
                for (int i = 0; i < Maps.Length; i++)
                    if (Maps[i].Index == classIndex) return Maps[i];
                return null;
            }
        }

        /// <summary>
        /// The map of the PREDICTED class - used for the headline region line.
        /// </summary>
        public ClassMapInfo PredictedMap => PredictedIndex >= 0 ? this[PredictedIndex] : null;

        public string RegionSummary()
        {
            ClassMapInfo m = PredictedMap;
            if (m == null) return "Region attribution unavailable";
            if (m.Diffuse) return "Attribution is diffuse — no single region stands out";
            return m.Region.Describe();
        }

        /// <summary>
        /// Which class maps an overlay view draws, ordered WEAKEST FIRST so the
        /// strongest ends up on top.
        /// </summary>
        public List<int> IndicesForView(OverlayView view, int displayIndex)
        {
            var list = new List<int>();

            if (view == OverlayView.All)
            {
                foreach (ClassMapInfo m in Maps) list.Add(m.Index);
                list.Sort((a, b) => this[a].RelativeStrength.CompareTo(this[b].RelativeStrength));
            }
            else if (view == OverlayView.Predicted)
            {
                int idx = displayIndex >= 0 ? displayIndex : PredictedIndex;
                if (idx >= 0 && this[idx] != null) list.Add(idx);
            }
            else
            {
                int idx = ClassPalette.ViewToClassIndex(view);
                if (idx >= 0 && this[idx] != null) list.Add(idx);
            }

            return list;
        }

        public void Dispose()
        {
            if (Maps != null)
                foreach (ClassMapInfo m in Maps) m?.Dispose();
            Maps = new ClassMapInfo[0];

            SourceThumb?.Dispose();
            SourceThumb = null;
        }
    }
}
