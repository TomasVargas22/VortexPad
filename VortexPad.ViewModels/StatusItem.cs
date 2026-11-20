using CommunityToolkit.Mvvm.ComponentModel;

namespace VortexPad.ViewModels;

/// <summary>Severity of a status chip. Order matters: higher is worse (used for the summary chip).</summary>
public enum StatusSeverity
{
    Neutral,
    Live,
    Ok,
    Info,
    Warning,
    Error
}

/// <summary>
/// One status-bar chip (emulation, ViGEmBus, HidHide, saved). The color always comes with an icon and a word,
/// because <c>ok</c> and <c>danger</c> differ little in lightness.
/// </summary>
public sealed class StatusItem : ObservableObject
{
    private string _text;
    private StatusSeverity _severity;
    private string? _toolTip;

    public StatusItem(string label, string text = "", StatusSeverity severity = StatusSeverity.Neutral)
    {
        Label = label;
        _text = text;
        _severity = severity;
    }

    /// <summary>Fixed key shown before the value ("ViGEmBus"); empty for chips that are only a value.</summary>
    public string Label { get; }

    public string Text
    {
        get => _text;
        private set => SetProperty(ref _text, value);
    }

    public StatusSeverity Severity
    {
        get => _severity;
        private set => SetProperty(ref _severity, value);
    }

    /// <summary>Optional longer explanation (tooltip).</summary>
    public string? ToolTip
    {
        get => _toolTip;
        private set => SetProperty(ref _toolTip, value);
    }

    /// <summary>Segoe Fluent glyph for the severity.</summary>
    public string Glyph => GlyphFor(_severity);

    /// <summary>Name read by screen readers: label + value, without the icon.</summary>
    public string AccessibleText => string.IsNullOrEmpty(Label) ? Text : $"{Label}: {Text}";

    public void Set(string text, StatusSeverity severity, string? toolTip = null)
    {
        bool changed = Text != text || Severity != severity;
        Text = text;
        Severity = severity;
        ToolTip = toolTip;
        if (changed)
        {
            OnPropertyChanged(nameof(Glyph));
            OnPropertyChanged(nameof(AccessibleText));
        }
    }

    public static string GlyphFor(StatusSeverity severity) => severity switch
    {
        StatusSeverity.Ok => "",       // correcto
        StatusSeverity.Live => "",     // reproduciendo
        StatusSeverity.Info => "",     // info
        StatusSeverity.Warning => "",  // aviso
        StatusSeverity.Error => "",    // error
        _ => ""                        // detenido
    };

    /// <summary>The worst item (for the grouped chip); null if the list is empty.</summary>
    public static StatusItem? Worst(IEnumerable<StatusItem> items) =>
        items.OrderByDescending(i => i.Severity).FirstOrDefault();
}
