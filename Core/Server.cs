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

using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using RurouniJones.Dcs.Grpc.V0.Mission;
using RurouniJones.Telemachus.Core.Collectors;

namespace RurouniJones.Telemachus.Core
{
    public class Server
    {
        private readonly ILogger<Server> _logger;
        private readonly CollectorFactory _collectorFactory;

        private Task? _sessionUpdateTask;
        private readonly List<Task> _collectionTasks;
        private CancellationTokenSource? _sessionStoppingTokenSource;

        private long? _sessionId; 

        public Server(ILogger<Server> logger, CollectorFactory collectorFactory)
        {
            _logger = logger;
            _collectorFactory = collectorFactory;
            _collectionTasks = new();
        }

        public async Task StartMonitoringAsync(string shortName, GrpcChannel channel, CancellationToken stoppingToken)
        {
            _sessionUpdateTask = StartUpdatingSessionIdAsync(shortName, channel, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                if(_sessionId == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }
                _sessionStoppingTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var sessionStoppingToken = _sessionStoppingTokenSource.Token;

                var collectorConfig = new ICollector.CollectorConfig(shortName, (long)_sessionId, channel, sessionStoppingToken);

                var ballisticCollector =
                    _collectorFactory.CreateCollector(ICollector.CollectorType.BallisticCollector);
                _collectionTasks.Add(ballisticCollector.MonitorAsync(collectorConfig));

                var eventCollector =
                    _collectorFactory.CreateCollector(ICollector.CollectorType.EventCollector);
                _collectionTasks.Add(eventCollector.MonitorAsync(collectorConfig));

                var playerDetailsCollector =
                    _collectorFactory.CreateCollector(ICollector.CollectorType.PlayerDetailsCollector);
                _collectionTasks.Add(playerDetailsCollector.MonitorAsync(collectorConfig));

                var unitCollector =
                    _collectorFactory.CreateCollector(ICollector.CollectorType.UnitCollector);
                _collectionTasks.Add(unitCollector.MonitorAsync(collectorConfig));


                Task.WaitAll(_collectionTasks.ToArray(), CancellationToken.None);
                _collectionTasks.Clear();
            }
            _sessionUpdateTask.Wait(TimeSpan.FromSeconds(5));
        }

        public async Task StartUpdatingSessionIdAsync(string shortName, GrpcChannel channel, CancellationToken stoppingToken)
        {
            var service = new MissionService.MissionServiceClient(channel);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogTrace("Getting Session ID for {shortName}", shortName);
                    var response = await service.GetSessionIdAsync(new GetSessionIdRequest(), cancellationToken: stoppingToken);
                    _sessionId = response.SessionId;
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
                {
                    _logger.LogWarning("Timed out calling {shortName}", shortName);
                    _sessionStoppingTokenSource?.Cancel();
                    _sessionId = null;
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Exception calling {shortName}. Exception {exception}", shortName, ex.Message);
                    _sessionStoppingTokenSource?.Cancel();
                    _sessionId = null;
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
        }
    }
}
