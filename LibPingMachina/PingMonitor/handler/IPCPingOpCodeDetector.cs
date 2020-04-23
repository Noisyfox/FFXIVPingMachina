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

            var indexMap = new Dictionary<ulong, int>(); // pkt index -> occurrence count
            var opCodeMap = new Dictionary<ushort, HashSet<ulong>>(); // OpCode -> pkt index
            for (long i = _bufferPointer - 1; i >= head; i--)
            {
                ref var currBuf = ref _buffer[i & BufferMask];
                if (Math.Abs(currBuf.TimeStamp - _lastKeepAliveTimeStamp) > TimeWindow)
                {
                    break;
                }

                ulong index = currBuf.PingTimeStamp;
                if (!currBuf.ClientSent)
                {
                    index -= IPCHandler.TIMESTAMP_DELTA;
                }

                {
                    if (!opCodeMap.TryGetValue(currBuf.OpCode, out var s))
                    {
                        s = new HashSet<ulong>();
                        opCodeMap.Add(currBuf.OpCode, s);
                    }

                    s.Add(index);
                }
                {
                    if (!indexMap.TryGetValue(index, out var c))
                    {
                        c = 0;
                    }

                    indexMap[index] = c + 1;
                }
            }

            indexMap = indexMap
                .Where(kvp => kvp.Value > 1)
                .ToDictionary(i => i.Key, i => i.Value);

            // Confidence = 1 / ((opCode occurence - 1.5) ^ 2)
            var confidence = opCodeMap.ToDictionary(i => i.Key, i =>
            {
                double c = i.Value.Select(it =>
                {
                    if (indexMap.TryGetValue(it, out var occur))
                    {
                        return Math.Pow(1.0d / (occur - 1.5), 2);
                    }

                    return 0;
                }).Sum();

                return c / i.Value.Count;
            });

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
