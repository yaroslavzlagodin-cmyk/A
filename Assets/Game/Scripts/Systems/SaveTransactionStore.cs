using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BrokenHeart.Core.Profiles;
using BrokenHeart.World;
using UnityEngine;

namespace BrokenHeart.Systems
{
    public sealed class SaveSnapshot
    {
        public LibraryData Library { get; set; }
        public bool Recovered { get; }
        internal Dictionary<string, SaveData> Worlds { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> ChangedWorlds { get; } = new(StringComparer.OrdinalIgnoreCase);

        internal SaveSnapshot(LibraryData library, bool recovered = false)
        {
            Library = library;
            Recovered = recovered;
        }

        public bool HasWorld(string id) => ReadWorld(id) != null;

        public SaveData ReadWorld(string id)
        {
            RequireWorldId(id);
            return Worlds.TryGetValue(id, out var data) ? data : null;
        }

        // Call inside Update to persist a changed world with the matching library.
        public void WriteWorld(string id, SaveData data)
        {
            RequireWorldId(id);
            Worlds[id] = data ?? throw new ArgumentNullException(nameof(data));
            ChangedWorlds.Add(id);
        }

        private static void RequireWorldId(string id)
        {
            if (!SaveTransactionStore.ValidId(id)) throw new InvalidDataException("Invalid world ID.");
        }
    }

    public static class SaveTransactionStore
    {
        public const string ManifestFileName = "save-manifest.json";
        private const int ManifestVersion = 1;
        private static readonly Mutex Gate = new(false, "BrokenHeart.SaveTransactions.v1");
        private static readonly Dictionary<string, string> RejectedRevisions = new(StringComparer.OrdinalIgnoreCase);
        private static readonly UTF8Encoding Utf8 = new(false, true);

#if UNITY_EDITOR
        public static Action<string> CommitCheckpoint { get; set; }
#endif

        [Serializable] private sealed class Manifest
        {
            public int format;
            public string revision, libraryHash;
            public WorldFile[] worlds;
        }

        [Serializable] private sealed class WorldFile
        {
            public string id, revision, hash;
        }

        private enum Source { Empty, Legacy, Primary, Backup }

        private sealed class Loaded
        {
            public readonly SaveSnapshot Snapshot;
            public readonly Source Origin;
            public readonly Manifest Manifest;

            public Loaded(SaveSnapshot snapshot, Source origin, Manifest manifest = null)
            {
                Snapshot = snapshot;
                Origin = origin;
                Manifest = manifest;
            }
        }

        private readonly struct SaveLock : IDisposable
        {
            public void Dispose() => Gate.ReleaseMutex();
        }

        private static SaveLock Enter()
        {
            try { Gate.WaitOne(); }
            catch (AbandonedMutexException) { /* The manifest still identifies the last committed revision. */ }
            return new SaveLock();
        }

        public static SaveSnapshot Read(string root, Action<SaveSnapshot> validate = null)
        {
            using var saveLock = Enter();
            return Load(NormalizeRoot(root), validate).Snapshot;
        }

        public static void Update(string root, Action<SaveSnapshot> change)
        {
            if (change == null) throw new ArgumentNullException(nameof(change));
            root = NormalizeRoot(root);
            using var saveLock = Enter();
            var current = Load(root, null);
            if (current.Origin == Source.Legacy)
            {
                // Keep the old save available as the first transaction's recovery point.
                Commit(root, current, false);
                current = Load(root, null);
            }
            change(current.Snapshot);
            Commit(root, current);
        }

        public static bool Import(string root, string legacyRoot)
        {
            root = NormalizeRoot(root);
            legacyRoot = NormalizeRoot(legacyRoot);
            if (string.Equals(root, legacyRoot, StringComparison.OrdinalIgnoreCase)) return false;
            using var saveLock = Enter();
            string manifest = Path.Combine(root, ManifestFileName);
            if (File.Exists(manifest) || File.Exists(manifest + ".bak")) return false;
            if (Load(root, null).Origin != Source.Empty || File.Exists(Path.Combine(root, "world.json")))
                return false;
            var original = Load(legacyRoot, null);
            if (original.Origin == Source.Empty) return false;
            // Paths in the source manifest belong to the source folder; copy the payloads instead.
            Commit(root, new Loaded(original.Snapshot, Source.Empty));
            return true;
        }

        private static Loaded Load(string root, Action<SaveSnapshot> validate)
        {
            var errors = new List<Exception>();
            string primary = Path.Combine(root, ManifestFileName);
            foreach (var (path, source) in new[] { (primary, Source.Primary), (primary + ".bak", Source.Backup) })
            {
                string manifestJson;
                try { manifestJson = File.ReadAllText(path, Utf8); }
                catch (FileNotFoundException) { continue; }
                catch (DirectoryNotFoundException) { continue; }
                catch (Exception error) when (IsFileOrFormatError(error))
                {
                    errors.Add(error);
                    continue;
                }

                Loaded candidate;
                try
                {
                    var manifest = ParseManifest(manifestJson);
                    if (source == Source.Primary && RejectedRevisions.TryGetValue(root, out string rejected) && rejected == manifest.revision)
                        throw new InvalidDataException("The primary revision failed gameplay validation.");
                    candidate = LoadManifest(root, manifest, source);
                }
                catch (Exception error) when (IsFileOrFormatError(error))
                {
                    errors.Add(error);
                    continue;
                }

                try { validate?.Invoke(candidate.Snapshot); }
                catch (Exception error) when (error is InvalidDataException || error is FormatException)
                {
                    if (source == Source.Primary) RejectedRevisions[root] = candidate.Manifest.revision;
                    errors.Add(error);
                    continue;
                }
                return candidate;
            }

            if (errors.Count > 0)
                throw new InvalidDataException("No valid save revision could be recovered.", new AggregateException(errors));

            var legacy = LoadLegacy(root);
            validate?.Invoke(legacy.Snapshot);
            return legacy;
        }

        private static Manifest ParseManifest(string json)
        {
            var manifest = Parse<Manifest>(json);
            if (manifest.format != ManifestVersion || !ValidId(manifest.revision) || manifest.worlds == null)
                throw new InvalidDataException("Invalid save manifest.");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in manifest.worlds)
                if (record == null || !ValidId(record.id) || !ValidId(record.revision) || !ids.Add(record.id))
                    throw new InvalidDataException("Invalid world reference in save manifest.");
            return manifest;
        }

        private static Loaded LoadManifest(string root, Manifest manifest, Source source)
        {
            var library = ReadPayload<LibraryData>(LibraryFile(root, manifest.revision), manifest.libraryHash);
            var profiles = ValidateLibrary(library);
            var loaded = new Loaded(new SaveSnapshot(library, source == Source.Backup), source, manifest);
            foreach (var record in manifest.worlds)
            {
                if (!profiles.TryGetValue(record.id, out var profile))
                    throw new InvalidDataException("Invalid world reference in save manifest.");

                var world = ReadPayload<SaveData>(WorldFilePath(root, record.revision, record.id), record.hash);
                ValidateWorld(profile, world);
                loaded.Snapshot.Worlds.Add(record.id, world);
            }
            return loaded;
        }

        private static Loaded LoadLegacy(string root)
        {
            string libraryPath = Path.Combine(root, "library.json");
            string worldFolder = Path.Combine(root, "worlds");
            string json = ReadOptional(libraryPath);
            if (json == null)
            {
                if (File.Exists(libraryPath + ".bak") ||
                    (Directory.Exists(worldFolder) && HasLegacyWorldFiles(worldFolder)))
                    throw new InvalidDataException("The legacy library is missing; its world data is still present.");
                return new Loaded(new SaveSnapshot(new LibraryData()), Source.Empty);
            }

            var library = Parse<LibraryData>(json);
            var profiles = ValidateLibrary(library);
            var loaded = new Loaded(new SaveSnapshot(library), Source.Legacy);
            foreach (var profile in profiles.Values)
            {
                string path = Path.Combine(worldFolder, profile.id + ".json");
                string worldJson = ReadOptional(path);
                if (worldJson == null)
                {
                    if (File.Exists(path + ".bak"))
                        throw new InvalidDataException("A legacy world is missing its primary save.");
                    continue;
                }
                var world = Parse<SaveData>(worldJson);
                ValidateWorld(profile, world);
                loaded.Snapshot.Worlds.Add(profile.id, world);
            }
            return loaded;
        }

        private static bool HasLegacyWorldFiles(string folder)
        {
            foreach (string path in Directory.EnumerateFiles(folder))
                if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void Commit(string root, Loaded current, bool reportCheckpoints = true)
        {
            var snapshot = current.Snapshot;
            var profiles = ValidateLibrary(snapshot.Library);
            foreach (string id in snapshot.ChangedWorlds)
                if (!profiles.ContainsKey(id)) throw new InvalidDataException("World save has no matching profile.");
            foreach (var profile in profiles.Values)
                if (snapshot.Worlds.TryGetValue(profile.id, out var world)) ValidateWorld(profile, world);

            string revision = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.Combine(root, "revisions"));
            var previousWorlds = new Dictionary<string, WorldFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in current.Manifest?.worlds ?? Array.Empty<WorldFile>()) previousWorlds.Add(file.id, file);
            var worldFiles = new List<WorldFile>();
            foreach (var profile in snapshot.Library.worlds)
            {
                string id = profile.id;
                if (!snapshot.Worlds.TryGetValue(id, out var data)) continue;
                if (!snapshot.ChangedWorlds.Contains(id) && previousWorlds.TryGetValue(id, out var published))
                {
                    worldFiles.Add(published);
                    continue;
                }
                string hash = WritePayload(WorldFilePath(root, revision, id), data, written => ValidateWorld(profile, written));
                worldFiles.Add(new WorldFile { id = id, revision = revision, hash = hash });
                if (reportCheckpoints) Checkpoint("world-written");
            }

            string libraryHash = WritePayload(LibraryFile(root, revision), snapshot.Library, written => ValidateLibrary(written));
            if (reportCheckpoints) Checkpoint("library-written");
            var manifest = new Manifest { format = ManifestVersion, revision = revision, libraryHash = libraryHash, worlds = worldFiles.ToArray() };
            var oldBackup = current.Origin == Source.Primary ? ReadOldBackup(root) : null;
            PublishManifest(root, current.Origin, manifest, reportCheckpoints);
            RejectedRevisions.Remove(root);
            if (oldBackup != null) PruneOldBackup(root, oldBackup, current.Manifest, manifest);
        }

        private static Manifest ReadOldBackup(string root)
        {
            try
            {
                string json = ReadOptional(Path.Combine(root, ManifestFileName + ".bak"));
                return json == null ? null : ParseManifest(json);
            }
            catch (Exception error) when (IsFileOrFormatError(error)) { return null; }
        }

        private static void PruneOldBackup(string root, Manifest oldBackup, Manifest backup, Manifest primary)
        {
            try
            {
                string revisions = Path.Combine(root, "revisions");
                if ((File.GetAttributes(revisions) & FileAttributes.ReparsePoint) != 0) return;
                // Files shared by the new primary and backup can still belong to any world revision.
                var retained = new HashSet<string>(Files(root, primary), StringComparer.OrdinalIgnoreCase);
                retained.UnionWith(Files(root, backup));
                foreach (string file in Files(root, oldBackup))
                    if (!retained.Contains(file)) File.Delete(file);
            }
            catch (Exception error) when (IsFileOrFormatError(error)) { /* Cleanup can wait for a later save. */ }
        }

        private static IEnumerable<string> Files(string root, Manifest manifest)
        {
            yield return LibraryFile(root, manifest.revision);
            foreach (var file in manifest.worlds) yield return WorldFilePath(root, file.revision, file.id);
        }

        private static void PublishManifest(string root, Source source, Manifest manifest, bool reportCheckpoints)
        {
            string path = Path.Combine(root, ManifestFileName);
            string temporary = path + "." + manifest.revision + ".tmp";
            try
            {
                WriteNew(temporary, Utf8.GetBytes(JsonUtility.ToJson(manifest, true)));
                if (reportCheckpoints) Checkpoint("before-manifest");
                // The manifest is the commit point. A corrupt primary must never displace a verified backup.
                if (File.Exists(path))
                    File.Replace(temporary, path, source == Source.Primary ? path + ".bak" : null);
                else File.Move(temporary, path);
                if (reportCheckpoints) Checkpoint("after-manifest");
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static Dictionary<string, WorldProfile> ValidateLibrary(LibraryData library)
        {
            ProfileLibrary.Validate(library);
            var worlds = new Dictionary<string, WorldProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var world in library.worlds)
                if (!ValidId(world.id) || !worlds.TryAdd(world.id, world)) throw new InvalidDataException("Invalid or duplicate world ID.");
            var characters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var character in library.characters)
                if (!ValidId(character.id) || !characters.Add(character.id)) throw new InvalidDataException("Invalid or duplicate character ID.");
            return worlds;
        }

        private static void ValidateWorld(WorldProfile profile, SaveData world)
        {
            if (world == null || world.version != 1 || world.seed != profile.Seed ||
                world.generatorVersion < WorldGenerationData.LegacyVersion || world.generatorVersion > WorldGenerationData.CurrentVersion ||
                world.blocks == null || world.drops == null ||
                !float.IsFinite(world.elapsed) || world.elapsed < 0 || (int)world.weather < 0 || (int)world.weather > 3)
                throw new InvalidDataException("Invalid or incompatible world payload.");
        }

        internal static bool ValidId(string value) => value?.Length == 32 && Guid.TryParseExact(value, "N", out _);

        private static string NormalizeRoot(string root)
        {
            string fullPath = Path.GetFullPath(root);
            return fullPath == Path.GetPathRoot(fullPath) ? fullPath : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string LibraryFile(string root, string revision) =>
            Path.Combine(root, "revisions", revision + ".library.json");

        private static string WorldFilePath(string root, string revision, string worldId) =>
            Path.Combine(root, "revisions", revision + "." + worldId + ".world.json");

        private static bool IsFileOrFormatError(Exception error) =>
            error is IOException || error is InvalidDataException || error is UnauthorizedAccessException ||
            error is ArgumentException || error is FormatException;

        private static string ReadOptional(string path)
        {
            try { return File.ReadAllText(path, Utf8); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }

        private static T Parse<T>(string json) where T : class =>
            JsonUtility.FromJson<T>(json) ?? throw new InvalidDataException("Missing save payload.");

        private static T ReadPayload<T>(string path, string expectedHash) where T : class
        {
            var bytes = File.ReadAllBytes(path);
            if (!string.Equals(Hash(bytes), expectedHash, StringComparison.Ordinal))
                throw new InvalidDataException("Save payload checksum mismatch.");
            return Parse<T>(Utf8.GetString(bytes));
        }

        private static string WritePayload<T>(string path, T data, Action<T> validate) where T : class
        {
            var bytes = Utf8.GetBytes(JsonUtility.ToJson(data, true));
            WriteNew(path, bytes);
            string hash = Hash(bytes);
            validate(ReadPayload<T>(path, hash));
            return hash;
        }

        private static void WriteNew(string path, byte[] bytes)
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(bytes, 0, bytes.Length);
            file.Flush(true);
        }

        private static string Hash(byte[] bytes)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private static void Checkpoint(string name)
        {
#if UNITY_EDITOR
            CommitCheckpoint?.Invoke(name);
#endif
        }
    }
}
