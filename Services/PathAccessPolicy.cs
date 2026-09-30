namespace CUE4Parse.Mcp.Services;

public sealed class PathAccessPolicy
{
    private sealed record AllowedRoot(string LogicalPath, string PhysicalPath);

    private readonly List<AllowedRoot> _allowedRoots = [];

    public bool IsRestricted => _allowedRoots.Count > 0;
    public IReadOnlyList<string> AllowedDirectories => _allowedRoots.Select(root => root.LogicalPath).ToArray();

    public PathAccessPolicy(IEnumerable<string>? allowedDirectories = null)
    {
        if (allowedDirectories == null)
            return;

        foreach (var path in allowedDirectories)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Whitelist directory must not be empty.", nameof(allowedDirectories));

            string fullPath;
            try
            {
                fullPath = NormalizePath(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new ArgumentException($"Invalid whitelist directory '{path}': {ex.Message}", nameof(allowedDirectories), ex);
            }

            if (!Directory.Exists(fullPath))
                throw new DirectoryNotFoundException($"Whitelist directory '{fullPath}' does not exist.");

            var physicalPath = ResolveExistingPath(fullPath);
            if (_allowedRoots.Any(root => PathsEqual(root.LogicalPath, fullPath)))
                continue;

            _allowedRoots.Add(new AllowedRoot(fullPath, physicalPath));
        }
    }

    public bool TryValidateDirectory(
        string path,
        out string normalizedPath,
        out string errorCode,
        out string errorMessage) =>
        TryValidateExistingPath(path, expectDirectory: true, out normalizedPath, out errorCode, out errorMessage);

    public bool TryValidateFile(
        string path,
        out string normalizedPath,
        out string errorCode,
        out string errorMessage) =>
        TryValidateExistingPath(path, expectDirectory: false, out normalizedPath, out errorCode, out errorMessage);

    public bool TryValidateDirectoryTree(
        string path,
        bool recursive,
        out string normalizedPath,
        out string errorCode,
        out string errorMessage)
    {
        if (!TryValidateDirectory(path, out normalizedPath, out errorCode, out errorMessage))
            return false;

        if (!IsRestricted)
            return true;

        try
        {
            var pending = new Stack<string>();
            pending.Push(normalizedPath);

            while (pending.Count > 0)
            {
                var current = pending.Pop();
                foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                {
                    FileSystemInfo info = Directory.Exists(entry)
                        ? new DirectoryInfo(entry)
                        : new FileInfo(entry);

                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
                        if (resolved != null && !_allowedRoots.Any(root => IsWithin(root.PhysicalPath, NormalizePath(resolved.FullName))))
                        {
                            errorCode = "path_not_allowed";
                            errorMessage = $"Path '{entry}' resolves outside the configured whitelist.";
                            return false;
                        }

                        // Do not traverse reparse points ourselves. CUE4Parse may choose to follow them,
                        // but the resolved target was already checked above.
                        continue;
                    }

                    if (recursive && info is DirectoryInfo)
                        pending.Push(entry);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            errorCode = "path_resolution_failed";
            errorMessage = $"Could not safely inspect directory tree: {ex.Message}";
            return false;
        }

        errorCode = "";
        errorMessage = "";
        return true;
    }

    public bool TryValidatePotentialFile(
        string path,
        out string normalizedPath,
        out string errorCode,
        out string errorMessage)
    {
        if (!TryNormalizeAndCheckLogicalContainment(path, out normalizedPath, out var allowedRoot, out errorCode, out errorMessage))
            return false;

        if (!File.Exists(normalizedPath))
            return true;

        return TryCheckPhysicalContainment(normalizedPath, allowedRoot, out errorCode, out errorMessage);
    }

    private bool TryValidateExistingPath(
        string path,
        bool expectDirectory,
        out string normalizedPath,
        out string errorCode,
        out string errorMessage)
    {
        if (!TryNormalizeAndCheckLogicalContainment(path, out normalizedPath, out var allowedRoot, out errorCode, out errorMessage))
            return false;

        var exists = expectDirectory ? Directory.Exists(normalizedPath) : File.Exists(normalizedPath);
        if (!exists)
        {
            errorCode = expectDirectory ? "directory_not_found" : "file_not_found";
            errorMessage = expectDirectory
                ? $"The directory '{normalizedPath}' does not exist."
                : $"The file '{normalizedPath}' does not exist.";
            return false;
        }

        return TryCheckPhysicalContainment(normalizedPath, allowedRoot, out errorCode, out errorMessage);
    }

    private bool TryNormalizeAndCheckLogicalContainment(
        string path,
        out string normalizedPath,
        out AllowedRoot? allowedRoot,
        out string errorCode,
        out string errorMessage)
    {
        try
        {
            normalizedPath = NormalizePath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            normalizedPath = "";
            allowedRoot = null;
            errorCode = "invalid_path";
            errorMessage = ex.Message;
            return false;
        }

        if (!IsRestricted)
        {
            allowedRoot = null;
            errorCode = "";
            errorMessage = "";
            return true;
        }

        allowedRoot = _allowedRoots.FirstOrDefault(root => IsWithin(root.LogicalPath, normalizedPath));
        if (allowedRoot == null)
        {
            errorCode = "path_not_allowed";
            errorMessage = "Path is outside the configured whitelist.";
            return false;
        }

        errorCode = "";
        errorMessage = "";
        return true;
    }

    private static bool TryCheckPhysicalContainment(
        string normalizedPath,
        AllowedRoot? allowedRoot,
        out string errorCode,
        out string errorMessage)
    {
        if (allowedRoot == null)
        {
            errorCode = "";
            errorMessage = "";
            return true;
        }

        try
        {
            var physicalPath = ResolveExistingPath(normalizedPath);
            if (!IsWithin(allowedRoot.PhysicalPath, physicalPath))
            {
                errorCode = "path_not_allowed";
                errorMessage = "Path resolves outside the configured whitelist.";
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            errorCode = "path_resolution_failed";
            errorMessage = $"Could not safely resolve path: {ex.Message}";
            return false;
        }

        errorCode = "";
        errorMessage = "";
        return true;
    }

    private static string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string ResolveExistingPath(string path)
    {
        var fullPath = NormalizePath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new IOException($"Could not determine filesystem root for '{fullPath}'.");

        var current = Path.TrimEndingDirectorySeparator(root);
        var remainder = fullPath[root.Length..];
        var segments = remainder.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            var candidate = Path.Combine(current, segment);
            FileSystemInfo info;
            if (Directory.Exists(candidate))
                info = new DirectoryInfo(candidate);
            else if (File.Exists(candidate))
                info = new FileInfo(candidate);
            else
                throw new IOException($"Path component '{candidate}' does not exist.");

            var linkTarget = info.ResolveLinkTarget(returnFinalTarget: true);
            current = linkTarget == null
                ? candidate
                : linkTarget.FullName;
            current = NormalizePath(current);
        }

        return current;
    }

    private static bool IsWithin(string root, string path)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(root, path, comparison))
            return true;

        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        return path.StartsWith(rootWithSeparator, comparison);
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(left, right, comparison);
    }
}
