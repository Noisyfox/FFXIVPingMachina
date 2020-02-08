using System;

namespace LibPingMachina.PingMonitor
{
    public class ConnectionPing: IComparable<ConnectionPing>
    {
        public ConnectionIdentifier Connection { get; set; }

        public double Ping { get; set; }

        public DateTime SampleTime { get; set; }

        public int CompareTo(ConnectionPing other)
        {
            return Ping.CompareTo(other.Ping);
        }
    }
}
