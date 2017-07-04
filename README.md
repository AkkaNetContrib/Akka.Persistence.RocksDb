# Akka.Persistence.RocksDb

[![Build status](https://ci.appveyor.com/api/projects/status/emu7p6xn56y8ra0e/branch/master?svg=true)](https://ci.appveyor.com/project/akkadotnet-contrib/akka-persistence-rocksdb/branch/master)

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
`EventsByTag` is used for retrieving events that were marked with a given tag, e.g. all domain events of an Aggregate Root type.

```C#
var readJournal = Sys.ReadJournalFor<RocksDbReadJournal>(RocksDbReadJournal.Identifier);

Source<EventEnvelope, NotUsed> willNotCompleteTheStream = queries.EventsByTag("apple", 0L);
Source<EventEnvelope, NotUsed> willCompleteTheStream = queries.CurrentEventsByTag("apple", 0L);
```

To tag events you create an Event Adapters that wraps the events in a `Akka.Persistence.Journal.Tagged` with the given tags.

```C#
public class ColorTagger : IWriteEventAdapter
{
    public string Manifest(object evt) => string.Empty;
    internal Tagged WithTag(object evt, string tag) => new Tagged(evt, ImmutableHashSet.Create(tag));

    public object ToJournal(object evt)
    {
        switch (evt)
        {
            case string s when s.Contains("green"):
                return WithTag(evt, "green");
            case string s when s.Contains("black"):
                return WithTag(evt, "black");
            case string s when s.Contains("blue"):
                return WithTag(evt, "blue");
            default:
                return evt;
        }
    }
}
```
You can use `0L` to retrieve all events with a given tag . The offset corresponds to an ordered sequence number for the specific tag. Note that the corresponding offset of each event is provided in the `EventEnvelope`, which makes it possible to resume the stream at a later point from a given offset.

The offset is exclusive, i.e. the event with the exact same sequence number will not be included in the returned stream. This means that you can use the offset that is returned in `EventEnvelope` as the offset parameter in a subsequent query.

In addition to the offset the `EventEnvelope` also provides `persistenceId` and `sequenceNr` for each event. The `sequenceNr` is the sequence number for the persistent actor with the `persistenceId` that persisted the event. The `persistenceId` + `sequenceNr` is an unique identifier for the event.

The returned event stream is ordered by the offset (tag sequence number), which corresponds to the same order as the write journal stored the events. The same stream elements (in same order) are returned for multiple executions of the query. Deleted events are not deleted from the tagged event stream.

> Note
> Events deleted using `DeleteMessages(toSequenceNr)` are not deleted from the “tagged stream”.

The stream is not completed when it reaches the end of the currently stored events, but it continues to push new events when new events are persisted. Corresponding query that is completed when it reaches the end of the currently stored events is provided by `CurrentEventsByTag`.

The `RocksDb` write journal is notifying the query side as soon as tagged events are persisted, but for efficiency reasons the query side retrieves the events in batches that sometimes can be delayed up to the configured refresh-interval or given `RefreshInterval` hint.

The stream is completed with failure if there is a failure in executing the query in the backend journal.

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
Are messages are serialized using [MessagePack-CSharp](https://github.com/neuecc/MessagePack-CSharp). If you want to change the serialization format, you should change HOCON settings
```
akka.actor {
  serializers {
    rocksdb = "Akka.Serialization.YourOwnSerializer, YourOwnSerializer"
  }
  serialization-bindings {
    "Akka.Persistence.IPersistentRepresentation, Akka.Persistence" = rocksdb
    "Akka.YourOwnType, YourOwnAssembly" = rocksdb
  }
}
```

## Maintainer
- [alexvaluyskiy](https://github.com/alexvaluyskiy)