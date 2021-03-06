﻿using Akka.Configuration;
using Akka.Persistence.RocksDb.Query;
using Akka.Persistence.RocksDb.Tests.Query;
using Akka.Persistence.TestKit.Performance;
using Akka.Util.Internal;
using Xunit.Abstractions;

namespace Akka.Persistence.RocksDb.Tests
{
    public class RocksDbJournalPerfSpec : JournalPerfSpec
    {
        public static readonly AtomicCounter Counter = new AtomicCounter(0);

        public static Config Config(int id) => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.rocksdb""
            akka.persistence.journal.rocksdb {{
                class = ""Akka.Persistence.RocksDb.Journal.RocksDbJournal, Akka.Persistence.RocksDb""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                auto-initialize = on
                path = rocks_perf_{id}.db
            }}
            akka.actor.serialization-bindings.""Akka.Persistence.TestKit.Performance.Cmd, Akka.Persistence.TCK"" = msgpack
            akka.test.single-expect-default = 3s")
            .WithFallback(RocksDbReadJournal.DefaultConfiguration());

        public RocksDbJournalPerfSpec(ITestOutputHelper output) 
            : base(Config(Counter.GetAndIncrement()), nameof(RocksDbPersistenceIdsSpec), output)
        {
        }
    }
}
