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

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using RurouniJones.Dcs.Grpc.V0.Common;
using RurouniJones.Dcs.Grpc.V0.Net;

namespace RurouniJones.Telemachus.Core.Collectors
{ 
    public class PlayerCountCollector : ICollector
    {
        private readonly ILogger<PlayerCountCollector> _logger;

        private Meter _meter;

        public PlayerCountCollector(ILogger<PlayerCountCollector> logger)
        {
            _meter = new Meter("Telemachus.Core.Collectors.PlayerCountCollector");
            _logger = logger;
        }

        public void Execute(Dictionary<string, GrpcChannel> gameServerChannels, CancellationToken stoppingToken)
        {
            _meter.CreateObservableGauge("players", () => { return GetPlayersOnServers(gameServerChannels, stoppingToken); },
                description: "The number of players on a server");
        }

        private List<Measurement<int>> GetPlayersOnServers(Dictionary<string, GrpcChannel> gameServerChannels, CancellationToken stoppingToken)
        {
            ConcurrentBag<Measurement<int>> results = new();
            List<Task> tasks = new();

            foreach (KeyValuePair<string, GrpcChannel> server in gameServerChannels)
            {
                tasks.Add(Task.Run(async () =>
                {
                    foreach (var item in await GetPlayersOnServer(server.Key, server.Value, stoppingToken))
                    {
                        results.Add(item);
                    }
                }, stoppingToken));
            }
            Task.WaitAll(tasks.ToArray(), stoppingToken);

            return results.ToList();
        }
        private async Task<List<Measurement<int>>> GetPlayersOnServer(string shortName, GrpcChannel channel, CancellationToken stoppingToken)
        {
            _logger.LogInformation($"Getting Players for {shortName}");
            List<Measurement<int>> results = new();

            var service = new NetService.NetServiceClient(channel);
            try
            {
                var response = await service.GetPlayersAsync(new GetPlayersRequest { }, deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: stoppingToken);
                var players = response.Players;
                var blueFor = players.Count(x => x.Coalition == Coalition.Blue);
                var redFor = players.Count(x => x.Coalition == Coalition.Red);

                results.Add(new Measurement<int>(blueFor,
                    new KeyValuePair<string, object?>(ICollector.SERVER_SHORT_NAME_LABEL, shortName),
                    new KeyValuePair<string, object?>("coalition", "Blue")));
                results.Add(new Measurement<int>(redFor,
                    new KeyValuePair<string, object?>(ICollector.SERVER_SHORT_NAME_LABEL, shortName),
                    new KeyValuePair<string, object?>("coalition", "Red")));
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                _logger.LogWarning($"Timed out calling {shortName}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Exception calling {shortName}. Exception {ex.Message}");
            }

            return results;
        }
    }
}
