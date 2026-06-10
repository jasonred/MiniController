using MiniController.Core.Protocol;

namespace MiniController.Core.Ac;

/// <summary>
/// High-level handle for one AC unit: refresh state and apply changes over the LAN.
/// Serializes access so the underlying <see cref="LanClient"/> is used by one caller at a time.
/// </summary>
public sealed class AcDevice : IDisposable
{
    private readonly LanClient _client;
    private readonly byte[] _token;
    private readonly byte[] _key;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AcStatus? LastStatus { get; private set; }

    public AcDevice(string ip, int port, long deviceId, byte[] token, byte[] key)
    {
        _client = new LanClient(ip, port, deviceId);
        _token = token;
        _key = key;
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        if (!_client.IsAuthenticated)
            await _client.AuthenticateAsync(_token, _key, ct).ConfigureAwait(false);
    }

    public async Task<AcStatus> RefreshAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);
            var response = await _client.SendAsync(AcCommands.GetState(), ct).ConfigureAwait(false);
            if (!AcStateParser.TryParse(response, out var status) || status is null)
                throw new ProtocolException("Device did not return a state response.");
            LastStatus = status;
            return status;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Apply a change. The request should be seeded from current state (see <see cref="Mutate"/>).</summary>
    public async Task<AcStatus> ApplyAsync(SetStateRequest request, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);
            var response = await _client.SendAsync(AcCommands.SetState(request), ct).ConfigureAwait(false);
            if (AcStateParser.TryParse(response, out var status) && status is not null)
                LastStatus = status;
            return LastStatus ?? throw new ProtocolException("No state available after apply.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Refresh, mutate a request seeded from the fresh state, then apply it.</summary>
    public async Task<AcStatus> Mutate(Action<SetStateRequest> change, CancellationToken ct = default)
    {
        var current = await RefreshAsync(ct).ConfigureAwait(false);
        var request = SetStateRequest.FromStatus(current);
        change(request);
        return await ApplyAsync(request, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _client.Dispose();
        _gate.Dispose();
    }
}
