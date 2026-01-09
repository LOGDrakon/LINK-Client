using Link.Client.Models;

namespace Link.Client.Helpers;

public sealed class AuthHelper
{
    private readonly LinkClient _client;

    public AuthHelper(LinkClient client)
    {
        _client = client;
    }

    public async Task<LinkSecurityState> ExecuteAsync(string appId, string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException(nameof(password));

        var frame = await _client.SendCommandAsync(appId, "AUTH", password);

        if (!frame.IsReturn || frame.ReturnedCommand != "AUTH")
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
