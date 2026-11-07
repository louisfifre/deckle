namespace Deckle.Audio.Internal;

// Puts the calling thread in the multithreaded apartment for the length of a
// scope, as WASAPI wants of the thread that opens and reads a stream. A thread
// that is already in an apartment keeps it (RPC_E_CHANGED_MODE): the COM
// objects work from either, so this only declines to initialise a second time.
//
// Every successful CoInitializeEx is balanced by one CoUninitialize on the same
// thread, so the scope must be disposed on the thread that created it, after
// the COM objects it covers are released.
internal readonly struct ComApartment : IDisposable
{
    private readonly bool _initialized;

    private ComApartment(bool initialized) => _initialized = initialized;

    public static ComApartment Enter()
    {
        // S_OK and S_FALSE (already initialised on this thread, same mode) both
        // count a reference to balance; a failure counts none.
        int hr = MediaFoundationInterop.CoInitializeEx(0, MediaFoundationInterop.COINIT_MULTITHREADED);
        return new ComApartment(hr >= 0);
    }

    public void Dispose()
    {
        if (_initialized) MediaFoundationInterop.CoUninitialize();
    }
}
