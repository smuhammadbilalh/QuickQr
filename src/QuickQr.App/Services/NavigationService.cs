namespace QuickQr.Services;

/// <summary>Shell navigation. Tabs are absolute routes (<c>//scan</c>); detail pages are pushed relative routes.</summary>
public sealed class NavigationService : INavigationService
{
    public Task GoToAsync(string route, IDictionary<string, object>? parameters = null) =>
        MainThread.InvokeOnMainThreadAsync(() =>
        {
            var shell = RequireShell();
            return parameters is null
                ? shell.GoToAsync(route)
                : shell.GoToAsync(route, true, new ShellNavigationQueryParameters(parameters));
        });

    public Task GoBackAsync() =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var shell = Shell.Current;
            if (shell is null) return;

            if (shell.Navigation.ModalStack.Count > 0)
                await shell.Navigation.PopModalAsync();
            else if (shell.Navigation.NavigationStack.Count > 1)
                await shell.GoToAsync("..");
        });

    public Task GoToTabAsync(string tabRoute, IDictionary<string, object>? parameters = null)
    {
        var route = tabRoute.StartsWith("//", StringComparison.Ordinal) ? tabRoute : "//" + tabRoute;
        return GoToAsync(route, parameters);
    }

    private static Shell RequireShell() =>
        Shell.Current ?? throw new InvalidOperationException("Navigation is not available before the shell is shown.");
}
