using System.Collections.ObjectModel;

namespace Link.Client.Discovery;

public sealed class LinkDeviceCollection : ObservableCollection<LinkDetectedDevice>
{
    public void Update(IEnumerable<LinkDetectedDevice> devices)
    {
        Clear();
        foreach (var device in devices)
            Add(device);
    }
}
