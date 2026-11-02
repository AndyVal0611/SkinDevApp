// ============================================================================
// V6FeatureFlag.cs  -  namespace SkinDevApp.V6
//
// The single switch for the V6 unified assessment. DEFAULT: OFF. While it is off, nothing in this folder is called and the application
// behaves exactly as before (V5 classifier, separate detectors, existing result screens).
//
// It is switched on only by (a) the environment variable LUMYVUE_V6_UNIFIED=1 or (b) a provider the application sets at startup
// (Phase 4 will read a setting). No dependency on WPF, configuration files or the database.
// ============================================================================

using System;

namespace SkinDevApp.V6
{
    public static class V6FeatureFlag
    {
        public const string EnvironmentVariable = "LUMYVUE_V6_UNIFIED";

        /// <summary>Optional application-supplied setting. Null means "use the environment variable".</summary>
        public static Func<bool> Provider { get; set; }

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    if (Provider != null) return Provider();
                    string v = Environment.GetEnvironmentVariable(EnvironmentVariable);
                    return string.Equals(v, "1", StringComparison.Ordinal) || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }           // a broken setting never turns V6 on
            }
        }
    }
}
