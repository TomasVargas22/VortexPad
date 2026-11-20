namespace VortexPad.App.Views;

/// <summary>"Sticks y gatillos" section of the editor (all logic in ProfileEditorViewModel).</summary>
public partial class EditorSticksView : System.Windows.Controls.UserControl
{
    public EditorSticksView()
    {
        InitializeComponent();
    }

    /// <summary>Live point on the editable curve (raw vs. output magnitude).</summary>
    public void UpdateLiveDot(double rawMagnitude, double outputMagnitude) => Curve.UpdateLiveDot(rawMagnitude, outputMagnitude);
}
