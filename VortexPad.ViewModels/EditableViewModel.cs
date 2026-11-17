using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VortexPad.ViewModels;

/// <summary>
/// Base for ViewModels that wrap a profile model object and edit it in place.
/// Every effective change notifies the UI (PropertyChanged) and the editor root (<c>onEdited</c>),
/// which publishes the new snapshot to the 1000 Hz loop.
/// </summary>
public abstract class EditableViewModel : ObservableObject
{
    private readonly Action<string> _onEdited;

    protected EditableViewModel(Action<string>? onEdited)
    {
        _onEdited = onEdited ?? (_ => { });
    }

    /// <summary>Applies <paramref name="value"/> if it differs from <paramref name="current"/>. Returns true if it changed.</summary>
    protected bool Edit<T>(T current, T value, Action<T> apply, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return false;
        apply(value);
        OnPropertyChanged(propertyName);
        NotifyEdited(propertyName ?? string.Empty);
        return true;
    }

    /// <summary>Like <see cref="Edit{T}"/> but clamps to [min, max] first (range validation).</summary>
    protected bool EditClamped(double current, double value, double min, double max, Action<double> apply,
        [CallerMemberName] string? propertyName = null)
    {
        if (double.IsNaN(value)) return false;
        return Edit(current, Math.Clamp(value, min, max), apply, propertyName);
    }

    protected virtual void NotifyEdited(string propertyName) => _onEdited(propertyName);

    /// <summary>Passed to child ViewModels so their changes reach the root.</summary>
    protected Action<string> EditedCallback => _onEdited;
}

/// <summary>Option for a ComboBox: value + Spanish label.</summary>
public sealed record EnumOption<T>(T Value, string Label)
{
    public override string ToString() => Label;
}
