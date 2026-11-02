using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Utils;
using Survival.Core;

namespace Survival.Networking
{
    public enum QueryStatus
    {
        Pending,
        Online,
        Offline,
        DnsFailed,
    }

    public sealed class ServerQueryResult
    {
        public string Host;
        public int Port;
        public QueryStatus Status = QueryStatus.Pending;
        public ServerInfo Info;
        /// <summary>Measured UDP round trip in milliseconds (-1 when unknown).</summary>
        public int PingMs = -1;
        public string Error;

        public bool IsFull => Status == QueryStatus.Online && Info.MaxPlayers > 0 && Info.Players >= Info.MaxPlayers;
        public bool IsEmpty => Status == QueryStatus.Online && Info.Players == 0;
        public bool VersionMismatch => Status == QueryStatus.Online && Info.ProtocolVersion != NetProtocol.Version;
    }

    /// <summary>
    /// Queries dedicated servers over unconnected UDP (no connection, no player slot used) and
    /// measures the real round-trip time. Also discovers servers on the local Wi-Fi via broadcast.
    /// Call <see cref="Update"/> every frame from one thread.
    /// </summary>
    public sealed class ServerQueryClient : IDisposable
    {
        private sealed class Pending
        {
            public ServerQueryResult Result;
            public Action<ServerQueryResult> Callback;
            public Task<IPAddress[]> Dns;
            public IPEndPoint EndPoint;
            public double StartTime;
            public double NextSend;
            public int Sends;
            public double Timeout;
        }

        private readonly NetManager _net;
        private readonly EventBasedNetListener _listener = new EventBasedNetListener();
        private readonly List<Pending> _pending = new List<Pending>();
        private readonly NetDataWriter _writer = new NetDataWriter();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private Action<IPEndPoint, ServerInfo, int> _lanCallback;
        private double _lanUntil;

        public ServerQueryClient()
        {
            _listener.NetworkReceiveUnconnectedEvent += OnUnconnected;
            _net = new NetManager(_listener)
            {
                UnconnectedMessagesEnabled = true,
                BroadcastReceiveEnabled = true,
                IPv6Enabled = false,
            };
            if (!_net.Start())
            {
                Log.Warn("Query", "Could not open UDP socket for server queries");
            }
        }

        private double Now => _clock.Elapsed.TotalSeconds;

        /// <summary>Starts a status query. The callback fires exactly once (online or offline).</summary>
        public void Query(string host, int port, Action<ServerQueryResult> callback, double timeoutSeconds = 3.0)
        {
            var p = new Pending
            {
                Result = new ServerQueryResult { Host = host, Port = port },
                Callback = callback,
                StartTime = Now,
                Timeout = timeoutSeconds,
            };

            if (IPAddress.TryParse(host, out IPAddress ip))
            {
                p.EndPoint = new IPEndPoint(ip, port);
            }
            else
            {
                try
                {
                    p.Dns = System.Net.Dns.GetHostAddressesAsync(host);
                }
                catch (Exception e)
                {
                    p.Result.Status = QueryStatus.DnsFailed;
                    p.Result.Error = e.Message;
                    callback?.Invoke(p.Result);
                    return;
                }
            }
            _pending.Add(p);
        }

        /// <summary>Broadcasts a query on the local network for <paramref name="seconds"/>.</summary>
        public void DiscoverLan(int port, double seconds, Action<IPEndPoint, ServerInfo, int> onFound)
        {
            _lanCallback = onFound;
            _lanUntil = Now + seconds;
            _writer.Reset();
            ServerInfo.WriteRequest(_writer, _clock.ElapsedTicks);
            _net.SendBroadcast(_writer, port);
        }

        public void Update()
        {
            _net.PollEvents();
            double now = Now;

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Pending p = _pending[i];
                if (p.EndPoint == null && p.Dns != null)
                {
                    if (!p.Dns.IsCompleted)
                    {
                        if (now - p.StartTime > p.Timeout) Finish(i, QueryStatus.DnsFailed, "DNS timeout");
                        continue;
                    }
                    IPAddress addr = null;
                    if (p.Dns.Status == TaskStatus.RanToCompletion && p.Dns.Result != null)
                    {
                        foreach (IPAddress a in p.Dns.Result)
                        {
                            if (a.AddressFamily == AddressFamily.InterNetwork)
                            {
                                addr = a;
                                break;
                            }
                        }
                    }
                    if (addr == null)
                    {
                        Finish(i, QueryStatus.DnsFailed, "Host not found");
                        continue;
                    }
                    p.EndPoint = new IPEndPoint(addr, p.Result.Port);
                }

                if (p.EndPoint != null && now >= p.NextSend && p.Sends < 4)
                {
                    _writer.Reset();
                    ServerInfo.WriteRequest(_writer, _clock.ElapsedTicks);
                    _net.SendUnconnectedMessage(_writer, p.EndPoint);
                    p.Sends++;
                    p.NextSend = now + 0.6;
                }

                if (now - p.StartTime > p.Timeout) Finish(i, QueryStatus.Offline, "No response");
            }

            if (_lanCallback != null && now > _lanUntil) _lanCallback = null;
        }

        private void Finish(int index, QueryStatus status, string error)
        {
            Pending p = _pending[index];
            _pending.RemoveAt(index);
            p.Result.Status = status;
            p.Result.Error = error;
            try
            {
                p.Callback?.Invoke(p.Result);
            }
            catch (Exception e)
            {
                Log.Exception("Query", e);
            }
        }

        private void OnUnconnected(IPEndPoint remote, NetPacketReader reader, UnconnectedMessageType type)
        {
            try
            {
                if (!ServerInfo.TryReadResponse(reader, out ServerInfo info, out long echo)) return;
                long elapsedTicks = _clock.ElapsedTicks - echo;
                int ping = (int)Math.Max(0, elapsedTicks * 1000L / Stopwatch.Frequency);

                bool matched = false;
                for (int i = _pending.Count - 1; i >= 0; i--)
                {
                    Pending p = _pending[i];
                    if (p.EndPoint != null && SameEndPoint(p.EndPoint, remote))
                    {
                        p.Result.Info = info;
                        p.Result.PingMs = ping;
                        matched = true;
                        Finish(i, QueryStatus.Online, null);
                    }
                }

                if (!matched && _lanCallback != null)
                {
                    _lanCallback(remote, info, ping);
                }
            }
            finally
            {
                reader.Recycle();
            }
        }

        private static bool SameEndPoint(IPEndPoint a, IPEndPoint b)
        {
            if (a.Port != b.Port) return false;
            IPAddress x = a.Address.IsIPv4MappedToIPv6 ? a.Address.MapToIPv4() : a.Address;
            IPAddress y = b.Address.IsIPv4MappedToIPv6 ? b.Address.MapToIPv4() : b.Address;
            return x.Equals(y);
        }

        public void Dispose()
        {
            _net.Stop();
        }
    }
}
