using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Event;

namespace Akka.Persistence.RocksDb.Journal
{
    internal interface ISubscriptionCommand { }

    public sealed class SubscribePersistenceId : ISubscriptionCommand
    {
        public SubscribePersistenceId(string persistenceId)
        {
            PersistenceId = persistenceId;
        }

        public string PersistenceId { get; }
    }

    public sealed class EventAppended : IDeadLetterSuppression
    {
        public EventAppended(string persistenceId)
        {
            PersistenceId = persistenceId;
        }

        public string PersistenceId { get; }
    }


    public sealed class SubscribeAllPersistenceIds : ISubscriptionCommand
    {
        public static SubscribeAllPersistenceIds Instance { get; } = new SubscribeAllPersistenceIds();
        private SubscribeAllPersistenceIds() { }
    }

    public sealed class CurrentPersistenceIds : IDeadLetterSuppression
    {
        public CurrentPersistenceIds(IEnumerable<string> allPersistenceIds)
        {
            AllPersistenceIds = allPersistenceIds.ToImmutableHashSet();
        }

        public IImmutableSet<string> AllPersistenceIds { get; }
    }

    public sealed class PersistenceIdAdded : IDeadLetterSuppression
    {
        public PersistenceIdAdded(string persistenceId)
        {
            PersistenceId = persistenceId;
        }

        public string PersistenceId { get; }
    }

    public sealed class SubscribeTag : ISubscriptionCommand
    {
        public SubscribeTag(string tag)
        {
            Tag = tag;
        }

        public string Tag { get; }
    }

    public sealed class TaggedEventAppended : IDeadLetterSuppression
    {
        public TaggedEventAppended(string tag)
        {
            Tag = tag;
        }

        public string Tag { get; }
    }

    public sealed class ReplayTaggedMessages : ISubscriptionCommand
    {
        public ReplayTaggedMessages(long fromSequenceNr, long toSequenceNr, long max, string tag, IActorRef replyTo)
        {
            FromSequenceNr = fromSequenceNr;
            ToSequenceNr = toSequenceNr;
            Max = max;
            Tag = tag;
            ReplyTo = replyTo;
        }

        public long FromSequenceNr { get; }

        public long ToSequenceNr { get; }

        public long Max { get; }

        public string Tag { get; }

        public IActorRef ReplyTo { get; }
    }

    public sealed class ReplayedTaggedMessage : IDeadLetterSuppression, INoSerializationVerificationNeeded 
    {
        public ReplayedTaggedMessage(IPersistentRepresentation persistent, string tag, long offset)
        {
            Persistent = persistent;
            Tag = tag;
            Offset = offset;
        }

        public IPersistentRepresentation Persistent { get; }

        public string Tag { get; }

        public long Offset { get; }
    }
}