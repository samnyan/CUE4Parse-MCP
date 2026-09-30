namespace CUE4Parse.Mcp.Configuration;

public sealed class ServerOptions
{
    public bool Http { get; init; }
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 3001;
    public IReadOnlyList<string> WhitelistDirectories { get; init; } = [];
    public bool HelpRequested { get; init; }

    public static bool TryParse(string[] args, out ServerOptions options, out string? error)
    {
        var http = false;
        var host = "127.0.0.1";
        var port = 3001;
        var whitelistDirectories = new List<string>();
        var helpRequested = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--http":
                    http = true;
                    break;

                case "--host":
                    if (!TryReadValue(args, ref i, "--host", out host, out error))
                    {
                        options = new ServerOptions();
                        return false;
                    }
                    if (string.IsNullOrWhiteSpace(host) ||
                        host.Contains("://", StringComparison.Ordinal) ||
                        host.Contains('/') ||
                        host.Contains('\\'))
                    {
                        options = new ServerOptions();
                        error = "--host must be a hostname or IP address, not a URL.";
                        return false;
                    }
                    break;

                case "--port":
                    if (!TryReadValue(args, ref i, "--port", out var portText, out error))
                    {
                        options = new ServerOptions();
                        return false;
                    }
                    if (!int.TryParse(portText, out port) || port is < 1 or > 65535)
                    {
                        options = new ServerOptions();
                        error = "--port must be an integer between 1 and 65535.";
                        return false;
                    }
                    break;

                case "--whitelist-dir":
                    if (!TryReadValue(args, ref i, "--whitelist-dir", out var whitelistDir, out error))
                    {
                        options = new ServerOptions();
                        return false;
                    }
                    whitelistDirectories.Add(whitelistDir);
                    break;

                case "--help":
                case "-h":
                    helpRequested = true;
                    break;

                default:
                    options = new ServerOptions();
                    error = $"Unknown argument '{args[i]}'.";
                    return false;
            }
        }

        options = new ServerOptions
        {
            Http = http,
            Host = host,
            Port = port,
            WhitelistDirectories = whitelistDirectories,
            HelpRequested = helpRequested
        };
        error = null;
        return true;
    }

    public static string HelpText =>
        """
        CUE4Parse MCP Server

        Usage:
          CUE4Parse.Mcp.exe [--http] [--host <host>] [--port <port>]
                            [--whitelist-dir <directory>]...

        Options:
          --http                       Run the MCP server over Streamable HTTP.
          --host <host>                HTTP bind host. Default: 127.0.0.1.
          --port <port>                HTTP listen port. Default: 3001.
          --whitelist-dir <directory>  Restrict filesystem access to this directory and its descendants.
                                       May be specified multiple times.
          --help, -h                   Show this help text.

        Without --http, the server uses the existing stdio transport.
        """;

    private static bool TryReadValue(
        string[] args,
        ref int index,
        string option,
        out string value,
        out string? error)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = "";
            error = $"{option} requires a value.";
            return false;
        }

        value = args[++index];
        error = null;
        return true;
    }
}
