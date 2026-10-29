using SteelSeriesAssist.Sonar;

internal static class EventClientTests
{
    public static async Task RestartAndDispose()
    {
        var client = new SonarEventClient();
        await Task.WhenAll(Enumerable.Range(0, 300).Select(_ => client.StartAsync(new Uri("http://127.0.0.1:1"))));
        await Task.WhenAll(client.DisposeAsync().AsTask(), client.DisposeAsync().AsTask());
        if (client.IsConnected) throw new Exception("Disposed client remained connected.");
        try { await client.StartAsync(new Uri("http://127.0.0.1:1")); }
        catch (ObjectDisposedException) { return; }
        throw new Exception("A disposed client restarted.");
    }

}
