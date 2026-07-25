namespace TestSuite.Common;

public static class FileAppender
{
    public static async Task<bool> AppendAsync(string file, byte[] byteData)
    {
        await using var stream = new FileStream(file, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
        stream.Seek(0, SeekOrigin.End);
        await stream.WriteAsync(byteData);
        await stream.FlushAsync();
        return false;
    }
    
    public static async Task<bool> AppendAsync(string file, string hexData)
    {
        var byteData = Convert.FromHexString(hexData);
        return await AppendAsync(file, byteData);
    }
}