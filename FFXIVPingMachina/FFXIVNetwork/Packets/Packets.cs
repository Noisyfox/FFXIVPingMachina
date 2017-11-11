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

    public static class Packets
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="header"></param>
        /// <returns>
        /// How many bytes should be skipped to read the segment data.
        /// If error occurs, return a negative value.
        /// </returns>
        public static int ParseSegmentHeader(byte[] buffer, int offset, ref FFXIVSegmentHeader header)
        {
            var headerSize = Marshal.SizeOf(typeof(FFXIVSegmentHeader));

            if (buffer.Length - offset < headerSize)
            {
                return (int) PacketParseResult.Incomplete;
            }

            var h = Util.ByteArrayToStructure<FFXIVSegmentHeader>(buffer, offset);

            // Max size of individual message is capped at 256KB for now.
            if (h.Size > 256 * 1024)
            {
                return (int) PacketParseResult.Malformed;
            }

            header = h;

            return headerSize;
        }
    }
}
