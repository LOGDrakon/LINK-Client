using Link.Client.Crypto;
using Link.Client.Helpers;
using Link.Client.Models;

namespace Link.Client.Extensions;

public static class CryptoExtensions
{
    public static Task<ILinkCryptoProvider> NegotiateEncryptionAsync(
        this LinkClient client,
        string appId,
        LinkDeviceInfo deviceInfo,
        Func<string, ILinkCryptoProvider?> providerFactory)
    {
        var helper = new CryptoNegotiationHelper(client);
        return helper.NegotiateAsync(appId, deviceInfo, providerFactory);
    }
}
