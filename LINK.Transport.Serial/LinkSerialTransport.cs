using System.IO.Ports;
using System.Text;
using Link.Core.Frames;
using Link.Core.Parsing;
using Link.Core.Transport;

namespace Link.Transport.Serial;

public sealed class LinkSerialTransport : ILinkTransport
{
    private readonly SerialPort _port;
    private readonly LinkParser _parser = new();
    private readonly Encoding _encoding = Encoding.ASCII;

    public event Action<LinkFrame>? FrameReceived;

    public bool IsOpen => _port.IsOpen;

    public LinkSerialTransport(LinkSerialOptions options)
    {
        _port = new SerialPort(
            options.PortName,
            options.BaudRate,
            options.Parity,
            options.DataBits,
            options.StopBits
        );

        _parser.FrameReceived += f => FrameReceived?.Invoke(f);
        _port.DataReceived += OnDataReceived;
    }

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        _port.Open();
        return Task.CompletedTask;
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_port.IsOpen)
            _port.Close();

        return Task.CompletedTask;
    }

    public Task SendAsync(LinkFrame frame, CancellationToken cancellationToken = default)
    {
        if (!_port.IsOpen)
            throw new InvalidOperationException("Serial port not open");

        var data = _encoding.GetBytes(frame.ToString());
        _port.Write(data, 0, data.Length);
        return Task.CompletedTask;
    }

    private void OnDataReceived(object? sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var text = _port.ReadExisting();
            _parser.Feed(text);
        }
        catch
        {
            // transport ne doit jamais throw
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        _port.Dispose();
    }
}
