namespace VortexPad.Core.Diagnostics;

public enum NoticeSeverity
{
    Info,
    Warning,
    Error
}

public enum NoticeAction
{
    None,
    OpenUrl,
    RestartElevated
}

/// <summary>Notice for the top bar (missing driver, missing permissions…).</summary>
public sealed record DependencyNotice(
    string Id,
    NoticeSeverity Severity,
    string Message,
    NoticeAction Action = NoticeAction.None,
    string? ActionLabel = null,
    string? Url = null);

/// <summary>State of the external dependencies when the app starts.</summary>
public readonly record struct DependencyState(
    bool ViGEmBusInstalled,
    bool HidHideInstalled,
    bool IsElevated,
    bool ProfileWantsHidHide,
    bool ProfileNeedsViGEm);

/// <summary>
/// Decides which notices to show: ViGEmBus (required for the virtual Xbox/DS4), HidHide (optional) and
/// elevation (only asked for when HidHide is actually going to be used, never on every launch).
/// </summary>
public static class DependencyNotices
{
    public const string ViGEmBusUrl = "https://github.com/nefarius/ViGEmBus/releases/latest";
    public const string HidHideUrl = "https://github.com/nefarius/HidHide/releases/latest";

    public static IReadOnlyList<DependencyNotice> Evaluate(DependencyState s)
    {
        var notices = new List<DependencyNotice>();

        if (!s.ViGEmBusInstalled && s.ProfileNeedsViGEm)
        {
            notices.Add(new DependencyNotice(
                "vigembus",
                NoticeSeverity.Error,
                "Falta el driver ViGEmBus: sin él no se puede crear el mando virtual Xbox 360 / DualShock 4 " +
                "(el modo teclado y ratón sí funciona).",
                NoticeAction.OpenUrl, "Descargar ViGEmBus", ViGEmBusUrl));
        }

        if (s.ProfileWantsHidHide)
        {
            if (!s.HidHideInstalled)
            {
                notices.Add(new DependencyNotice(
                    "hidhide",
                    NoticeSeverity.Info,
                    "HidHide no está instalado: los juegos podrían ver el mando físico y el virtual a la vez (doble input).",
                    NoticeAction.OpenUrl, "Descargar HidHide", HidHideUrl));
            }
            else if (!s.IsElevated)
            {
                notices.Add(new DependencyNotice(
                    "elevation",
                    NoticeSeverity.Warning,
                    "Ocultar el mando físico con HidHide requiere permisos de administrador.",
                    NoticeAction.RestartElevated, "Reiniciar como administrador"));
            }
        }

        return notices;
    }
}
