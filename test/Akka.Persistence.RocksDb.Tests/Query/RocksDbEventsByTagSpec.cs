using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Persistence.Query.RocksDb;
using Akka.Util.Internal;
using Xunit.Abstractions;
using Akka.Persistence.TCK.Query;

namespace Akka.Persistence.RocksDb.Tests.Query
{
    public class RocksDbEventsByTagSpec : EventsByTagSpec
    {
        public static readonly AtomicCounter Counter = new AtomicCounter(0);

        public static Config Config(int id) => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.rocksdb""
            akka.persistence.journal.rocksdb {{
                event-adapters {{
                  color-tagger  = ""Akka.Persistence.TCK.Query.ColorFruitTagger, Akka.Persistence.TCK""
                }}
                event-adapter-bindings = {{
                  ""System.String"" = color-tagger
                }}
                class = ""Akka.Persistence.RocksDb.Journal.RocksDbJournal, Akka.Persistence.RocksDb""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                auto-initialize = on
                path = rocks_tag_{id}.db
            }}
            akka.test.single-expect-default = 25s")
            .WithFallback(RocksDbReadJournal.DefaultConfiguration());

        public RocksDbEventsByTagSpec(ITestOutputHelper output) 
            : base(Config(Counter.GetAndIncrement()), nameof(RocksDbEventsByTagSpec), output)
        {
            ReadJournal = Sys.ReadJournalFor<RocksDbReadJournal>(RocksDbReadJournal.Identifier);
        }
    }
}
