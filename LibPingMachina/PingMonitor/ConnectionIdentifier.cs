using System;
using System.Net;
using Machina.Infrastructure;

namespace LibPingMachina.PingMonitor
{
    public class ConnectionIdentifier
    {
        public string LocalIP { get; }
        public ushort LocalPort { get; }

        public string RemoteIP { get; }
        public ushort RemotePort { get; }

        private readonly string _connection;

        /// <param name="connection">Format: "192.168.1.165:123=>116.211.8.5:456"</param>
        public ConnectionIdentifier(string connection)
        {
            _connection = connection;

            try
            {
                var parts = connection.Split(new[] {"=>"}, StringSplitOptions.RemoveEmptyEntries);
                var local = parts[0].Split(':');
                var remote = parts[1].Split(':');

                LocalIP = local[0];
                RemoteIP = remote[0];

                LocalPort = ushort.Parse(local[1]);
                RemotePort = ushort.Parse(remote[1]);
            }
            catch (Exception)
            {
                LocalIP = "Unknown";
                LocalPort = 0;
                RemoteIP = "Unknown";
                RemotePort = 0;
            }
        }

        public override bool Equals(Object obj)
        {
            var c = obj as ConnectionIdentifier;
            if (c == null)
                return false;
            return _connection == c._connection;
        }

        public override int GetHashCode()
        {
            return _connection.GetHashCode();
        }

        public override string ToString()
        {
            return _connection;
        }

        internal static string GetStringIdentifier(TCPConnection connection)
        {
            return $"{new IPAddress(connection.LocalIP)}:{connection.LocalPort}=>" +
                   $"{new IPAddress(connection.RemoteIP)}:{connection.RemotePort}";
        }
    }
}
