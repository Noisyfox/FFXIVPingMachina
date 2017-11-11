using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace FFXIVPingMachina.FFXIVNetwork.Packets
{
    public enum PacketParseResult : int
    {
        /// Buffer is too short to dissect a message.
        Incomplete = -1,

        /// Invalid data detected.
        Malformed = -2
    };

    public class ParseException : Exception
    {
        private readonly PacketParseResult _result;

        public ParseException(string message, PacketParseResult reason) : base(message)
        {
            _result = reason;
        }

        public override string ToString()
        {
            return $"{Message} - {_result}.\n{StackTrace}";
        }
    }

    public static class Packets
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="output"></param>
        /// <returns>
        /// How many bytes should be skipped.
        /// </returns>
        public static int NaiveParsePacket<T>(byte[] buffer, int offset, out T output) where T : struct
        {
            var size = Marshal.SizeOf(typeof(T));

            if (buffer.Length - offset < size)
            {
                throw new ParseException("NaiveParsePacket failed", PacketParseResult.Incomplete);
            }

            output = Util.ByteArrayToStructure<T>(buffer, offset);

            return size;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="header"></param>
        /// <returns>
        /// How many bytes should be skipped to read the segment data.
        /// </returns>
        public static int ParseSegmentHeader(byte[] buffer, int offset, out FFXIVSegmentHeader header)
        {
            var headerSize = NaiveParsePacket<FFXIVSegmentHeader>(buffer, offset, out var h);

            // Max size of individual message is capped at 256KB for now.
            if (h.Size > 256 * 1024)
            {
                throw new ParseException("ParseSegmentHeader failed", PacketParseResult.Malformed);
            }

            header = h;

            return headerSize;
        }

        public static int ParseIPCHeader(byte[] buffer, int offset, out FFXIVIpcHeader header)
        {
            var headerSize = NaiveParsePacket<FFXIVIpcHeader>(buffer, offset, out var h);

//            if (h.Reserved1 != 0x14 || h.Reserved2 != 0x00)
//            {
//                throw new ParseException("ParseIPCHeader failed", PacketParseResult.Malformed);
//            }

            header = h;
            return headerSize;
        }
    }
}
