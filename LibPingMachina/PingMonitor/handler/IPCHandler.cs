using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVPingMachina.FFXIVNetwork.Packets;
using LibPingMachina.PingMonitor.handler;

namespace FFXIVPingMachina.PingMonitor.handler
{
    public class IPCHandler
    {
        public delegate void IPCDelegate(FFXIVIpcHeader header, byte[] data, int offset);

        public event PerConnectionMonitor.PingSampleDelegate OnPingSample;
        public event IPCDelegate OnClientSent;
        public event IPCDelegate OnClientRecv;

        private readonly SortedDictionary<uint, DateTime> _pingRecords = new SortedDictionary<uint, DateTime>();
        private DateTime _pingLastUpdate = DateTime.UtcNow;

        public IPCPingOpCodeDetector.PingOpCode PingOpCode = new IPCPingOpCodeDetector.PingOpCode(0, 0);

        public void ClientSent(byte[] data, int offset)
        {
            //            Console.Out.WriteLine($"IPCHandler.ClientSent: {Utility.ByteArrayToHexString(data, offset)}.");
            var headerLen = Packets.ParseIPCHeader(data, offset, out var pkt);
            //            Console.Out.WriteLine($"FFXIVIpcHeader.Type = 0x{pkt.Type:X4}.");

            OnClientSent?.Invoke(pkt, data, offset + headerLen);
            if (pkt.Type == PingOpCode.Client)
            {
                HandleClientPing(data, offset + headerLen);
            }
        }

        public void ClientRecv(byte[] data, int offset)
        {
            //            Console.Out.WriteLine($"IPCHandler.ClientRecv: {Utility.ByteArrayToHexString(data, offset)}.");
            var headerLen = Packets.ParseIPCHeader(data, offset, out var pkt);
            //            Console.Out.WriteLine($"FFXIVIpcHeader.Type = 0x{pkt.Type:X4}.");

            OnClientRecv?.Invoke(pkt, data, offset + headerLen);
            if (pkt.Type == PingOpCode.Server)
            {
                HandleServerPing(data, offset + headerLen);
            }
        }

        private void HandleClientPing(byte[] data, int offset)
        {
            //            Console.Out.WriteLine($"Ping sent: {Utility.ByteArrayToHexString(data, offset)}.");
            Packets.NaiveParsePacket<FFXIVClientIpcPingData>(data, offset, out var pkt);
            //            Console.Out.WriteLine($"HandleClientPing: Timestamp={pkt.Timestamp}.");

            _pingRecords[pkt.Timestamp] = DateTime.UtcNow;
        }

        private void HandleServerPing(byte[] data, int offset)
        {
            //            Console.Out.WriteLine($"Pong recv: {Utility.ByteArrayToHexString(data, offset)}.");
            Packets.NaiveParsePacket<FFXIVServerIpcPingData>(data, offset, out var pkt);
            //            Console.Out.WriteLine($"HandleServerPing: Timestamp={pkt.Timestamp - 0x000014D00000000}.");

            var index = (uint) (pkt.Timestamp - TIMESTAMP_DELTA);

            if (_pingRecords.TryGetValue(index, out var time))
            {
                var now = DateTime.UtcNow;
                var millis = (now - time).TotalMilliseconds;
                OnPingSample?.Invoke(millis, now);
                _pingRecords.Remove(index);
            }
            _pingRecords.Keys.Where(it => it < index).ToList().ForEach(it => _pingRecords.Remove(it));
        }

        /// <summary>
        /// Difference of ServerPing.TimeStamp - ClientPing.TimeStamp
        /// </summary>
        public const ulong TIMESTAMP_DELTA = 0x000014D00000000ul;
    }
}
