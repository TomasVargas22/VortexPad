using System.Windows;
using VortexPad.ViewModels;

namespace VortexPad.App.Views;

/// <summary>"Botones y capas" section of the editor.</summary>
public partial class EditorButtonsView : System.Windows.Controls.UserControl
{
    /// <summary>Same limit as the spec: 8 Shift layers.</summary>
    public const int MaxShiftLayers = 8;

    public EditorButtonsView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ProfileEditorViewModel oldVm) oldVm.ShiftLayers.CollectionChanged -= OnLayersChanged;
            if (e.NewValue is ProfileEditorViewModel newVm) newVm.ShiftLayers.CollectionChanged += OnLayersChanged;
            UpdateAddLayer();
        };
    }

    private void OnLayersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => UpdateAddLayer();

    /// <summary>Disables "Agregar capa" at the limit and explains why.</summary>
    public void UpdateAddLayer()
    {
        int count = (DataContext as ProfileEditorViewModel)?.ShiftLayers.Count ?? 0;
        bool full = count >= MaxShiftLayers;
        BtnAddLayer.IsEnabled = !full;
        BtnAddLayer.ToolTip = full
            ? $"Máximo {MaxShiftLayers} capas"
            : "Una capa Shift cambia los botones mientras mantienes otro botón";
        System.Windows.Controls.ToolTipService.SetShowOnDisabled(BtnAddLayer, true);
    }
}
