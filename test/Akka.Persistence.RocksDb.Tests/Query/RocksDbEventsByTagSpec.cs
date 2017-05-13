using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.Persistence.Query;
using Akka.Persistence.Query.RocksDb;
using Akka.Streams;
using Akka.Streams.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.RocksDb.Tests.Query
{
    public class EventsByTagSpec : Akka.TestKit.Xunit2.TestKit
    {
        public static readonly AtomicCounter Counter = new AtomicCounter(0);

        public static Config Config(int id) => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.rocksdb""
            akka.persistence.journal.rocksdb {{
                event-adapters {{
                  color-tagger  = ""Akka.Persistence.RocksDb.Tests.Query.ColorTagger, Akka.Persistence.RocksDb.Tests""
                }}
                event-adapter-bindings = {{
                  ""System.String"" = color-tagger
                }}
                class = ""Akka.Persistence.RocksDb.Journal.RocksDbJournal, Akka.Persistence.RocksDb""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                auto-initialize = on
                path = rocks_tag_{id}.db
            }}
            akka.persistence.query.journal.rocksdb.refresh-interval = 1s
            akka.test.single-expect-default = 10s")
            .WithFallback(RocksDbReadJournal.DefaultConfiguration());

        private readonly ActorMaterializer _materializer;

        public EventsByTagSpec() : base(Config(Counter.GetAndIncrement()))
        {
            _materializer = Sys.Materializer();
        }

        [Fact]
        public void RocksDb_query_EventsByTag_should_implement_standard_EventsByTagQuery()
        {
            var queries = Sys.ReadJournalFor<RocksDbReadJournal>(RocksDbReadJournal.Identifier);
            queries.Should().BeAssignableTo<IEventsByTagQuery>();
        }

        [Fact]
        public void RocksDb_query_EventsByTag_should_find_existing_events()
        {
            var queries = Sys.ReadJournalFor<RocksDbReadJournal>(RocksDbReadJournal.Identifier);
            var a = Sys.ActorOf(TestKit.TestActor.Props("a"));
            var b = Sys.ActorOf(TestKit.TestActor.Props("b"));

            a.Tell("hello");
            ExpectMsg("hello-done");
            a.Tell("a green apple");
            ExpectMsg("a green apple-done");
            b.Tell("a black car");
            ExpectMsg("a black car-done");
            a.Tell("a green banana");
            ExpectMsg("a green banana-done");
            b.Tell("a green leaf");
            ExpectMsg("a green leaf-done");

            var greenSrc = queries.CurrentEventsByTag("green", offset: 0L);
            var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), _materializer);
            probe.Request(2)
                .ExpectNext(new EventEnvelope(1L, "a", 2L, "a green apple"))
                .ExpectNext(new EventEnvelope(2L, "a", 3L, "a green banana"));
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            probe.Request(2)
                .ExpectNext(new EventEnvelope(3L, "b", 2L, "a green leaf"))
                .ExpectComplete();// TODO: Complete never raised

            var blackSrc = queries.CurrentEventsByTag("black", offset: 0L);
            probe = blackSrc.RunWith(this.SinkProbe<EventEnvelope>(), _materializer);
            probe.Request(5)
                .ExpectNext(new EventEnvelope(1L, "b", 1L, "a black car"))
                .ExpectComplete();
        }

        [Fact]
        public void RocksDb_query_EventsByTag_should_not_see_new_events_after_demand_request()
        {
            var queries = Sys.ReadJournalFor<RocksDbReadJournal>(RocksDbReadJournal.Identifier);
            RocksDb_query_EventsByTag_should_find_existing_events();

            var c = Sys.ActorOf(TestKit.TestActor.Props("c"));

            var greenSrc = queries.CurrentEventsByTag("green", offset: 0L);
            var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), _materializer);
            probe.Request(2)
                .ExpectNext(new EventEnvelope(1L, "a", 2L, "a green apple"))
                .ExpectNext(new EventEnvelope(2L, "a", 3L, "a green banana"))
                .ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            c.Tell("a green cucumber");
            ExpectMsg("a green cucumber-done");

            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            probe.Request(5)
                .ExpectNext(new EventEnvelope(3L, "b", 2L, "a green leaf"))
                .ExpectComplete(); // green cucumber not seen
        }

        [Fact]
        public void RocksDb_query_EventsByTag_should_find_events_from_offset()
        {
            var queries = Sys.ReadJournalFor<RocksDbReadJournal>(RocksDbReadJournal.Identifier);
            RocksDb_query_EventsByTag_should_not_see_new_events_after_demand_request();

            var greenSrc = queries.CurrentEventsByTag("green", offset: 2L);
            var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), _materializer);
            probe.Request(10)
                .ExpectNext(new EventEnvelope(2L, "a", 3L, "a green banana"))
                .ExpectNext(new EventEnvelope(3L, "b", 2L, "a green leaf"))
                .ExpectNext(new EventEnvelope(4L, "c", 1L, "a green cucumber"))
                .ExpectComplete();
        }

        [Fact]
        public void RocksDb_live_query_EventsByTag_should_find_new_events()
        {
            var queries = Sys.ReadJournalFor<RocksDbReadJournal>(RocksDbReadJournal.Identifier);
            //RocksDb_query_EventsByTag_should_find_events_from_offset(); // TODO: should we run it here?

            var d = Sys.ActorOf(TestKit.TestActor.Props("d"));

            var blackSrc = queries.EventsByTag("black", offset: 0L);
            var probe = blackSrc.RunWith(this.SinkProbe<EventEnvelope>(), _materializer);
            probe.Request(2)
                .ExpectNext(new EventEnvelope(1L, "b", 1L, "a black car"))
                .ExpectNoMsg(TimeSpan.FromMilliseconds(100));

            d.Tell("a black dog");
            ExpectMsg("a black dog-done");
            d.Tell("a black night");
            ExpectMsg("a black night-done");

            probe.ExpectNext(new EventEnvelope(2L, "d", 1L, "a black dog")) // TODO: it returns EventEnvelope(1L, "b", 1L, "a black car") and stop after that
                .ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            probe.Request(10)
                .ExpectNext(new EventEnvelope(3L, "d", 2L, "a black night"));
        }

        [Fact]
        public void RocksDb_live_query_EventsByTag_should_find_events_from_offset()
        {
            var queries = Sys.ReadJournalFor<RocksDbReadJournal>(RocksDbReadJournal.Identifier);
            RocksDb_live_query_EventsByTag_should_find_new_events();

            var greenSrc = queries.EventsByTag("green", offset: 2L);
            var probe = greenSrc.RunWith(this.SinkProbe<EventEnvelope>(), _materializer);
            probe.Request(10)
                .ExpectNext(new EventEnvelope(2L, "a", 3L, "a green banana"))
                .ExpectNext(new EventEnvelope(3L, "b", 2L, "a green leaf"))
                .ExpectNext(new EventEnvelope(4L, "c", 1L, "a green cucumber"))
                .ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        protected override void Dispose(bool disposing)
        {
            _materializer.Dispose();
            base.Dispose(disposing);
        }
    }

    public class ColorTagger : IWriteEventAdapter
    {
        public static readonly IImmutableSet<string> Colors = ImmutableHashSet.CreateRange(new[] { "green", "black", "blue" });
        public string Manifest(object evt) => string.Empty;

        public object ToJournal(object evt)
        {
            var s = evt as string;
            if (s != null)
            {
                var tags = Colors.Aggregate(ImmutableHashSet<string>.Empty, (acc, color) => s.Contains(color) ? acc.Add(color) : acc);
                return tags.IsEmpty
                    ? evt
                    : new Tagged(evt, tags);
            }
            else return evt;
        }
    }
}
