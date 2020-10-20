using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices.ComTypes;
using System.Threading.Tasks;
using System.Xml;
using Newtonsoft.Json;
using Npgsql;
using System.Linq;

namespace client
{
    public static class Client
    {

        public static Dictionary<string, AmongUsClient> clients = new Dictionary<string, AmongUsClient>();
        public static string connString = "Host=localhost;Username=postgres;Password=UnityLeaf;Database=postgres";
        public static IPAddress REGION_IP = IPAddress.Parse("198.58.99.71");
        public static Dictionary<int, string> colors = new Dictionary<int, string>
        {
            { 0, "red" },
            { 1, "blue" },
            { 2, "green" },
            { 3, "pink" },
            { 4, "orange" },
            { 5, "yellow" },
            { 6, "black" },
            { 7, "white" },
            { 8, "purple" },
            { 9, "brown" },
            { 10, "cyan" },
            { 11, "lime" }
        };
        public static Dictionary<int, bool> alive = new Dictionary<int, bool>
        {
            { 0, true },
            { 1, false },
            { 2, true },
            { 3, false },
            { 4, false },
        };

        public static void updateRow(int id, UpdateIntention intent, NpgsqlConnection conn, string data = "")
        {
            NpgsqlCommand cmd;

            Console.Out.WriteLine(intent.ToString() + " " + data);
            switch (intent)
            {
                case UpdateIntention.Activated:
                    {
                        cmd = new NpgsqlCommand("update amongus_games set \"is_active\" = :is_active, \"state\" = :state where \"id\" = '" + id + "' ;", conn);
                        cmd.Parameters.Add(new NpgsqlParameter("is_active", NpgsqlTypes.NpgsqlDbType.Boolean));
                        cmd.Parameters.Add(new NpgsqlParameter
                        {
                            ParameterName = "state",
                            Value = States.lobby
                        });
                        cmd.Parameters[0].Value = true;

                        break;
                    }
                case UpdateIntention.Deactivated:
                    {
                        cmd = new NpgsqlCommand("update amongus_games set \"is_active\" = @is_active where \"id\" = '" + id + "' ;", conn);
                        cmd.Parameters.Add(new NpgsqlParameter("is_active", NpgsqlTypes.NpgsqlDbType.Boolean));
                        cmd.Parameters[0].Value = false;

                        break;
                    }
                case UpdateIntention.NewGameData:
                    {
                        cmd = new NpgsqlCommand("update amongus_games set \"playerdata\" = @playerdata where \"id\" = '" + id + "' ;", conn);
                        cmd.Parameters.Add(new NpgsqlParameter("playerdata", NpgsqlTypes.NpgsqlDbType.Text));
                        cmd.Parameters[0].Value = data;

                        break;
                    }
                case UpdateIntention.SetDeliberation:
                    {
                        cmd = new NpgsqlCommand("update amongus_games set \"state\" = @state where \"id\" = '" + id + "' ;", conn);
                        cmd.Parameters.Add(new NpgsqlParameter
                        {
                            ParameterName = "state",
                            Value = States.deliberation
                        });
                        break;
                    }
                case UpdateIntention.SetLobby:
                    {
                        cmd = new NpgsqlCommand("update amongus_games set \"state\" = @state where \"id\" = '" + id + "' ;", conn);
                        cmd.Parameters.Add(new NpgsqlParameter
                        {
                            ParameterName = "state",
                            Value = States.lobby
                        });

                        break;
                    }
                case UpdateIntention.SetPlaying:
                    {
                        cmd = new NpgsqlCommand("update amongus_games set \"state\" = @state where \"id\" = '" + id + "' ;", conn);
                        cmd.Parameters.Add(new NpgsqlParameter
                        {
                            ParameterName = "state",
                            Value = States.playing
                        });

                        break;
                    }
                case UpdateIntention.Error:
                    {
                        cmd = new NpgsqlCommand("update amongus_games set \"last_error\" = @last_error where \"id\" = '" + id + "' ;", conn);
                        cmd.Parameters.Add(new NpgsqlParameter("last_error", NpgsqlTypes.NpgsqlDbType.Text));
                        cmd.Parameters[0].Value = data;

                        break;
                    }
                default:
                    {
                        cmd = new NpgsqlCommand("");
                        break;
                    }
            }
            cmd.ExecuteNonQuery();
        }

        public static string parsePlayerData(List<PlayerData> data)
        {
            var output = new List<ParsedPlayerData>();
            foreach (PlayerData player in data)
            {
                var parsed = new ParsedPlayerData();
                parsed.alive = alive[player.statusBitField];
                parsed.name = player.name;
                parsed.clientId = (int)player.clientId;
                parsed.color = colors[player.color];
                parsed.hatId = player.hatId;
                parsed.petId = player.petId;
                parsed.skinId = player.skinId;
                output.Add(parsed);
            }

            return JsonConvert.SerializeObject(output);
        }

        public static async Task engageNewGame(IPAddress region, string game_code, int id, NpgsqlConnection conn)
        {
            var client = new AmongUsClient();

            clients[game_code] = client;
            clients.Select(i => $"{i.Key}: {i.Value}").ToList().ForEach(Console.Out.WriteLine);

            client.OnConnect += () => updateRow(id, UpdateIntention.Activated, conn);
            client.OnDisconnect += () =>
            {
                updateRow(id, UpdateIntention.Deactivated, conn);
                Environment.Exit(0);
            };
            client.OnTalkingEnd += () => updateRow(id, UpdateIntention.SetPlaying, conn);
            client.OnTalkingStart += () => updateRow(id, UpdateIntention.SetDeliberation, conn);
            client.OnGameEnd += () => updateRow(id, UpdateIntention.SetLobby, conn);
            client.OnPlayerDataUpdate += data => updateRow(id, UpdateIntention.NewGameData, conn, parsePlayerData(data));


            try
            {
                Console.Out.WriteLine("Attempting to connect.");
                await client.Connect(region, game_code);
                Console.Out.WriteLine("Completed.");
            }
            catch (AUException ex)
            {
                WriteMessage(new { type = "error", message = ex.Message });
                return;
            }

            while (true)
            {
                await Task.Delay(30000);
            }
        }

        public static async Task Main(string[] args)
        {
            NpgsqlConnection.GlobalTypeMapper.MapEnum<States>("game_state");

            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            var settings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore
            };

            conn.Notification += (o, e) =>
            {
                Console.Out.WriteLine("Detected notification.");
                SessionRow currentSession = JsonConvert.DeserializeObject<SessionRow>(e.Payload, settings);
                try
                {
                    Console.Out.WriteLine("Trying to engage ");
                    engageNewGame(REGION_IP, currentSession.join_code, currentSession.id, conn);
                    Console.Out.WriteLine("Done I guess?");
                }
                catch (AUException ex)
                {
                    WriteMessage(new { type = "error", message = ex.Message });
                    updateRow(currentSession.id, UpdateIntention.Error, conn, ex.Message);
                    return;
                }
            };

            await using (var cmd = new NpgsqlCommand("LISTEN \"newGameProposed\";", conn))
            {
                Console.Out.WriteLine("Listening.");
                await cmd.ExecuteNonQueryAsync();
                Console.Out.WriteLine("Query execute passed.");
            }


            // Trap ctrlc (SIGINT) to disconnect before terminating.
            Console.CancelKeyPress += (sender, ev) =>
            {
                ev.Cancel = true; // cancel direct shutdown, so we can disconnect and then kill ourselves
                foreach(KeyValuePair<string, AmongUsClient> entry in clients)
                {
                    entry.Value.DisconnectAndExit();
                }
            };

            await conn.WaitAsync();

            while (true)
            {
                Task.Delay(30000);
            }
        }

        private static void WriteMessage(object obj)
        {
            Console.Out.WriteLine(JsonConvert.SerializeObject(obj));
            Console.Out.Flush();
        }
    }

    public class SessionRow
    {
        public bool is_active { get; set; }
        public string last_error { get; set; }
        public string state { get; set; }

        public string playerdata { get; set; }
        public string join_code { get; set; }
        public int id { get; set; }
    }

    public class ParsedPlayerData
    {
        public bool alive { get; set; }
        public string name { get; set; }
        public int clientId { get; set; }
        public string color { get; set; }
        public int hatId { get; set; }
        public int petId { get; set; }

        public int skinId { get; set; }
    }

    public enum UpdateIntention
    {
        Activated,
        NewGameData,
        Deactivated,
        SetPlaying,
        SetLobby,
        SetDeliberation,
        Error
    }

    public enum States
    {
        deliberation,
        lobby,
        playing
    }
}