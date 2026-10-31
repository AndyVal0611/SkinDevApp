// Minimal stand-ins for the two fusion types that live in MultiViewSession.cs (which needs OpenCV / ONNX). Same names and members the linked files use.
using System.Collections.Generic;

namespace SkinDevApp.Scanning
{
    public sealed class FusedResult
    {
        public Dictionary<string, double> PooledPercent { get; set; } = new Dictionary<string, double>();
        public List<string> Notes { get; set; } = new List<string>();
    }

    public static class ViewFusion
    {
        public const double DisagreementSpread = 30.0;   // same value as MultiViewSession.cs
    }
}
