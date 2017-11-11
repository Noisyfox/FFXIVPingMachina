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
        private readonly KeepAliveHandler _keepAliveHandler = new KeepAliveHandler();

        public void MessageSent(long epoch, byte[] message)
        {
            Console.Out.WriteLine("MessageSent");

            var segHdr = new FFXIVSegmentHeader();
            var result = Packets.ParseSegmentHeader(message, 0, ref segHdr);

            if (result < 0)
            {
                var r = (PacketParseResult)result;
                Console.Out.WriteLine($"ParseSegmentHeader failed, result = {r}.");
                return;
            }
            Console.Out.WriteLine($"segHdr.SegmentType = 0x{segHdr.SegmentType:X4}.");

            switch ((ClientSegmentType)segHdr.SegmentType)
            {
                case ClientSegmentType.KeepAlive:
                    _keepAliveHandler.ClientSent(message, result);
                    break;
            }
        }

        public void MessageReceived(long epoch, byte[] message)
        {
            Console.Out.WriteLine("MessageReceived");

            var segHdr = new FFXIVSegmentHeader();
            var result = Packets.ParseSegmentHeader(message, 0, ref segHdr);

            if (result < 0)
            {
                var r = (PacketParseResult)result;
                Console.Out.WriteLine($"ParseSegmentHeader failed, result = {r}.");
                return;
            }
            Console.Out.WriteLine($"segHdr.SegmentType = 0x{segHdr.SegmentType:X4}.");

            switch ((ServerSegmentType)segHdr.SegmentType)
            {
                case ServerSegmentType.KeepAlive:
                    _keepAliveHandler.ClientRecv(message, result);
                    break;
            }
        }
    }

    public class KeepAliveHandler
    {
        private uint _currentId = 0;
        private DateTime _lastKeepAliveSent = DateTime.Now;

        public void ClientSent(byte[] data, int offset)
        {
            Console.Out.WriteLine($"KeepAliveHandler.ClientSent: {Utility.ByteArrayToHexString(data, offset)}.");
            var pkt = Util.ByteArrayToStructure<FFXIVKeepAliveData>(data, offset);
            Console.Out.WriteLine($"KeepAliveHandler.ClientSent: ID={pkt.Id}, Timestamp={pkt.Timestamp}.");

            _currentId = pkt.Id;
            _lastKeepAliveSent = DateTime.Now;
        }

        public void ClientRecv(byte[] data, int offset)
        {
            Console.Out.WriteLine($"KeepAliveHandler.ClientRecv: {Utility.ByteArrayToHexString(data, offset)}.");
            var pkt = Util.ByteArrayToStructure<FFXIVKeepAliveData>(data, offset);
            Console.Out.WriteLine($"KeepAliveHandler.ClientSent: ID={pkt.Id}, Timestamp={pkt.Timestamp}.");

            if (pkt.Id != _currentId)
            {
                return;
            }
            var millis = (DateTime.Now - _lastKeepAliveSent).TotalMilliseconds;

            Console.Out.WriteLine($"TTL by KeepAlive={millis}.");
        }
    }
}
