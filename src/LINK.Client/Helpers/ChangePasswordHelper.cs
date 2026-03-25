using System.IO.Hashing;
using System.Text;
using Link.Client.Hashing;
using Link.Client.Models;
using Link.Core.Commands;

namespace Link.Client.Helpers;

public sealed class ChangePasswordHelper
{
    private readonly LinkClient _client;

    public ChangePasswordHelper(LinkClient client)
    {
        _client = client;
    }

    public async Task<LinkChangePasswordResult> ExecuteAsync(
        string appId,
        string oldPassword,
        string newPassword,
        LinkDeviceInfo deviceInfo,
        LinkAuthNonces nonces,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new ArgumentException(nameof(appId));

        if (string.IsNullOrWhiteSpace(oldPassword))
            throw new ArgumentException(nameof(oldPassword));

        if (string.IsNullOrWhiteSpace(newPassword))
            throw new ArgumentException(nameof(newPassword));

        if (deviceInfo.HashMethod == "NONE")
            throw new InvalidOperationException("Device does not support password hashing");

        var hashProvider = LinkHashProviderFactory.Create(deviceInfo.HashMethod)
            ?? throw new NotSupportedException(
                $"Hash method not supported: {deviceInfo.HashMethod}");

        var oldHash = hashProvider.ComputeHash(nonces.ClientNonce + nonces.DeviceNonce + oldPassword);
        var newHash = hashProvider.ComputeHash(nonces.ClientNonce + nonces.DeviceNonce + newPassword);
        var crc = ComputeCrc32(oldHash + newHash);

        var frame = await _client.SendCommandAsync(appId, LinkCommand.ChangePassword, ct, oldHash, newHash, crc)
            .ConfigureAwait(false);

        if (!frame.IsReturn || frame.ReturnedCommand != LinkCommand.ChangePassword)
            throw new InvalidOperationException("Invalid CHPWD response");

        var result = frame.ReturnArguments.FirstOrDefault();
        var errorDetail = frame.ReturnArguments.Count > 1 ? frame.ReturnArguments[1] : null;

        return result switch
        {
            "OK" => new LinkChangePasswordResult { Success = true },
            "ERR" => new LinkChangePasswordResult { Success = false, Error = errorDetail ?? "ERR" },
            _ => new LinkChangePasswordResult { Success = false, Error = result ?? "no response" }
        };
    }

    public static string ComputeCrc32(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var crc = Crc32.HashToUInt32(bytes);
        return crc.ToString("x8");
    }
}
