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

using Grpc.Net.Client;
using Microsoft.Extensions.Options;
using RurouniJones.Telemachus.Configuration;
using RurouniJones.Telemachus.Core.Collectors;

namespace RurouniJones.Telemachus.Service
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        private Dictionary<string, GrpcChannel> _gameServerChannels = new();

        private HashSet<ICollector> _collectors = new();

        public Worker(ILogger<Worker> logger, IOptions<Application> appConfig, PlayerCountCollector playerCountCollector, EventCollector eventCollector)
        {
            _logger = logger;

            _collectors.Add(playerCountCollector);
            _collectors.Add(eventCollector);

            _gameServerChannels = new();
            foreach (var gameServer in appConfig.Value.GameServers)
            {
                _gameServerChannels[gameServer.ShortName] = GrpcChannel.ForAddress($"http://{gameServer.Rpc.Host}:{gameServer.Rpc.Port}",
                    new GrpcChannelOptions
                    {
                        HttpHandler = new SocketsHttpHandler
                        {
                            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
                            EnableMultipleHttp2Connections = true,
                        }
                    }
                );
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            foreach (var collector in _collectors) { 
                collector.Execute(_gameServerChannels, stoppingToken);
            }
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
    }
}
