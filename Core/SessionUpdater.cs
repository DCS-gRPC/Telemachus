// Custodian is a DCS server administration tool for Discord
// Copyright (C) 2022 Jeffrey Jones
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as
// published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY

using System.Runtime.CompilerServices;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using RurouniJones.Dcs.Grpc.V0.Mission;
using RurouniJones.Telemachus.Core.Collectors;

namespace RurouniJones.Telemachus.Core
{
    public class SessionUpdater
    {
        private readonly ILogger<BallisticCollector> _logger;
        private readonly Session _session;

        public SessionUpdater(ILogger<BallisticCollector> logger, Session session)
        {
            _logger = logger;
            _session = session;
        }

        public async Task ExecuteAsync(Dictionary<string, GrpcChannel> gameServerChannels, CancellationToken stoppingToken)
        {
            _logger.LogDebug("Executing SessionUpdater");
            List<Task> tasks = new();
            foreach (KeyValuePair<string, GrpcChannel> server in gameServerChannels)
            {
                tasks.Add(GetSessionIdOnServer(server.Key, server.Value, stoppingToken));
            }
        }

        private async Task GetSessionIdOnServer(string shortName, GrpcChannel channel, CancellationToken stoppingToken)
        {
            var service = new MissionService.MissionServiceClient(channel);
            while(!stoppingToken.IsCancellationRequested)
            {
                try {
                    _logger.LogDebug("Getting Session ID for {shortName}", shortName);
                    var response = await service.GetSessionIdAsync(new GetSessionIdRequest());
                    _session.SetSessionId(shortName, response.SessionId);
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
                {
                    _logger.LogWarning("Timed out calling {shortName}", shortName);
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Exception calling {shortName}. Exception {exception}", shortName, ex.Message);
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
        }
    }
}
