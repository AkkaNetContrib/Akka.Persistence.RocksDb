using System;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Persistence.Query.RocksDb;
using Akka.Streams;
using Akka.Streams.TestKit;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;
using Akka.Actor;

namespace Akka.Persistence.RocksDb.Tests.Query
{
    public class RocksDbEventsByTagSpec : Akka.TestKit.Xunit2.TestKit
    {
        public static readonly AtomicCounter Counter = new AtomicCounter(0);

        public static Config Config(int id) => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.rocksdb""
            akka.persistence.journal.rocksdb {{
                event-adapters {{
                  color-tagger  = ""Akka.Persistence.TCK.Query.ColorFruitTagger, Akka.Persistence.TCK""
                }}
                event-adapter-bindings = {{
                  ""System.String"" = color-tagger
                }}
                class = ""Akka.Persistence.RocksDb.Journal.RocksDbJournal, Akka.Persistence.RocksDb""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                auto-initialize = on
                path = rocks_tag_{id}.db
            }}
            akka.test.single-expect-default = 25s")
            .WithFallback(RocksDbReadJournal.DefaultConfiguration());

        protected ActorMaterializer Materializer { get; }

        protected IReadJournal ReadJournal { get; set; }

        public RocksDbEventsByTagSpec(ITestOutputHelper output) 
            : base(Config(Counter.GetAndIncrement()), nameof(RocksDbEventsByTagSpec), output)
        {
            Materializer = Sys.Materializer();
            ReadJournal = Sys.ReadJournalFor<RocksDbReadJournal>(RocksDbReadJournal.Identifier);
        }

        [Fact]
        public void ReadJournal_should_implement_IEventsByTagQuery()
        {
            Assert.IsAssignableFrom<IEventsByTagQuery>(ReadJournal);
        }

        [Fact]
        public virtual void ReadJournal_live_query_EventsByTag_should_find_new_events()
        {
            var queries = ReadJournal as IEventsByTagQuery;

            var b = Sys.ActorOf(TestKit.TestActor.Props("b"));
            var d = Sys.ActorOf(TestKit.TestActor.Props("d"));

            b.Tell("a black car");
            ExpectMsg("a black car-done");

            var blackSrc = queries.EventsByTag("black", offset: 0L);
            var probe = blackSrc.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe.Request(2);
            probe.ExpectNext<EventEnvelope>(p => p.PersistenceId == "b" && p.SequenceNr == 1L && p.Event.Equals("a black car"));
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            d.Tell("a black dog");
            ExpectMsg("a black dog-done");
            d.Tell("a black night");
            ExpectMsg("a black night-done");

            probe.ExpectNext<EventEnvelope>(p => p.PersistenceId == "d" && p.SequenceNr == 1L && p.Event.Equals("a black dog"));
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            probe.Request(10);
            probe.ExpectNext<EventEnvelope>(p => p.PersistenceId == "d" && p.SequenceNr == 2L && p.Event.Equals("a black night"));
            probe.Cancel();
        }

        [Fact]
        public virtual void ReadJournal_live_query_EventsByTag_should_find_events_from_offset()
        {
            var queries = ReadJournal as IEventsByTagQuery;

            var a = Sys.ActorOf(TestKit.TestActor.Props("a"));
            var b = Sys.ActorOf(TestKit.TestActor.Props("b"));
            var c = Sys.ActorOf(TestKit.TestActor.Props("c"));

            a.Tell("hello");
            ExpectMsg("hello-done");
            a.Tell("a green apple");
            ExpectMsg("a green apple-done");
            b.Tell("a black car");
            ExpectMsg("a black car-done");
            a.Tell("something else");
            ExpectMsg("something else-done");
            a.Tell("a green banana");
            ExpectMsg("a green banana-done");
            b.Tell("a green leaf");
            ExpectMsg("a green leaf-done");
            c.Tell("a green cucumber");
            ExpectMsg("a green cucumber-done");

            var greenSrc1 = queries.EventsByTag("green", offset: 0L);
            var probe1 = greenSrc1.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe1.Request(2);
            probe1.ExpectNext<EventEnvelope>(p => p.PersistenceId == "a" && p.SequenceNr == 2L && p.Event.Equals("a green apple"));
            var offs = probe1.ExpectNext<EventEnvelope>(p => p.PersistenceId == "a" && p.SequenceNr == 4L && p.Event.Equals("a green banana"));
            probe1.Cancel();

            var greenSrc2 = queries.EventsByTag("green", offset: offs.Offset);
            var probe2 = greenSrc2.RunWith(this.SinkProbe<EventEnvelope>(), Materializer);
            probe2.Request(10);
            probe2.ExpectNext<EventEnvelope>(p => p.PersistenceId == "b" && p.SequenceNr == 2L && p.Event.Equals("a green leaf"));
            probe2.ExpectNext<EventEnvelope>(p => p.PersistenceId == "c" && p.SequenceNr == 1L && p.Event.Equals("a green cucumber"));
            probe2.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            probe2.Cancel();
        }
    }
}
