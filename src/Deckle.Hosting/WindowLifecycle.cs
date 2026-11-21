using Deckle.Diagnostics;
using Microsoft.UI.Xaml;

namespace Deckle.Hosting;

public static class WindowLifecycle
{
    // The constructor is where XAML parsing and page composition fail. The
    // exception is named here, then rethrown so the crash net and the caller
    // keep their current behaviour.
    public static T CreateOrLog<T>(string name, Func<T> create)
        where T : Window
    {
        T window;
        try
        {
            window = create();
        }
        catch (Exception ex)
        {
            DeckleWindowingSource.Log.WindowOpenFailed(name, ex.GetType().Name, ex.Message);
            throw;
        }
        DeckleWindowingSource.Log.WindowCreated(name);
        return window;
    }
}
