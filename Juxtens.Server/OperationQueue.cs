namespace Juxtens.Server;

public sealed class OperationQueue
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private int _queuedCount;

    public int QueuedCount => _queuedCount;

    public async Task<T> EnqueueAsync<T>(Func<Task<T>> operation)
    {
        Interlocked.Increment(ref _queuedCount);
        try
        {
            await _semaphore.WaitAsync();
            try
            {
                return await operation();
            }
            finally
            {
                _semaphore.Release();
            }
        }
        finally
        {
            Interlocked.Decrement(ref _queuedCount);
        }
    }

    public Task EnqueueAsync(Func<Task> operation) =>
        EnqueueAsync(async () => { await operation(); return 0; });
}
