using System;
using System.Linq;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Persistence.Query.RocksDb;
using Akka.Streams;
using Akka.Streams.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Akka.Actor;

namespace Akka.Persistence.RocksDb.Tests.Query
{
    public class RocksDbPersistenceIdsSpec : Akka.TestKit.Xunit2.TestKit
    {
        public static readonly AtomicCounter Counter = new AtomicCounter(0);

        public static Config Config(int id) => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.rocksdb""
            akka.persistence.journal.rocksdb {{
                class = ""Akka.Persistence.RocksDb.Journal.RocksDbJournal, Akka.Persistence.RocksDb""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                auto-initialize = on
                path = rocks_pid_{id}.db
            }}
            akka.test.single-expect-default = 10s")
            .WithFallback(RocksDbReadJournal.DefaultConfiguration());


        private readonly ActorMaterializer _materializer;

        public RocksDbPersistenceIdsSpec() : base(Config(Counter.GetAndIncrement()))
        {
            _materializer = Sys.Materializer();
        }

        [Fact]
        public void Sql_query_AllPersistenceIds_should_implement_standard_AllPersistenceIdsQuery()
        {
            var queries = Sys.ReadJournalFor<RocksDbReadJournal>(RocksDbReadJournal.Identifier);
            (queries is IAllPersistenceIdsQuery).Should().BeTrue();
        }

        [Fact]
        public void Sql_query_AllPersistenceIds_should_find_existing_persistence_ids()
        {
            var queries = Sys.ReadJournalFor<RocksDbReadJournal>(RocksDbReadJournal.Identifier);
            Sys.ActorOf(TestKit.TestActor.Props("a")).Tell("a1");
            ExpectMsg("a1-done");
            Sys.ActorOf(TestKit.TestActor.Props("b")).Tell("b1");
            ExpectMsg("b1-done");
            Sys.ActorOf(TestKit.TestActor.Props("c")).Tell("c1");
            ExpectMsg("c1-done");

            var source = queries.CurrentPersistenceIds();
            var probe = source.RunWith(this.SinkProbe<string>(), _materializer);
            probe.Within(TimeSpan.FromSeconds(10), () =>
                probe.Request(5)
                    .ExpectNextUnordered("a", "b", "c")
                    .ExpectComplete());
        }

        [Fact]
        public void Sql_query_AllPersistenceIds_should_find_new_persistence_ids()
        {
            var queries = Sys.ReadJournalFor<RocksDbReadJournal>(RocksDbReadJournal.Identifier);
            Sys.ActorOf(TestKit.TestActor.Props("a")).Tell("a1");
            ExpectMsg("a1-done");
            Sys.ActorOf(TestKit.TestActor.Props("b")).Tell("b1");
            ExpectMsg("b1-done");
            Sys.ActorOf(TestKit.TestActor.Props("c")).Tell("c1");
            ExpectMsg("c1-done");

            var source = queries.CurrentPersistenceIds();
            var probe = source.RunWith(this.SinkProbe<string>(), _materializer);
            probe.Within(TimeSpan.FromSeconds(10), () =>
                probe.Request(5)
                    .ExpectNextUnordered("a", "b", "c")
                    .ExpectComplete());
            // a, b, c created by previous step

            Sys.ActorOf(TestKit.TestActor.Props("d")).Tell("d1");
            ExpectMsg("d1-done");

            source = queries.AllPersistenceIds();
            var newprobe = source.RunWith(this.SinkProbe<string>(), _materializer);
            newprobe.Within(TimeSpan.FromSeconds(10), () =>
            {
                newprobe.Request(5).ExpectNextUnordered("a", "b", "c", "d");

                Sys.ActorOf(TestKit.TestActor.Props("e")).Tell("e1");
                newprobe.ExpectNext("e");

                var more = Enumerable.Range(1, 100).Select(i => "f" + i).ToArray();
                foreach (var x in more)
                    Sys.ActorOf(TestKit.TestActor.Props(x)).Tell(x);

                newprobe.Request(100);
                return newprobe.ExpectNextUnorderedN(more);
            });
        }

        protected override void Dispose(bool disposing)
        {
            _materializer.Dispose();
            base.Dispose(disposing);
        }
    }
}
