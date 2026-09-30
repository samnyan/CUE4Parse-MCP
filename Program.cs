using CUE4Parse.Mcp.Configuration;
using CUE4Parse.Mcp.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using Serilog;
using Serilog.Events;

if (!ServerOptions.TryParse(args, out var serverOptions, out var parseError))
{
    Console.Error.WriteLine(parseError);
    Console.Error.WriteLine();
    Console.Error.WriteLine(ServerOptions.HelpText);
    Environment.ExitCode = 2;
    return;
}

if (serverOptions.HelpRequested)
{
    Console.WriteLine(ServerOptions.HelpText);
    return;
}

PathAccessPolicy pathAccessPolicy;
try
{
    pathAccessPolicy = new PathAccessPolicy(serverOptions.WhitelistDirectories);
}
catch (Exception ex) when (ex is ArgumentException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Failed to configure filesystem whitelist: {ex.Message}");
    Environment.ExitCode = 2;
    return;
}

// Redirect Serilog (used internally by CUE4Parse) to stderr in both transports.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Warning()
    .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose)
    .CreateLogger();

if (serverOptions.Http)
{
    await RunHttpAsync(serverOptions, pathAccessPolicy);
}
else
{
    await RunStdioAsync(pathAccessPolicy);
}

static async Task RunStdioAsync(PathAccessPolicy pathAccessPolicy)
{
    var builder = Host.CreateApplicationBuilder([]);

    // stdout is reserved for MCP JSON-RPC in stdio mode.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

    AddCommonServices(builder.Services, pathAccessPolicy);
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    await builder.Build().RunAsync();
}

static async Task RunHttpAsync(ServerOptions options, PathAccessPolicy pathAccessPolicy)
{
    var builder = WebApplication.CreateBuilder([]);

    // Keep logs on stderr for consistent behavior across transports.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

    AddCommonServices(builder.Services, pathAccessPolicy);
    builder.Services
        .AddMcpServer()
        .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
        .WithToolsFromAssembly();

    var app = builder.Build();
    app.MapMcp("/mcp");

    var listenHost = options.Host.Contains(':') && !options.Host.StartsWith('[')
        ? $"[{options.Host}]"
        : options.Host;
    var listenUrl = $"http://{listenHost}:{options.Port}";
    app.Urls.Add(listenUrl);

    Console.Error.WriteLine($"CUE4Parse MCP Streamable HTTP: {listenUrl}/mcp");
    if (pathAccessPolicy.IsRestricted)
    {
        Console.Error.WriteLine("Filesystem whitelist:");
        foreach (var path in pathAccessPolicy.AllowedDirectories)
            Console.Error.WriteLine($"  {path}");
    }
    else
    {
        Console.Error.WriteLine("WARNING: filesystem access is unrestricted.");
    }

    await app.RunAsync();
}

static void AddCommonServices(IServiceCollection services, PathAccessPolicy pathAccessPolicy)
{
    services.AddSingleton<Cue4ParseSessionRegistry>();
    services.AddSingleton(pathAccessPolicy);
}
