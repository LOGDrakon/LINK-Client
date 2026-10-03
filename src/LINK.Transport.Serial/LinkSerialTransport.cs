using System.IO.Ports;
using Link.Core.Transport;

namespace Link.Transport.Serial;

/// <summary>Transport série / USB CDC (port COM, /dev/ttyACM*, /dev/ttyUSB*).</summary>
public sealed class LinkSerialTransport : LinkByteTransportBase
{
    private readonly SerialPort _port;

    public override bool IsOpen => _port.IsOpen;

    public LinkSerialTransport(LinkSerialOptions options)
        : base(options.MaxPacketSize, options.MaxFrameSize)
    {
        if (string.IsNullOrWhiteSpace(options.PortName))
            throw new ArgumentException(nameof(options.PortName));

        _port = new SerialPort(
            options.PortName,
            options.BaudRate,
            options.Parity,
            options.DataBits,
            options.StopBits
        )
        {
            // Les ports CDC de nombreux MCU n'émettent qu'une fois DTR levé.
            DtrEnable = options.DtrEnable,
            RtsEnable = options.RtsEnable,
        };

        WireFormat = options.WireFormat;
        _port.DataReceived += OnDataReceived;
    }

    protected override Task OpenCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _port.Open();
        return Task.CompletedTask;
    }

    protected override Task CloseCoreAsync(CancellationToken cancellationToken)
    {
        if (_port.IsOpen)
            _port.Close();
        return Task.CompletedTask;
    }

    protected override ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_port.IsOpen)
            throw new InvalidOperationException("Serial port not open");

        _port.BaseStream.Write(data.Span);
        return ValueTask.CompletedTask;
    }

    private void OnDataReceived(object? sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            int available = _port.BytesToRead;
            if (available <= 0)
                return;
            var buffer = new byte[available];
            int read = _port.Read(buffer, 0, available);
            OnBytesReceived(buffer.AsSpan(0, read));
        }
        catch (Exception ex)
        {
            OnTransportError(ex);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        _port.Dispose();
    }
}
