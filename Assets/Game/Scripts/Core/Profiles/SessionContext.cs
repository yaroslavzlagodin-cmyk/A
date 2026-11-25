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
            var library=ProfileLibrary.Load();
            var character=library.characters.Find(c=>c.id==Character.id) ?? throw new InvalidDataException("Героя немає в бібліотеці.");
            character.slots=data.slots;character.equipment=data.equipment;character.memories=data.memories;character.selected=data.selected;character.health=data.health;
            character.experience = data.experience; character.studyItems = data.studyItems;
            character.knownMaterials = data.knownMaterials;
            character.sanity=data.sanity;character.awakeSeconds=data.awakeSeconds;character.collapsed=data.collapsed;character.dead=Character.dead;
            var position=library.journeys.Find(j=>j.worldId==World.id && j.characterId==Character.id);
            if(position==null){position=new JourneyPosition {worldId=World.id,characterId=Character.id};library.journeys.Add(position);}
            position.position=data.position;library.lastWorld=World.id;library.lastCharacter=Character.id;
            SaveFileStore.WriteAtomic(ProfileLibrary.WorldPath(World.id),JsonUtility.ToJson(data,true));
            ProfileLibrary.Save(library);Character=character;
        }
        public static SaveData Load(SaveData fresh)
        {
            var library=ProfileLibrary.Load();
            var character=library.characters.Find(c=>c.id==Character.id) ?? throw new InvalidDataException("Героя немає в бібліотеці.");
            if(character.dead)throw new InvalidDataException("Цей герой загинув остаточно.");
            string path=ProfileLibrary.WorldPath(World.id);
            var data=File.Exists(path)?JsonUtility.FromJson<SaveData>(SaveFileStore.Read(path)):fresh;
            if(data==null || data.seed!=World.Seed)throw new InvalidDataException("Seed збереження не збігається зі світом.");
            // JsonUtility serializes an uninitialized array as []; a new hero still needs starter slots.
            data.slots=character.slots!=null && character.slots.Length>0 ? character.slots : fresh.slots;
            data.equipment=character.equipment;
            data.memories=character.memories??fresh.memories;
            data.experience = character.experience; data.studyItems = character.studyItems;
            data.knownMaterials = character.knownMaterials;
            data.selected=character.selected;data.health=character.health;data.sanity=character.sanity;data.awakeSeconds=character.awakeSeconds;data.collapsed=character.collapsed;
            data.position=library.journeys.Find(j=>j.worldId==World.id && j.characterId==character.id)?.position??new Vector3(0.5f,4,0);
            Character=character;return data;
        }
    }
}
