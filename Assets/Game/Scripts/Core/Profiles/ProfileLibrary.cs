using System;
using System.Collections.Generic;
using System.IO;
using BrokenHeart.Systems;
using BrokenHeart.World;
using UnityEngine;
namespace BrokenHeart.Core.Profiles
{
    [Serializable] public sealed class WorldProfile
    {
        public string id=Guid.NewGuid().ToString("N"), name="Мій світ", seed="1207";
        public int width=3000, height=400, difficulty=2, resources=2, layout;
        public float terrainScale=1, biomeScale=1, mountainHeight=28;
        public bool mountains=true, favorite;
        public int Seed => SeedFromText(seed);
        public static int SeedFromText(string text)
        {
            if(int.TryParse(text,out int number))return number;
            uint hash=2166136261;
            foreach(char c in text ?? "") { hash^=c;hash=unchecked(hash*16777619); }
            return unchecked((int)hash);
        }
        public void Apply(WorldGenerationData data)
        {
            data.mountains=mountains; data.mountainHeight=mountainHeight;data.terrainScale=terrainScale;data.biomeScale=biomeScale;
            data.biomeLayout=layout;data.resourceMultiplier=new[] {2.5f,1.5f,1,0.65f,0.3f}[resources];
        }
    }
    [Serializable] public sealed class CharacterProfile
    {
        public string id=Guid.NewGuid().ToString("N"),name="Мандрівник";
        public int race,challenge=1;
        public bool favorite,dead;
        public SavedSlot[] slots, equipment;
        public string[] memories, studyItems, knownMaterials;
        public int experience;
        public int selected;
        public float health=100,sanity=100,awakeSeconds;
        public bool collapsed;
        public int sanityStage;
    }
    [Serializable] public sealed class JourneyPosition { public string worldId,characterId; public Vector3 position; public int trailStep; public Vector2Int giantTrace; }
    [Serializable] public sealed class LibraryData
    {
        public int version=1;
        public List<WorldProfile> worlds=new();
        public List<CharacterProfile> characters=new();
        public List<JourneyPosition> journeys=new();
        public string lastWorld,lastCharacter;
        public bool fullscreen;
        public string language="uk";
        public int fps=60;
        public int windowWidth=1280,windowHeight=720;
    }
    public static class ProfileLibrary
    {
        private static string LegacyRoot => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Saves");
        private static string DefaultRoot => Application.isEditor ? LegacyRoot : Path.Combine(Application.persistentDataPath, "Saves");
        private static string rootOverride;
        public static string Root { get => rootOverride ?? DefaultRoot; set => rootOverride = value; }
        public static bool ValidId(string id)=>Guid.TryParseExact(id,"N",out _);

        public static LibraryData Load() => ReadSnapshot().Library;
        public static bool HasSavedWorld(string id) => ReadSnapshot().HasWorld(id);
        public static void Save(LibraryData library) => UpdateSnapshot(snapshot => snapshot.Library = library);

        internal static SaveSnapshot ReadSnapshot(Action<SaveSnapshot> validate = null)
        {
            EnsureLegacyImport();
            return SaveTransactionStore.Read(Root, validate);
        }

        internal static void UpdateSnapshot(Action<SaveSnapshot> change)
        {
            EnsureLegacyImport();
            SaveTransactionStore.Update(Root, change);
        }

        private static void EnsureLegacyImport()
        {
            // The standalone player can move to another folder and still find its saves.
            if (!Application.isEditor && Root == DefaultRoot)
                SaveTransactionStore.Import(Root, LegacyRoot);
        }
        public static void Validate(LibraryData data)
        {
            if(data==null || data.version!=1 || data.worlds==null || data.characters==null || data.journeys==null)throw new InvalidDataException("Пошкоджена бібліотека.");
            if(data.windowWidth<800 || data.windowWidth>7680 || data.windowHeight<450 || data.windowHeight>4320)
            { data.windowWidth=1280;data.windowHeight=720; }
            data.fps=Mathf.Clamp(data.fps,30,240);
            var ids=new HashSet<string>();
            foreach(var world in data.worlds)
                if(world==null || !ValidId(world.id) || !ids.Add(world.id) || string.IsNullOrWhiteSpace(world.name) || string.IsNullOrEmpty(world.seed) ||
                    world.width<128 || world.width>20000 || world.height<160 || world.height>2400 || world.difficulty<0 || world.difficulty>5 || world.resources<0 || world.resources>4 ||
                    world.layout<0 || world.layout>2 || !float.IsFinite(world.terrainScale) || world.terrainScale<0.25f || world.terrainScale>3 ||
                    !float.IsFinite(world.biomeScale) || world.biomeScale<0.25f || world.biomeScale>3 || !float.IsFinite(world.mountainHeight) || world.mountainHeight<0 || world.mountainHeight>48)
                    throw new InvalidDataException("Некоректний профіль світу.");
            ids.Clear();
            foreach(var character in data.characters)
                if(character==null || !ValidId(character.id) || !ids.Add(character.id) || string.IsNullOrWhiteSpace(character.name) || character.race<0 || character.race>4 || character.challenge<0 || character.challenge>5)
                    throw new InvalidDataException("Некоректний профіль героя.");
            var journeys = new HashSet<(string World, string Character)>();
            foreach (var journey in data.journeys)
            {
                if (journey == null || !ValidId(journey.worldId) || !ValidId(journey.characterId) ||
                    !journeys.Add((journey.worldId, journey.characterId)) ||
                    !float.IsFinite(journey.position.x) || !float.IsFinite(journey.position.y) || !float.IsFinite(journey.position.z))
                    throw new InvalidDataException("Некоректна позиція подорожі.");
            }
        }
    }
}

