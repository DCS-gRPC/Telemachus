/* 
Custodian is a DCS server administration tool for Discord
Copyright (C) 2022 Jeffrey Jones

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RurouniJones.Telemachus.Configuration.Util;
using RurouniJones.Telemachus.Core.Collectors;
using Serilog;
using System.Runtime.InteropServices;

namespace RurouniJones.Telemachus.Service
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            try
            {
                AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

                Environment.CurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;

                if (OperatingSystem.IsWindows() && Environment.UserInteractive)
                    ConsoleProperties.DisableQuickEdit();
                Console.Title = $"Telemachus Version {typeof(Worker).Assembly.GetName().Version}";

                // Setup the alternative configuration files instead of appsettings.json
                IConfigurationRoot configuration = new ConfigurationBuilder()
                    .AddYamlFile("configuration.yaml", false, true)
                    .AddYamlFile("configuration.development.yaml", true, true)
                    .Build();

                // Setup the logger configuration
                Log.Logger = new LoggerConfiguration()
                    .ReadFrom.Configuration(configuration)
                    .CreateLogger();

                IHostBuilder hostBuilder = Host.CreateDefaultBuilder(args)
                    .ConfigureServices(services =>
                    {
                        services.AddHostedService<Worker>();
                        services.AddOpenTelemetryMetrics(builder => builder
                            .SetResourceBuilder(ResourceBuilder.CreateDefault()
                                .AddService("Telemachus")
                            )
                            .AddConsoleExporter()
                            .AddOtlpExporter((o, m) =>
                            {
                                o.Protocol = OtlpExportProtocol.Grpc;
                                o.HttpClientFactory = () =>
                                {
                                    HttpClient client = new HttpClient();
                                    return client;
                                };
                                o.ExportProcessorType = OpenTelemetry.ExportProcessorType.Simple;
                                m.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = (int) TimeSpan.FromSeconds(1).TotalMilliseconds;
                            })
                            .AddMeter("Telemachus.Core.Collectors.PlayerCountCollector")
                            .AddMeter("Telemachus.Core.Collectors.EventCollector")
                        );
                        services.Configure<HostOptions>(opts => opts.ShutdownTimeout = TimeSpan.FromSeconds(60));
                        services.AddOptions<Configuration.Application>()
                            .Bind(configuration.GetSection("Application"))
                            .ValidateDataAnnotationsRecursively();
                        services.AddTransient<PlayerDetailsCollector>();
                        services.AddTransient<EventCollector>();
                    })
                    .UseSerilog();

                if (OperatingSystem.IsWindows())
                    hostBuilder.UseWindowsService();

                var host = hostBuilder.Build();
                await host.RunAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, ex.Message);
                if (!Environment.UserInteractive) return;
                Console.ReadKey();
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }

    // Taken from
    // https://stackoverflow.com/questions/13656846/how-to-programmatic-disable-c-sharp-console-applications-quick-edit-mode
    internal static class ConsoleProperties
    {

        // STD_INPUT_HANDLE (DWORD): -10 is the standard input device.
        private const int StdInputHandle = -10;

        private const uint QuickEdit = 0x0040;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll")]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        internal static bool DisableQuickEdit()
        {
            var consoleHandle = GetStdHandle(StdInputHandle);
            GetConsoleMode(consoleHandle, out var consoleMode);
            consoleMode &= ~QuickEdit;
            return SetConsoleMode(consoleHandle, consoleMode);
        }
    }
}
