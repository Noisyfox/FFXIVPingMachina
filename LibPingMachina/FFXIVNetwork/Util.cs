using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace FFXIVPingMachina.FFXIVNetwork
{
    public static class Util
    {
        public static T ByteArrayToStructure<T>(byte[] bytes, int offset = 0, Endianness endianness = Endianness.LittleEndian) where T : struct
        {
            MaybeAdjustEndianness(typeof(T), bytes, endianness, offset);

            T stuff;
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                stuff = (T) Marshal.PtrToStructure(handle.AddrOfPinnedObject() + offset, typeof(T));
            }
            finally
            {
                handle.Free();
            }
            return stuff;
        }

        public enum Endianness
        {
            BigEndian,
            LittleEndian
        }

        public static void MaybeAdjustEndianness(Type type, byte[] data, Endianness endianness, int startOffset = 0)
        {
            if ((BitConverter.IsLittleEndian) == (endianness == Endianness.LittleEndian))
            {
                // nothing to change => return
                return;
            }

            foreach (var field in type.GetFields())
            {
                var fieldType = field.FieldType;
                if (field.IsStatic)
                    // don't process static fields
                    continue;

                if (fieldType == typeof(string))
                    // don't swap bytes for strings
                    continue;

                var offset = Marshal.OffsetOf(type, field.Name).ToInt32();

                // handle enums
                if (fieldType.IsEnum)
                    fieldType = Enum.GetUnderlyingType(fieldType);

                // check for sub-fields to recurse if necessary
                var subFields = fieldType.GetFields().Where(subField => subField.IsStatic == false).ToArray();

                var effectiveOffset = startOffset + offset;

                if (subFields.Length == 0)
                {
                    Array.Reverse(data, effectiveOffset, Marshal.SizeOf(fieldType));
                }
                else
                {
                    // recurse
                    MaybeAdjustEndianness(fieldType, data, endianness, effectiveOffset);
                }
            }
        }

        private static readonly long _dt1970 = new DateTime(1970, 1, 1).Ticks;

        public static long EpochMillis(this DateTime t)
        {
            var unixTimestamp = t.Ticks - _dt1970;
            unixTimestamp /= TimeSpan.TicksPerMillisecond;
            return unixTimestamp;
        }
    }
}
