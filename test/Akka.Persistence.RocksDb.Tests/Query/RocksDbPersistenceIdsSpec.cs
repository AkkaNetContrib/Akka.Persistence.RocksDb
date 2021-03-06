﻿using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Persistence.RocksDb.Query;
using Akka.Persistence.TCK.Query;
using Akka.Util.Internal;
using Xunit.Abstractions;

namespace Akka.Persistence.RocksDb.Tests.Query
{
    public class RocksDbPersistenceIdsSpec : PersistenceIdsSpec
    {
        public static readonly AtomicCounter Counter = new AtomicCounter(0);

        public static Config Config(int id) => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.rocksdb""
            akka.persistence.journal.rocksdb {{
                class = ""Akka.Persistence.RocksDb.Journal.RocksDbJournal, Akka.Persistence.RocksDb""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                auto-initialize = on
                path = rocks_pid_{id}.db
            }}
            akka.test.single-expect-default = 3s")
            .WithFallback(RocksDbReadJournal.DefaultConfiguration());

        public RocksDbPersistenceIdsSpec(ITestOutputHelper output) 
            : base(Config(Counter.GetAndIncrement()), nameof(RocksDbPersistenceIdsSpec), output)
        {
            ReadJournal = Sys.ReadJournalFor<RocksDbReadJournal>(RocksDbReadJournal.Identifier);
        }
    }
}
