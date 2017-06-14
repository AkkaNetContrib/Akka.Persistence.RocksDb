using Akka.Actor;
using Akka.Configuration;

namespace Akka.Persistence.Query.RocksDb
{
    public class RocksDbReadJournalProvider : IReadJournalProvider
    {
        private readonly ExtendedActorSystem _system;
        private readonly Config _config;

        public RocksDbReadJournalProvider(ExtendedActorSystem system, Config config)
        {
            _system = system;
            _config = config;
        }

        public IReadJournal GetReadJournal()
        {
            return new RocksDbReadJournal(_system, _config);
        }
    }
}
