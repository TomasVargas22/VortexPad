namespace VortexPad.Core.Devices;

/// <summary>
/// Decides which controller is selected after refreshing the list (startup or hot-plug):
/// 1. the one already selected, if it is still connected;
/// 2. if it disappeared, it stays in the list marked as disconnected (and selected), so it is
///    picked up automatically when it comes back instead of jumping to another controller;
/// 3. if nothing was selected, the last one saved in the settings;
/// 4. otherwise, the first one in the list.
/// </summary>
public static class DeviceSelectionPolicy
{
    public sealed record Result<T>(IReadOnlyList<T> Items, T? Selected, bool SelectedIsMissing) where T : class;

    public static Result<T> Merge<T>(
        IReadOnlyList<T> fresh,
        T? previous,
        string? savedId,
        Func<T, string> idOf,
        Func<T, T> asMissing) where T : class
    {
        ArgumentNullException.ThrowIfNull(fresh);
        ArgumentNullException.ThrowIfNull(idOf);
        ArgumentNullException.ThrowIfNull(asMissing);

        string? wanted = previous != null ? idOf(previous) : savedId;

        if (!string.IsNullOrEmpty(wanted))
        {
            var match = fresh.FirstOrDefault(d => string.Equals(idOf(d), wanted, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return new Result<T>(fresh, match, SelectedIsMissing: false);
            }
        }

        if (previous != null)
        {
            var placeholder = asMissing(previous);
            var items = fresh.Append(placeholder).ToList();
            return new Result<T>(items, placeholder, SelectedIsMissing: true);
        }

        return new Result<T>(fresh, fresh.FirstOrDefault(), SelectedIsMissing: false);
    }
}
