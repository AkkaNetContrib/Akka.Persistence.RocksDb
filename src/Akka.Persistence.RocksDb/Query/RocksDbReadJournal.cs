using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Streams.Dsl;

namespace Akka.Persistence.RocksDb.Query
{
    public class RocksDbReadJournal : 
        IReadJournal,
        IPersistenceIdsQuery,
        ICurrentPersistenceIdsQuery,
        IEventsByPersistenceIdQuery,
        ICurrentEventsByPersistenceIdQuery,
        IEventsByTagQuery,
        ICurrentEventsByTagQuery
    {
        private readonly TimeSpan _refreshInterval;
        private readonly string _writeJournalPluginId;
        private readonly int _maxBufferSize;

        public static string Identifier = "akka.persistence.query.journal.rocksdb";

        public static Config DefaultConfiguration()
        {
           return ConfigurationFactory.FromResource<RocksDbReadJournal>("Akka.Persistence.RocksDb.reference.conf");
        }

        public RocksDbReadJournal(ExtendedActorSystem system, Config config)
        {
            _refreshInterval = config.GetTimeSpan("refresh-interval");
            _writeJournalPluginId = config.GetString("write-plugin");
            _maxBufferSize = config.GetInt("max-buffer-size");
        }

        public Source<string, NotUsed> PersistenceIds() =>
            // no polling for this query, the write journal will push all changes, i.e. no refreshInterval
            Source.ActorPublisher<string>(AllPersistenceIdsPublisher.Props(true, _writeJournalPluginId))
                .MapMaterializedValue(_ => NotUsed.Instance)
                .Named("PersistenceIds");

        public Source<string, NotUsed> CurrentPersistenceIds() =>
            Source.ActorPublisher<string>(AllPersistenceIdsPublisher.Props(false, _writeJournalPluginId))
                .MapMaterializedValue(_ => NotUsed.Instance)
                .Named("CurrentPersistenceIds");

        public Source<EventEnvelope, NotUsed> EventsByPersistenceId(string persistenceId, long fromSequenceNr = 0L, long toSequenceNr = long.MaxValue) =>
            Source.ActorPublisher<EventEnvelope>(EventsByPersistenceIdPublisher.Props(persistenceId, fromSequenceNr, toSequenceNr, _refreshInterval, _maxBufferSize, _writeJournalPluginId))
                .MapMaterializedValue(_ => NotUsed.Instance)
                .Named($"EventsByPersistenceId-{persistenceId}");

        public Source<EventEnvelope, NotUsed> CurrentEventsByPersistenceId(string persistenceId, long fromSequenceNr = 0L, long toSequenceNr = long.MaxValue) =>
            Source.ActorPublisher<EventEnvelope>(EventsByPersistenceIdPublisher.Props(persistenceId, fromSequenceNr, toSequenceNr, null, _maxBufferSize, _writeJournalPluginId))
                .MapMaterializedValue(_ => NotUsed.Instance)
                .Named($"CurrentEventsByPersistenceId-{persistenceId}");

        public Source<EventEnvelope, NotUsed> EventsByTag(string tag, Offset offset = null)
        {
            offset = offset ?? new Sequence(0L);
            switch (offset)
            {
                case Sequence seq:
                    return Source.ActorPublisher<EventEnvelope>(EventsByTagPublisher.Props(tag, seq.Value, long.MaxValue, _refreshInterval, _maxBufferSize, _writeJournalPluginId))
                        .MapMaterializedValue(_ => NotUsed.Instance)
                        .Named($"EventsByTag-{tag}");
                case NoOffset _:
                    return EventsByTag(tag, new Sequence(0L));
                default:
                    throw new ArgumentException($"RocksDbReadJournal does not support {offset.GetType().Name} offsets");
            }
        }

        public Source<EventEnvelope, NotUsed> CurrentEventsByTag(string tag, Offset offset = null)
        {
            offset = offset ?? new Sequence(0L);
            switch (offset)
            {
                case Sequence seq:
                    return Source.ActorPublisher<EventEnvelope>(EventsByTagPublisher.Props(tag, seq.Value, long.MaxValue, null, _maxBufferSize, _writeJournalPluginId))
                        .MapMaterializedValue(_ => NotUsed.Instance)
                        .Named($"CurrentEventsByTag-{tag}");
                case NoOffset _:
                    return CurrentEventsByTag(tag, new Sequence(0L));
                default:
                    throw new ArgumentException($"RocksDbReadJournal does not support {offset.GetType().Name} offsets");
            }
        }
    }
}
