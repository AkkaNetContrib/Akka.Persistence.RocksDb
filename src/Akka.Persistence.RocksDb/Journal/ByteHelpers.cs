using System;
using System.Net;

namespace Akka.Persistence.RocksDb.Journal
{
    public static class ByteHelpers
    {
        public static int GetInt(byte[] source, int offset = 0)
        {
            return IPAddress.NetworkToHostOrder(BitConverter.ToInt32(source, offset));
        }

        public static long GetLong(byte[] source, int offset = 0)
        {
            return IPAddress.NetworkToHostOrder(BitConverter.ToInt64(source, offset));
        }

        public static byte[] PutInt(this byte[] target, int x, int offset = 0)
        {
            target[offset + 0] = (byte)(x >> 24);
            target[offset + 1] = (byte)(x >> 16);
            target[offset + 2] = (byte)(x >> 8);
            target[offset + 3] = (byte)(x >> 0);

            return target;
        }

        public static byte[] PutLong(this byte[] target, long x, int offset = 0)
        {
            target[offset + 0] = (byte)(x >> 56);
            target[offset + 1] = (byte)(x >> 48);
            target[offset + 2] = (byte)(x >> 40);
            target[offset + 3] = (byte)(x >> 32);
            target[offset + 4] = (byte)(x >> 24);
            target[offset + 5] = (byte)(x >> 16);
            target[offset + 6] = (byte)(x >> 8);
            target[offset + 7] = (byte)(x >> 0);

            return target;
        }
    }

    public enum ByteOrder
    {
        BigEndian,
        LittleEndian
    }
}
