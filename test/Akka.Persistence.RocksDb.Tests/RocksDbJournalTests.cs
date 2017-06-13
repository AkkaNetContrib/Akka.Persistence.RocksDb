using System.IO;
using System.Linq;
using Akka.Configuration;
using Akka.Persistence.TCK.Journal;
using Akka.Util.Internal;

namespace Akka.Persistence.RocksDB.Tests
{
    public class RocksDbJournalSpec : JournalSpec
    {
        private static readonly AtomicCounter Counter = new AtomicCounter(0);

        public RocksDbJournalSpec() : base(CreateSpecConfig())
        {
            RocksDbPersistence.Get(Sys);
            Initialize();
        }

        private static Config CreateSpecConfig()
        {
            var name = $"rocks_journal_{Counter.GetAndIncrement()}.db";

            return ConfigurationFactory.ParseString($@"
                akka.test.single-expect-default = 3s
                akka.persistence {{
                    publish-plugin-commands = on
                    journal {{
                        plugin = ""akka.persistence.journal.rocksdb""
                        rocksdb {{
                            class = ""Akka.Persistence.RocksDb.Journal.RocksDbJournal, Akka.Persistence.RocksDb""
                            plugin-dispatcher = ""akka.actor.default-dispatcher""
                            path = {name}
                            auto-initialize = on
                        }}
                    }}
                }}");
        }

        protected override bool SupportsRejectingNonSerializableObjects { get; } = false;

        private static void Clean()
        {
            Directory
                .GetDirectories("./")
                .Where(c => c.Contains("rocks"))
                .Select(c => c)
                .ForEach(c => Directory.Delete(c, true));
        }

    }
}
