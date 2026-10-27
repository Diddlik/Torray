using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Torray.Application;
using Torray.Domain;
using Torray.Infrastructure;
using Velopack;

namespace Torray.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Velopack must run before anything else: it handles install/update hooks and may exit the process.
        VelopackApp.Build().Run();

        using var singleInstance = new Mutex(true, @"Local\Torray.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            return;
        }

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Torray");

        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(
                Path.Combine(dataDirectory, "logs", "torray-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true)
            .CreateLogger();

        try
        {
            Log.Information("Torray {Version} starting", AppInfo.Version);

            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = args,
                ContentRootPath = AppContext.BaseDirectory,
            });
            builder.Services.AddSerilog();
            builder.Services.AddSingleton<ISettingsStore>(
                new JsonSettingsStore(Path.Combine(dataDirectory, "settings.json")));
            builder.Services.AddSingleton<ISecretStore>(new DpapiSecretStore(Path.Combine(dataDirectory, "secrets")));
            builder.Services.AddSingleton<ITransmissionRpcClientFactory, TransmissionRpcClientFactory>();
            builder.Services.AddSingleton<TorrentService>();
            builder.Services.AddHostedService(services => services.GetRequiredService<TorrentService>());
            builder.Services.AddSingleton<IWatchJobStore>(new JsonWatchJobStore(Path.Combine(dataDirectory, "watch-jobs.json")));
            builder.Services.AddSingleton<WatchFolderService>();
            builder.Services.AddHostedService(services => services.GetRequiredService<WatchFolderService>());
            builder.Services.AddSingleton<App>();

            using var host = builder.Build();
            host.Start();

            var app = host.Services.GetRequiredService<App>();
            app.InitializeComponent();
            app.Run();

            host.StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Torray terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
