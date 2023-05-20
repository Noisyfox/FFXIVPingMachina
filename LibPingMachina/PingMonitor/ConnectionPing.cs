using System;

namespace LibPingMachina.PingMonitor
{
    public class ConnectionPing: IComparable<ConnectionPing>
    {
        public ConnectionIdentifier Connection { get; set; }

        public double Ping { get; set; }

        /// <summary>
        /// Time when this Ping was sampled, in UTC
        /// </summary>
        public DateTime SampleTime { get; set; }

        public int CompareTo(ConnectionPing other)
        {
            return Ping.CompareTo(other.Ping);
        }
    }
}
