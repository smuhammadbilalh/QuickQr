using System.Collections.Concurrent;
using Android.App;
using Android.Content;

namespace QuickQr;

/// <summary>Awaitable <c>StartActivityForResult</c>: <see cref="MainActivity"/> forwards every result here.</summary>
public static class ActivityResultBridge
{
    private static readonly ConcurrentDictionary<int, TaskCompletionSource<Result>> Pending = new();
    private static int _nextRequestCode = 4200;

    public static Task<Result> StartAsync(Activity activity, Intent intent)
    {
        var requestCode = Interlocked.Increment(ref _nextRequestCode) & 0xFFFF;
        var completion = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        Pending[requestCode] = completion;
        try
        {
            activity.StartActivityForResult(intent, requestCode);
        }
        catch (Exception ex)
        {
            Pending.TryRemove(requestCode, out _);
            completion.TrySetException(ex);
        }

        return completion.Task;
    }

    internal static void OnActivityResult(int requestCode, Result resultCode)
    {
        if (Pending.TryRemove(requestCode, out var completion))
            completion.TrySetResult(resultCode);
    }
}
