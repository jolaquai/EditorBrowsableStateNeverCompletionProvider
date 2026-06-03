using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace EditorBrowsableStateNeverCompletionProvider;

/// <summary>
/// WPF content for the Tools &gt; Options page. Hosted by <see cref="GeneralOptionsPage"/> through
/// <see cref="Microsoft.VisualStudio.Shell.UIElementDialogPage"/> so the options render as real
/// checkboxes rather than the property-grid's True/False dropdowns. The checkboxes bind two-way to
/// the page (set as DataContext), which is the persisted settings source.
/// </summary>
internal sealed class OptionsControl : UserControl
{
    public OptionsControl()
    {
        var panel = new StackPanel { Margin = new Thickness(5) };

        var groupBoxCompletionEntryAppearance = new GroupBox
        {
            Header = "Completion entry appearance",
            Padding = new Thickness(5),
        };
        {
            var groupBoxCompletionEntryAppearanceStackPanel = new StackPanel();
            groupBoxCompletionEntryAppearance.Content = groupBoxCompletionEntryAppearanceStackPanel;
            groupBoxCompletionEntryAppearanceStackPanel.Children.Add(MakeCheckBox(
                "Sort hidden members to bottom",
                "When enabled, members marked [EditorBrowsable(EditorBrowsableState.Never)] are sorted to the bottom of the completion list.",
                nameof(GeneralOptionsPage.SortHiddenToBottom)));
            groupBoxCompletionEntryAppearanceStackPanel.Children.Add(MakeCheckBox(
                "Show \"(hidden)\" tag",
                "When enabled, hidden members display a \"(hidden)\" tag next to them in the completion list.",
                nameof(GeneralOptionsPage.ShowHiddenTag)));
        }

        panel.Children.Add(groupBoxCompletionEntryAppearance);
        Content = panel;
    }

    private static CheckBox MakeCheckBox(string content, string tooltip, string bindingPath)
    {
        var checkBox = new CheckBox
        {
            Content = content,
            ToolTip = tooltip,
            Margin = new Thickness(0, 4, 0, 4),
        };
        // IsChecked is a nullable bool but a two-state checkbox never goes null, so the two-way
        // binding round-trips cleanly to the non-nullable bool source property.
        checkBox.SetBinding(CheckBox.IsCheckedProperty, new Binding(bindingPath) { Mode = BindingMode.TwoWay });
        return checkBox;
    }
}
