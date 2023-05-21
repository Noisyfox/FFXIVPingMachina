using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVPingMachina.FFXIVNetwork;
using FFXIVPingMachina.FFXIVNetwork.Packets;
using FFXIVPingMachina.PingMonitor.handler;
using LibPingMachina.PingMonitor;
using LibPingMachina.PingMonitor.handler;

namespace FFXIVPingMachina.PingMonitor
{

    public class PerConnectionMonitor
    {
        public delegate void PingSampleDelegate(double RTT, DateTime sampleTime);

        public event IPCPingOpCodeDetector.PingOpCodeDetectDelegate OnPingOpCodeDetected;
        public event ConnectionPingSampleDelegate OnPingSample;

        public ConnectionIdentifier Connection { get; }

        public ConnectionPing CurrentPing { get; private set; }
        public DateTime LastActivity { get; private set; }
        private readonly IPCPingOpCodeDetector _pingOpCodeDetector = new IPCPingOpCodeDetector();
        private readonly KeepAliveHandler _keepAliveHandler = new KeepAliveHandler();
        private readonly IPCHandler _ipcHandler = new IPCHandler();

        public PerConnectionMonitor(string connection)
        {
            Connection = new ConnectionIdentifier(connection);
            _keepAliveHandler.OnPingSample += KeepAliveHandlerOnOnPingSample;
            _ipcHandler.OnPingSample += IpcHandlerOnOnPingSample;

            _keepAliveHandler.OnClientRecv += _pingOpCodeDetector.ClientRecv;
            _keepAliveHandler.OnClientSent += _pingOpCodeDetector.ClientSent;
            _ipcHandler.OnClientRecv += _pingOpCodeDetector.ClientRecv;
            _ipcHandler.OnClientSent += _pingOpCodeDetector.ClientSent;

            _pingOpCodeDetector.OnPingOpCodeDetected += opCode =>
            {
                _ipcHandler.PingOpCode = opCode;
                OnPingOpCodeDetected?.Invoke(opCode);
            };
        }

        public void MessageSent(long epoch, byte[] message)
        {
            LastActivity = DateTime.UtcNow;
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
            LastActivity = DateTime.UtcNow;
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

        
        private void KeepAliveHandlerOnOnPingSample(double rtt, DateTime sampleTime)
        {
            Console.Out.WriteLine($"RTT by KeepAlive={rtt}.");
            HandleNewSample(rtt, sampleTime);
        }

        private void IpcHandlerOnOnPingSample(double rtt, DateTime sampleTime)
        {
            Console.Out.WriteLine($"RTT by Ping={rtt}.");
            HandleNewSample(rtt, sampleTime);
        }

        private void HandleNewSample(double rtt, DateTime sampleTime)
        {
            var now = sampleTime.EpochMillis();
            _records[now] = rtt;

            var windowLeft = now - 5 * 1000; // window size = 5s
            // Remove records out of window
            _records.Keys.TakeWhile(it => it < windowLeft).ToList().ForEach(it => _records.Remove(it));

            // Use the min value in that window as the current ping
            CurrentPing = new ConnectionPing()
            {
                Connection = Connection,
                Ping = _records.Values.Min(),
                SampleTime = sampleTime.ToUniversalTime(),
            };

            OnPingSample?.Invoke(CurrentPing);
        }

        #endregion
    }
}
