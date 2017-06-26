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
            var bytes = new byte[16];
            bytes.PutInt(key.PersistenceId);
            bytes.PutLong(key.SequenceNr, 4);
            bytes.PutInt(key.MappingId, 4 + 8);
            return bytes;
        }

        public static Key KeyFromBytes(byte[] bytes)
        {
            int persistenceId = ByteHelpers.GetInt(bytes, 0);
            long sequenceNr = ByteHelpers.GetLong(bytes, 4);
            int mappingId = ByteHelpers.GetInt(bytes, 4 + 8);
            return new Key(persistenceId, sequenceNr, mappingId);
        }

        public static Key CounterKey(int persistenceId) => new Key(persistenceId, 0L, 0);

        public static byte[] CounterToBytes(long ctr)
        {
            var bytes = new byte[8];
            bytes.PutLong(ctr);
            return bytes;
        }

        public static long CounterFromBytes(byte[] bytes) => ByteHelpers.GetLong(bytes, 0);

        public static Key MappingKey(int id) => new Key(1, 0L, id);
        public static bool IsMappingKey(Key key) => key.PersistenceId == 1;

        public static Key DeletionKey(int persistenceId, long sequenceNr) => new Key(persistenceId, sequenceNr, 1);
        public static bool IsDeletionKey(Key key) => key.MappingId == 1;
    }
}
