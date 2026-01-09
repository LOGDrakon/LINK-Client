using Link.Client.Crypto;
using Link.Client.Models;

namespace Link.Client.Helpers;

public sealed class CryptoNegotiationHelper
{
    private readonly LinkClient _client;

    public CryptoNegotiationHelper(LinkClient client)
    {
        _client = client;
    }

    public async Task<ILinkCryptoProvider> NegotiateAsync(
        string appId,
        LinkDeviceInfo deviceInfo,
        Func<string, ILinkCryptoProvider?> providerFactory)
    {
        // Pas de chiffrement supporté
        if (deviceInfo.EncryptionMode == "NONE")
            return new NullCryptoProvider();

        // Device verrouillé → AUTH requis avant
        if (deviceInfo.IsLocked)
            throw new InvalidOperationException("Device is locked");

        var provider = providerFactory(deviceInfo.EncryptionMode);
        if (provider == null)
            throw new NotSupportedException(
                $"Encryption mode not supported: {deviceInfo.EncryptionMode}");

        return provider;
    }
}
