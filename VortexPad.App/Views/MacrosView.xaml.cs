using System.Windows;
using VortexPad.ViewModels;

namespace VortexPad.App.Views;

/// <summary>"Macros y turbo" section. Testing a macro needs the engine, so it is raised to the window.</summary>
public partial class MacrosView : System.Windows.Controls.UserControl
{
    public MacrosView()
    {
        InitializeComponent();
    }

    /// <summary>The user asked to run the selected macro once (the window sends it to the pipeline).</summary>
    public event Action<MacroViewModel>? TestRequested;

    private void BtnTestMacro_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MacroViewModel macro) TestRequested?.Invoke(macro);
    }

    private void BtnDeleteMacro_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProfileEditorViewModel editor || (sender as FrameworkElement)?.DataContext is not MacroViewModel macro) return;

        var owner = Window.GetWindow(this);
        var answer = System.Windows.MessageBox.Show(owner,
            $"¿Borrar la macro «{macro.Name}»? No se puede deshacer.",
            "Borrar macro", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (answer == MessageBoxResult.OK) editor.RemoveMacroCommand.Execute(macro);
    }
}
