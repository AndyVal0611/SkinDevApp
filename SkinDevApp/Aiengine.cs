using System;
using System.IO;
using OpenCvSharp;
using SkinDevApp.AI;

namespace SkinDevApp
{
    /// <summary>
    /// Application-wide singleton around the ONNX classifier. Mirrors the
    /// existing static-class pattern already used by DatabaseHelper.
    ///
    /// ================================================================
    /// PLUG IN YOUR precisionskin.onnx HERE - step by step
    /// ================================================================
    /// 1. In Visual Studio Solution Explorer, right-click the SkinDevApp
    ///    project -> Add -> New Folder -> name it "Model".
    /// 2. Right-click the new "Model" folder -> Add -> Existing Item ->
    ///    select precisionskin.onnx from wherever your notebook exported it
    ///    (runs/with_aux/models/final/precisionskin.onnx).
    /// 3. Click the file you just added in Solution Explorer, open the
    ///    Properties panel (F4 if it's not visible), and set:
    ///        Build Action              = Content
    ///        Copy to Output Directory  = Copy if newer
    ///    This makes Visual Studio copy the .onnx next to SkinDevApp.exe on
    ///    every build, so ModelPath below finds it with no absolute path
    ///    and no editing required on your end.
    /// 4. Build and run. AiEngine.EnsureLoaded() (called automatically at
    ///    startup) will find it there.
    ///
    /// If you'd rather keep the model outside the project folder entirely,
    /// just change ModelPath below to an absolute path instead.
    /// ================================================================
    /// </summary>
    public static class AiEngine
    {
        public static readonly string ModelPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Model", "precisionskin.onnx");

        private static readonly object _lock = new object();
        private static OnnxInference _classifier;
        private static Exception _loadError;

        /// <summary>True once the ONNX model is loaded and ready to predict.</summary>
        public static bool IsAvailable
        {
            get { lock (_lock) { return _classifier != null; } }
        }

        /// <summary>Set when loading failed - show this to the user instead of crashing.</summary>
        public static string LoadErrorMessage
        {
            get { lock (_lock) { return _loadError?.Message; } }
        }

        /// <summary>
        /// Loads the model if it hasn't been already. Safe to call more than
        /// once and from a background thread - App.xaml.cs fires this once
        /// at startup so the model is warm before the user's first click.
        /// </summary>
        public static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_classifier != null || _loadError != null) return;

                try
                {
                    if (!File.Exists(ModelPath))
                    {
                        throw new FileNotFoundException(
                            "precisionskin.onnx not found at:\n" + ModelPath + "\n\n" +
                            "See the step-by-step instructions at the top of AiEngine.cs " +
                            "for how to add it to the project.",
                            ModelPath);
                    }

                    _classifier = new OnnxInference(ModelPath);
                }
                catch (Exception ex)
                {
                    _loadError = ex;
                }
            }
        }

        /// <summary>
        /// Runs the classifier on a BGR frame. Throws if the model failed to
        /// load - callers should check IsAvailable first and show
        /// LoadErrorMessage to the user rather than letting this throw
        /// straight into the UI.
        /// </summary>
        public static PredictionResult Predict(Mat frameBgr)
        {
            EnsureLoaded();

            if (_classifier == null)
                throw _loadError ?? new InvalidOperationException("Model not loaded.");

            return _classifier.Predict(frameBgr);
        }
    }
}