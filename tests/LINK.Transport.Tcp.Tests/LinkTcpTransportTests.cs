using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Link.Core.Frames;
using Link.Transport.Tcp;

/// <summary>
/// Integration-style tests that spin up a real TCP listener on loopback
/// to verify that LinkTcpTransport sends and receives LINK frames correctly.
/// </summary>
public class LinkTcpTransportTests : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly int _port;

    public LinkTcpTransportTests()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    // -----------------------------------------------------------------------
    // Helper: send a raw ASCII frame from the server side and close
    // -----------------------------------------------------------------------
    private async Task<TcpClient> AcceptAndSendAsync(string frame)
    {
        var server = await _listener.AcceptTcpClientAsync();
        var stream = server.GetStream();
        var data = Encoding.ASCII.GetBytes(frame);
        await stream.WriteAsync(data);
        return server;
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task OpenAsync_ConnectsSuccessfully()
    {
        var acceptTask = _listener.AcceptTcpClientAsync();

        var transport = new LinkTcpTransport(new LinkTcpOptions
        {
            Host = "127.0.0.1",
            Port = _port
        });

        await transport.OpenAsync();
        Assert.True(transport.IsOpen);

        var serverClient = await acceptTask;
        serverClient.Dispose();
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task SendAsync_WritesEncodedFrameToStream()
    {
        var acceptTask = _listener.AcceptTcpClientAsync();

        var transport = new LinkTcpTransport(new LinkTcpOptions
        {
            Host = "127.0.0.1",
            Port = _port
        });
        await transport.OpenAsync();

        var serverClient = await acceptTask;
        var serverStream = serverClient.GetStream();

        var frame = new LinkFrame("DRAGON", "GETV");
        await transport.SendAsync(frame);

        var buf = new byte[64];
        serverStream.ReadTimeout = 2000;
        int read = serverStream.Read(buf, 0, buf.Length);
        var received = Encoding.ASCII.GetString(buf, 0, read);

        Assert.Equal("LINK:DRAGON:GETV\0", received);

        serverClient.Dispose();
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task FrameReceived_IsRaisedForIncomingFrame()
    {
        var tcs = new TaskCompletionSource<LinkFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = AcceptAndSendAsync("LINK:DRAGON:RETURN:GETV:LINKv1.1\0");

        var transport = new LinkTcpTransport(new LinkTcpOptions
        {
            Host = "127.0.0.1",
            Port = _port
        });
        transport.FrameReceived += f => tcs.TrySetResult(f);
        await transport.OpenAsync();

        var serverClient = await serverTask;

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("DRAGON", received.AppId);
        Assert.Equal("RETURN", received.Command);
        Assert.Equal("GETV", received.ReturnedCommand);
        Assert.Equal("LINKv1.1", received.ReturnArguments[0]);

        serverClient.Dispose();
        await transport.DisposeAsync();
    }

    [Fact]
    public async Task FrameReceived_HandlesMultipleFramesInOneChunk()
    {
        var frames = new System.Collections.Generic.List<LinkFrame>();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var serverTask = AcceptAndSendAsync(
            "LINK:DRAGON:RETURN:AUTH:OK\0LINK:DRAGON:RETURN:GETTEMP:24.6\0");

        var transport = new LinkTcpTransport(new LinkTcpOptions
        {
            Host = "127.0.0.1",
            Port = _port
        });
        transport.FrameReceived += f =>
        {
            lock (frames) { frames.Add(f); }
            if (frames.Count == 2)
                tcs.TrySetResult();
        };
        await transport.OpenAsync();

        var serverClient = await serverTask;
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, frames.Count);
        Assert.Equal("AUTH", frames[0].ReturnedCommand);
        Assert.Equal("GETTEMP", frames[1].ReturnedCommand);

        serverClient.Dispose();
        await transport.DisposeAsync();
    }

    [Fact]
    public void Constructor_ThrowsForEmptyHost()
    {
        Assert.Throws<ArgumentException>(() =>
            new LinkTcpTransport(new LinkTcpOptions { Host = "" }));
    }

    [Fact]
    public void Constructor_ThrowsForInvalidPort()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LinkTcpTransport(new LinkTcpOptions { Host = "127.0.0.1", Port = 0 }));
    }

    [Fact]
    public async Task CloseAsync_SetsIsOpenFalse()
    {
        var acceptTask = _listener.AcceptTcpClientAsync();

        var transport = new LinkTcpTransport(new LinkTcpOptions
        {
            Host = "127.0.0.1",
            Port = _port
        });
        await transport.OpenAsync();
        Assert.True(transport.IsOpen);

        var serverClient = await acceptTask;
        await transport.CloseAsync();
        Assert.False(transport.IsOpen);

        serverClient.Dispose();
    }

    [Fact]
    public async Task SendAsync_ChunksLargeFrame()
    {
        var acceptTask = _listener.AcceptTcpClientAsync();

        var transport = new LinkTcpTransport(new LinkTcpOptions
        {
            Host = "127.0.0.1",
            Port = _port,
            MaxPacketSize = 16
        });
        await transport.OpenAsync();

        var serverClient = await acceptTask;
        var serverStream = serverClient.GetStream();

        // Build a frame larger than MaxPacketSize (16)
        var frame = new LinkFrame("DRAGON", "AUTH", "abcdefghij1234567890");
        var expected = frame.ToString();
        Assert.True(expected.Length > 16);

        await transport.SendAsync(frame);

        // Read all chunks from the server side
        var buf = new byte[256];
        serverStream.ReadTimeout = 2000;
        int totalRead = 0;
        while (totalRead < expected.Length)
        {
            int read = serverStream.Read(buf, totalRead, buf.Length - totalRead);
            if (read == 0)
                break;
            totalRead += read;
        }

        var received = Encoding.ASCII.GetString(buf, 0, totalRead);
        Assert.Equal(expected, received);

        serverClient.Dispose();
        await transport.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        await Task.CompletedTask;
    }
}
