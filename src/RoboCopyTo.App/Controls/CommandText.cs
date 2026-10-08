using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using RoboCopyTo.Core.Options;
using RoboCopyTo.Core.Planning;

namespace RoboCopyTo.App.Controls;

/// <summary>Fills a TextBlock with a command's segments, dimming the switches the app requires.</summary>
public static class CommandText
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.RegisterAttached(
        "Segments", typeof(IReadOnlyList<ArgumentSegment>), typeof(CommandText), new PropertyMetadata(null, OnChanged));

    public static IReadOnlyList<ArgumentSegment>? GetSegments(DependencyObject d) => (IReadOnlyList<ArgumentSegment>?)d.GetValue(SegmentsProperty);
    public static void SetSegments(DependencyObject d, IReadOnlyList<ArgumentSegment>? value) => d.SetValue(SegmentsProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb)
            return;
        tb.Inlines.Clear();
        if (e.NewValue is not IReadOnlyList<ArgumentSegment> segments)
            return;
        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            var run = new Run(s.Text);
            if (s.IsAppRequired)
            {
                run.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorTertiaryBrush");
                run.ToolTip = OptionHelp.AppRequired;
            }
            tb.Inlines.Add(run);
            if (i < segments.Count - 1)
                tb.Inlines.Add(new Run(" "));
        }
    }
}
