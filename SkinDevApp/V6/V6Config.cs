// ============================================================================
// V6Config.cs  -  namespace SkinDevApp.V6
//
// Reads v6/deployment/v6_deployment_config.json (generated from the run's own metadata) and v6_ood_model.json.
// Fails closed: a missing or malformed field throws, so the service reports itself unavailable rather than guessing.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SkinDevApp.V6
{
    public sealed class PlattParam
    {
        public double A, B;
    }

    public sealed class FusionCalibration
    {
        public PlattParam[] Platt;
        public double[] Thresholds;
    }

    public sealed class RoiSettings
    {
        public int Size, MaxTier1, MaxTotal, FeatureDim;
        public double Margin, AltMargin, MinSideFrac, OverlapSuppressIou;
    }

    public sealed class ModelFileSpec
    {
        public string File, Sha256;
    }

    public sealed class V6Config
    {
        public string RunId, FeatureSchemaVersion;
        public ModelFileSpec GlobalFeatures, FusionGlobalYolo, FusionGlobalOnly;
        public ModelFileSpec FusionFull, RoiFeatures;          // optional: the full variant (Global + YOLO + ROI MobileNetV2)
        public FusionCalibration GlobalYolo, GlobalOnly, Full;
        public RoiSettings Roi;
        public double Band;
        public double[] DeployedThresholds;        // detector thresholds, palette order
        public double MemberFloor, PoolMinConf;
        public int MaxBoxesPerCondition;
        public string OodFile;
        public double OodThreshold;
        public string QualityStatus;
        public Dictionary<string, string> Raw = new Dictionary<string, string>();

        public static V6Config Load(string path)
        {
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement r = doc.RootElement;
                var c = new V6Config
                {
                    RunId = Str(r, "run_id"),
                    FeatureSchemaVersion = Str(r, "feature_schema_version")
                };
                JsonElement m = r.GetProperty("models");
                c.GlobalFeatures = Model(m.GetProperty("global_features"));
                c.FusionGlobalYolo = Model(m.GetProperty("fusion_global_yolo"));
                c.FusionGlobalOnly = Model(m.GetProperty("fusion_global_only"));
                JsonElement cal = r.GetProperty("calibration");
                c.GlobalYolo = Calibration(cal.GetProperty("global_yolo"));
                c.GlobalOnly = Calibration(cal.GetProperty("global_only"));
                if (m.TryGetProperty("fusion_full", out JsonElement mf) && m.TryGetProperty("roi_features", out JsonElement mr) && cal.TryGetProperty("full", out JsonElement cf) && r.TryGetProperty("roi", out JsonElement ro))
                {
                    c.FusionFull = Model(mf); c.RoiFeatures = Model(mr); c.Full = Calibration(cf);
                    c.Roi = new RoiSettings
                    {
                        Size = ro.GetProperty("size").GetInt32(), Margin = ro.GetProperty("margin").GetDouble(), AltMargin = ro.GetProperty("alt_margin").GetDouble(),
                        MinSideFrac = ro.GetProperty("min_side_frac").GetDouble(), MaxTier1 = ro.GetProperty("max_tier1").GetInt32(), MaxTotal = ro.GetProperty("max_total").GetInt32(),
                        OverlapSuppressIou = ro.GetProperty("overlap_suppress_iou").GetDouble(), FeatureDim = ro.GetProperty("feature_dim").GetInt32()
                    };
                }
                JsonElement d = r.GetProperty("decision");
                c.Band = d.GetProperty("band").GetDouble();
                double[] thr = c.GlobalYolo.Thresholds;
                if (d.TryGetProperty("thresholds", out JsonElement dt))
                {
                    double[] x = dt.EnumerateArray().Select(e => e.GetDouble()).ToArray();
                    if (x.Length != V6Conditions.Count || x.Zip(thr, (p, q) => Math.Abs(p - q) > 1e-12).Any(differs => differs))
                        throw new InvalidDataException("decision.thresholds differs from the global_yolo calibration thresholds");
                }
                JsonElement det = r.GetProperty("detectors");
                c.DeployedThresholds = V6Conditions.Names.Select(n => det.GetProperty("per_condition").GetProperty(n).GetProperty("deployed_threshold").GetDouble()).ToArray();
                c.MemberFloor = det.GetProperty("member_floor").GetDouble();
                c.PoolMinConf = det.GetProperty("candidate_pool_min_conf").GetDouble();
                c.MaxBoxesPerCondition = det.GetProperty("max_boxes_per_condition").GetInt32();
                if (det.GetProperty("face_filter_for_evidence").GetBoolean())
                    throw new InvalidDataException("face_filter_for_evidence must be false: training evidence was computed without it");
                JsonElement ood = r.GetProperty("ood");
                c.OodFile = Str(ood, "file");
                c.OodThreshold = ood.GetProperty("threshold").GetDouble();
                c.QualityStatus = r.GetProperty("quality").GetProperty("status").GetString();
                return c;
            }
        }

        private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString();

        private static ModelFileSpec Model(JsonElement e) => new ModelFileSpec { File = Str(e, "file"), Sha256 = Str(e, "sha256") };

        private static FusionCalibration Calibration(JsonElement e)
        {
            PlattParam[] p = e.GetProperty("platt").EnumerateArray().Select(x => new PlattParam { A = x.GetProperty("a").GetDouble(), B = x.GetProperty("b").GetDouble() }).ToArray();
            double[] t = e.GetProperty("thresholds").EnumerateArray().Select(x => x.GetDouble()).ToArray();
            if (p.Length != V6Conditions.Count || t.Length != V6Conditions.Count)
                throw new InvalidDataException("calibration must have one Platt pair and one threshold per condition");
            return new FusionCalibration { Platt = p, Thresholds = t };
        }
    }

    /// <summary>Mahalanobis distance of the 128-d Global embedding to the training distribution. A flag means "unlike the training data", not an unknown condition.</summary>
    public sealed class OodModel
    {
        public double[] Mu;
        public double[,] Precision;
        public double Threshold;

        public static OodModel Load(string path)
        {
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement r = doc.RootElement;
                double[] mu = r.GetProperty("mu").EnumerateArray().Select(x => x.GetDouble()).ToArray();
                JsonElement[] rows = r.GetProperty("precision").EnumerateArray().ToArray();
                if (rows.Length != mu.Length) throw new InvalidDataException("OOD precision matrix does not match mu");
                var p = new double[mu.Length, mu.Length];
                for (int i = 0; i < rows.Length; i++)
                {
                    double[] row = rows[i].EnumerateArray().Select(x => x.GetDouble()).ToArray();
                    if (row.Length != mu.Length) throw new InvalidDataException("OOD precision matrix is not square");
                    for (int j = 0; j < row.Length; j++) p[i, j] = row[j];
                }
                return new OodModel { Mu = mu, Precision = p, Threshold = r.GetProperty("threshold").GetDouble() };
            }
        }

        public double Score(float[] embedding)
        {
            if (embedding.Length != Mu.Length) throw new ArgumentException("embedding length " + embedding.Length + " != " + Mu.Length);
            int n = Mu.Length; var d = new double[n];
            for (int i = 0; i < n; i++) d[i] = embedding[i] - Mu[i];
            double s = 0;
            for (int i = 0; i < n; i++)
            {
                double row = 0;
                for (int j = 0; j < n; j++) row += Precision[i, j] * d[j];
                s += d[i] * row;
            }
            return Math.Sqrt(Math.Max(s, 0));
        }

        public bool IsOut(double score) => score > Threshold;
    }
}
