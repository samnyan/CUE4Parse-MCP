using System.Collections.Concurrent;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets;
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
    public int CachedPackageCount => _packageCache.Count;

    private const int MaxCachedPackages = 64;
    private readonly ConcurrentDictionary<string, Lazy<IPackage?>> _packageCache;
    private readonly ConcurrentQueue<string> _packageCacheOrder = new();

    public Cue4ParseSession(string sessionId, DefaultFileProvider provider, string rootDirectory, EGame gameVersion)
    {
        SessionId = sessionId;
        Provider = provider;
        RootDirectory = rootDirectory;
        GameVersion = gameVersion;
        CreatedAt = DateTimeOffset.UtcNow;
        _packageCache = new ConcurrentDictionary<string, Lazy<IPackage?>>(provider.PathComparer);
    }

    public bool TryLoadPackage(string path, out IPackage? package, out string normalizedPath)
    {
        normalizedPath = path;
        try { normalizedPath = Provider.FixPath(path); } catch { }
        var originalPath = path;
        var added = false;
        var lazy = _packageCache.GetOrAdd(normalizedPath, key =>
        {
            added = true;
            return new Lazy<IPackage?>(() =>
            {
                if (Provider.TryLoadPackage(key, out var loaded)) return loaded;
                return Provider.TryLoadPackage(originalPath, out loaded) ? loaded : null;
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        });
        if (added)
        {
            _packageCacheOrder.Enqueue(normalizedPath);
            while (_packageCache.Count > MaxCachedPackages && _packageCacheOrder.TryDequeue(out var oldest))
                _packageCache.TryRemove(oldest, out _);
        }
        package = lazy.Value;
        if (package != null) return true;
        _packageCache.TryRemove(normalizedPath, out _);
        return false;
    }

    public void ClearPackageCache()
    {
        _packageCache.Clear();
        while (_packageCacheOrder.TryDequeue(out _)) { }
    }

    public void Dispose()
    {
        ClearPackageCache();
        Provider.Dispose();
    }
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
