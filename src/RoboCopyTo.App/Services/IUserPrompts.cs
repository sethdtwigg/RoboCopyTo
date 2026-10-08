namespace RoboCopyTo.App.Services;

/// <summary>Dialogs the view model needs, implemented by the window.</summary>
public interface IUserPrompts
{
    void ShowError(string title, string message);
    void ShowInfo(string title, string message);
    bool Confirm(string title, string message, bool warning = false);
    string? AskText(string title, string prompt, string initial);
    string? BrowseFolder(string? initial);
    void CloseWindow();
}
