using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVPingMachina.FFXIVNetwork.Packets;
using LibPingMachina.PingMonitor;

namespace FFXIVPingMachina.PingMonitor
{
    public delegate void ConnectionPingSampleDelegate(ConnectionPing ping);

    public class PacketMonitor
    {
        public static FFXIVClientVersion ClientVersion { get; set; } = FFXIVClientVersion.Unknown;

        public event ConnectionPingSampleDelegate OnPingSample;
        public ConnectionPing CurrentPing { get; private set; }

        private readonly Dictionary<string, PerConnectionMonitor> _connections =
            new Dictionary<string, PerConnectionMonitor>();

        public void MessageSent(string connection, long epoch, byte[] message)
        {
            if (!_connections.TryGetValue(connection, out var monitor))
            {
                monitor = new PerConnectionMonitor(connection);
                monitor.OnPingSample += MonitorOnOnPingSample;
                _connections[connection] = monitor;
            }

            try
            {
                monitor.MessageSent(epoch, message);
            }
            catch (ParseException ex)
            {
                Console.Out.WriteLine(ex.ToString());
            }
            finally
            {
                CheckActivity();
            }
        }

        public void MessageReceived(string connection, long epoch, byte[] message)
        {
            if (!_connections.TryGetValue(connection, out var monitor))
            {
                monitor = new PerConnectionMonitor(connection);
                monitor.OnPingSample += MonitorOnOnPingSample;
                _connections[connection] = monitor;
            }

            try
            {
                monitor.MessageReceived(epoch, message);
            }
            catch (ParseException ex)
            {
                Console.Out.WriteLine(ex.ToString());
            }
            finally
            {
                CheckActivity();
            }
        }

        private void CheckActivity()
        {
            var now = DateTime.UtcNow;
            _connections.Where(it => now.Subtract(it.Value.LastActivity).TotalMinutes > 2)
                .Select(it => it.Key).ToList().ForEach(k => _connections.Remove(k));
        }

        private void MonitorOnOnPingSample(ConnectionPing ping)
        {
            CurrentPing = _connections.Select(it => it.Value.CurrentPing).Max();
            OnPingSample?.Invoke(CurrentPing);
        }
    }
}
