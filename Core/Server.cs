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
        public readonly string Name;
        public readonly string ShortName;
        private readonly GrpcChannel _channel;

        private Task? _sessionUpdateTask;
        private readonly List<Task> _collectionTasks;
        private CancellationTokenSource? _sessionStoppingTokenSource;

        private long? _sessionId; 

        public Server(ILogger<Server> logger, CollectorFactory collectorFactory, string name, string shortName, GrpcChannel channel)
        {
            _logger = logger;
            _collectorFactory = collectorFactory;
            Name = name;
            ShortName = shortName;
            _channel = channel;
            _collectionTasks = new();
        }

        public async Task StartMonitoringAsync(CancellationToken stoppingToken)
        {
            _sessionUpdateTask = StartUpdatingSessionIdAsync(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                if(_sessionId == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }
                _sessionStoppingTokenSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var sessionStoppingToken = _sessionStoppingTokenSource.Token;

                var collectorConfig = new ICollector.CollectorConfig(ShortName, (long)_sessionId, _channel, sessionStoppingToken);

                var ballisticCollector =
                    _collectorFactory.CreateCollector(ICollector.CollectorType.BallisticCollector, collectorConfig);
                _collectionTasks.Add(ballisticCollector.MonitorAsync());

                var eventCollector =
                    _collectorFactory.CreateCollector(ICollector.CollectorType.EventCollector, collectorConfig);
                _collectionTasks.Add(eventCollector.MonitorAsync());

                var playerDetailsCollector =
                    _collectorFactory.CreateCollector(ICollector.CollectorType.PlayerDetailsCollector, collectorConfig);
                _collectionTasks.Add(playerDetailsCollector.MonitorAsync());

                var unitCollector =
                    _collectorFactory.CreateCollector(ICollector.CollectorType.UnitCollector, collectorConfig);
                _collectionTasks.Add(unitCollector.MonitorAsync());

                Task.WaitAll(_collectionTasks.ToArray(), CancellationToken.None);
                _collectionTasks.Clear();
            }
            _sessionUpdateTask.Wait(TimeSpan.FromSeconds(5));
        }

        public async Task StartUpdatingSessionIdAsync(CancellationToken stoppingToken)
        {
            var service = new MissionService.MissionServiceClient(_channel);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogTrace("Getting Session ID for {shortName}", ShortName);
                    var response = await service.GetSessionIdAsync(new GetSessionIdRequest(), cancellationToken: stoppingToken);
                    _sessionId = response.SessionId;
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
                {
                    _logger.LogWarning("Timed out calling {shortName}", ShortName);
                    _sessionStoppingTokenSource?.Cancel();
                    _sessionId = null;
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Exception calling {shortName}. Exception {exception}", ShortName, ex.Message);
                    _sessionStoppingTokenSource?.Cancel();
                    _sessionId = null;
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
        }
    }
}
