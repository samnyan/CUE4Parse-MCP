using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;

namespace CUE4Parse.Mcp.Services;

public sealed class ResolvedObjectTarget
{
    public required UObject Object { get; init; }
    public required IPackage Package { get; init; }
    public required string RequestedPath { get; init; }
    public required string ObjectPath { get; init; }
    public int? ExportIndex { get; init; }
}

public static class PackageObjectResolver
{
    public static bool TryLoadPackage(Cue4ParseSession session, string packagePath, out IPackage? package, out string normalizedPath, out string? error)
    {
        if (session.TryLoadPackage(packagePath, out package, out normalizedPath))
        {
            error = null;
            return true;
        }

        error = $"Could not load package at '{packagePath}'.";
        return false;
    }

    public static bool TryResolve(
        Cue4ParseSession session,
        string? objectPath,
        string? packagePath,
        int? exportIndex,
        string? exportName,
        out ResolvedObjectTarget? target,
        out string? error)
    {
        target = null;
        var hasObjectPath = !string.IsNullOrWhiteSpace(objectPath);
        var hasPackageSelector = !string.IsNullOrWhiteSpace(packagePath);
        if (hasObjectPath == hasPackageSelector)
        {
            error = "Specify either objectPath or packagePath with exportIndex/exportName, but not both.";
            return false;
        }

        if (hasPackageSelector)
        {
            if (exportIndex is null && string.IsNullOrWhiteSpace(exportName))
            {
                error = "packagePath requires exportIndex or exportName.";
                return false;
            }
            if (exportIndex is not null && !string.IsNullOrWhiteSpace(exportName))
            {
                error = "Specify exportIndex or exportName, not both.";
                return false;
            }
            if (!TryLoadPackage(session, packagePath!, out var package, out _, out error)) return false;
            var index = exportIndex ?? package!.GetExportIndex(exportName!, StringComparison.OrdinalIgnoreCase);
            if (index < 0 || index >= package!.ExportMapLength)
            {
                error = $"Export '{exportName ?? exportIndex?.ToString()}' was not found in '{packagePath}'.";
                return false;
            }
            var export = package.GetExport(index);
            if (export == null)
            {
                error = $"Export index {index} in '{packagePath}' could not be loaded.";
                return false;
            }
            target = CreateTarget(export, package, packagePath!, index);
            error = null;
            return true;
        }

        try
        {
            var loaded = session.Provider.SafeLoadPackageObject(objectPath!);
            if (loaded != null && loaded.Owner != null)
            {
                target = CreateTarget(loaded, loaded.Owner, objectPath!, FindExportIndex(loaded.Owner, loaded));
                error = null;
                return true;
            }
        }
        catch
        {
        }

        return TryResolveNestedPath(session, objectPath!, out target, out error);
    }

    private static bool TryResolveNestedPath(Cue4ParseSession session, string objectPath, out ResolvedObjectTarget? target, out string? error)
    {
        target = null;
        var dot = objectPath.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0)
        {
            error = $"Could not load object at '{objectPath}'.";
            return false;
        }

        var packagePath = objectPath[..dot];
        if (!TryLoadPackage(session, packagePath, out var package, out _, out error)) return false;
        var objectSelector = objectPath[(dot + 1)..];
        var leaf = objectSelector.Contains(':') ? objectSelector[(objectSelector.LastIndexOf(':') + 1)..] : objectSelector;
        var index = int.TryParse(leaf, out var numericIndex)
            ? numericIndex
            : package!.GetExportIndex(leaf, StringComparison.OrdinalIgnoreCase);
        if (index < 0 || index >= package!.ExportMapLength)
        {
            error = $"Could not resolve nested export '{leaf}' in '{packagePath}'.";
            return false;
        }

        var export = package.GetExport(index);
        if (export == null)
        {
            error = $"Export index {index} in '{packagePath}' could not be loaded.";
            return false;
        }

        target = CreateTarget(export, package, objectPath, index);
        error = null;
        return true;
    }

    private static ResolvedObjectTarget CreateTarget(UObject obj, IPackage package, string requestedPath, int? exportIndex) => new()
    {
        Object = obj,
        Package = package,
        RequestedPath = requestedPath,
        ObjectPath = obj.GetPathName(),
        ExportIndex = exportIndex
    };

    public static int? FindExportIndex(IPackage package, UObject obj)
    {
        var index = package.GetExportIndex(obj.Name, StringComparison.Ordinal);
        if (index >= 0) return index;
        for (var i = 0; i < package.ExportsLazy.Length; i++)
        {
            if (package.ExportsLazy[i].IsValueCreated && ReferenceEquals(package.ExportsLazy[i].Value, obj)) return i;
        }
        return null;
    }
}
