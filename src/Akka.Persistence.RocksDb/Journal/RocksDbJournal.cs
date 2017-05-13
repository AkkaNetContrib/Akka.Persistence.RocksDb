using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Persistence.Journal;
using Akka.Persistence.RocksDB;
using Akka.Serialization;
using Akka.Util.Internal;
using RocksDbSharp;
using static Akka.Persistence.RocksDb.Journal.RocksDbKey;
using RocksDbDatabase = RocksDbSharp.RocksDb;

namespace Akka.Persistence.RocksDb.Journal
{
    public sealed class RocksDbJournal : AsyncWriteJournal
    {
        internal RocksDbSettings Settings { get; } = RocksDbPersistence.Get(Context.System).JournalSettings;
        internal Serializer Serializer { get; } = Context.System.Serialization.FindSerializerFor(typeof(Serialization.IMessage));

        internal DbOptions RocksDbOptions { get; } = new DbOptions().SetCreateIfMissing(true);
        internal ReadOptions RocksDbReadOptions { get; }
        internal WriteOptions RocksDbWriteOptions { get; }
        internal RocksDbDatabase Database { get; private set; }

        private Dictionary<string, HashSet<IActorRef>> persistenceIdSubscribers = new Dictionary<string, HashSet<IActorRef>>();
        private Dictionary<string, HashSet<IActorRef>> tagSubscribers = new Dictionary<string, HashSet<IActorRef>>();
        private HashSet<IActorRef> allPersistenceIdsSubscribers = new HashSet<IActorRef>();

        private Dictionary<string, long> tagSequenceNrs = new Dictionary<string, long>();
        private const string TagPersistenceIdPrefix = "$$$";

        public RocksDbJournal()
        {
            RocksDbReadOptions = new ReadOptions().SetVerifyChecksums(Settings.Checksum);
            RocksDbWriteOptions = new WriteOptions().SetSync(Settings.FSync);
        }

        protected override bool ReceivePluginInternal(object message)
        {
            if (message is ReplayTaggedMessages rtm)
            {
                var readHighestSequenceNrFrom = Math.Max(0L, rtm.FromSequenceNr - 1);
                ReadHighestSequenceNrAsync(TagAsPersistenceId(rtm.Tag), readHighestSequenceNrFrom)
                    .ContinueWith(task =>
                    {
                        var highSeqNr = task.Result;
                        var toSeqNr = Math.Min(rtm.ToSequenceNr, highSeqNr);
                        if (highSeqNr == 0L || rtm.FromSequenceNr > toSeqNr)
                        {
                            return highSeqNr;
                        }
                        else
                        {
                            ReplayTaggedMessagesAsync(rtm.Tag, rtm.FromSequenceNr, toSeqNr, rtm.Max, taggedMessage =>
                            {
                                AdaptFromJournal(taggedMessage.Persistent).ForEach(adaptedPersistentRepr =>
                                {
                                    rtm.ReplyTo.Tell(new ReplayedTaggedMessage(adaptedPersistentRepr, rtm.Tag,
                                        taggedMessage.Offset));
                                });
                            }).Wait();

                            return highSeqNr;
                        }
                    }).ContinueWith<IJournalResponse>(task =>
                    {
                        if (!task.IsFaulted && !task.IsCanceled)
                            return new RecoverySuccess(task.Result);
                        else
                            return new ReplayMessagesFailure(task.Exception);
                    }).PipeTo(rtm.ReplyTo);
            }
            else if (message is SubscribePersistenceId subscribePersistenceId)
            {
                AddPersistenceIdSubscriber(Sender, subscribePersistenceId.PersistenceId);
                Context.Watch(Sender);
            }
            else if (message is SubscribeAllPersistenceIds)
            {
                AddAllPersistenceIdsSubscriber(Sender);
                Context.Watch(Sender);
            }
            else if (message is SubscribeTag subscribeTag)
            {
                AddTagSubscriber(Sender, subscribeTag.Tag);
                Context.Watch(Sender);
            }
            else if (message is Terminated t)
            {
                RemoveSubscriber(t.ActorRef);
            }

            return true;
        }

        #region RocksDb Recovery
        private MessageDispatcher replayDispatcher = Context.System.Dispatchers.Lookup("akka.persistence.dispatchers.default-replay-dispatcher");

        public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            var nid = NumericId(persistenceId);
            return Task.FromResult(ReadHighestSequenceNr(nid));
        }

        public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max,
            Action<IPersistentRepresentation> recoveryCallback)
        {
            var nid = NumericId(persistenceId);
            return Task.Run(() => ReplayMessages(nid, fromSequenceNr, toSequenceNr, max, recoveryCallback));
        }

        private void ReplayMessages(
            int persistenceId,
            long fromSequenceNr,
            long toSequenceNr,
            long max,
            Action<IPersistentRepresentation> recoveryCallback)
        {
            void Go(Iterator iter, Key key, long ctr, Action<IPersistentRepresentation> replayCallback)
            {
                if (iter.Valid())
                {
                    var nextEntry = iter.PeekAndNext();
                    var nextKey = KeyFromBytes(nextEntry.Key);
                    if (nextKey.SequenceNr > toSequenceNr)
                    {
                        // end iteration here
                    }
                    else if (IsDeletionKey(nextKey))
                    {
                        // this case is needed to discard old events with deletion marker
                        Go(iter, nextKey, ctr, replayCallback);
                    }
                    else if (key.PersistenceId == nextKey.PersistenceId)
                    {
                        var msg = PersistentFromBytes(nextEntry.Value);
                        var del = Deletion(iter, nextKey);
                        if (ctr < max)
                        {
                            if (!del)
                                replayCallback(msg);
                            Go(iter, nextKey, ctr + 1L, replayCallback);
                        }
                    }
                }
            }

            // need to have this to be able to read journal created with 1.0.x, which
            // supported deletion of individual events
            bool Deletion(Iterator iter, Key key)
            {
                if (iter.Valid())
                {
                    var nextEntry = iter.Peek();
                    var nextKey = KeyFromBytes(nextEntry.Key);
                    if (key.PersistenceId == nextKey.PersistenceId && key.SequenceNr == nextKey.SequenceNr &&
                        IsDeletionKey(nextKey))
                    {
                        iter.Next();
                        return true;
                    }
                    else return false;
                }
                else
                {
                    return false;
                }
            }

            WithIterator<NotUsed>(iter =>
            {
                var startKey = new Key(persistenceId, fromSequenceNr < 1L ? 1L : fromSequenceNr, 0);
                iter.Seek(KeyToBytes(startKey));
                Go(iter, startKey, 0L, recoveryCallback);

                return NotUsed.Instance;
            });
        }

        private Task ReplayTaggedMessagesAsync(
            string tag,
            long fromSequenceNr,
            long toSequenceNr,
            long max,
            Action<ReplayedTaggedMessage> recoveryCallback)
        {
            var tagNid = TagNumericId(tag);
            return Task.Run(() => ReplayTaggedMessages(tag, tagNid, fromSequenceNr, toSequenceNr, max, recoveryCallback));
        }

        private void ReplayTaggedMessages(
            string tag,
            int tagInt,
            long fromSequenceNr,
            long toSequenceNr,
            long max,
            Action<ReplayedTaggedMessage> recoveryCallback)
        {
            void Go(Iterator iter, Key key, long ctr, Action<ReplayedTaggedMessage> replayCallback)
            {
                if (iter.Valid())
                {
                    var nextEntry = iter.PeekAndNext();
                    var nextKey = KeyFromBytes(nextEntry.Key);
                    if (nextKey.SequenceNr > toSequenceNr)
                    {
                        // end iteration here
                    }
                    else if (key.PersistenceId == nextKey.PersistenceId)
                    {
                        var msg = PersistentFromBytes(nextEntry.Value);
                        if (ctr < max)
                        {
                            replayCallback(new ReplayedTaggedMessage(msg, tag, nextKey.SequenceNr));
                            Go(iter, nextKey, ctr + 1L, replayCallback);
                        }
                    }
                }
            }

            WithIterator(iter =>
            {
                var startKey = new Key(tagInt, fromSequenceNr < 1L ? 1L : fromSequenceNr, 0);
                iter.Seek(KeyToBytes(startKey));
                Go(iter, startKey, 0L, recoveryCallback);

                return NotUsed.Instance;
            });
        }

        private long ReadHighestSequenceNr(int persistenceId)
        {
            var ro = RocksDbSnapshot();
            try
            {
                var num = Database.Get(KeyToBytes(CounterKey(persistenceId)), cf: null, readOptions: ro);
                return num == null ? 0L : CounterFromBytes(num);
            }
            finally
            {
                //ro.Snapshot().Dispose();
            }
        }
        #endregion

        protected override async Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages)
        {
            var persistenceIds = new HashSet<string>();
            var allTags = new HashSet<string>();

            var writeTasks = new List<Task>();
            foreach (var atomicWrite in messages)
            {
                writeTasks.Add(Task.Run(() =>
                {
                    WithBatch(batch =>
                    {
                        var payloads = atomicWrite.Payload.AsInstanceOf<ImmutableList<IPersistentRepresentation>>();
                        foreach (var p in payloads)
                        {
                            ValueTuple<IPersistentRepresentation, HashSet<string>> tuple;
                            if (p.Payload is Tagged tagged)
                            {
                                tuple = (p.WithPayload(tagged.Payload), tagged.Tags.ToHashSet());
                            }
                            else
                            {
                                tuple = (p, new HashSet<string>());
                            }

                            var (p2, tags) = tuple;

                            if (tags.Count > 0 && HasTagSubscribers)
                                allTags = allTags.Union(tags).ToHashSet();

                            if (p.PersistenceId.StartsWith(TagPersistenceIdPrefix))
                                throw new InvalidOperationException($"Persistence Id {p.PersistenceId} must not start with {TagPersistenceIdPrefix}");

                            AddToMessageBatch(p2, tags, batch);
                        }

                        if (HasPersistenceIdSubscribers())
                            persistenceIds.Add(atomicWrite.PersistenceId);

                        return NotUsed.Instance;
                    });
                }));
            }

            writeTasks.ForEach(c => c.Wait());

            var result = writeTasks.Select(t => t.IsFaulted ? TryUnwrapException(t.Exception) : null).ToImmutableList();

            if (HasPersistenceIdSubscribers())
            {
                persistenceIds.ForEach(NotifyPersistenceIdChange);
            }

            if (HasTagSubscribers && allTags.Count > 0)
            {
                allTags.ForEach(NotifyTagChange);
            }

            return result;
        }

        protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
        {
            return Task.FromResult(WithBatch(batch =>
            {
                var nid = NumericId(persistenceId);

                // seek to first existing message
                var fromSequenceNr = WithIterator<long>(iter =>
                {
                    var startKey = new Key(nid, 1L, 0);
                    iter.Seek(KeyToBytes(startKey));
                    return iter.Valid() ? KeyFromBytes(iter.Peek().Key).SequenceNr : long.MaxValue;
                });

                if (fromSequenceNr != long.MaxValue)
                {
                    var toSeqNr = Math.Min(toSequenceNr, ReadHighestSequenceNr(nid));
                    var sequenceNr = fromSequenceNr;
                    while (sequenceNr <= toSeqNr)
                    {
                        batch.Delete(KeyToBytes(new Key(nid, sequenceNr, 0)));
                        sequenceNr++;
                    }
                }

                return NotUsed.Instance;
            }));
        }

        private ReadOptions RocksDbSnapshot() => RocksDbReadOptions.SetSnapshot(Database.CreateSnapshot());

        private T WithIterator<T>(Func<Iterator, T> body)
        {
            var ro = RocksDbSnapshot();
            var iterator = Database.NewIterator(cf: null, readOptions: ro);
            try
            {
                return body(iterator);
            }
            finally
            {
                iterator.Dispose();
                //ro.Snapshot().Dispose();
            }
        }

        private T WithBatch<T>(Func<WriteBatch, T> body)
        {
            var batch = new WriteBatch();
            try
            {
                var r = body(batch);
                Database.Write(batch, RocksDbWriteOptions);
                return r;
            }
            finally
            {
                batch.Dispose();
            }
        }

        private byte[] PersistentToBytes(IPersistentRepresentation p) => Serializer.ToBinary(p);

        private IPersistentRepresentation PersistentFromBytes(byte[] a) => Serializer.FromBinary<IPersistentRepresentation>(a);

        private void AddToMessageBatch(IPersistentRepresentation persistent, HashSet<string> tags, WriteBatch batch)
        {
            var persistentBytes = PersistentToBytes(persistent);
            var nid = NumericId(persistent.PersistenceId);
            batch.Put(KeyToBytes(CounterKey(nid)), CounterToBytes(persistent.SequenceNr));
            batch.Put(KeyToBytes(new Key(nid, persistent.SequenceNr, 0)), persistentBytes);

            tags.ForEach(tag =>
            {
                var tagNid = TagNumericId(tag);
                var tagSequenceNr = NextTagSequenceNr(tag);
                batch.Put(KeyToBytes(CounterKey(tagNid)), CounterToBytes(tagSequenceNr));
                batch.Put(KeyToBytes(new Key(tagNid, tagSequenceNr, 0)), persistentBytes);
            });
        }

        private long NextTagSequenceNr(string tag)
        {
            long n;
            if (!tagSequenceNrs.TryGetValue(tag, out n))
            {
                n = ReadHighestSequenceNr(TagNumericId(tag));
            }
            tagSequenceNrs[tag] = n + 1;
            return n + 1;
        }

        private int TagNumericId(string tag) => NumericId(TagAsPersistenceId(tag));

        private string TagAsPersistenceId(string tag) => TagPersistenceIdPrefix + tag;

        protected override void PreStart()
        {
            Database = RocksDbDatabase.Open(RocksDbOptions, Settings.Path);
            lock (_idMapLock)
            {
                _idMap = ReadIdMap();
            }
            base.PreStart();
        }

        protected override void PostStop()
        {
            Database?.Dispose();
            base.PostStop();
        }

        #region Rocksdb Persistence Query
        private bool HasPersistenceIdSubscribers() => persistenceIdSubscribers.Count > 0;

        private void AddPersistenceIdSubscriber(IActorRef subscriber, string persistenceId)
        {
            persistenceIdSubscribers.AddBinding(persistenceId, subscriber);
        }

        private void RemoveSubscriber(IActorRef subscriber)
        {
            var keys = persistenceIdSubscribers.Where(kv => kv.Value.Contains(subscriber)).Select(kv => kv.Key);
            keys.ForEach(key => persistenceIdSubscribers.RemoveBinding(key, subscriber));

            var tagKeys = tagSubscribers.Where(kv => kv.Value.Contains(subscriber)).Select(kv => kv.Key);
            tagKeys.ForEach(key => tagSubscribers.RemoveBinding(key, subscriber));

            allPersistenceIdsSubscribers.Remove(subscriber);
        }

        private bool HasTagSubscribers => tagSubscribers.Count > 0;

        private void AddTagSubscriber(IActorRef subscriber, string tag)
        {
            tagSubscribers.AddBinding(tag, subscriber);
        }

        private bool HasAllPersistenceIdsSubscribers => allPersistenceIdsSubscribers.Count > 0;

        private void AddAllPersistenceIdsSubscriber(IActorRef subscriber)
        {
            allPersistenceIdsSubscribers.Add(subscriber);
            subscriber.Tell(new CurrentPersistenceIds(AllPersistenceIds()));
        }

        private void NotifyPersistenceIdChange(string persistenceId)
        {
            if (persistenceIdSubscribers.ContainsKey(persistenceId))
            {
                var changed = new EventAppended(persistenceId);
                persistenceIdSubscribers[persistenceId].ForEach(s => s.Tell(changed));
            }
        }

        private void NotifyTagChange(string tag)
        {
            if (tagSubscribers.ContainsKey(tag))
            {
                var changed = new TaggedEventAppended(tag);
                tagSubscribers[tag].ForEach(s => s.Tell(changed));
            }
        }

        private void NewPersistenceIdAdded(string id)
        {
            if (HasAllPersistenceIdsSubscribers && !id.StartsWith(TagPersistenceIdPrefix))
            {
                var added = new PersistenceIdAdded(id);
                allPersistenceIdsSubscribers.ForEach(s => s.Tell(added));
            }
        }
        #endregion

        #region Rocksdb IdMapping  

        private const int IdOffset = 10;
        private Dictionary<string, int> _idMap = new Dictionary<string, int>();
        private readonly object _idMapLock = new object();

        /// <summary>
        /// Get the mapped numeric id for the specified persistent actor <paramref name="id"/>. Creates and
        /// stores a new mapping if necessary.
        /// </summary>
        private int NumericId(string id)
        {
            lock (_idMapLock)
            {
                return _idMap.TryGetValue(id, out int v) ? v : WriteIdMapping(id, _idMap.Count + IdOffset);
            }
        }

        private bool IsNewPersistenceId(string id)
        {
            lock (_idMapLock)
            {
                return !_idMap.ContainsKey(id);
            }
        }

        private HashSet<string> AllPersistenceIds()
        {
            lock (_idMapLock)
            {
                return _idMap.Keys.ToHashSet();
            }
        }

        private Dictionary<string, int> ReadIdMap() => WithIterator(iter =>
        {
            iter.Seek(KeyToBytes(MappingKey(IdOffset)));
            return ReadIdMap(new Dictionary<string, int>(), iter);
        });

        private Dictionary<string, int> ReadIdMap(Dictionary<string, int> pathMap, Iterator iter)
        {
            if (!iter.Valid())
            {
                return pathMap;
            }
            else
            {
                Iterator nextEntry = iter.Next();
                var nextKey = KeyFromBytes(nextEntry.Key());
                if (!IsMappingKey(nextKey))
                {
                    return pathMap;
                }
                else
                {
                    var nextVal = Encoding.UTF8.GetString(nextEntry.Value());
                    return ReadIdMap(new Dictionary<string, int>(pathMap) { [nextVal] = nextKey.MappingId }, iter);
                }
            }
        }

        private int WriteIdMapping(string id, int numericId)
        {
            _idMap.Add(id, numericId);
            Database.Put(KeyToBytes(MappingKey(numericId)), Encoding.UTF8.GetBytes(id));
            NewPersistenceIdAdded(id);
            return numericId;
        }

        #endregion
    }
}