using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Panel = System.Windows.Controls.Panel;

namespace VortexPad.App;

/// <summary>
/// Buttons whose content is "icon + text" (a StackPanel) get no automatic UI Automation name in WPF.
/// This fills <see cref="AutomationProperties.NameProperty"/> with the visible text, skipping the icon glyphs,
/// so screen readers announce "Nuevo perfil" instead of nothing (WCAG 4.1.2).
/// </summary>
internal static class AccessibleNames
{
    public static void Apply(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ButtonBase button && string.IsNullOrEmpty(AutomationProperties.GetName(button)))
            {
                string text = VisibleText(button.Content);
                if (text.Length > 0) AutomationProperties.SetName(button, text);
            }
            Apply(child);
        }
    }

    private static string VisibleText(object? content) => content switch
    {
        string s => s,
        TextBlock tb when !IsIcon(tb) => tb.Text,
        Panel panel => string.Join(" ", panel.Children.OfType<object>().Select(VisibleText).Where(t => t.Length > 0)),
        ContentControl cc => VisibleText(cc.Content),
        _ => string.Empty
    };

    private static bool IsIcon(TextBlock tb) =>
        tb.FontFamily.Source.Contains("Fluent Icons", StringComparison.OrdinalIgnoreCase)
        || tb.FontFamily.Source.Contains("MDL2", StringComparison.OrdinalIgnoreCase);
}
