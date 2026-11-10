// ============================================================================
// ClinicianVoice.cs  -  namespace SkinDevApp.Views
//
// The dermatologist is the person using LUMYVUE, so the screens speak to them ("Needs your review"). The stored AI results and the
// patient's receipt keep the neutral wording ("Needs dermatologist review"); this only changes how the text is shown on screen.
// ============================================================================

using System.Globalization;
using System.Windows.Data;

namespace SkinDevApp.Views
{
    public static class ClinicianVoice
    {
        public static string Of(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return text
                .Replace("Needs dermatologist review", "Needs your review")
                .Replace("needs a dermatologist's review", "needs your review")
                .Replace("Dermatologist review is recommended for this image.", "Your review is recommended for this image.")
                .Replace("A dermatologist must confirm this.", "It needs your review.")
                .Replace("A dermatologist must confirm it.", "It needs your review.")
                .Replace("pending dermatologist review", "waiting for your review");
        }
    }

    /// <summary>For grid columns bound to a stored AI title.</summary>
    public sealed class ClinicianVoiceConverter : IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter, CultureInfo culture) => ClinicianVoice.Of(value as string);
        public object ConvertBack(object value, System.Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
