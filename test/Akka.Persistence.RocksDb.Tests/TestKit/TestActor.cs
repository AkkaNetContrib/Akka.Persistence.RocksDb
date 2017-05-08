using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Persistence.RocksDb.Tests.TestKit
{
    public class TestActor : PersistentActor
    {
        [Serializable]
        public sealed class DeleteCommand
        {
            public readonly long ToSequenceNr;

            public DeleteCommand(long toSequenceNr)
            {
                ToSequenceNr = toSequenceNr;
            }
        }

        public static Props Props(string persistenceId) => Actor.Props.Create(() => new TestActor(persistenceId));

        public TestActor(string persistenceId)
        {
            PersistenceId = persistenceId;
        }

        public override string PersistenceId { get; }
        protected override bool ReceiveRecover(object message) => true;
        private IActorRef _parentTestActor;

        protected override bool ReceiveCommand(object message) => message.Match()
            .With<DeleteCommand>(delete =>
            {
                _parentTestActor = Sender;
                DeleteMessages(delete.ToSequenceNr);
            })
            .With<DeleteMessagesSuccess>(deleteSuccess =>
            {
                _parentTestActor.Tell(deleteSuccess.ToSequenceNr.ToString() + "-deleted");
            })
            .With<string>(cmd =>
            {
                var sender = Sender;
                Persist(cmd, e => sender.Tell(e + "-done"));
            })
            .WasHandled;
    }
}
