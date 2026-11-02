using System;
using System.Collections.Generic;
using Survival.Core;
using Survival.Networking;
using UnityEngine;
using UnityEngine.Networking;

namespace Survival.Client
{
    public enum ServerSource
    {
        BuiltIn,
        Remote,
        Custom,
        Lan,
    }

    /// <summary>One row of the server browser.</summary>
    public sealed class ServerEntry
    {
        public string Name;
        public string Host;
        public int Port = NetProtocol.DefaultPort;
        public string Region = "Asia";
        public string Map = "Demo Island";
        public ServerSource Source;
        public ServerQueryResult Result;
        public bool Querying;
        public float LastQueryTime = -100f;

        public string Key => Host + ":" + Port;
        public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
    }

    [Serializable]
    internal sealed class ClientConfigJson
    {
        public string serverListUrl;
        public ServerJson[] servers;
    }

    [Serializable]
    internal sealed class ServerListJson
    {
        public ServerJson[] servers;
    }

    [Serializable]
    internal sealed class ServerJson
    {
        public string name;
        public string host;
        public int port;
        public string region;
        public string map;
    }

    /// <summary>
    /// Server list: built-in entries (Resources/client_config.json), an optional remote list
    /// (config/serverlist.json in the GitHub repository, so the server address can be changed from a
    /// phone without rebuilding the APK), servers added by hand, and servers found on the LAN.
    /// Status, player count and ping come from real UDP queries to each server.
    /// </summary>
    public sealed class ServerDirectory : IDisposable
    {
        private readonly List<ServerEntry> _entries = new List<ServerEntry>();
        private ServerQueryClient _query;
        private UnityWebRequest _remote;
        private string _remoteUrl;

        public IReadOnlyList<ServerEntry> Entries => _entries;
        public string RemoteStatus { get; private set; } = string.Empty;
        public event Action Changed;

        public ServerDirectory()
        {
            LoadBuiltIn();
            foreach (string custom in App.Settings.CustomServers) AddCustom(custom, false);
            FetchRemote();
        }

        private ServerQueryClient Query => _query ?? (_query = new ServerQueryClient());

        private void LoadBuiltIn()
        {
            var cfgAsset = Resources.Load<TextAsset>("client_config");
            if (cfgAsset == null) return;
            try
            {
                var cfg = JsonUtility.FromJson<ClientConfigJson>(cfgAsset.text);
                _remoteUrl = cfg.serverListUrl;
                if (cfg.servers != null)
                    foreach (ServerJson s in cfg.servers) Upsert(s, ServerSource.BuiltIn);
            }
            catch (Exception e)
            {
                Log.Warn("Servers", "client_config.json invalid: " + e.Message);
            }
        }

        private void Upsert(ServerJson s, ServerSource source)
        {
            if (s == null || string.IsNullOrEmpty(s.name)) return;
            ServerEntry existing = _entries.Find(e => e.Name == s.name && (e.Source == ServerSource.BuiltIn || e.Source == ServerSource.Remote));
            if (existing == null)
            {
                existing = new ServerEntry { Name = s.name, Source = source };
                _entries.Add(existing);
            }
            if (!string.IsNullOrWhiteSpace(s.host) || source == ServerSource.BuiltIn) existing.Host = (s.host ?? string.Empty).Trim();
            existing.Port = s.port > 0 ? s.port : NetProtocol.DefaultPort;
            if (!string.IsNullOrEmpty(s.region)) existing.Region = s.region;
            if (!string.IsNullOrEmpty(s.map)) existing.Map = s.map;
            if (source == ServerSource.Remote) existing.Source = ServerSource.Remote;
        }

        public void FetchRemote()
        {
            if (string.IsNullOrEmpty(_remoteUrl) || _remote != null) return;
            _remote = UnityWebRequest.Get(_remoteUrl + (_remoteUrl.Contains("?") ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks);
            _remote.timeout = 8;
            _remote.SendWebRequest();
            RemoteStatus = "fetching";
        }

        /// <summary>Adds "host" or "host:port". Returns the entry or null when invalid.</summary>
        public ServerEntry AddCustom(string address, bool persist = true)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;
            address = address.Trim();
            string host = address;
            int port = NetProtocol.DefaultPort;
            int colon = address.LastIndexOf(':');
            if (colon > 0 && int.TryParse(address.Substring(colon + 1), out int p) && p > 0 && p < 65536)
            {
                host = address.Substring(0, colon);
                port = p;
            }
            ServerEntry e = _entries.Find(x => x.Host == host && x.Port == port);
            if (e != null) return e;
            e = new ServerEntry { Name = host + ":" + port, Host = host, Port = port, Region = "?", Map = "?", Source = ServerSource.Custom };
            _entries.Add(e);
            if (persist && !App.Settings.CustomServers.Contains(address))
            {
                App.Settings.CustomServers.Add(address);
                App.Settings.Save();
            }
            Changed?.Invoke();
            return e;
        }

        public void Remove(ServerEntry e)
        {
            if (e.Source != ServerSource.Custom && e.Source != ServerSource.Lan) return;
            _entries.Remove(e);
            App.Settings.CustomServers.Remove(e.Host + ":" + e.Port);
            App.Settings.CustomServers.Remove(e.Host);
            App.Settings.Save();
            Changed?.Invoke();
        }

        public void RefreshAll()
        {
            foreach (ServerEntry e in _entries) Refresh(e);
        }

        public void Refresh(ServerEntry e)
        {
            if (!e.IsConfigured || e.Querying) return;
            e.Querying = true;
            e.LastQueryTime = Time.unscaledTime;
            Query.Query(e.Host, e.Port, r =>
            {
                e.Querying = false;
                e.Result = r;
                if (r.Status == QueryStatus.Online && e.Source != ServerSource.BuiltIn && e.Source != ServerSource.Remote)
                {
                    e.Name = r.Info.Name;
                    e.Region = r.Info.Region;
                    e.Map = r.Info.MapName;
                }
                Changed?.Invoke();
            });
            Changed?.Invoke();
        }

        public void DiscoverLan()
        {
            Query.DiscoverLan(NetProtocol.DefaultPort, 3.0, (endPoint, info, ping) =>
            {
                string host = endPoint.Address.ToString();
                ServerEntry e = _entries.Find(x => x.Host == host && x.Port == endPoint.Port);
                if (e == null)
                {
                    e = new ServerEntry { Host = host, Port = endPoint.Port, Source = ServerSource.Lan };
                    _entries.Add(e);
                }
                e.Name = info.Name + " (LAN)";
                e.Region = "LAN";
                e.Map = info.MapName;
                e.Result = new ServerQueryResult { Host = host, Port = endPoint.Port, Status = QueryStatus.Online, Info = info, PingMs = ping };
                Changed?.Invoke();
            });
        }

        public ServerEntry Find(string key) => _entries.Find(e => e.Key == key);

        public void Update()
        {
            _query?.Update();
            if (_remote != null && _remote.isDone)
            {
                if (_remote.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var list = JsonUtility.FromJson<ServerListJson>(_remote.downloadHandler.text);
                        if (list?.servers != null)
                        {
                            foreach (ServerJson s in list.servers) Upsert(s, ServerSource.Remote);
                            RemoteStatus = "ok";
                            RefreshAll();
                        }
                    }
                    catch (Exception e)
                    {
                        RemoteStatus = "invalid";
                        Log.Warn("Servers", "Remote server list invalid: " + e.Message);
                    }
                }
                else
                {
                    RemoteStatus = "unavailable";
                    Log.Info("Servers", "Remote server list unavailable (" + _remote.error + "), using built-in list");
                }
                _remote.Dispose();
                _remote = null;
                Changed?.Invoke();
            }
        }

        public void Dispose()
        {
            _query?.Dispose();
            _query = null;
            _remote?.Dispose();
        }
    }
}
