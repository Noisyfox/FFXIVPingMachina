using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using FFXIVPingMachina.FFXIVNetwork;
using FFXIVPingMachina.FFXIVNetwork.Packets;
using FFXIVPingMachina.PingMonitor.handler;

namespace LibPingMachina.PingMonitor.handler
{
    /// <summary>
    /// Automatically detect the IPC OpCode of Ping packet
    /// </summary>
    public class IPCPingOpCodeDetector
    {
        public delegate void PingOpCodeDetectDelegate(ushort opCode);

        public event PingOpCodeDetectDelegate OnPingOpCodeDetected;
        public ushort CurrentOpCode = 0;

        private readonly int ClientIPCPingDataSize = Marshal.SizeOf(typeof(FFXIVClientIpcPingData));
        private readonly int ServerIPCPingDataSize = Marshal.SizeOf(typeof(FFXIVServerIpcPingData));

        private struct PktHolder
        {
            public long TimeStamp;
            public bool ClientSent; // true -> sent by client; false -> sent by server
            public ushort OpCode;
            public ulong PingTimeStamp;
        }

        private const long BufferMask = 0x1FF;
        private PktHolder[] _buffer = new PktHolder[BufferMask + 1]; // 512 slots
        private long _bufferPointer = 0;
        private long _lastKeepAliveTimeStamp = 0;
        private const long TimeWindow = 20 * 1000;


        unsafe bool IsAllZeros(byte* ptr, int size)
        {
            for (var i = 0; i < size; i++)
            {
                if (ptr[i] != 0)
                {
                    return false;
                }
            }

            return true;
        }


        public void ClientSent(FFXIVIpcHeader header, byte[] data, int offset)
        {
            if (data.Length - offset < ClientIPCPingDataSize)
            {
                return;
            }

            Packets.NaiveParsePacket<FFXIVClientIpcPingData>(data, offset, out var pkt);
            unsafe
            {
                if (!IsAllZeros(pkt.Unknown, 20))
                {
                    return;
                }
            }

            if (pkt.Timestamp == 0)
            {
                return;
            }

            lock (_buffer)
            {
                ref var slot = ref _buffer[_bufferPointer & BufferMask];
                slot.TimeStamp = DateTime.UtcNow.EpochMillis();
                slot.ClientSent = true;
                slot.OpCode = header.Type;
                slot.PingTimeStamp = pkt.Timestamp;

                _bufferPointer++;
            }
        }

        public void ClientRecv(FFXIVIpcHeader header, byte[] data, int offset)
        {
            if (data.Length - offset < ServerIPCPingDataSize)
            {
                return;
            }

            Packets.NaiveParsePacket<FFXIVServerIpcPingData>(data, offset, out var pkt);
            unsafe
            {
                if (!IsAllZeros(pkt.Unknown, 24))
                {
                    return;
                }
            }

            if (pkt.Timestamp <= IPCHandler.TIMESTAMP_DELTA)
            {
                return;
            }

            lock (_buffer)
            {
                ref var slot = ref _buffer[_bufferPointer & BufferMask];
                slot.TimeStamp = DateTime.UtcNow.EpochMillis();
                slot.ClientSent = false;
                slot.OpCode = header.Type;
                slot.PingTimeStamp = pkt.Timestamp;

                _bufferPointer++;

                DetectPingPkt();
            }
        }

        public void ClientSent(FFXIVKeepAliveData keepAlive)
        {
            lock (_buffer)
            {
                _lastKeepAliveTimeStamp = DateTime.UtcNow.EpochMillis();
                DetectPingPkt();
            }
        }

        public void ClientRecv(FFXIVKeepAliveData keepAlive)
        {
            lock (_buffer)
            {
                _lastKeepAliveTimeStamp = DateTime.UtcNow.EpochMillis();
                DetectPingPkt();
            }
        }

        private class OpCodeStatistic
        {
            private class PingIndex
            {
                public int SendCount;
                public int RecvCount;

                /// <summary>
                /// Let α = the angle between vector A(1, 1) and B(Send, Recv),
                /// Then bias = Cos(α)^2 * 2 - 1
                ///
                /// Ideally a Ping-pong protocol should has SendCount == RecvCount, the
                /// Bias is a measure of the difference between Send and Recv. If Send == Recv then Bias == 1.0,
                /// and if any of the Send or Recv is 0, Bias == 0.
                /// </summary>
                private double Bias => 2 * SendCount * RecvCount / (Math.Pow(SendCount, 2) + Math.Pow(RecvCount, 2));

                // Confidence = 1 / ((opCode occurence - 1.5) ^ 2) * Bias
                /// <summary>
                /// Ideally each index should have only 1 send and 1 recv pkt, so the more the total occurence is
                /// greater than 2, the less likely this will be the ping pkt.
                /// </summary>
                public double Confidence => Math.Pow(1.0d / (SendCount + RecvCount - 1.5), 2) * Bias;
            }

            private Dictionary<ulong, PingIndex> _indexes = new Dictionary<ulong, PingIndex>();

            private PingIndex GetIndex(ulong index)
            {
                if (!_indexes.TryGetValue(index, out var i))
                {
                    i = new PingIndex();
                    _indexes.Add(index, i);
                }

                return i;
            }

            public void OnSend(ulong index)
            {
                GetIndex(index).SendCount++;
            }

            public void OnRecv(ulong index)
            {
                GetIndex(index).RecvCount++;
            }

            public double Confidence => _indexes.Values.Select(it => it.Confidence).Average();
        }

        private void DetectPingPkt()
        {
            long head;
            if (_bufferPointer <= _buffer.Length)
            {
                head = 0;
            }
            else
            {
                head = _bufferPointer - _buffer.Length;
            }

            var statistics = new Dictionary<ushort, OpCodeStatistic>();
            for (long i = _bufferPointer - 1; i >= head; i--)
            {
                ref var currBuf = ref _buffer[i & BufferMask];
                if (Math.Abs(currBuf.TimeStamp - _lastKeepAliveTimeStamp) > TimeWindow)
                {
                    break;
                }

                {

                    if (!statistics.TryGetValue(currBuf.OpCode, out var s))
                    {
                        s = new OpCodeStatistic();
                        statistics.Add(currBuf.OpCode, s);
                    }

                    if (currBuf.ClientSent)
                    {
                        s.OnSend(currBuf.PingTimeStamp);
                    }
                    else
                    {
                        s.OnRecv(currBuf.PingTimeStamp - IPCHandler.TIMESTAMP_DELTA);
                    }
                }
            }

            var confidence = statistics
                .ToDictionary(it => it.Key, it => it.Value.Confidence)
                .Where(it => it.Value > 0)
                .ToList();

            if (confidence.Count > 0)
            {
                // Get the OpCode with max confidence
                var opCode = confidence.Aggregate((max, next) => max.Value > next.Value ? max : next).Key;
                if (opCode != CurrentOpCode)
                {
                    CurrentOpCode = opCode;
                    OnPingOpCodeDetected?.Invoke(opCode);
                }
            }
        }
    }
}
