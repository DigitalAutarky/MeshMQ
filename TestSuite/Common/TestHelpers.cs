namespace TestSuite.Common;

public static class TestHelpers
{
    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string failureMessage)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition() && !cts.IsCancellationRequested)
            await Task.Delay(5, CancellationToken.None);

        Assert.That(condition(), Is.True, failureMessage);
    }
}