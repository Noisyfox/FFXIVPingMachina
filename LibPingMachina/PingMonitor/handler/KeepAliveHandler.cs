using System;
using FFXIVPingMachina.FFXIVNetwork.Packets;

namespace FFXIVPingMachina.PingMonitor.handler
{
    public class KeepAliveHandler
    {
        public event PingSampleDelegate OnPingSample;

        private uint _currentId;
        private DateTime _lastKeepAliveSent = DateTime.Now;

        public void ClientSent(byte[] data, int offset)
        {
            //            Console.Out.WriteLine($"KeepAliveHandler.ClientSent: {Utility.ByteArrayToHexString(data, offset)}.");
            Packets.NaiveParsePacket<FFXIVKeepAliveData>(data, offset, out var pkt);
            //            Console.Out.WriteLine($"KeepAliveHandler.ClientSent: ID={pkt.Id}, Timestamp={pkt.Timestamp}.");

            _currentId = pkt.Id;
            _lastKeepAliveSent = DateTime.UtcNow;
        }

        public void ClientRecv(byte[] data, int offset)
        {
            //            Console.Out.WriteLine($"KeepAliveHandler.ClientRecv: {Utility.ByteArrayToHexString(data, offset)}.");
            Packets.NaiveParsePacket<FFXIVKeepAliveData>(data, offset, out var pkt);
            //            Console.Out.WriteLine($"KeepAliveHandler.ClientRecv: ID={pkt.Id}, Timestamp={pkt.Timestamp}.");

            if (pkt.Id != _currentId)
            {
                return;
            }
            var now = DateTime.UtcNow;
            var millis = (now - _lastKeepAliveSent).TotalMilliseconds;
            OnPingSample?.Invoke(millis, now);
        }
    }
}
