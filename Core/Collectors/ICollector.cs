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

namespace RurouniJones.Telemachus.Core.Collectors
{
    public interface ICollector
    {
        public const string SERVER_SHORT_NAME_LABEL = "server_short_name";
        public const string AIRCRAFT_TYPE_LABEL = "aircraft_type";
        public const string COALITION_LABEL = "coalition";
        public const string IS_PLAYER_LABEL = "is_player";
        public const string CATEGORY_LABEL = "category";
        public const string WEAPON_LABEL = "weapon";
        public const string AIRBASE_LABEL = "airbase";
        public const string AIRBASE_CATEGORY_LABEL = "airbase_category";
        public const string SHOOTER_TYPE_LABEL = "shooter_type";
        public const string SHOOTER_COALITION_LABEL = "shooter_coalition";
        public const string SHOOTER_IS_PLAYER_LABEL = "shooter_is_player";
        public const string SHOOTER_CATEGORY_LABEL = "shooter_category";
        public const string TARGET_TYPE_LABEL = "target_type";
        public const string TARGET_IS_PLAYER_LABEL = "target_is_player";
        public const string TARGET_CATEGORY_LABEL = "target_category";
        public const string TARGET_COALITION_LABEL = "target_coalition";
        public const string UNIT_TYPE_LABEL = "unit_type";
        public const string STATIC_TYPE_LABEL = "static_type";


        public void Execute(Dictionary<string, GrpcChannel> gameServerChannels, CancellationToken stoppingToken);
    }
}
