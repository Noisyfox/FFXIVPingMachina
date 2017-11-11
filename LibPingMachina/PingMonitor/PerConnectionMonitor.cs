using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVPingMachina.FFXIVNetwork;
using FFXIVPingMachina.FFXIVNetwork.Packets;
using FFXIVPingMachina.PingMonitor.handler;

namespace FFXIVPingMachina.PingMonitor
{

    public class PerConnectionMonitor
    {
        public event PingSampleDelegate OnPingSample;

        public double CurrentPing { get; private set; }
        public DateTime LastActivity { get; private set; }
        private readonly KeepAliveHandler _keepAliveHandler = new KeepAliveHandler();
        private readonly IPCHandler _ipcHandler = new IPCHandler();

        public PerConnectionMonitor()
        {
            _keepAliveHandler.OnPingSample += KeepAliveHandlerOnOnPingSample;
            _ipcHandler.OnPingSample += IpcHandlerOnOnPingSample;
        }

        public void MessageSent(long epoch, byte[] message)
        {
            LastActivity = DateTime.Now;
            //           Console.Out.WriteLine("MessageSent");

            var headerLen = Packets.ParseSegmentHeader(message, 0, out var segHdr);
            //           Console.Out.WriteLine($"segHdr.SegmentType = 0x{segHdr.SegmentType:X4}.");

            switch ((ClientSegmentType)segHdr.SegmentType)
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

            switch ((ServerSegmentType)segHdr.SegmentType)
            {
                case ServerSegmentType.KeepAlive:
                    _keepAliveHandler.ClientRecv(message, headerLen);
                    break;
                case ServerSegmentType.IPC:
                    _ipcHandler.ClientRecv(message, headerLen);
                    break;
            }
        }

        #region Stastics

        private readonly SortedDictionary<long, double> _records = new SortedDictionary<long, double>();

        
        private void KeepAliveHandlerOnOnPingSample(double ttl, DateTime sampleTime)
        {
            Console.Out.WriteLine($"TTL by KeepAlive={ttl}.");
            HandleNewSample(ttl, sampleTime);
        }

        private void IpcHandlerOnOnPingSample(double ttl, DateTime sampleTime)
        {
            Console.Out.WriteLine($"TTL by Ping={ttl}.");
            HandleNewSample(ttl, sampleTime);
        }

        private void HandleNewSample(double ttl, DateTime sampleTime)
        {
            var now = sampleTime.EpochMillis();
            _records[now] = ttl;

            var windowLeft = now - 5 * 1000; // window size = 5s
            // Remove records out of window
            _records.Keys.TakeWhile(it => it < windowLeft).ToList().ForEach(it => _records.Remove(it));

            // Use the min value in that window as the current ping
            CurrentPing = _records.Values.Min();
            OnPingSample?.Invoke(CurrentPing, sampleTime);
        }

        #endregion
    }
}
