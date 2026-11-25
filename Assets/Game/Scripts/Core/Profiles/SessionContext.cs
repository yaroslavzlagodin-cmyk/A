using System;
using System.IO;
using BrokenHeart.Systems;
using UnityEngine;
namespace BrokenHeart.Core.Profiles
{
    public static class SessionContext
    {
        public static WorldProfile World { get; private set; }
        public static CharacterProfile Character { get; private set; }
        internal static bool OpenCharacterSelection { get; set; }
        public static bool Active=>World!=null && Character!=null;
        public static void Begin(WorldProfile world,CharacterProfile character)
        {
            if(character.dead)throw new InvalidOperationException("Ця подорож героя завершена.");
            World=JsonUtility.FromJson<WorldProfile>(JsonUtility.ToJson(world));
            Character=JsonUtility.FromJson<CharacterProfile>(JsonUtility.ToJson(character));
        }
        public static void Clear(){World=null;Character=null;OpenCharacterSelection=false;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnPlayStart()=>Clear();
        public static void Save(SaveData data)
        {
            CharacterProfile updated = null;
            ProfileLibrary.UpdateSnapshot(snapshot =>
            {
                // The world, backpack, and hero's position advance together.
                var library = snapshot.Library;
                var character = FindCharacter(library);
                if (data.seed != World.Seed) throw new InvalidDataException("Seed збереження не збігається зі світом.");
                character.slots = data.slots;
                character.equipment = data.equipment;
                character.memories = data.memories;
                character.selected = data.selected;
                character.health = data.health;
                character.experience = data.experience;
                character.studyItems = data.studyItems;
                character.knownMaterials = data.knownMaterials;
                character.sanity = data.sanity;
                character.awakeSeconds = data.awakeSeconds;
                character.collapsed = data.collapsed;
                character.dead = Character.dead;

                var journey = library.journeys.Find(j => j.worldId == World.id && j.characterId == character.id);
                if (journey == null)
                {
                    journey = new JourneyPosition { worldId = World.id, characterId = character.id };
                    library.journeys.Add(journey);
                }
                journey.position = data.position;
                library.lastWorld = World.id;
                library.lastCharacter = character.id;
                snapshot.WriteWorld(World.id, data);
                updated = character;
            });
            Character = updated;
        }

        public static SaveData Load(SaveData fresh, Action<SaveData> validate, out bool recovered)
        {
            SaveData loaded = null;
            CharacterProfile loadedCharacter = null;
            var selected = ProfileLibrary.ReadSnapshot(snapshot =>
            {
                var library = snapshot.Library;
                var character = FindCharacter(library);
                var data = snapshot.ReadWorld(World.id) ?? fresh.Copy();
                if (data.seed != World.Seed) throw new InvalidDataException("Seed збереження не збігається зі світом.");

                // A new hero has an empty saved slot array and receives the starting backpack.
                data.slots = character.slots != null && character.slots.Length > 0 ? character.slots : fresh.slots;
                data.equipment = character.equipment;
                data.memories = character.memories ?? fresh.memories;
                data.experience = character.experience;
                data.studyItems = character.studyItems;
                data.knownMaterials = character.knownMaterials;
                data.selected = character.selected;
                data.health = character.health;
                data.sanity = character.sanity;
                data.awakeSeconds = character.awakeSeconds;
                data.collapsed = character.collapsed;
                data.position = library.journeys.Find(j => j.worldId == World.id && j.characterId == character.id)?.position
                    ?? new Vector3(0.5f, 4, 0);

                validate(data);
                loadedCharacter = character;
                loaded = data;
            });
            Character = loadedCharacter;
            recovered = selected.Recovered;
            return loaded;
        }

        private static CharacterProfile FindCharacter(LibraryData library)
        {
            var world = library.worlds.Find(w => w.id == World.id);
            if (world == null) throw new InvalidOperationException("Світу немає в бібліотеці.");
            if (world.Seed != World.Seed) throw new InvalidOperationException("Seed світу змінився після запуску.");
            var character = library.characters.Find(c => c.id == Character.id);
            if (character == null) throw new InvalidOperationException("Героя немає в бібліотеці.");
            if (character.dead) throw new InvalidOperationException("Цей герой загинув остаточно.");
            return character;
        }
    }
}
