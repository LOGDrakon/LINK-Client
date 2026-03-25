using System.Security.Cryptography;
using Link.Client.Hashing;
using Link.Client.Models;
using Link.Core.Commands;

namespace Link.Client.Helpers;

public sealed class AuthHelper
{
    private readonly LinkClient _client;
    private const int NonceByteSize = 32;

    public AuthHelper(LinkClient client)
    {
        _client = client;
    }

    public async Task<LinkAuthResult> ExecuteAsync(
        string appId,
        string password,
        LinkDeviceInfo deviceInfo,
        LinkAuthNonces? existingNonces = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new ArgumentException(nameof(appId));

        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException(nameof(password));

        if (deviceInfo.HashMethod == "NONE")
            throw new InvalidOperationException("Device does not support password hashing");

        var hashProvider = LinkHashProviderFactory.Create(deviceInfo.HashMethod)
            ?? throw new NotSupportedException(
                $"Hash method not supported: {deviceInfo.HashMethod}");

        var nonces = existingNonces ?? await NegotiateNoncesAsync(appId, ct).ConfigureAwait(false);

        var hashInput = nonces.ClientNonce + nonces.DeviceNonce + password;
        var hashedPassword = hashProvider.ComputeHash(hashInput);

        var frame = await _client.SendCommandAsync(appId, LinkCommand.Auth, ct, hashedPassword)
            .ConfigureAwait(false);

        if (!frame.IsReturn || frame.ReturnedCommand != LinkCommand.Auth)
            throw new InvalidOperationException("Invalid AUTH response");

        var result = frame.ReturnArguments.FirstOrDefault();

        var state = result switch
        {
            "OK" => LinkSecurityState.Unlocked(),
            "ERR" => LinkSecurityState.Locked(),
            _ => throw new InvalidOperationException("Unknown AUTH result")
        };

        return new LinkAuthResult { State = state, Nonces = nonces };
    }

    private async Task<LinkAuthNonces> NegotiateNoncesAsync(string appId, CancellationToken ct)
    {
        var clientNonce = GenerateNonce();

        var frame = await _client.SendCommandAsync(appId, LinkCommand.AuthInit, ct, clientNonce)
            .ConfigureAwait(false);

        if (!frame.IsReturn || frame.ReturnedCommand != LinkCommand.AuthInit)
            throw new InvalidOperationException("Invalid AUTH_INIT response");

        var deviceNonce = frame.ReturnArguments.FirstOrDefault()
            ?? throw new InvalidOperationException("Device nonce missing from AUTH_INIT response");

        return new LinkAuthNonces
        {
            ClientNonce = clientNonce,
            DeviceNonce = deviceNonce
        };
    }

    private static string GenerateNonce()
    {
        var bytes = RandomNumberGenerator.GetBytes(NonceByteSize);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
