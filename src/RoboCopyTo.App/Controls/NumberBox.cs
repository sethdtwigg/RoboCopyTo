using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace RoboCopyTo.App.Controls;

/// <summary>An integer spinner: text box plus up/down repeat buttons, clamped to Minimum..Maximum.</summary>
public sealed class NumberBox : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(NumberBox),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((NumberBox)d).SyncText(), Coerce));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(int), typeof(NumberBox), new PropertyMetadata(0));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(int), typeof(NumberBox), new PropertyMetadata(int.MaxValue));

    private readonly TextBox _text;

    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int Minimum { get => (int)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public int Maximum { get => (int)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    public NumberBox()
    {
        Focusable = true;
        IsTabStop = false;
        _text =new TextBox { MinWidth = 64, VerticalContentAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        _text.LostFocus += (_, _) => Commit();
        _text.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
            else if (e.Key == Key.Up) { Step(1); e.Handled = true; }
            else if (e.Key == Key.Down) { Step(-1); e.Handled = true; }
        };
        _text.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsAsciiDigit);

        var up = MakeButton("", 1);
        var down = MakeButton("", -1);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 0) };
        buttons.Children.Add(up);
        buttons.Children.Add(down);

        var panel = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        panel.Children.Add(buttons);
        panel.Children.Add(_text);
        Content = panel;
        SyncText();

        // Screen readers announce the focused TextBox, so give it this control's name and tooltip.
        _text.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty,
            new System.Windows.Data.Binding { Source = this, Path = new PropertyPath(System.Windows.Automation.AutomationProperties.NameProperty) });
        _text.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(nameof(ToolTip)) { Source = this });
        ToolTipService.SetShowOnDisabled(this, true);
    }

    /// <summary>Lets a Label's access key (Label.Target) move focus into the text box.</summary>
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (ReferenceEquals(e.NewFocus, this))
        {
            _text.Focus();
            _text.SelectAll();
        }
    }

    private RepeatButton MakeButton(string glyph, int delta)
    {
        var b = new RepeatButton
        {
            Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10 },
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(2, 0, 0, 0),
            Focusable = false,
            Delay = 400,
            Interval = 60,
        };
        b.Click += (_, _) => Step(delta);
        return b;
    }

    private void Step(int delta)
    {
        Commit();
        var next = (long)Value + delta;
        Value = (int)Math.Clamp(next, Minimum, Maximum);
    }

    private void Commit()
    {
        if (int.TryParse(_text.Text, NumberStyles.None, CultureInfo.CurrentCulture, out var v))
            Value = Math.Clamp(v, Minimum, Maximum);
        SyncText();
    }

    private void SyncText() => _text.Text = Value.ToString(CultureInfo.CurrentCulture);

    private static object Coerce(DependencyObject d, object value)
    {
        var box = (NumberBox)d;
        return Math.Clamp((int)value, box.Minimum, box.Maximum);
    }
}


