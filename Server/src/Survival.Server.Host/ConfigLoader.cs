using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Survival.Server.Host
{
    /// <summary>
    /// Builds a <see cref="ServerConfig"/> from server.json, OROL_* environment variables and
    /// command line arguments. Keys are the camelCase field names of ServerConfig
    /// (e.g. "botCount", env OROL_BOT_COUNT, arg --botCount 75).
    /// </summary>
    public static class ConfigLoader
    {
        public const string HelpText =
@"orol-server [--config path/server.json] [--key value ...] [--no-console]

Common keys (JSON / --arg / OROL_ENV):
  serverName        DEMO SERVER #1         OROL_SERVER_NAME
  region            Asia                   OROL_REGION
  port              7777 (UDP)             OROL_PORT
  maxPlayers        100                    OROL_MAX_PLAYERS
  botCount          70  (60-90 for demo)   OROL_BOT_COUNT
  tickRate          30                     OROL_TICK_RATE
  worldSeed         20261102               OROL_WORLD_SEED
  worldSize         5000                   OROL_WORLD_SIZE
  resourceDensity   1.0                    OROL_RESOURCE_DENSITY
  animalDensity     1.0                    OROL_ANIMAL_DENSITY
  eventFrequency    1.0                    OROL_EVENT_FREQUENCY
  interestRadius    320                    OROL_INTEREST_RADIUS
  dataDirectory     data                   OROL_DATA_DIRECTORY
  statusHttpPort    8080 (TCP, 0 = off)    OROL_STATUS_HTTP_PORT
  adminToken        (empty = admin off)    OROL_ADMIN_TOKEN

Console commands: help, status, players, bots <n>, kick <name>, say <text>, save, time <0..1>, config, quit";

        public static bool HasFlag(string[] args, string flag)
        {
            foreach (string a in args) if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static ServerConfig Load(string[] args, out string source)
        {
            var cfg = new ServerConfig();
            var sources = new List<string> { "defaults" };

            string path = GetArg(args, "--config") ?? Environment.GetEnvironmentVariable("OROL_CONFIG");
            if (path == null)
            {
                string local = Path.Combine(AppContext.BaseDirectory, "server.json");
                if (File.Exists("server.json")) path = "server.json";
                else if (File.Exists(local)) path = local;
            }
            if (path != null)
            {
                if (!File.Exists(path)) throw new FileNotFoundException("Config file not found", path);
                using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }))
                {
                    foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                    {
                        string value = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText();
                        Apply(cfg, p.Name, value, path);
                    }
                }
                sources.Add(path);
            }

            foreach (FieldInfo f in typeof(ServerConfig).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                string env = Environment.GetEnvironmentVariable("OROL_" + ToSnake(f.Name));
                if (env != null)
                {
                    Apply(cfg, f.Name, env, "env");
                    if (!sources.Contains("env")) sources.Add("env");
                }
            }

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (!a.StartsWith("--", StringComparison.Ordinal) || a == "--config" || a == "--no-console" || a == "--help") { if (a == "--config") i++; continue; }
                if (i + 1 >= args.Length) throw new ArgumentException("Missing value for " + a);
                Apply(cfg, a.Substring(2), args[++i], "args");
                if (!sources.Contains("args")) sources.Add("args");
            }

            source = string.Join(" < ", sources);
            return cfg;
        }

        private static string GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        private static void Apply(ServerConfig cfg, string key, string value, string origin)
        {
            FieldInfo field = null;
            foreach (FieldInfo f in typeof(ServerConfig).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (string.Equals(f.Name, key, StringComparison.OrdinalIgnoreCase)) { field = f; break; }
            }
            if (field == null)
            {
                Survival.Core.Log.Warn("Config", $"Unknown key '{key}' in {origin} (ignored)");
                return;
            }
            try
            {
                object v;
                Type t = field.FieldType;
                if (t == typeof(string)) v = value;
                else if (t == typeof(int)) v = int.Parse(value, CultureInfo.InvariantCulture);
                else if (t == typeof(float)) v = float.Parse(value, CultureInfo.InvariantCulture);
                else if (t == typeof(bool)) v = bool.Parse(value);
                else throw new NotSupportedException(t.Name);
                field.SetValue(cfg, v);
            }
            catch (Exception e)
            {
                throw new ArgumentException($"Bad value '{value}' for '{key}' in {origin}: {e.Message}");
            }
        }

        private static string ToSnake(string camel)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < camel.Length; i++)
            {
                char c = camel[i];
                if (char.IsUpper(c) && i > 0) sb.Append('_');
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }
    }
}
