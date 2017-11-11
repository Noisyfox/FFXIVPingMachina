using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FFXIVPingMachina.FFXIVNetwork;
using FFXIVPingMachina.FFXIVNetwork.Packets;
using Machina;
using Machina.FFXIV;

namespace FFXIVPingMachina
{
    class Program
    {
        static void Main(string[] args)
        {
            var pm = new PacketMonitor();

            var monitor = new FFXIVNetworkMonitor();
            monitor.MessageReceived = pm.MessageReceived;
            monitor.MessageSent = pm.MessageSent;
            monitor.Start();


            Console.WriteLine("Press any key to stop...");
            Console.ReadKey();

            monitor.Stop();
        }
    }

    public class PacketMonitor
    {
        private readonly Dictionary<string, PerConnectionMonitor> _connections = new Dictionary<string, PerConnectionMonitor>();

        public void MessageSent(string connection, long epoch, byte[] message)
        {
            if (!_connections.TryGetValue(connection, out var monitor))
            {
                monitor = new PerConnectionMonitor();
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
        }

        public void MessageReceived(string connection, long epoch, byte[] message)
        {
            if (!_connections.TryGetValue(connection, out var monitor))
            {
                monitor = new PerConnectionMonitor();
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
        }
    }

    public class PerConnectionMonitor
    {
        public DateTime LastActivity { get; private set; }
        private readonly KeepAliveHandler _keepAliveHandler = new KeepAliveHandler();
        private readonly IPCHandler _ipcHandler = new IPCHandler();

        public void MessageSent(long epoch, byte[] message)
        {
            LastActivity = DateTime.Now;
//           Console.Out.WriteLine("MessageSent");

            var headerLen = Packets.ParseSegmentHeader(message, 0, out var segHdr);
//           Console.Out.WriteLine($"segHdr.SegmentType = 0x{segHdr.SegmentType:X4}.");

            switch ((ClientSegmentType) segHdr.SegmentType)
            {
                case ClientSegmentType.KeepAlive:
                    _keepAliveHandler.ClientSent(message, headerLen);
                    break;
                case ClientSegmentType.IPC:
                    _ipcHandler.ClientSent(message, headerLen);
                    break;
            }
        }

        public void MessageReceived(long epoch, byte[] message)
        {
            LastActivity = DateTime.Now;
//           Console.Out.WriteLine("MessageReceived");

            var headerLen = Packets.ParseSegmentHeader(message, 0, out var segHdr);
//           Console.Out.WriteLine($"segHdr.SegmentType = 0x{segHdr.SegmentType:X4}.");

            switch ((ServerSegmentType) segHdr.SegmentType)
            {
                case ServerSegmentType.KeepAlive:
                    _keepAliveHandler.ClientRecv(message, headerLen);
                    break;
                case ServerSegmentType.IPC:
                    _ipcHandler.ClientRecv(message, headerLen);
                    break;
            }
        }
    }

    #region Handlers

    public class KeepAliveHandler
    {
        private uint _currentId;
        private DateTime _lastKeepAliveSent = DateTime.Now;

        public void ClientSent(byte[] data, int offset)
        {
//            Console.Out.WriteLine($"KeepAliveHandler.ClientSent: {Utility.ByteArrayToHexString(data, offset)}.");
            Packets.NaiveParsePacket<FFXIVKeepAliveData>(data, offset, out var pkt);
//            Console.Out.WriteLine($"KeepAliveHandler.ClientSent: ID={pkt.Id}, Timestamp={pkt.Timestamp}.");

            _currentId = pkt.Id;
            _lastKeepAliveSent = DateTime.Now;
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
            var millis = (DateTime.Now - _lastKeepAliveSent).TotalMilliseconds;

            Console.Out.WriteLine($"TTL by KeepAlive={millis}.");
        }
    }

    public class IPCHandler
    {
        private readonly SortedDictionary<uint, DateTime> _pingRecords = new SortedDictionary<uint, DateTime>();
        private DateTime _pingLastUpdate = DateTime.Now;

        public void ClientSent(byte[] data, int offset)
        {
//            Console.Out.WriteLine($"IPCHandler.ClientSent: {Utility.ByteArrayToHexString(data, offset)}.");
            var headerLen = Packets.ParseIPCHeader(data, offset, out var pkt);
//            Console.Out.WriteLine($"FFXIVIpcHeader.Type = 0x{pkt.Type:X4}.");

            switch ((ClientZoneIpcType) pkt.Type)
            {
                case ClientZoneIpcType.PingHandler:
                    HandleClientPing(data, offset + headerLen);
                    break;
            }
        }

        public void ClientRecv(byte[] data, int offset)
        {
//            Console.Out.WriteLine($"IPCHandler.ClientRecv: {Utility.ByteArrayToHexString(data, offset)}.");
            var headerLen = Packets.ParseIPCHeader(data, offset, out var pkt);
//            Console.Out.WriteLine($"FFXIVIpcHeader.Type = 0x{pkt.Type:X4}.");

            switch ((ServerZoneIpcType)pkt.Type)
            {
                case ServerZoneIpcType.Ping:
                    HandleServerPing(data, offset + headerLen);
                    break;
            }
        }

        private void HandleClientPing(byte[] data, int offset)
        {
//            Console.Out.WriteLine($"Ping sent: {Utility.ByteArrayToHexString(data, offset)}.");
            Packets.NaiveParsePacket<FFXIVClientIpcPingData>(data, offset, out var pkt);
//            Console.Out.WriteLine($"HandleClientPing: Timestamp={pkt.Timestamp}.");

            _pingRecords[pkt.Timestamp] = DateTime.Now;
        }

        private void HandleServerPing(byte[] data, int offset)
        {
//            Console.Out.WriteLine($"Pong recv: {Utility.ByteArrayToHexString(data, offset)}.");
            Packets.NaiveParsePacket<FFXIVServerIpcPingData>(data, offset, out var pkt);
//            Console.Out.WriteLine($"HandleServerPing: Timestamp={pkt.Timestamp - 0x000014D00000000}.");

            var index = (uint) (pkt.Timestamp - 0x000014D00000000);

            if (_pingRecords.TryGetValue(index, out var time))
            {
                var millis = (DateTime.Now - time).TotalMilliseconds;
                Console.Out.WriteLine($"TTL by Ping={millis}.");
                _pingRecords.Remove(index);
            }
            _pingRecords.Keys.Where(it => it < index).ToList().ForEach(it => _pingRecords.Remove(it));
        }
    }

    #endregion
}
