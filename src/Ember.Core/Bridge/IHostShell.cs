namespace Ember.Core;

/// <summary>What only the desktop host can do (dialogs, reloading the UI, restarting).</summary>
public interface IHostShell
{
    Task<string?> PickFolderAsync(string title, string? initial);

    Task<string?> PickFileAsync(string title, string filter);

    /// <summary>Switches to the newest downloaded UI package and reloads the page.</summary>
    void ApplyUiUpdate();

    /// <summary>Replaces the exe with the downloaded one and restarts.</summary>
    void ApplyExeUpdate();

    void SetDevTools(bool enabled);

    /// <summary>Dark or light window frame (title bar, background behind the page).</summary>
    void SetDarkFrame(bool dark);

    void OpenUrl(string url);
}
