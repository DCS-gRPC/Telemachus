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

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using RurouniJones.Dcs.Grpc.V0.Common;
using RurouniJones.Dcs.Grpc.V0.Mission;

namespace RurouniJones.Telemachus.Core.Collectors
{
    public class EventCollector : ICollector
    {

        private readonly ILogger<EventCollector> _logger;

        private readonly Meter _meter;

        private readonly Counter<int> _birthCounter;
        private readonly Counter<int> _lostCounter;
        private readonly Counter<int> _shootCounter;
        private readonly Counter<int> _takeoffCounter;
        private readonly Counter<int> _landingCounter;
        private readonly Counter<int> _crashCounter;
        private readonly Counter<int> _hitCounter;
        private readonly Counter<int> _killCounter;
        private readonly Counter<int> _ejectionCounter;
        private readonly Counter<int> _deadCounter;
        private readonly Counter<int> _pilotDeadCounter;
        private readonly Counter<int> _connectCounter;
        private readonly Counter<int> _disconnectCounter;
        private readonly Counter<int> _shootingStartCounter;

        private readonly ConcurrentDictionary<string, int> _serverSimulationFramesPerSecond;

        private readonly Session _session;

        public EventCollector(ILogger<EventCollector> logger, Session session)
        {
            _meter = new Meter("Telemachus.Core.Collectors.EventCollector");
            _logger = logger;
            _session = session;

            _birthCounter = _meter.CreateCounter<int>("birth_counter", "births", "Number of units birth");
            _shootCounter = _meter.CreateCounter<int>("shoot_counter", "shots", "Number of shots");
            _takeoffCounter = _meter.CreateCounter<int>("takeoff_counter", "takeoffs", "Number of takeoffs");
            _landingCounter = _meter.CreateCounter<int>("landing_counter", "landings", "Number of landings");
            _crashCounter = _meter.CreateCounter<int>("crash_counter", "crashes", "Number of crashes");
            _hitCounter = _meter.CreateCounter<int>("hit_counter", "hits", "Number of hits");
            _ejectionCounter = _meter.CreateCounter<int>("ejection_counter", "ejections", "Number of ejections");
            _killCounter = _meter.CreateCounter<int>("kill_counter", "kills", "Number of kills");
            _deadCounter = _meter.CreateCounter<int>("dead_counter", "deaths", "Number of unit deaths");
            _pilotDeadCounter = _meter.CreateCounter<int>("pilot_dead_counter", "deaths", "Number of pilot deaths");
            _lostCounter = _meter.CreateCounter<int>("lost_counter", "losses", "Number of units lost");
            _connectCounter = _meter.CreateCounter<int>("connect_counter", "connections", "Number of player connection attempts regardless of success or failure");
            _disconnectCounter = _meter.CreateCounter<int>("disconnect_counter", "disconnections", "Number of player disconnections");
            _shootingStartCounter = _meter.CreateCounter<int>("shooting_start_counter", "starts", "Number of times a rapid-fire weapon starts firing");
            _serverSimulationFramesPerSecond = new();
        }

        public void Execute(Dictionary<string, GrpcChannel> gameServerChannels, CancellationToken stoppingToken)
        {
            _logger.LogDebug("Executing EventCollector");
            List<Task> tasks = new();
            foreach (KeyValuePair<string, GrpcChannel> entry in gameServerChannels)
            {
                var serverShortName = entry.Key;
                var grpcChannel = entry.Value;

                tasks.Add(MonitorAsync(serverShortName, grpcChannel, stoppingToken));
                _meter.CreateObservableGauge("simulation_frames_per_second_gauge",
                    () => { return GetSimulationFramesPerSecond(serverShortName); },
                    description: "The number of server simulation frames per second");
            }
        }

        public async Task MonitorAsync(string serverShortName, GrpcChannel channel, CancellationToken stoppingToken)
        {
            while(!stoppingToken.IsCancellationRequested) {
                StreamEventsResponse? eventUpdate = null;
                try
                { 
                    var client = new MissionService.MissionServiceClient(channel);
                    var events = client.StreamEvents(new StreamEventsRequest {}, cancellationToken: stoppingToken);
                    _serverSimulationFramesPerSecond[serverShortName] = 0;

                    while (await events.ResponseStream.MoveNext(stoppingToken))
                    {
                        eventUpdate = events.ResponseStream.Current;

                        var tags = new System.Diagnostics.TagList
                        {
                            new KeyValuePair<string, object?>(ICollector.SESSION_ID_LABEL, _session.GetSessionId(serverShortName)),
                            new KeyValuePair<string, object?>(ICollector.SERVER_SHORT_NAME_LABEL, serverShortName)
                        };
                        switch (eventUpdate.EventCase)
                        {
                            case StreamEventsResponse.EventOneofCase.None:
                                break;
                            case StreamEventsResponse.EventOneofCase.Shot:
                                var shotEvent = eventUpdate.Shot;
                                tags.Add(new KeyValuePair<string, object?>(ICollector.SHOOTER_TYPE_LABEL, shotEvent.Initiator.Unit.Type));
                                tags.Add(new KeyValuePair<string, object?>(ICollector.SHOOTER_COALITION_LABEL, shotEvent.Initiator.Unit.Coalition));
                                tags.Add(new KeyValuePair<string, object?>(ICollector.SHOOTER_IS_PLAYER_LABEL, shotEvent.Initiator.Unit.HasPlayerName));
                                tags.Add(new KeyValuePair<string, object?>(ICollector.SHOOTER_CATEGORY_LABEL, shotEvent.Initiator.Unit.Category));
                                tags.Add(new KeyValuePair<string, object?>(ICollector.WEAPON_LABEL, shotEvent.Weapon.Type));
                                _shootCounter.Add(1, tags);
                                break;
                            case StreamEventsResponse.EventOneofCase.Hit:
                                var hitEvent = eventUpdate.Hit;
                                tags.Add(new KeyValuePair<string, object?>(ICollector.SHOOTER_TYPE_LABEL, hitEvent.Initiator.Unit.Type));
                                tags.Add(new KeyValuePair<string, object?>(ICollector.SHOOTER_COALITION_LABEL, hitEvent.Initiator.Unit.Coalition));
                                tags.Add(new KeyValuePair<string, object?>(ICollector.SHOOTER_IS_PLAYER_LABEL, hitEvent.Initiator.Unit.HasPlayerName));
                                tags.Add(new KeyValuePair<string, object?>(ICollector.SHOOTER_CATEGORY_LABEL, hitEvent.Initiator.Unit.Category));
                                if (hitEvent.Weapon != null)
                                {
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.WEAPON_LABEL, hitEvent.Weapon.Type));
                                }
                                else
                                {
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.WEAPON_LABEL, hitEvent.WeaponName));
                                }
                                if (hitEvent.Target.Unit != null)
                                {
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.TARGET_TYPE_LABEL, hitEvent.Target.Unit.Type));
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.TARGET_COALITION_LABEL, hitEvent.Target.Unit.Coalition));
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.TARGET_IS_PLAYER_LABEL, hitEvent.Target.Unit.HasPlayerName));
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.TARGET_CATEGORY_LABEL, hitEvent.Target.Unit.Category));
                                }
                                else
                                {
                                    _logger.LogInformation("Hit event target was not a unit. Skipping processing"); // TODO handle other cases like statics etc.
                                }
                                _hitCounter.Add(1, tags);
                                break;
                            case StreamEventsResponse.EventOneofCase.Takeoff:
                                var takeoffEvent = eventUpdate.Takeoff;
                                tags = StandardSingleUnitEventTags(tags, takeoffEvent.Initiator.Unit);
                                if(takeoffEvent.Place != null && takeoffEvent.Place.Name != null)
                                {
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.AIRBASE_LABEL, takeoffEvent.Place.Name));
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.AIRBASE_CATEGORY_LABEL, takeoffEvent.Place.Category));

                                }
                                _takeoffCounter.Add(1, tags);
                                break;
                            case StreamEventsResponse.EventOneofCase.Land:
                                var landEvent = eventUpdate.Land;
                                tags = StandardSingleUnitEventTags(tags, landEvent.Initiator.Unit);
                                if (landEvent.Place != null && landEvent.Place.Name != null)
                                {
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.AIRBASE_LABEL, landEvent.Place.Name));
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.AIRBASE_CATEGORY_LABEL, landEvent.Place.Category));
                                }
                                _landingCounter.Add(1, tags);
                                break;
                            case StreamEventsResponse.EventOneofCase.Crash:
                                var crashEvent = eventUpdate.Crash;
                                tags = StandardSingleUnitEventTags(tags, crashEvent.Initiator.Unit);
                                _crashCounter.Add(1, tags);
                                break;
                            case StreamEventsResponse.EventOneofCase.Ejection:
                                var ejectionEvent = eventUpdate.Ejection;
                                tags = StandardSingleUnitEventTags(tags, ejectionEvent.Initiator.Unit);
                                _ejectionCounter.Add(1, tags);
                                break;
                            case StreamEventsResponse.EventOneofCase.Refueling:
                                break;
                            case StreamEventsResponse.EventOneofCase.Dead:
                                var deadEvent = eventUpdate.Dead;
                                tags = StandardSingleUnitEventTags(tags, deadEvent.Initiator.Unit);
                                _deadCounter.Add(1, tags);
                                break;
                            case StreamEventsResponse.EventOneofCase.PilotDead:
                                var pilotDeadEvent = eventUpdate.PilotDead;
                                tags = StandardSingleUnitEventTags(tags, pilotDeadEvent.Initiator.Unit);
                                _pilotDeadCounter.Add(1, tags);
                                break;
                            case StreamEventsResponse.EventOneofCase.BaseCapture:
                                break;
                            case StreamEventsResponse.EventOneofCase.MissionStart:
                                break;
                            case StreamEventsResponse.EventOneofCase.MissionEnd:
                                break;
                            case StreamEventsResponse.EventOneofCase.RefuelingStop:
                                break;
                            case StreamEventsResponse.EventOneofCase.Birth:
                                var birthEvent = eventUpdate.Birth;
                                if(birthEvent.Initiator.Unit != null) {
                                    tags = StandardSingleUnitEventTags(tags, birthEvent.Initiator.Unit);
                                    _birthCounter.Add(1, tags);
                                }
                                break;
                            case StreamEventsResponse.EventOneofCase.HumanFailure:
                                break;
                            case StreamEventsResponse.EventOneofCase.DetailedFailure:
                                break;
                            case StreamEventsResponse.EventOneofCase.EngineStartup:
                                break;
                            case StreamEventsResponse.EventOneofCase.EngineShutdown:
                                break;
                            case StreamEventsResponse.EventOneofCase.PlayerEnterUnit:
                                break;
                            case StreamEventsResponse.EventOneofCase.PlayerLeaveUnit:
                                break;
                            case StreamEventsResponse.EventOneofCase.ShootingStart:
                                var shootingStartEvent = eventUpdate.ShootingStart;
                                tags.Add(new KeyValuePair<string, object?>(ICollector.SHOOTER_TYPE_LABEL, shootingStartEvent.Initiator.Unit.Type));
                                tags.Add(new KeyValuePair<string, object?>(ICollector.WEAPON_LABEL, shootingStartEvent.WeaponName));
                                _shootingStartCounter.Add(1, tags);
                                break;
                            case StreamEventsResponse.EventOneofCase.ShootingEnd:
                                break;
                            case StreamEventsResponse.EventOneofCase.MarkAdd:
                                break;
                            case StreamEventsResponse.EventOneofCase.MarkChange:
                                break;
                            case StreamEventsResponse.EventOneofCase.MarkRemove:
                                break;
                            case StreamEventsResponse.EventOneofCase.Kill:
                                var killEvent = eventUpdate.Kill;
                                if(killEvent.Initiator.Unit == null) {
                                    _logger.LogWarning("Kill event with no initiator unit");
                                    continue;
                                }
                                if (killEvent.Weapon == null)
                                {
                                    _logger.LogWarning("Kill event with no weapon");
                                    continue;
                                }
                                tags.Add(new KeyValuePair<string, object?>(ICollector.SHOOTER_TYPE_LABEL, killEvent.Initiator.Unit.Type));
                                tags.Add(new KeyValuePair<string, object?>(ICollector.SHOOTER_COALITION_LABEL, killEvent.Initiator.Unit.Coalition));
                                tags.Add(new KeyValuePair<string, object?>(ICollector.SHOOTER_IS_PLAYER_LABEL, killEvent.Initiator.Unit.HasPlayerName));
                                tags.Add(new KeyValuePair<string, object?>(ICollector.SHOOTER_CATEGORY_LABEL, killEvent.Initiator.Unit.Category));
                                if(killEvent.Weapon != null) { 
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.WEAPON_LABEL, killEvent.Weapon.Type));
                                } else {
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.WEAPON_LABEL, killEvent.WeaponName));
                                }
                                if (killEvent.Target.Unit != null)
                                {
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.TARGET_TYPE_LABEL, killEvent.Target.Unit.Type));
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.TARGET_COALITION_LABEL, killEvent.Target.Unit.Coalition));
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.TARGET_IS_PLAYER_LABEL, killEvent.Target.Unit.HasPlayerName));
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.TARGET_CATEGORY_LABEL, killEvent.Target.Unit.Category));
                                }
                                if (killEvent.Target.Static != null)
                                {
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.TARGET_TYPE_LABEL, killEvent.Target.Static.Type));
                                    tags.Add(new KeyValuePair<string, object?>(ICollector.TARGET_COALITION_LABEL, killEvent.Target.Static.Coalition));
                                }
                                else
                                {
                                    _logger.LogWarning("Kill event target was not a unit");
                                }
                                _killCounter.Add(1, tags);
                                break;
                            case StreamEventsResponse.EventOneofCase.Score:
                                break;
                            case StreamEventsResponse.EventOneofCase.UnitLost:
                                var lostEvent = eventUpdate.UnitLost;
                                tags = StandardSingleUnitEventTags(tags, lostEvent.Initiator.Unit);
                                _lostCounter.Add(1, tags);
                                break;
                            case StreamEventsResponse.EventOneofCase.LandingAfterEjection:
                                break;
                            case StreamEventsResponse.EventOneofCase.DiscardChairAfterEjection:
                                break;
                            case StreamEventsResponse.EventOneofCase.WeaponAdd:
                                break;
                            case StreamEventsResponse.EventOneofCase.LandingQualityMark:
                                break;
                            case StreamEventsResponse.EventOneofCase.PlayerSendChat:
                                break;
                            case StreamEventsResponse.EventOneofCase.PlayerChangeSlot:
                                break;
                            case StreamEventsResponse.EventOneofCase.MissionCommand:
                                break;
                            case StreamEventsResponse.EventOneofCase.CoalitionCommand:
                                break;
                            case StreamEventsResponse.EventOneofCase.GroupCommand:
                                break;
                            case StreamEventsResponse.EventOneofCase.Disconnect:
                                var disconnectEvent = eventUpdate.Disconnect;
                                _disconnectCounter.Add(1, tags);
                                break;
                            case StreamEventsResponse.EventOneofCase.Connect:
                                var connectEvent = eventUpdate.Connect;
                                _connectCounter.Add(1, tags);
                                break;
                            case StreamEventsResponse.EventOneofCase.SimulationFps:
                                _serverSimulationFramesPerSecond[serverShortName] = (int) eventUpdate.SimulationFps.Average;
                                break;
                            default:
                                break;
                        }
                    }
                } catch (Exception ex) {
                    if(eventUpdate != null) { 
                        _logger.LogError("Exception processing event stream for {event}: {exception}", eventUpdate, ex.Message);
                    } else
                    {
                        _logger.LogError("Exception processing event stream: {exception}", ex.Message);
                    }
                    continue;
                }
            }
        }

        private Measurement<int> GetSimulationFramesPerSecond(string serverShortName)
        {
            return new Measurement<int>(_serverSimulationFramesPerSecond[serverShortName],
                new KeyValuePair<string, object?>(ICollector.SERVER_SHORT_NAME_LABEL, serverShortName),
                new KeyValuePair<string, object?>(ICollector.SESSION_ID_LABEL, _session.GetSessionId(serverShortName)));
        }

        public static System.Diagnostics.TagList StandardSingleUnitEventTags(System.Diagnostics.TagList tags, Unit unit)
        {
            tags.Add(new KeyValuePair<string, object?>(ICollector.UNIT_TYPE_LABEL, unit.Type));
            tags.Add(new KeyValuePair<string, object?>(ICollector.COALITION_LABEL, unit.Coalition));
            tags.Add(new KeyValuePair<string, object?>(ICollector.IS_PLAYER_LABEL, unit.HasPlayerName));
            tags.Add(new KeyValuePair<string, object?>(ICollector.CATEGORY_LABEL, unit.Category));
            return tags;
        }
    }
}
