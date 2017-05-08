using Akka.Actor;
using Akka.Persistence.RocksDb.Journal;
using Akka.Streams.Actors;

namespace Akka.Persistence.Query.RocksDb
{
    public class AllPersistenceIdsPublisher : ActorPublisher<string>
    {
        private readonly bool _liveQuery;
        private readonly DeliveryBuffer<string> _buffer;
        private readonly IActorRef _journal;

        public AllPersistenceIdsPublisher(bool liveQuery, string writeJournalPluginId)
        {
            _liveQuery = liveQuery;
            _journal = Persistence.Instance.Apply(Context.System).JournalFor(writeJournalPluginId);
            _buffer = new DeliveryBuffer<string>(OnNext);
        }

        protected override bool Receive(object message)
        {
            if (message is Request)
            {
                _journal.Tell(SubscribeAllPersistenceIds.Instance);
                Context.Become(Active);
                return true;
            }
            else if (message is Cancel)
            {
                Context.Stop(Self);
                return true;
            }
            else return false;
        }

        private bool Active(object message)
        {
            if (message is CurrentPersistenceIds current)
            {
                _buffer.AddRange(current.AllPersistenceIds);
                _buffer.DeliverBuffer(TotalDemand);

                if (!_liveQuery && _buffer.IsEmpty)
                    OnCompleteThenStop();

                return true;
            }
            else if (message is PersistenceIdAdded added)
            {
                if (_liveQuery)
                {
                    _buffer.Add(added.PersistenceId);
                    _buffer.DeliverBuffer(TotalDemand);
                }
                return true;
            }
            else if (message is Request)
            {
                _buffer.DeliverBuffer(TotalDemand);
                if (!_liveQuery && _buffer.IsEmpty)
                    OnCompleteThenStop();
                return true;
            }
            else if (message is Cancel)
            {
                Context.Stop(Self);
                return true;
            }
            else return false;
        }

        public static Props Props(bool liveQuery, string writeJournalPluginId)
        {
            return Actor.Props.Create(() => new AllPersistenceIdsPublisher(liveQuery, writeJournalPluginId));
        }
    }
}
