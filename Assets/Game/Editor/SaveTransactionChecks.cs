using System;
using System.IO;
using BrokenHeart.Core.Profiles;
using BrokenHeart.Systems;
using BrokenHeart.World;
using UnityEditor;
using UnityEngine;

namespace BrokenHeart.Editor
{
    public static class SaveTransactionChecks
    {
        [MenuItem("Broken Heart/Run Save Transaction Checks")]
        public static void Run()
        {
            string root = Path.GetFullPath(Path.Combine("Logs", "Roadmap", "Checks", Guid.NewGuid().ToString("N")));
            string current = Path.Combine(root, "current");
            string legacy = Path.Combine(root, "legacy");
            var first = new WorldProfile { seed = "86" };
            var second = new WorldProfile { seed = "87" };
            var hero = new CharacterProfile();
            var library = new LibraryData();
            library.worlds.Add(first);
            library.worlds.Add(second);
            library.characters.Add(hero);
            library.journeys.Add(new JourneyPosition { worldId = first.id, characterId = hero.id });
            var oldWorld = MakeWorld(first, WorldGenerationData.LegacyVersion, 1);
            string oldLibraryPath = Path.Combine(legacy, "library.json");
            string oldWorldPath = Path.Combine(legacy, "worlds", first.id + ".json");
            SaveFileStore.WriteAtomic(oldLibraryPath, JsonUtility.ToJson(library));
            SaveFileStore.WriteAtomic(oldWorldPath, JsonUtility.ToJson(oldWorld));
            string originalLibrary = File.ReadAllText(oldLibraryPath);
            string originalWorld = File.ReadAllText(oldWorldPath);

            Require(SaveTransactionStore.Import(current, legacy), "Legacy import did not create a save.");
            Require(!SaveTransactionStore.Import(current, legacy), "Legacy import replaced existing data.");
            Require(File.ReadAllText(oldLibraryPath) == originalLibrary && File.ReadAllText(oldWorldPath) == originalWorld,
                "Legacy import changed an original file.");
            Require(SaveTransactionStore.Read(current).ReadWorld(first.id).generatorVersion == WorldGenerationData.LegacyVersion,
                "The old world changed generator versions.");

            SaveTransactionStore.Update(current, save => save.WriteWorld(second.id, MakeWorld(second, 3, 40)));
            WriteJourney(current, first, 5);
            foreach (string point in new[] { "world-written", "library-written", "before-manifest", "after-manifest" })
            {
                SaveTransactionStore.CommitCheckpoint = reached =>
                {
                    if (reached == point) throw new IOException("Injected interruption.");
                };
                bool interrupted = false;
                try { WriteJourney(current, first, 7); }
                catch (IOException error) when (error.Message == "Injected interruption.") { interrupted = true; }
                finally { SaveTransactionStore.CommitCheckpoint = null; }
                Require(interrupted, "The interruption checkpoint was skipped.");
                CheckJourney(SaveTransactionStore.Read(current), first, second, point == "after-manifest" ? 7 : 5);
            }

            var rejected = SaveTransactionStore.Read(current, candidate =>
            {
                if (candidate.ReadWorld(first.id).elapsed == 7) throw new InvalidDataException("Rejected by gameplay.");
            });
            Require(rejected.Recovered, "A rejected candidate did not select the backup.");
            CheckJourney(rejected, first, second, 5);
            File.WriteAllText(Path.Combine(current, SaveTransactionStore.ManifestFileName), "{");
            var recovered = SaveTransactionStore.Read(current);
            Require(recovered.Recovered, "A corrupt manifest did not select the backup.");
            CheckJourney(recovered, first, second, 5);
            WriteJourney(current, first, 10);
            CheckJourney(SaveTransactionStore.Read(current), first, second, 10);
            Debug.Log("SAVE TRANSACTION CHECKS: PASS");
        }

        private static SaveData MakeWorld(WorldProfile world, int generator, int elapsed) => new()
        {
            seed = world.Seed,
            generatorVersion = generator,
            elapsed = elapsed,
            blocks = Array.Empty<BlockEdit>(),
            drops = Array.Empty<SavedDrop>()
        };

        private static void WriteJourney(string root, WorldProfile world, int value)
        {
            SaveTransactionStore.Update(root, save =>
            {
                save.Library.characters[0].experience = value;
                save.Library.journeys[0].position = new Vector3(value, 4, 0);
                save.WriteWorld(world.id, MakeWorld(world, WorldGenerationData.CurrentVersion, value));
            });
        }

        private static void CheckJourney(SaveSnapshot snapshot, WorldProfile first, WorldProfile second, int expected)
        {
            Require(snapshot.ReadWorld(first.id).elapsed == expected &&
                snapshot.Library.characters[0].experience == expected &&
                snapshot.Library.journeys[0].position.x == expected, "World and hero were recovered from different revisions.");
            Require(snapshot.ReadWorld(second.id).elapsed == 40 && snapshot.ReadWorld(second.id).generatorVersion == 3,
                "Another world's progress was overwritten.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
