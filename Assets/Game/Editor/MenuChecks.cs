using System;
using System.IO;
using System.Linq;
using BrokenHeart.Combat;
using BrokenHeart.Core.Profiles;
using BrokenHeart.Crafting;
using BrokenHeart.UI;
using BrokenHeart.World;
using BrokenHeart.Inventory;
using BrokenHeart.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
namespace BrokenHeart.Editor
{
    [InitializeOnLoad] public static class MenuChecks
    {
        const string Key="BrokenHeart.MenuChecks", Report="Logs/MenuChecks.txt", EditorSceneKey=Key+".EditorScene";
        static int step;
        static double next,deadline;
        static string originalRoot;
        static WorldProfile world;
        static CharacterProfile first,second;
        static SaveData beforeRecipePreview;
        static int initialDirt;
        static int firstExperience;
        static float pausedAt;
        static bool active;
        static MenuChecks()
        {
            EditorApplication.update+=Tick;
            EditorApplication.playModeStateChanged+=state=>{
                if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key,false))Start();
                if(state==PlayModeStateChange.ExitingPlayMode && active)Finish("FAIL: Play Mode interrupted.");
            };
        }
        [MenuItem("Broken Heart/Run Menu Checks")]
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            LobbyStartup.UseLobby();
            SessionState.SetString(EditorSceneKey, EditorSceneManager.GetActiveScene().name);
            Directory.CreateDirectory("Logs");File.WriteAllText(Report,"RUNNING\n");
            SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
        }
        static void Start()
        {
            active=true;step=0;next=EditorApplication.timeSinceStartup+2;deadline=next+100;
            originalRoot=ProfileLibrary.Root;
            ProfileLibrary.Root=Path.GetFullPath("Logs/MenuChecks/"+Guid.NewGuid().ToString("N"));
            Application.runInBackground=true;
        }
        static void Check(bool value,string message)
        {if(!value)throw new Exception(message);File.AppendAllText(Report,"PASS: "+message+"\n");}
        static void Shot(string name)=>ScreenCapture.CaptureScreenshot(Path.GetFullPath("Logs/"+name+".png"));
        static void Tick()
        {
            if(!active)return;
            if(EditorApplication.timeSinceStartup>deadline){Finish("FAIL: Timeout.");return;}
            if(EditorApplication.timeSinceStartup<next || EditorApplication.isCompiling)return;
            next=EditorApplication.timeSinceStartup+1.5;
            try
            {
                var menu=Object.FindAnyObjectByType<FrontEndMenu>();
                switch(step++)
                {
                    case 0:
                        Check(menu != null && menu.Page == "main" && SceneManager.GetActiveScene().path == MenuSetup.Path,
                            $"Play from the {SessionState.GetString(EditorSceneKey, "")} editor scene opens the lobby.");
                        Check(SceneUtility.GetScenePathByBuildIndex(0) == MenuSetup.Path, "The lobby is the first scene in the build.");
                        Check(!SessionContext.Active && Object.FindAnyObjectByType<PlayerInventory>() == null,
                            "The world and player load after a journey starts.");
                        Shot("MenuMain");break;
                    case 1: menu.PreviewPage("character_editor");break;
                    case 2: Shot("MenuCharacter");break;
                    case 3: menu.PreviewPage("world_editor");break;
                    case 4:
                        Shot("MenuWorld");break;
                    case 5:
                        world=new WorldProfile{name="Test world",seed="20261117",width=3000,height=400,difficulty=0};
                        first=new CharacterProfile{name="Test elf",race=4};second=new CharacterProfile{name="Test human",race=0};
                        var library=new LibraryData();library.worlds.Add(world);library.characters.Add(first);library.characters.Add(second);
                        ProfileLibrary.Save(library);Check(ProfileLibrary.Load().characters.Count==2,"Hero and world library roundtrip.");
                        SceneManager.LoadScene("Menu");break;
                    case 6: menu.Launch(world,first);break;
                    case 7:
                        var terrain=Object.FindAnyObjectByType<BlockWorld>();var backpack=Object.FindAnyObjectByType<PlayerInventory>();
                        Check(terrain!=null && terrain.Seed==world.Seed && terrain.Bounds.width==3000 && terrain.Bounds.height==400,"Menu applies selected seed and world dimensions.");
                        Check(Time.timeScale==1,"New world loads without pause or load failure: "+Object.FindAnyObjectByType<SaveSystem>().Status);
                        Check(backpack.GetComponent<SpriteRenderer>().sprite==AssetDatabase.LoadAssetAtPath<HeroData>("Assets/Game/Data/Heroes/elf.asset").sprite,"Selected elf appears in game.");
                        Check(backpack.Model.Capacity==36 && backpack.HotbarSize==9,"Inventory has three storage rows and a separate nine-slot hotbar.");
                        backpack.Select(8);Check(backpack.SelectedIndex==8,"Ninth hotbar slot is selectable.");backpack.Select(0);
                        var save=Object.FindAnyObjectByType<SaveSystem>();var original=save.Capture();
                        var legacy=JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(original));legacy.slots=new SavedSlot[24];legacy.equipment=null;
                        for(int n=0;n<24;n++)legacy.slots[n]=new SavedSlot{id="dirt",count=n+1};
                        save.Restore(legacy);
                        for(int n=0;n<24;n++)if(backpack.Model[(n/8)*9+n%8].Count!=n+1)throw new Exception("Legacy slot migration lost items.");
                        Check(backpack.Model[8].Empty && backpack.Model[17].Empty && backpack.Model[26].Empty,"Legacy 24-slot save expands row by row without item loss.");
                        legacy.slots = new SavedSlot[27];
                        legacy.slots[8] = new SavedSlot { id = "dirt", count = 7 };
                        legacy.slots[26] = new SavedSlot { id = "dirt", count = 3 };
                        save.Restore(legacy);
                        Check(backpack.Model[8].Count == 7 && backpack.Model[26].Count == 3 && backpack.Model[35].Empty,
                            "Legacy 27-slot save preserves hotbar and storage positions.");
                        save.Restore(original);
                        Check(backpack.Progress.Level == 1 && backpack.Progress.TotalExperience == 0,
                            "A hero starts at level 1 with zero experience.");
                        beforeRecipePreview = save.Capture();
                        var partialRecipe = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(beforeRecipePreview));
                        partialRecipe.knownMaterials = new[] { "stone" };
                        for (int i = 0; i < partialRecipe.slots.Length; i++)
                            if (partialRecipe.slots[i].id != "stone") partialRecipe.slots[i] = default;
                        save.Restore(partialRecipe);
                        OpenWorkbench(backpack);Object.FindAnyObjectByType<DayNightSystem>().SetHour(10);break;
                    case 8: Shot("InventoryPartial-uk");break;
                    case 9:
                        var equippedInventory = Object.FindAnyObjectByType<PlayerInventory>();
                        var equippedSave = Object.FindAnyObjectByType<SaveSystem>();
                        equippedSave.Restore(beforeRecipePreview);
                        beforeRecipePreview = null;
                        CheckRecipeLearning(equippedInventory, equippedSave);
                        CheckEquipment(equippedInventory, equippedSave);
                        equippedInventory.Collect(equippedInventory.Catalog.Find("iron_helmet"), 1);
                        firstExperience = equippedInventory.Progress.TotalExperience;
                        initialDirt=equippedInventory.Model.Count(equippedInventory.Catalog.Find("dirt"));
                        equippedInventory.Model.Add(equippedInventory.Catalog.Find("dirt"),7);
                        Object.FindAnyObjectByType<BlockWorld>().Store.Set(new Vector2Int(3,-5),null);
                        Check(Object.FindAnyObjectByType<SaveSystem>().Save(),"Session saves to isolated world file.");
                        equippedInventory.SetOpen(true);
                        break;
                    case 10: Shot("InventoryLayout-uk");break;
                    case 11: Localization.SetLanguage("en");break;
                    case 12: Shot("InventoryLayout-en");break;
                    case 13: Localization.SetLanguage("uk");Object.FindAnyObjectByType<SessionGameBootstrap>().ReturnToMenu(false);break;
                    case 14: menu.Launch(world,second);break;
                    case 15:
                        var t=Object.FindAnyObjectByType<BlockWorld>();var p=Object.FindAnyObjectByType<PlayerInventory>();
                        Check(Time.timeScale==1 && t.Store.Get(new Vector2Int(3,-5))==null,"Second hero loads shared world edits.");
                        Check(p.Model.Count(p.Catalog.Find("dirt"))==initialDirt,"New hero has independent starter inventory.");
                        Check(p.Model.Capacity == 36 && p.Equipped(6).Empty, "A different hero starts without the first hero's gear.");
                        Check(p.Progress.TotalExperience == 0 && !p.GetComponent<MemorySystem>().StudyItems.Any(),
                            "Heroes have independent experience and study items.");
                        Check(p.GetComponent<SpriteRenderer>().sprite==AssetDatabase.LoadAssetAtPath<HeroData>("Assets/Game/Data/Heroes/human.asset").sprite,"Switching heroes applies human appearance.");
                        Object.FindAnyObjectByType<SessionGameBootstrap>().ReturnToMenu(false);break;
                    case 16: menu.Launch(world,first);break;
                    case 17:
                        var restored=Object.FindAnyObjectByType<PlayerInventory>();
                        Check(restored.Model.Count(restored.Catalog.Find("dirt"))==initialDirt+7,"Returning hero restores own inventory.");
                        Check(restored.Model.Capacity == 45 && restored.Equipped(6).Item.Id == "trail_backpack",
                            "Returning hero restores backpack and expanded storage.");
                        Check(restored.Progress.TotalExperience == firstExperience &&
                            restored.GetComponent<MemorySystem>().CanStudy(restored.Catalog.Find("iron_helmet")),
                            "Returning hero restores experience and items awaiting study.");
                        Check(restored.GetComponent<MemorySystem>().KnowsMaterial(restored.Catalog.Find("leaves")) &&
                            restored.Model.Count(restored.Catalog.Find("leaves")) == 0,
                            "Discovered materials survive a session and remain known after consumption.");
                        Check(Object.FindObjectsByType<AudioSource>().Length==0,"Menu and game contain no audio sources.");
                        Object.FindAnyObjectByType<SessionGameBootstrap>().ReturnToMenu(false);break;
                    case 18:
                        Check(menu!=null,"Return to main menu succeeds.");
                        var preferences=ProfileLibrary.Load();preferences.windowWidth=1600;preferences.windowHeight=900;preferences.fullscreen=true;preferences.fps=120;
                        ProfileLibrary.Save(preferences);var loaded=ProfileLibrary.Load();
                        Check(loaded.windowWidth==1600 && loaded.windowHeight==900 && loaded.fullscreen && loaded.fps==120,"Display preferences persist independently of heroes and worlds.");
                        preferences.windowWidth=0;preferences.windowHeight=0;ProfileLibrary.Validate(preferences);
                        Check(preferences.windowWidth==1280 && preferences.windowHeight==720,"Old display preferences receive safe window dimensions.");
                        menu.PreviewPage("settings");break;
                    case 19: Shot("MenuSettings");break;
                    case 20:
                        Check(menu.ApplyDisplay(),"Settings apply action saves preferences.");
                        menu.ChangeLanguage("en");
                        Check(ProfileLibrary.Load().language=="en" && Localization.T("Зберегти")=="Save","English language selection persists and translates menu.");
                        menu.PreviewPage("main");break;
                    case 21: Shot("MenuEnglish");break;
                    case 22:
                        menu.ChangeLanguage("uk");
                        Check(Localization.T("Missing materials")=="Бракує матеріалів","Ukrainian translation covers crafting feedback.");
                        menu.Launch(world, first);break;
                    case 23:
                        var mapInventory = Object.FindAnyObjectByType<PlayerInventory>();
                        mapInventory.SetOpen(true);
                        mapInventory.SetMapOpen(true);
                        Check(mapInventory.IsMapOpen && !mapInventory.IsOpen && Object.FindAnyObjectByType<PrototypeHud>().BlocksWorldPointer,
                            "The map replaces the inventory and blocks world clicks.");
                        break;
                    case 24: Shot("WorldMap");break;
                    case 25:
                        Object.FindAnyObjectByType<PlayerInventory>().SetMapOpen(false);
                        Shot("Minimap");
                        next += 0.5;
                        break;
                    case 26:
                        Object.FindAnyObjectByType<SessionGameBootstrap>().SetPaused(true);
                        pausedAt = Object.FindAnyObjectByType<DayNightSystem>().Elapsed;
                        Check(Time.timeScale == 0, "Pausing stops the game simulation.");
                        break;
                    case 27:
                        Check(Object.FindAnyObjectByType<DayNightSystem>().Elapsed == pausedAt, "The clock stays frozen on the pause screen.");
                        Shot("PauseMenu");
                        break;
                    case 28:
                        var session = Object.FindAnyObjectByType<SessionGameBootstrap>();
                        session.SetPaused(false);
                        Check(Time.timeScale == 1, "Continuing resumes the game simulation.");
                        session.SetPaused(true);
                        Check(session.ReturnToMenu(true, true), "Leaving a paused game can save and request character selection.");
                        break;
                    case 29:
                        Check(menu != null && menu.Page == "characters" && Time.timeScale == 1 && !SessionContext.Active,
                            "Exit to character selection opens the hero library and clears the game session.");
                        menu.Launch(world, first);
                        break;
                    case 30:
                        var journalSave = Object.FindAnyObjectByType<SaveSystem>();
                        var journalState = journalSave.Capture();
                        var journalMemory = Object.FindAnyObjectByType<MemorySystem>();
                        journalState.memories = journalState.memories.Concat(journalMemory.Journal.Select(entry => entry.id)).ToArray();
                        journalState.trailStep = 4;
                        journalState.giantTrace = new Vector2Int(7, 1);
                        journalSave.Restore(journalState);
                        Object.FindAnyObjectByType<PlayerInventory>().SetOpen(true);
                        Check(Object.FindAnyObjectByType<PrototypeHud>().ToggleJournal(), "The inventory opens the completed field journal.");
                        break;
                    case 31: Shot("FieldJournal-uk"); break;
                    case 32:
                        Check(!Object.FindAnyObjectByType<PrototypeHud>().ToggleJournal(), "The journal returns to the inventory.");
                        Object.FindAnyObjectByType<SessionGameBootstrap>().ReturnToMenu(false);
                        break;
                    case 33: Finish("COMPLETE"); break;
                }
            }
            catch(Exception error){Finish("FAIL: "+error);}
        }

        static void CheckRecipeLearning(PlayerInventory inventory, SaveSystem save)
        {
            var original = save.Capture();
            var crafting = inventory.GetComponent<CraftingSystem>();
            OpenWorkbench(inventory);
            var memory = inventory.GetComponent<MemorySystem>();
            var backpack = crafting.Recipes.Single(recipe => recipe.Id == "trail_backpack");
            var shield = crafting.Recipes.Single(recipe => recipe.Id == "wooden_shield");
            var wood = inventory.Catalog.Find("wood");
            var leaves = inventory.Catalog.Find("leaves");
            Check(!memory.CanView(backpack), "A recipe with no discovered materials stays hidden.");
            inventory.Collect(wood, 6);
            Check(memory.CanView(backpack) && memory.KnowsMaterial(wood) && !memory.KnowsMaterial(leaves) &&
                !crafting.CanCraft(backpack, out string reason) && reason == "Undiscovered materials",
                "The first material reveals the recipe and its own requirement while other ingredients stay unknown.");
            inventory.Collect(leaves, 3);
            Check(memory.KnowsMaterial(leaves) && !memory.Knows(backpack) &&
                !crafting.CanCraft(backpack, out reason) && reason == "Missing materials",
                "A discovered ingredient appears immediately while an insufficient quantity still blocks crafting.");
            inventory.Collect(leaves, 1);
            Check(memory.Knows(backpack) && crafting.CanCraft(backpack, out _), "The full quantities enable crafting.");

            inventory.Model.Add(shield.Output, 1);
            Check(!memory.CanStudy(shield.Output), "Items added directly to storage do not qualify as finds.");
            inventory.Model.Remove(shield.Output, 1);
            inventory.Collect(shield.Output, 1);
            Check(!memory.Knows(shield) && memory.CanStudy(shield.Output), "A found item can be studied while its recipe stays locked.");
            var research = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save.Capture()));
            research.experience = 88;
            save.Restore(research);
            OpenWorkbench(inventory);
            Check(memory.CanStudy(shield.Output) && memory.Study(shield.Output) && memory.Knows(shield) &&
                inventory.Model.Count(shield.Output) == 1 && inventory.Progress.TotalExperience == 98 &&
                !memory.Study(shield.Output), "Studying a found item teaches its recipe and awards experience once.");
            Check(crafting.Craft(backpack, out _) && inventory.Progress.Level == 2 && inventory.Progress.ExperienceInLevel == 1,
                "Crafting awards experience and carries surplus across the level threshold.");

            var enemies = Object.FindAnyObjectByType<EnemySystem>();
            var enemy = enemies.Spawn(enemies.Types[0], (Vector2)inventory.transform.position + Vector2.right * 8);
            Check(enemy == null && inventory.Progress.TotalExperience == 101,
                "Paused monster encounters award no experience.");

            var focus = crafting.Recipes.Single(recipe => recipe.Id == "crystal_focus");
            var oldHero = save.Capture();
            oldHero.memories = oldHero.memories.Concat(new[] { "memory.mind_protection" }).ToArray();
            save.Restore(oldHero);
            Check(!memory.Knows(focus) && memory.HasUnknownMaterials(focus),
                "The mind-protection story memory does not identify recipe ingredients.");
            save.Restore(original);
        }

        static void CheckEquipment(PlayerInventory inventory, SaveSystem save)
        {
            OpenWorkbench(inventory);
            var crafting = inventory.GetComponent<CraftingSystem>();
            var backpack = inventory.Catalog.Find("trail_backpack");
            var recipe = crafting.Recipes.Single(entry => entry.Output == backpack);
            inventory.Collect(inventory.Catalog.Find("wood"), 6);
            inventory.Collect(inventory.Catalog.Find("leaves"), 4);
            Check(crafting.Craft(recipe, out _), "The unlocked backpack recipe crafts with gathered materials.");
            int index = FindSlot(inventory, backpack);
            Check(inventory.Equip(index, 6) && inventory.Model.Capacity == 45 && inventory.Equipped(6).Item == backpack,
                "Equipping a backpack adds a storage row.");
            var slots = inventory.Model.Copy();
            var withOverflow = new ItemStack[inventory.Model.Capacity];
            for (int i = 0; i < withOverflow.Length; i++) withOverflow[i] = slots[i];
            withOverflow[44] = new ItemStack(inventory.Catalog.Find("stone"), 1);
            inventory.Model.Restore(withOverflow);
            Check(!inventory.Unequip(6, index) && inventory.Equipped(6).Item == backpack && inventory.Model[44].Count == 1,
                "Unequipping cannot discard an occupied backpack row.");
            inventory.Model.ReplaceWith(slots);
            Check(inventory.Unequip(6, index) && inventory.Model.Capacity == 36 && inventory.Equip(index, 6),
                "An empty backpack row can be removed and added again.");

            var lamp = inventory.Catalog.Find("crystal_lamp");
            inventory.Model.Add(lamp, 2);
            int lampIndex = FindSlot(inventory, lamp);
            Check(!inventory.Equip(lampIndex, 0) && inventory.Equip(lampIndex, 4) && inventory.Model[lampIndex].Count == 1 &&
                inventory.GetComponentInChildren<Light2D>().enabled, "The lamp equips one item in its own slot and lights the hero.");
            var shield = inventory.Catalog.Find("wooden_shield");
            inventory.Model.Add(shield, 1);
            Check(inventory.Equip(FindSlot(inventory, shield), 5) && inventory.DamageTakenFactor < 1,
                "The shield fits its own slot and reduces incoming damage.");
            var snapshot = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save.Capture()));
            Check(inventory.Unequip(4, lampIndex) && !inventory.GetComponentInChildren<Light2D>().enabled,
                "Taking off the lamp disables its light.");
            save.Restore(snapshot);
            Check(inventory.Equipped(4).Item == lamp && inventory.Equipped(5).Item == shield && inventory.Model.Capacity == 45,
                "Equipment and capacity survive a save roundtrip.");
        }

        static void OpenWorkbench(PlayerInventory inventory)
        {
            var system = Object.FindAnyObjectByType<WorkstationSystem>();
            var cell = new Vector2Int(3, 1);
            if (system.At(cell) == null) system.Place(inventory.Catalog.Find("workbench"), cell);
            Check(system.Open(cell), "The crafting menu opens from the placed workbench.");
        }

        static int FindSlot(PlayerInventory inventory, ItemData item)
        {
            for (int i = 0; i < inventory.Model.Capacity; i++)
                if (inventory.Model[i].Item == item) return i;
            return -1;
        }
        static void Finish(string message)
        {
            active=false;SessionState.SetBool(Key,false);SessionContext.Clear();
            if(originalRoot!=null)ProfileLibrary.Root=originalRoot;
            File.AppendAllText(Report,message+"\n");EditorApplication.isPlaying=false;
        }
    }
}








