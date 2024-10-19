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
        public delegate void PingOpCodeDetectDelegate(PingOpCode opCode);

        public event PingOpCodeDetectDelegate OnPingOpCodeDetected;

        public class PingOpCode
        {
            public ushort Client { get; }
            public ushort Server { get; }

            public PingOpCode(ushort client, ushort server)
            {
                Client = client;
                Server = server;
            }

            protected bool Equals(PingOpCode other)
            {
                return Client == other.Client && Server == other.Server;
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((PingOpCode)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Client.GetHashCode() * 397) ^ Server.GetHashCode();
                }
            }

            public override string ToString()
            {
                return $"{nameof(Client)}: 0x{Client:x4}, {nameof(Server)}: 0x{Server:x4}";
            }
        }

        public PingOpCode CurrentOpCode = new PingOpCode(0, 0);

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
            var dataLen = data.Length - offset;
            if (dataLen < ClientIPCPingDataSize || dataLen > ClientIPCPingDataSize * 2)
            {
                return;
            }

            Packets.NaiveParsePacket<FFXIVClientIpcPingData>(data, offset, out var pkt);
            unsafe
            {
                if (!IsAllZeros(pkt.Unknown2, 6))
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
            var dataLen = data.Length - offset;
            if (dataLen < ServerIPCPingDataSize || dataLen > ServerIPCPingDataSize * 2)
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

            // Confidence = 1 / ((opCode occurrence - 1.5) ^ 2) * Bias
            /// <summary>
            /// Ideally each index should have only 1 send and 1 recv pkt, so the more the total occurence is
            /// greater than 2, the less likely this will be the ping pkt.
            /// </summary>
            public double Confidence => Math.Pow(1.0d / (SendCount + RecvCount - 1.5), 2) * Bias;
        }

        private class OpCodeStatistic
        {
            public class OpCodeHolder
            {
                public readonly ushort OpCode;
                public int SendCount = 0;
                public int RecvCount = 0;

                public OpCodeHolder(ushort opCode)
                {
                    OpCode = opCode;
                }
            }

            public readonly Dictionary<ushort, OpCodeHolder> OpCodes = new Dictionary<ushort, OpCodeHolder>();

            private OpCodeHolder GetOpCode(ushort opCode)
            {
                if (!OpCodes.TryGetValue(opCode, out var o))
                {
                    o = new OpCodeHolder(opCode);
                    OpCodes.Add(opCode, o);
                }

                return o;
            }

            public void OnSend(ushort opCode)
            {
                GetOpCode(opCode).SendCount++;
            }

            public void OnRecv(ushort opCode)
            {
                GetOpCode(opCode).RecvCount++;
            }
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

            var statistics = new Dictionary<ulong, OpCodeStatistic>();
            for (long i = _bufferPointer - 1; i >= head; i--)
            {
                ref var currBuf = ref _buffer[i & BufferMask];
                
                if (Math.Abs(currBuf.TimeStamp - _lastKeepAliveTimeStamp) > TimeWindow)
                {
                    break;
                }

                {
                    ulong pingIndex;

                    if (currBuf.ClientSent)
                    {
                        pingIndex = currBuf.PingTimeStamp;
                    }
                    else
                    {
                        pingIndex = currBuf.PingTimeStamp - IPCHandler.TIMESTAMP_DELTA;
                    }

                    if (!statistics.TryGetValue(pingIndex, out var s))
                    {
                        s = new OpCodeStatistic();
                        statistics.Add(pingIndex, s);
                    }

                    if (currBuf.ClientSent)
                    {
                        s.OnSend(currBuf.OpCode);
                    }
                    else
                    {
                        s.OnRecv(currBuf.OpCode);
                    }
                }
            }

            // Calculate confidence of different opCode combinations
            var pairStatistics = new Dictionary<PingOpCode, List<PingIndex>>();
            foreach (var s in statistics.Values)
            {
                var opCodes = s.OpCodes.Values;
                foreach (var send in opCodes)
                {
                    if (send.SendCount == 0)
                    {
                        continue;
                    }

                    foreach (var recv in opCodes)
                    {
                        if (recv.RecvCount == 0)
                        {
                            continue;
                        }

                        var pair = new PingOpCode(send.OpCode, recv.OpCode);
                        if (!pairStatistics.TryGetValue(pair, out var i))
                        {
                            i = new List<PingIndex>();
                            pairStatistics.Add(pair, i);
                        }
                        i.Add(new PingIndex
                        {
                            SendCount = send.SendCount,
                            RecvCount = recv.RecvCount,
                        });
                    }
                }
            }

            var confidence = pairStatistics
                .ToDictionary(it => it.Key, it => it.Value.Select(it2 => it2.Confidence).Average())
                .Where(it => it.Value > 0)
                .ToList();

            if (confidence.Count > 0)
            {
                // Get the OpCode with max confidence
                var opCode = confidence.Aggregate((max, next) => max.Value > next.Value ? max : next).Key;
                if (!Equals(opCode, CurrentOpCode))
                {
                    CurrentOpCode = opCode;
                    OnPingOpCodeDetected?.Invoke(opCode);
                }
            }
        }
    }
}
