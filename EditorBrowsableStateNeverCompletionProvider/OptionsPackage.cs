using System;
using System.Runtime.InteropServices;
using System.Threading;

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;

using Task = System.Threading.Tasks.Task;

namespace EditorBrowsableStateNeverCompletionProvider;

/// <summary>
/// Minimal VSPackage whose only job is to host the Tools &gt; Options page (<see cref="GeneralOptionsPage"/>)
/// and ensure its persisted values are loaded into <see cref="CompletionSettings"/> early. The extension's
/// actual work is done by the MEF <see cref="EditorBrowsableNeverProvider"/>, which reads those settings.
///
/// The package autoloads (in the background) once the shell is initialized so the user's options are applied
/// before any completion is requested. Until then, <see cref="CompletionSettings"/>' defaults apply, which
/// reproduce the extension's original behavior.
/// </summary>
[Guid(PackageGuidString)]
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideOptionPage(typeof(GeneralOptionsPage), "EditorBrowsableStateNeverCompletionProvider", "General", 0, 0, supportsAutomation: true)]
[ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
public sealed class OptionsPackage : AsyncPackage
{
    public const string PackageGuidString = "2f36f74c-7324-42fc-ac68-76b5ce613c70";

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress).ConfigureAwait(false);

        // Touching the options page forces DialogPage to load the persisted values from storage,
        // whose setters mirror them into CompletionSettings. GetDialogPage requires the UI thread.
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        _ = (GeneralOptionsPage)GetDialogPage(typeof(GeneralOptionsPage));
    }
}
