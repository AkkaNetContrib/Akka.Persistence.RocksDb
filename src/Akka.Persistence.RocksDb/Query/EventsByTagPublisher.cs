using System;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Query;
using Akka.Persistence.RocksDb.Journal;
using Akka.Streams.Actors;

namespace Akka.Persistence.RocksDb.Query
{
    public class EventsByTagPublisher
    {
        public sealed class Continue
        {
            public static Continue Instance { get; } = new Continue();
            private Continue() { }
        }

        public static Props Props(string tag, long fromOffset, long toOffset, TimeSpan? refreshInterval, int maxBufferSize, string writeJournalPluginId)
        {
            return refreshInterval.HasValue
                ? Actor.Props.Create(() => new LiveEventsByTagPublisher(tag, fromOffset, toOffset, refreshInterval.Value, maxBufferSize, writeJournalPluginId))
                : Actor.Props.Create(() => new CurrentEventsByTagPublisher(tag, fromOffset, toOffset, maxBufferSize, writeJournalPluginId));
        }
    }

    internal abstract class AbstractEventsByTagPublisher : ActorPublisher<EventEnvelope>
    {
        protected readonly DeliveryBuffer<EventEnvelope> Buffer;

        protected AbstractEventsByTagPublisher(string tag, long fromOffset, int maxBufferSize, string writeJournalPluginId)
        {
            Tag = tag;
            FromOffset = fromOffset;
            MaxBufferSize = maxBufferSize;
            WriteJournalPluginId = writeJournalPluginId;
            JournalRef = Persistence.Instance.Apply(Context.System).JournalFor(writeJournalPluginId);
            CurrentOffset = fromOffset;

            Buffer = new DeliveryBuffer<EventEnvelope>(OnNext);
        }

        protected ILoggingAdapter Log { get; } = Context.GetLogger();
        protected string Tag { get; }
        protected long FromOffset { get; }
        protected int MaxBufferSize { get; }
        protected string WriteJournalPluginId { get; }
        protected readonly IActorRef JournalRef;
        protected long CurrentOffset;
        protected long ToOffset { get; set; }

        protected override bool Receive(object message)
        {
            return Init(message);
        }

        protected bool Init(object message)
        {
            switch (message)
            {
                case Request _:
                    ReceiveInitialRequest();
                    return true;
                case EventsByPersistenceIdPublisher.Continue _:
                    // skip, wait for first Request
                    return true;
                case Cancel _:
                    Context.Stop(Self);
                    return true;
            }

            return false;
        }

        protected abstract void ReceiveInitialRequest();

        protected bool Idle(object message)
        {
            switch (message)
            {
                case EventsByPersistenceIdPublisher.Continue _:
                case TaggedEventAppended _:
                    if (IsTimeForReplay) Replay();
                    return true;
                case Request _:
                    ReceiveIdleRequest();
                    return true;
                case Cancel _:
                    Context.Stop(Self);
                    return true;
            }

            return false;
        }

        protected abstract void ReceiveIdleRequest();

        protected bool IsTimeForReplay => (Buffer.IsEmpty || Buffer.Length <= MaxBufferSize / 2) && (CurrentOffset <= ToOffset);

        protected void Replay()
        {
            var limit = MaxBufferSize - Buffer.Length;
            Log.Debug("Request replay for tag [{0}] from [{1}] to [{2}] limit [{3}]", Tag, CurrentOffset, ToOffset, limit);
            JournalRef.Tell(new ReplayTaggedMessages(CurrentOffset, ToOffset, limit, Tag, Self));
            Context.Become(Replaying(limit));
        }

        protected Receive Replaying(int limit)
        {
            return message =>
            {
                switch (message)
                {
                    case ReplayedTaggedMessage replayed:
                        Buffer.Add(new EventEnvelope(
                            offset: new Sequence(replayed.Offset),
                            persistenceId: replayed.Persistent.PersistenceId,
                            sequenceNr: replayed.Persistent.SequenceNr,
                            @event: replayed.Persistent.Payload));

                        CurrentOffset = replayed.Offset;
                        Buffer.DeliverBuffer(TotalDemand);
                        return true;
                    case RecoverySuccess success:
                        Log.Debug("Replay completed for tag [{0}], currentOffset [{1}]", Tag, CurrentOffset);
                        ReceiveRecoverySuccess(success.HighestSequenceNr);
                        return true;
                    case ReplayMessagesFailure failure:
                        Log.Debug("Replay failed for tag [{0}], due to [{1}]", Tag, failure.Cause.Message);
                        Buffer.DeliverBuffer(TotalDemand);
                        OnErrorThenStop(failure.Cause);
                        return true;
                    case Request _:
                        Buffer.DeliverBuffer(TotalDemand);
                        return true;
                    case EventsByPersistenceIdPublisher.Continue _:
                    case EventAppended _:
                        // skip during replay
                        return true;
                    case Cancel _:
                        Context.Stop(Self);
                        return true;
                }

                return false;
            };
        }

        protected abstract void ReceiveRecoverySuccess(long highestSequenceNr);
    }

    internal sealed class LiveEventsByTagPublisher : AbstractEventsByTagPublisher
    {
        private readonly ICancelable _tickCancelable;
        public LiveEventsByTagPublisher(string tag, long fromOffset, long toOffset, TimeSpan refreshInterval, int maxBufferSize, string writeJournalPluginId)
            : base(tag, fromOffset, maxBufferSize, writeJournalPluginId)
        {
            ToOffset = toOffset;
            _tickCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(refreshInterval, refreshInterval, Self, EventsByTagPublisher.Continue.Instance, Self);
        }

        protected override void PostStop()
        {
            _tickCancelable.Cancel();
            base.PostStop();
        }

        protected override void ReceiveInitialRequest()
        {
            JournalRef.Tell(new SubscribeTag(Tag));
            Replay();
        }

        protected override void ReceiveIdleRequest()
        {
            Buffer.DeliverBuffer(TotalDemand);
            if (Buffer.IsEmpty && CurrentOffset > ToOffset)
                OnCompleteThenStop();
        }

        protected override void ReceiveRecoverySuccess(long highestSequenceNr)
        {
            Buffer.DeliverBuffer(TotalDemand);
            if (Buffer.IsEmpty && CurrentOffset > ToOffset)
                OnCompleteThenStop();

            Context.Become(Idle);
        }
    }

    internal sealed class CurrentEventsByTagPublisher : AbstractEventsByTagPublisher
    {
        public CurrentEventsByTagPublisher(string tag, long fromOffset, long toOffset, int maxBufferSize, string writeJournalPluginId)
            : base(tag, fromOffset, maxBufferSize, writeJournalPluginId)
        {
            ToOffset = toOffset;
        }

        protected override void ReceiveInitialRequest()
        {
            Replay();
        }

        protected override void ReceiveIdleRequest()
        {
            Buffer.DeliverBuffer(TotalDemand);
            if (Buffer.IsEmpty && CurrentOffset >= ToOffset)
                OnCompleteThenStop();
            else
                Self.Tell(EventsByTagPublisher.Continue.Instance);
        }

        protected override void ReceiveRecoverySuccess(long highestSequenceNr)
        {
            Buffer.DeliverBuffer(TotalDemand);
            if (highestSequenceNr < ToOffset)
                ToOffset = highestSequenceNr;

            if (Buffer.IsEmpty && (CurrentOffset >= ToOffset || CurrentOffset == FromOffset))
                OnCompleteThenStop();
            else
                Self.Tell(EventsByTagPublisher.Continue.Instance); // more to fetch

            Context.Become(Idle);
        }
    }
}