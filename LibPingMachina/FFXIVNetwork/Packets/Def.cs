using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FFXIVPingMachina.FFXIVNetwork.Packets
{
    /**
    * Structure representing the header portion of a packet segment.
    *
    * NOTE: If the main packet header indicated the packet is compressed, this
    * header will be compressed as well! The header will NOT ever be encrypted.
    *
    * 0               4               8              12              16
    * +---------------+---------------+---------------+-------+-------+
    * | size          | source_actor  | target_actor  | type  |   ?   |
    * +---------------+---------------+---------------+-------+-------+
    * |                                                               |
    * :          type-specific data of length, size, follows          :
    * |          (NOTE: Some segments MAY be encrypted)               |
    * +---------------------------------------------------------------+
    */
    [StructLayout(LayoutKind.Explicit)]
    public struct FFXIVSegmentHeader
    {
        /** The size of the segment header and its data. */
        [FieldOffset(0)]
        public uint Size;

        /** The session ID this segment describes. */
        [FieldOffset(4)]
        public uint SourceActorId;

        /** The session ID this packet is being delivered to. */
        [FieldOffset(8)]
        public uint TargetActorId;

        /** The segment type. (1, 2, 3, 7, 8, 9, 10) */
        [FieldOffset(12)]
        public ushort SegmentType;

        [FieldOffset(14)]
        public ushort Reserved;
    }

    public enum ClientSegmentType : ushort
    {
        IPC = 3, // Game packets (IPC)
        KeepAlive = 7,
    }

    public enum ServerSegmentType : ushort
    {
        IPC = 3,
        KeepAlive = 8,
    }

    /**
    * Structural representation of the KeepAlive packate data.
    * NOTE: This is packet segment type 7(send from client) or 8(send from server).
    *
    * 0               4                 8
    * +---------------+-----------------+
    * |      id       |    timestamp    |
    * +---------------+-----------------+
    */
    [StructLayout(LayoutKind.Explicit)]
    public struct FFXIVKeepAliveData
    {
        [FieldOffset(0)]
        public uint Id;
        [FieldOffset(4)]
        public uint Timestamp;
    }

    /**
    * Structural representation of the common header for IPC packet segments.
    * NOTE: This is packet segment type 3.
    *
    * 0               4      6          8              12              16
    * +-------+-------+------+----------+---------------+---------------+
    * | 14 00 | type  |  ??  | serverId |   timestamp   |      ???      |
    * +-------+-------+------+----------+---------------+---------------+
    * |                                                                 |
    * :                             data                                :
    * |                                                                 |
    * +-----------------------------------------------------------------+
    */
    [StructLayout(LayoutKind.Explicit)]
    public struct FFXIVIpcHeader
    {
        [FieldOffset(0)]
        public byte Reserved1;
        [FieldOffset(1)]
        public byte Reserved2;
        [FieldOffset(2)]
        public ushort Type;
        [FieldOffset(4)]
        public ushort Unknown2;
        [FieldOffset(6)]
        public ushort ServerId;
        [FieldOffset(8)]
        public uint Timestamp;
        [FieldOffset(12)]
        public uint UnknownC;
    };

    public enum FFXIVClientVersion
    {
        Unknown,
        Global,
        CN
    }

    /**
    * Server IPC Zone Type Codes.
    */
    public class ServerZoneIpcType
    {
        public ushort Ping { get; set; }
    }

    /**
    * Client IPC Zone Type Codes.
    */
    public class ClientZoneIpcType
    {
        public ushort PingHandler { get; set; }
    }

    /// <summary>
    /// TODO: Use machina OpcodeManager once opcode for Ping is included.
    /// </summary>
    public class ZoneIpcType
    {
        public ServerZoneIpcType Server { get; set; }
        public ClientZoneIpcType Client { get; set; }

        public static readonly Dictionary<FFXIVClientVersion, ZoneIpcType> ZoneIpcTypes =
            new Dictionary<FFXIVClientVersion, ZoneIpcType>
            {
                {
                    FFXIVClientVersion.Global,
                    new ZoneIpcType
                    {
                        Server = new ServerZoneIpcType
                        {
                            Ping = 0x0200, // updated for 5.18
                        },
                        Client = new ClientZoneIpcType
                        {
                            PingHandler = 0x0200, // updated for 5.18
                        }
                    }
                },
                {
                    FFXIVClientVersion.CN,
                    new ZoneIpcType
                    {
                        Server = new ServerZoneIpcType
                        {
                            Ping = 0x0358, // updated for 5.1
                        },
                        Client = new ClientZoneIpcType
                        {
                            PingHandler = 0x0358, // updated for 5.1
                        }
                    }
                },
            };
    }

    /**
    * Structural representation of the Ping packate data send by client.
    *
    * 0               4                 24
    * +---------------+-----------------+
    * |   timestamp   |  unknown_zero   |
    * +---------------+-----------------+
    */
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct FFXIVClientIpcPingData
    {
        [FieldOffset(0)]
        public uint Timestamp;
        [FieldOffset(4)]
        unsafe public fixed byte Unknown[20];
    }

    /**
    * Structural representation of the Ping packate data send by server.
    *
    * 0               8                 32
    * +---------------+-----------------+
    * |   timestamp   |  unknown_zero   |
    * +---------------+-----------------+
    */
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct FFXIVServerIpcPingData
    {
        [FieldOffset(0)]
        public ulong Timestamp;
        [FieldOffset(8)]
        unsafe public fixed byte Unknown[24];
    }
}
