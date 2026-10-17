using System.Net.Http.Json;
using System.Text.Json;
using Nexora.Application;
using Nexora.Application.Licensing;
using Nexora.Application.Security;
using Nexora.Infrastructure.Backup;

namespace Nexora.Infrastructure.Licensing;

public sealed record ActivationState(Guid InstallationId, string Username = "", string? Token = null,
    SignedActivation? Lease = null, DateTimeOffset LastSeenUtc = default);

public interface IActivationStateStore
{
    ActivationState? Read();
    void Write(ActivationState state);
}

/// <summary>Kept outside company backups. DPAPI binds the installation to this Windows account.</summary>
public sealed class ProtectedActivationStateStore(string path, ISecretProtector protector) : IActivationStateStore
{
    public ActivationState? Read() => File.Exists(path)
        ? JsonSerializer.Deserialize<ActivationState>(protector.Unprotect(File.ReadAllText(path))) : null;

    public void Write(ActivationState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, protector.Protect(JsonSerializer.Serialize(state)));
        File.Move(temporary, path, overwrite: true);
    }
}

public sealed class ActivationClient(HttpClient http, string publicKey, IActivationStateStore store,
    TimeProvider? timeProvider = null) : IActivationAccess
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private readonly object _gate = new();
    private ActivationState? _state;
    private long _checkedTimestamp;
    private TimeSpan _remaining;
    private bool _loaded;
    private bool _clockRejected;
    private ActivationGrant? _grant;

    public bool IsConfigured => http.BaseAddress is { Scheme: "https" } && !string.IsNullOrWhiteSpace(publicKey);
    public Uri? AdminUri => IsConfigured ? new Uri(http.BaseAddress!, "Login") : null;
    public Guid InstallationId { get { lock (_gate) return _state?.InstallationId ?? Guid.Empty; } }
    public string Username { get { lock (_gate) return _state?.Username ?? ""; } }
    public string Message { get; private set; } = "Register this installation or sign in to your activation account.";
    public DateTimeOffset? ExpiresUtc { get { lock (_gate) return ValidGrant()?.ExpiresUtc; } }
    public bool HasAccount { get { lock (_gate) return _state?.Token is not null; } }
    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                if (_state is not null && _state.LastSeenUtc - _time.GetUtcNow() > TimeSpan.FromMinutes(5)) _clockRejected = true;
                return IsConfigured && !_clockRejected && _grant is not null && _grant.ExpiresUtc > _time.GetUtcNow()
                    && _grant.IssuedUtc <= _time.GetUtcNow()
                    && _time.GetElapsedTime(_checkedTimestamp) < _remaining;
            }
        }
    }

    public void Demand()
    {
        if (!IsActive) throw new AccessDeniedException("Internet is needed to check activation. Contact your admin if the account is inactive.");
    }

    public void Load()
    {
        lock (_gate)
        {
            if (_loaded) return;
            _loaded = true;
            try { _state = store.Read(); }
            catch (Exception ex) when (ex is IOException or WorkspaceValidationException or JsonException or System.Security.Cryptography.CryptographicException)
            { Message = "Saved activation could not be opened. Sign in again; your admin may need to reset this installation."; }
            if (_state is null)
            {
                _state = new ActivationState(Guid.NewGuid());
                store.Write(_state);
            }
            var now = _time.GetUtcNow();
            _clockRejected = _state.LastSeenUtc - now > TimeSpan.FromMinutes(5);
            ResetTimer();
            if (!IsConfigured) Message = "This preview installer has no activation server configured. Ask the supplier for a configured installer.";
        }
    }

    /// <summary>Persist clock observations without extending the signed offline allowance.</summary>
    public void ObserveClock()
    {
        lock (_gate)
        {
            if (_state is null) return;
            var now = _time.GetUtcNow();
            if (_state.LastSeenUtc - now > TimeSpan.FromMinutes(5)) _clockRejected = true;
            if (now <= _state.LastSeenUtc) return;
            var next = _state with { LastSeenUtc = now };
            try { store.Write(next); _state = next; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WorkspaceValidationException)
            { _clockRejected = true; Message = "Activation could not be saved. Check your Windows data-folder permissions and retry."; }
        }
    }

    public Task RegisterAsync(string username, string password, string customerName, string contact, CancellationToken cancellationToken = default)
    {
        Load();
        return SendAsync("api/activation/register", new ActivationRegistration(username, password, customerName, contact, InstallationId), username, cancellationToken);
    }
    public Task SignInAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        Load();
        return SendAsync("api/activation/login", new ActivationLogin(username, password, InstallationId), username, cancellationToken);
    }
    public Task CheckAsync(CancellationToken cancellationToken = default)
    {
        Load();
        return SendAsync("api/activation/check", new ActivationCheck(_state?.Token ?? "", InstallationId), Username, cancellationToken);
    }

    private async Task SendAsync<T>(string route, T request, string username, CancellationToken cancellationToken)
    {
        Load();
        if (!IsConfigured) { Message = "This installer has no activation server configured. Ask the supplier for a configured installer."; return; }
        await _requests.WaitAsync(cancellationToken);
        try
        {
            using var response = await http.PostAsJsonAsync(route, request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = response.StatusCode == System.Net.HttpStatusCode.BadRequest
                    ? await response.Content.ReadFromJsonAsync<ActivationError>(cancellationToken) : null;
                Message = response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                    ? "Too many attempts. Wait a few minutes and retry." : error?.Message ?? "Activation sign-in was refused. Check your details and try again.";
                return;
            }
            var reply = await response.Content.ReadFromJsonAsync<ActivationReply>(cancellationToken)
                ?? throw new JsonException();
            lock (_gate)
            {
                var grant = ActivationSignature.Verify(reply.Lease, publicKey, InstallationId, _time.GetUtcNow());
                if (reply.Status == "Active" && grant is null) { Message = "The activation response could not be verified. Contact the supplier."; return; }
                if (reply.Status is not ("Active" or "Pending" or "Suspended" or "SignInRequired")) throw new JsonException();
                var next = _state! with { Username = username.Trim(), Token = reply.Token ?? _state!.Token,
                    Lease = reply.Status == "Active" ? reply.Lease : null, LastSeenUtc = _time.GetUtcNow() };
                if (reply.Status == "SignInRequired") next = next with { Token = null };
                // Persist first: a failed write must not open the desktop.
                if (reply.Status != "Active") { _state = next; _grant = null; _remaining = TimeSpan.Zero; }
                store.Write(next);
                _state = next;
                _clockRejected = false;
                ResetTimer();
                Message = reply.Message;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException
            or UnauthorizedAccessException or WorkspaceValidationException)
        { Message = "Internet is needed to check activation. Connect and retry. If the problem continues, contact your admin."; }
        finally { _requests.Release(); }
    }

    private ActivationGrant? ValidGrant() => _state is null ? null
        : ActivationSignature.Verify(_state.Lease, publicKey, _state.InstallationId, _time.GetUtcNow());
    private void ResetTimer()
    {
        _grant = ValidGrant();
        _checkedTimestamp = _time.GetTimestamp();
        _remaining = (_grant?.ExpiresUtc ?? _time.GetUtcNow()) - _time.GetUtcNow();
    }
    private sealed record ActivationError(string Message);
}
