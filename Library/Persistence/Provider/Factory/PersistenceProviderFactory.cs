using HackyMessage.Common;
using HackyMessage.Extension;
using HackyMessage.Persistence.Provider.Disk;
using HackyMessage.Persistence.Provider.Disk.Index;
using Serilog;
using Index = HackyMessage.Persistence.Provider.Disk.Index.Index;

namespace HackyMessage.Persistence.Provider.Factory;

public sealed class PersistenceProviderFactory
{
    private static readonly ILogger Logger = Log.Logger.ForFriendlyContext<PersistenceProviderFactory>();

    public static async Task<IPersistenceProvider<T>>
    CreateFileBasedPersistenceAsync<T>(string topic, string queue, int partition, long highWatermark, long lowWatermark, long maxSize, string? fileName = null, string? indexName = null)
    {
        Logger.Debug("Initializing file based persistence for topic {topic}, queue {queue}, partition {partition}",
            topic, queue, partition);
        
        var directory = $"./data/{topic}/{queue}";
        Directory.CreateDirectory(directory);
        
        var partitionFileName = fileName ?? $"{directory}/part{partition}.store";
        Logger.Debug("Partition file name is {PartitionName}", partitionFileName);
        
        var indexFileName = indexName ?? $"{directory}/part{partition}.index";
        Logger.Debug("Partition index name is {IndexName}", indexFileName);

        FileStream? partitionReader = null;
        FileStream? partitionWriter = null;
        Index? index = null;
        try
        {
            // Setup and initialize the index
            index = new Index(indexFileName);
            await index.ReplayAsync(CancellationToken.None);

            // Setup FileStreams
            partitionWriter = CreateWriteStream(partitionFileName, maxSize);
            partitionReader = CreateReadStream(partitionFileName);
            
            // Initialize FileStream positions from index
            var readPosition = await index.GetOrDefaultAsync(IndexKey.ReadPosition, 0, CancellationToken.None);
            partitionReader.Position = readPosition;
            Logger.Debug("Updated reader position from index value to {Processed}", partitionReader.Position);
            
            var writePosition = await index.GetOrDefaultAsync(IndexKey.WritePosition, readPosition, CancellationToken.None);
            partitionWriter.Position = writePosition;
            Logger.Debug("Updated writer position from index value to {Position}", partitionWriter.Position);

            // Done
            var ioContext = new IoContext
                (partitionWriter, partitionReader, new MyAsyncLock(), new MyAsyncLock(), index, highWatermark, lowWatermark, maxSize);
            
            return new WriteAheadLog<T>(ioContext, OnCompletionDisposal);
        }
        catch
        {
            index?.Dispose();
            partitionWriter?.Dispose();
            partitionReader?.Dispose();
            throw;
        }

        //cleanup delegate
        void OnCompletionDisposal()
        {
            if (File.Exists(indexFileName)) File.Delete(indexFileName);
            if (File.Exists(partitionFileName)) File.Delete(partitionFileName);
        }
    }

    private static FileStream CreateWriteStream(string filename, long maxSize)
    {
        var fileInfo = new FileInfo(filename);
        var isNewFile = !fileInfo.Exists || fileInfo.Length == 0;
        if (isNewFile)
        {
            return new FileStream(filename, new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.Read,
                BufferSize = 0,
                Options = FileOptions.WriteThrough | FileOptions.Asynchronous,
                PreallocationSize = maxSize
            });
        }
        
        var stream = new FileStream(filename, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Write,
            Share = FileShare.Read,
            BufferSize = 0,
            Options = FileOptions.WriteThrough | FileOptions.Asynchronous
        });

        if (stream.Length != maxSize)
            stream.SetLength(maxSize);

        return stream;
    }
    
    private static FileStream CreateReadStream(string filename)
    {
        return new FileStream(filename, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite,
            BufferSize = 4096,
            Options = FileOptions.Asynchronous
        });
    }
}