using System.Collections.Concurrent;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;

namespace CUE4Parse.Mcp.Services;

public sealed class Cue4ParseSession : IDisposable
{
    public string SessionId { get; }
    public DefaultFileProvider Provider { get; }
    public string RootDirectory { get; }
    public EGame GameVersion { get; }
    public DateTimeOffset CreatedAt { get; }
    public bool IsInitialized { get; set; }
    public int EncryptedArchiveCount { get; set; }
    public List<string> Warnings { get; } = [];

    public Cue4ParseSession(string sessionId, DefaultFileProvider provider, string rootDirectory, EGame gameVersion)
    {
        SessionId = sessionId;
        Provider = provider;
        RootDirectory = rootDirectory;
        GameVersion = gameVersion;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void Dispose() => Provider.Dispose();
}

public sealed class Cue4ParseSessionRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, Cue4ParseSession> _sessions = new();
    private string? _defaultSessionId;

    public Cue4ParseSession CreateSession(DefaultFileProvider provider, string rootDirectory, EGame gameVersion)
    {
        var sessionId = Guid.NewGuid().ToString("N")[..12];
        var session = new Cue4ParseSession(sessionId, provider, rootDirectory, gameVersion);
        _sessions[sessionId] = session;
        _defaultSessionId = sessionId;
        return session;
    }

    public Cue4ParseSession? GetSession(string? sessionId)
    {
        if (!string.IsNullOrEmpty(sessionId) && _sessions.TryGetValue(sessionId, out var session))
            return session;
        if (_defaultSessionId != null && _sessions.TryGetValue(_defaultSessionId, out var defaultSession))
            return defaultSession;
        return null;
    }

    public bool RemoveSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            session.Dispose();
            if (_defaultSessionId == sessionId)
                _defaultSessionId = _sessions.Keys.FirstOrDefault();
            return true;
        }
        return false;
    }

    public IEnumerable<Cue4ParseSession> GetAllSessions() => _sessions.Values;

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
            session.Dispose();
        _sessions.Clear();
    }
}
