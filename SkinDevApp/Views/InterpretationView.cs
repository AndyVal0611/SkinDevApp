// InterpretationView.cs - the one WPF block that shows a ScanInterpretation (Results, patient record, dermatologist review),
// so the three screens use identical wording and layout.
using System.Windows;
using System.Windows.Controls;

namespace SkinDevApp.Views
{
    public static class InterpretationView
    {
        public static void AddTo(StackPanel sp, ScanInterpretation i)
        {
            if (i == null || i.Category == InterpretationCategory.Unavailable) return;
            sp.Children.Add(Ui.Callout(i.Headline, i.Detail, i.Level));
            if (!string.IsNullOrEmpty(i.UncertaintyLine))
                sp.Children.Add(Ui.Text(i.UncertaintyLine, 11.5, true, Ui.Warn, new Thickness(0, 4, 0, 0)));
            sp.Children.Add(Ui.Text("Lesion detectors (separate models; confidence is per box, not a percentage of the photograph)", 11, true, Ui.Muted, new Thickness(0, 6, 0, 0)));
            foreach (string line in InterpretationText.DetectorLines(i))
                sp.Children.Add(Ui.Text("•  " + line, 11, false, Ui.Muted, new Thickness(8, 0, 0, 0)));
            sp.Children.Add(Ui.Text(i.Caveat + " " + i.ProvisionalNote, 10.5, false, Ui.Muted, new Thickness(0, 4, 0, 0)));
        }
    }
}
