namespace EditorBrowsableStateNeverCompletionProvider;

/// <summary>
/// Process-wide cache of the user-configurable options exposed by <see cref="GeneralOptionsPage"/>.
/// The options page (UI thread) writes these; the MEF completion provider (background thread)
/// reads them. This static holder is the bridge between the two: they live in the same assembly
/// but in different composition worlds (VSPackage vs. Roslyn-instantiated MEF export), so a shared
/// static avoids any cross-reference between them.
///
/// Defaults match the extension's original hardcoded behavior, so completion behaves identically
/// to before until the options package has loaded and applied the user's persisted values.
/// </summary>
internal static class CompletionSettings
{
    private static volatile bool sortHiddenToBottom = true;
    private static volatile bool showHiddenTag = true;

    /// <summary>Whether "(hidden)" members are sorted to the bottom of the completion list.</summary>
    public static bool SortHiddenToBottom
    {
        get => sortHiddenToBottom;
        set => sortHiddenToBottom = value;
    }

    /// <summary>Whether the "(hidden)" inline tag is shown next to those members.</summary>
    public static bool ShowHiddenTag
    {
        get => showHiddenTag;
        set => showHiddenTag = value;
    }
}
