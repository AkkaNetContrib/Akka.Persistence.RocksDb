# Akka.Persistence.RocksDb

[![Build status](https://ci.appveyor.com/api/projects/status/swji9deq1pt89o2h/branch/master?svg=true)](https://ci.appveyor.com/project/ravengerUA/akka-persistence-rocksdb/branch/master)

Akka Persistence journal store backed by RocksDB embedded database. This is an alpha version; currently it is more of an experiment with RocksDB. It uses [rocksdb-sharp](https://github.com/warrenfalk/rocksdb-sharp) package.

## Journal
TDB

### Configuration
The default configuration for the journal
```hocon
akka.persistence.journal.rocksdb {
    # Class name of the plugin
    class = "Akka.Persistence.RocksDb.Journal.RocksDbJournal, Akka.Persistence.RocksDb"

    # Dispatcher for the plugin actor.
    plugin-dispatcher = "akka.persistence.dispatchers.default-plugin-dispatcher"

    # Dispatcher for message replay.
    replay-dispatcher = "akka.persistence.dispatchers.default-replay-dispatcher"

	# Storage location of RocksDB files.
    path = "journal"

    # Use fsync on write.
    fsync = on

    # Verify checksum on read.
    checksum = off
}
```

## Persistence Query
### How to get the ReadJournal
The `IReadJournal` is retrieved via `ReadJournalFor` extension method:
```C#
using Akka.Persistence.Query;
using Akka.Persistence.Query.RocksDb;

var queries = Sys.ReadJournalFor<RocksDbReadJournal>(RocksDbReadJournal.Identifier);
```

### Supported Queries

#### EventsByPersistenceIdQuery and CurrentEventsByPersistenceIdQuery
`EventsByPersistenceId` is used for retrieving events for a specific `PersistentActor` identified by `PersistenceId`
```C#
var queries = Sys.ReadJournalFor<RocksDbReadJournal>(RocksDbReadJournal.Identifier);

Source<EventEnvelope, NotUsed> src = 
    queries.EventsByPersistenceId("some-persistence-id", 0L, long.MaxValue);

Source<object, NotUsed> events = src.Select(e => e.Event);
```
You can retrieve a subset of all events by specifying `fromSequenceNr` and `toSequenceNr` or use `0L` and `Long.MaxValue` respectively to retrieve all events. Note that the corresponding sequence number of each event is provided in the `EventEnvelope`, which makes it possible to resume the stream at a later point from a given sequence number.

The returned event stream is ordered by sequence number, i.e. the same order as the `PersistentActor` persisted the events. The same prefix of stream elements (in same order) are returned for multiple executions of the query, except for when events have been deleted.

The stream is not completed when it reaches the end of the currently stored events, but it continues to push new events when new events are persisted. Corresponding query that is completed when it reaches the end of the currently stored events is provided by `CurrentEventsByPersistenceId`.

The `RocksDB` write journal is notifying the query side as soon as events are persisted, but for efficiency reasons the query side retrieves the events in batches that sometimes can be delayed up to the configured refresh-interval or given `RefreshInterval` hint.

The stream is completed with failure if there is a failure in executing the query in the backend journal.

#### AllPersistenceIdsQuery and CurrentPersistenceIdsQuery
`AllPersistenceIds` is used for retrieving all `AllPersistenceIds` of all persistent actors.
```C#
var queries = Sys.ReadJournalFor<RocksDbReadJournal>(RocksDbReadJournal.Identifier);

Source<string, NotUsed> src = queries.AllPersistenceIds();
```
The returned event stream is unordered and you can expect different order for multiple executions of the query.

The stream is not completed when it reaches the end of the currently used `AllPersistenceIds`, but it continues to push new persistenceIds when new persistent actors are created. Corresponding query that is completed when it reaches the end of the currently used persistenceIds is provided by `CurrentPersistenceIds`.

The `RocksDB` write journal is notifying the query side as soon as new `AllPersistenceIds` are created and there is no periodic polling or batching involved in this query.

The stream is completed with failure if there is a failure in executing the query in the backend journal.

#### EventsByTag and CurrentEventsByTag
`EventsByTag` and `CurrentEventsByTag` is not supported at the moment.

### Configuration
Configuration settings can be defined in the configuration section with the absolute path corresponding to the identifier, which is `akka.persistence.query.journal.rocksdb` for the default `RocksDbReadJournal.Identifier`.
```hocon
akka.persistence.query.journal.rocksdb {
  # Implementation class of the RocksDb ReadJournalProvider
  class = "Akka.Persistence.Query.RocksDb.RocksDbReadJournalProvider, Akka.Persistence.Query.RocksDb"
  
  # Absolute path to the write journal plugin configuration entry that this 
  # query journal will connect to. That must be a RocksDbJournal.
  # If undefined (or "") it will connect to the default journal as specified by the
  # akka.persistence.journal.plugin property.
  write-plugin = ""
  
  # The RocksDb write journal is notifying the query side as soon as things
  # are persisted, but for efficiency reasons the query side retrieves the events 
  # in batches that sometimes can be delayed up to the configured `refresh-interval`.
  refresh-interval = 3s
  
  # How many events to fetch in one query (replay) and keep buffered until they
  # are delivered downstreams.
  max-buffer-size = 100
}
```

## Serialization
Are messages are serialized using the default `Akka.Persistence` serializer for `IPersistentRepresentation` type. It could be changed in future releases.