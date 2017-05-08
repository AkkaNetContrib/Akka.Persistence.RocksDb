using System;
using Akka.IO;

namespace Akka.Persistence.RocksDb.Journal
{
    public sealed class Key
    {
        public Key(int persistenceId, long sequenceNr, int mappingId)
        {
            PersistenceId = persistenceId;
            SequenceNr = sequenceNr;
            MappingId = mappingId;
        }

        public int PersistenceId { get; }

        public long SequenceNr { get; }

        public int MappingId { get; }
    }

    public static class RocksDbKey
    {
        public static byte[] KeyToBytes(Key key)
        {
            var bb = ByteBuffer.Allocate(20);
            bb.Put(BitConverter.GetBytes(key.PersistenceId));
            bb.Put(BitConverter.GetBytes(key.SequenceNr));
            bb.Put(BitConverter.GetBytes(key.MappingId));
            return bb.Array();
        }

        public static Key KeyFromBytes(byte[] bytes)
        {
            int persistenceId = BitConverter.ToInt32(bytes, 0);
            long sequenceNr = BitConverter.ToInt64(bytes, 4);
            int mappingId = BitConverter.ToInt32(bytes, 12);
            return new Key(persistenceId, sequenceNr, mappingId);
        }

        public static Key CounterKey(int persistenceId) => new Key(persistenceId, 0L, 0);

        public static byte[] CounterToBytes(long ctr)
        {
            var bb = ByteBuffer.Allocate(8);
            bb.Put(BitConverter.GetBytes(ctr));
            return bb.Array();
        }

        public static long CounterFromBytes(byte[] bytes) => BitConverter.ToInt64(bytes, 0);

        public static Key MappingKey(int id) => new Key(1, 0L, id);
        public static bool IsMappingKey(Key key) => key.PersistenceId == 1;

        public static Key DeletionKey(int persistenceId, long sequenceNr) => new Key(persistenceId, sequenceNr, 1);
        public static bool IsDeletionKey(Key key) => key.MappingId == 1;
    }
}
