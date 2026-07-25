using System.Threading.Channels;

namespace HackyMessage.Common;

public static class ChannelBatcher
{
    public static async Task ProcessInBatchesAsync<T>(
        Func<ReadOnlyMemory<T>, Task> processBatchAsync, ChannelReader<T> reader,
        TimeSpan timeout, int maxBatchSize,
        CancellationToken ct = default)
    {
        var count = 0;
        var batch = new T[maxBatchSize];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // 1. Pull everything currently sitting in the channel's memory buffer
                count = FillBatchFromBuffer(reader, batch, count, maxBatchSize);

                // 2. If that filled our batch, ship it immediately and keep looping
                if (count == maxBatchSize)
                {
                    count = await ShipBatchAsync(batch, count, processBatchAsync);
                    continue; 
                }

                // 3. Buffer is empty. Wait for a new item, a timeout, or cancellation
                var result = await WaitForNextItemAsync(reader, hasItemsInBatch: count > 0, timeout, ct);
                if (result == WaitResult.TimedOut)
                    count = await ShipBatchAsync(batch, count, processBatchAsync);
                else if (result == WaitResult.ChannelClosed)
                    break; 

                // 4. If DataAvailable, the loop repeats and Step 1 will ingest it
            }
        }
        finally
        {
            // 4. Guarantees a full drain and flush on completion, error, or cancellation
            await DrainAndCleanupAsync(reader, batch, count, maxBatchSize, processBatchAsync, ct);
        }
    }

    private static int FillBatchFromBuffer<T>(ChannelReader<T> reader, T[] batch, int count, int maxBatchSize)
    {
        while (count < maxBatchSize && reader.TryRead(out T item))
            batch[count++] = item;
        
        return count;
    }

    private static async Task<WaitResult> WaitForNextItemAsync<T>
        (ChannelReader<T> reader, bool hasItemsInBatch, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            // Only enforce a timeout if we are actively holding onto un-shipped items
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (hasItemsInBatch)
                timeoutCts.CancelAfter(timeout);
            
            var hasData = await reader.WaitToReadAsync(timeoutCts.Token);
            return hasData ? WaitResult.DataAvailable : WaitResult.ChannelClosed;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The timeout token fired, but the main cancellation token did not.
            return WaitResult.TimedOut;
        }
    }

    private static async Task<int> ShipBatchAsync<T>
        (T[] batch, int count, Func<ReadOnlyMemory<T>, Task> processBatchAsync)
    {
        if (count <= 0) return count;
        await processBatchAsync(batch.AsMemory(0, count));
        return 0;

    }

    private static async Task DrainAndCleanupAsync<T>
        (ChannelReader<T> reader, T[] batch, int count, int maxBatchSize, Func<ReadOnlyMemory<T>, Task> processBatchAsync, CancellationToken ct)
    {
        // Vacuum up absolutely everything left in the channel memory
        while (reader.TryRead(out T item))
        {
            batch[count++] = item;
            if (count == maxBatchSize)
                count = await ShipBatchAsync(batch, count, processBatchAsync);
        }

        // Flush out the final partial batch
        await ShipBatchAsync(batch, count, processBatchAsync);
        
        //finally propagate the cancellation
        ct.ThrowIfCancellationRequested();
    }
    
    private enum WaitResult
    {
        DataAvailable,
        ChannelClosed,
        TimedOut
    }
}