﻿/* 
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
using RurouniJones.Telemachus.Core;

namespace RurouniJones.Telemachus.Service
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        private readonly List<Server> _servers;
        private readonly List<Task> _serverTasks;

        public Worker(ILogger<Worker> logger, IOptions<Application> appConfig, ServerFactory serverFactory)
        {
            _logger = logger;
            _servers = new();
            _serverTasks = new();

            foreach (var gameServer in appConfig.Value.GameServers)
            {
                var channel = GrpcChannel.ForAddress($"http://{gameServer.Rpc.Host}:{gameServer.Rpc.Port}",
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
                _servers.Add(serverFactory.CreateServer(gameServer.Name, gameServer.ShortName, channel));
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Run(() => {
                _logger.LogInformation("Starting Worker");
                foreach (var server in _servers) {
                    _serverTasks.Add(server.StartMonitoringAsync(stoppingToken));
                }
                Task.WaitAll(_serverTasks.ToArray(), CancellationToken.None);
            }, CancellationToken.None);
        }
    }
}
