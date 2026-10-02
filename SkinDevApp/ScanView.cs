// ============================================================================
// ScanView.cs  (NEW)  -  namespace SkinDevApp.Scanning
//
// The three standardised views of one multi-view scan, and their wording.
//
// NAMING CONVENTION (change only here):
//   Left  = the LEFT  side of the patient's face is shown to the camera
//           (the patient turns their head toward THEIR right).
//   Right = the RIGHT side of the patient's face is shown to the camera
//           (the patient turns their head toward THEIR left).
//   This is the usual clinical-photography meaning of "left/right lateral view".
//   The on-screen text spells it out so nobody has to guess.
// ============================================================================

using System;

namespace SkinDevApp.Scanning
{
    public enum ScanView
    {
        /// <summary>Single-capture mode: no view requirement (the old behaviour).</summary>
        Any = 0,
        Front = 1,
        Left = 2,
        Right = 3
    }

    public static class ScanViews
    {
        /// <summary>Capture order of a multi-view scan.</summary>
        public static readonly ScanView[] Sequence = { ScanView.Front, ScanView.Left, ScanView.Right };

        /// <summary>Folder / file / JSON name: Front, Left, Right (or Single).</summary>
        public static string Name(ScanView v)
        {
            switch (v)
            {
                case ScanView.Front: return "Front";
                case ScanView.Left: return "Left";
                case ScanView.Right: return "Right";
                default: return "Single";
            }
        }

        public static string Title(ScanView v)
        {
            switch (v)
            {
                case ScanView.Front: return "Front view";
                case ScanView.Left: return "Left side";
                case ScanView.Right: return "Right side";
                default: return "Single view";
            }
        }

        /// <summary>What the patient is asked to do for this view.</summary>
        public static string Instruction(ScanView v)
        {
            switch (v)
            {
                case ScanView.Front: return "Face the camera straight on";
                case ScanView.Left: return "Show your LEFT cheek: turn your head toward your right";
                case ScanView.Right: return "Show your RIGHT cheek: turn your head toward your left";
                default: return "Position your face in the oval";
            }
        }

        /// <summary>Step banner such as "Step 2 of 3: Left side".</summary>
        public static string StepText(ScanView v)
        {
            int i = Array.IndexOf(Sequence, v);
            return i < 0 ? Title(v) : "Step " + (i + 1) + " of " + Sequence.Length + ": " + Title(v);
        }

        public static ScanView Next(ScanView v)
        {
            int i = Array.IndexOf(Sequence, v);
            return i >= 0 && i + 1 < Sequence.Length ? Sequence[i + 1] : ScanView.Any;
        }

        public static ScanView Parse(string name)
        {
            foreach (ScanView v in Sequence)
                if (string.Equals(Name(v), name, StringComparison.OrdinalIgnoreCase)) return v;
            return ScanView.Any;
        }
    }
}
