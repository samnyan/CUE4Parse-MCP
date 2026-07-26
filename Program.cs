using CUE4Parse.Mcp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Serilog;
using Serilog.Events;

var builder = Host.CreateApplicationBuilder(args);

// All Microsoft.Extensions.Logging logs go to stderr — stdout is reserved for MCP JSON-RPC
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// Redirect Serilog (used internally by CUE4Parse) to stderr as well
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Warning()
    .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose)
    .CreateLogger();

builder.Services.AddSingleton<Cue4ParseSessionRegistry>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
