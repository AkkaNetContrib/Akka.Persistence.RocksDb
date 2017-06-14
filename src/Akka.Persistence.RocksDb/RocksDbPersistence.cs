//-----------------------------------------------------------------------
// <copyright file="RocksDbPersistence.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Persistence.RocksDb
{
    public class RocksDbSettings
    {
        public RocksDbSettings(string path, bool checksum, bool fSync, string replayDispatcher)
        {
            Path = path;
            Checksum = checksum;
            FSync = fSync;
            ReplayDispatcher = replayDispatcher;
        }

        public string Path { get; }

        public bool Checksum { get; }

        public bool FSync { get; }

        public string ReplayDispatcher { get; }

        public static RocksDbSettings Create(Config config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            return new RocksDbSettings(
                path: config.GetString("path"),
                checksum: config.GetBoolean("checksum"),
                fSync: config.GetBoolean("fsync"),
                replayDispatcher: config.GetString("replay-dispatcher"));
        }
    }

    public class RocksDbPersistence : IExtension
    {
        public static RocksDbPersistence Get(ActorSystem system) => system.WithExtension<RocksDbPersistence, RocksDbPersistenceProvider>();
        public static Config DefaultConfig() => ConfigurationFactory.FromResource<RocksDbPersistence>("Akka.Persistence.RocksDb.reference.conf");

        public RocksDbSettings JournalSettings { get; }

        public RocksDbPersistence(ExtendedActorSystem system)
        {
            system.Settings.InjectTopLevelFallback(DefaultConfig());

            JournalSettings = RocksDbSettings.Create(system.Settings.Config.GetConfig("akka.persistence.journal.rocksdb"));
        }
    }

    public class RocksDbPersistenceProvider : ExtensionIdProvider<RocksDbPersistence>
    {
        public override RocksDbPersistence CreateExtension(ExtendedActorSystem system)
        {
            return new RocksDbPersistence(system);
        }
    }
}
