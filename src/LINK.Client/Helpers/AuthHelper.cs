using Link.Client.Models;
using Link.Core.Commands;

namespace Link.Client.Helpers;

public sealed class AuthHelper
{
    private readonly LinkClient _client;

    public AuthHelper(LinkClient client)
    {
        _client = client;
    }

    public async Task<LinkSecurityState> ExecuteAsync(string appId, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new ArgumentException(nameof(appId));

        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException(nameof(password));

        var frame = await _client.SendCommandAsync(appId, LinkCommand.Auth, ct, password)
            .ConfigureAwait(false);

        if (!frame.IsReturn || frame.ReturnedCommand != LinkCommand.Auth)
            throw new InvalidOperationException("Invalid AUTH response");

        var result = frame.ReturnArguments.FirstOrDefault();

        return result switch
        {
            "OK" => LinkSecurityState.Unlocked(),
            "ERR" => LinkSecurityState.Locked(),
            _ => throw new InvalidOperationException("Unknown AUTH result")
        };
    }
}
