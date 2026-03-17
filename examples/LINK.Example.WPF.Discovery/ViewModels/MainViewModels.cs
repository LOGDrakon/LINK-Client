using Link.Client.Discovery;
using Link.Transport.Serial;

namespace LINK.Example.WPF.Discovery.ViewModels;

public sealed class MainViewModel
{
    public LinkDeviceWatcher Watcher { get; }

    public MainViewModel()
    {
        Watcher = new LinkDeviceWatcher(
            port => new LinkSerialTransport(new LinkSerialOptions
            {
                PortName = port,
                BaudRate = 115200
            }),
            timeout: TimeSpan.FromMilliseconds(800),
            appIdFilter: "DRAGON"
        );

        Watcher.Start();
    }
}
