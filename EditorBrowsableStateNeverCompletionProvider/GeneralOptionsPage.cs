using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

using Microsoft.VisualStudio.Shell;

namespace EditorBrowsableStateNeverCompletionProvider;

/// <summary>
/// Tools &gt; Options page for the extension. Derives from <see cref="UIElementDialogPage"/> so it can
/// host a WPF control (<see cref="OptionsControl"/>) with real checkboxes, instead of the default
/// property-grid that renders bool options as a True/False dropdown.
///
/// The base <see cref="DialogPage"/> still persists the public properties to the user's settings
/// storage (loading them by invoking the setters via reflection), and reverts them via
/// LoadSettingsFromStorage when the dialog is cancelled. Each setter mirrors the value into
/// <see cref="CompletionSettings"/> so the completion provider reads the current configuration, and
/// raises <see cref="PropertyChanged"/> so the checkboxes reflect storage-driven changes (e.g. a
/// cancel that reverts a toggle on the reused control instance).
/// </summary>
public sealed class GeneralOptionsPage : UIElementDialogPage, INotifyPropertyChanged
{
    private bool sortHiddenToBottom = true;
    private bool showHiddenTag = true;
    private OptionsControl control;

    public bool SortHiddenToBottom
    {
        get => sortHiddenToBottom;
        set
        {
            if (sortHiddenToBottom == value)
                return;
            sortHiddenToBottom = value;
            CompletionSettings.SortHiddenToBottom = value;
            OnPropertyChanged();
        }
    }

    public bool ShowHiddenTag
    {
        get => showHiddenTag;
        set
        {
            if (showHiddenTag == value)
                return;
            showHiddenTag = value;
            CompletionSettings.ShowHiddenTag = value;
            OnPropertyChanged();
        }
    }

    // Lazily created and reused. The getter runs after the base has loaded persisted values, so the
    // bindings read the correct initial state; later reverts propagate via PropertyChanged.
    protected override UIElement Child => control ??= new OptionsControl { DataContext = this };

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
