namespace Link.Client.Extensions;

public static class LinkClientDeviceExtensions
{
    public static LinkDeviceClient WithAppId(this LinkClient client, string appId)
        => new(client, appId);
}