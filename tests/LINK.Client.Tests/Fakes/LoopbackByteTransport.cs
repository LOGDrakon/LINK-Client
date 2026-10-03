using Link.Core.Transport;

namespace Link.Client.Tests.Fakes;

/// <summary>Transport octets relié en mémoire à un faux device.</summary>
internal sealed class LoopbackByteTransport : LinkByteTransportBase
{
    private bool _open;
    public Action<byte[]>? DeviceInput { get; set; }

    public override bool IsOpen => _open;

    public void Inject(byte[] data) => OnBytesReceived(data);

    protected override Task OpenCoreAsync(CancellationToken cancellationToken)
    {
        _open = true;
        return Task.CompletedTask;
    }

    protected override Task CloseCoreAsync(CancellationToken cancellationToken)
    {
        _open = false;
        return Task.CompletedTask;
    }

    protected override ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        DeviceInput?.Invoke(data.ToArray());
        return ValueTask.CompletedTask;
    }
}
