using System.IO;
using System.Linq;
using BrokenHeart.Inventory;
using BrokenHeart.Player;
using BrokenHeart.UI;
using BrokenHeart.World;
using BrokenHeart.Crafting;
using BrokenHeart.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using BrokenHeart.Systems.Energy;
using BrokenHeart.Combat;
using UnityEngine.Rendering.Universal;

namespace BrokenHeart.Editor
{
    public static class FoundationUpgrade
    {
        [MenuItem("Broken Heart/Stages/6 - Sanity and Rest")]
        public static void SanityStage()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorSceneManager.OpenScene(PrototypeBuilder.ScenePath);
            var world=Object.FindAnyObjectByType<BlockWorld>();var inventory=Object.FindAnyObjectByType<PlayerInventory>();
            var sanity=GetOrAdd<SanitySystem>(inventory.gameObject);var sleep=GetOrAdd<SleepSystem>(inventory.gameObject);
            Set(sleep,"sanity",sanity);Set(sleep,"motor",inventory.GetComponent<PlayerMotor>());Set(sleep,"inventory",inventory);
            Set(sleep,"vitals",inventory.GetComponent<PlayerVitals>());Set(sleep,"enemies",world.GetComponent<EnemySystem>());
            Set(sleep,"memory",inventory.GetComponent<MemorySystem>());Set(sleep,"mindFocus",inventory.Catalog.Find("crystal_focus"));
            var effects=GetOrAdd<SanityVisuals>(world.gameObject);Set(effects,"sanity",sanity);Set(effects,"observer",inventory.transform);Set(effects,"world",world);
            Set(effects,"eyeSprite",MakeIcon("UnrealEyes",new Color(0.9f,0.8f,0.65f),5));
            Set(effects,"shadowSprite",MakeIcon("Shadow",new Color(0.15f,0.10f,0.2f),4));
            Set(effects,"material",AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/PixelUnlit.mat"));
            var hud=Object.FindAnyObjectByType<WorldStatusHud>();Set(hud,"sanity",sanity);Set(hud,"sleep",sleep);
            Save();Debug.Log("BROKEN HEART: Sanity and rest configured.");
        }
        [MenuItem("Broken Heart/Stages/5 - Living World")]
        public static void LivingStage()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorSceneManager.OpenScene(PrototypeBuilder.ScenePath);
            var world=Object.FindAnyObjectByType<BlockWorld>(); var inventory=Object.FindAnyObjectByType<PlayerInventory>();
            string materialPath="Assets/Game/Materials/PixelLit.mat";
            var lit=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(lit==null) { lit=new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default")); AssetDatabase.CreateAsset(lit,materialPath); }
            world.Map.GetComponent<TilemapRenderer>().sharedMaterial=lit; inventory.GetComponent<SpriteRenderer>().sharedMaterial=lit;
            Set(world.GetComponent<EnergyNetwork>(),"material",lit);
            var weather=GetOrAdd<WorldWeather>(world.gameObject); Set(weather,"world",world); Set(weather,"observer",inventory.transform);
            var clock=GetOrAdd<DayNightSystem>(world.gameObject); Set(clock,"weather",weather); Set(clock,"view",Camera.main);
            Set(clock,"sunlight",Object.FindObjectsByType<Light2D>().First(l=>l.lightType==Light2D.LightType.Global));
            var vitals=GetOrAdd<PlayerVitals>(inventory.gameObject);
            var shade=Asset<EnemyData>("Assets/Game/Data/ForestShade.asset"); shade.damageKind=DamageKind.Mental; shade.sanityDamage=8; shade.biome="forest"; shade.underground=false;
            shade.sprite=MakeIcon("ForestShade",new Color(0.22f,0.18f,0.32f),4); shade.drop=inventory.Catalog.Find("wood"); EditorUtility.SetDirty(shade);
            var crawler=Asset<EnemyData>("Assets/Game/Data/CaveCrawler.asset"); crawler.sanityDamage=3; crawler.underground=true; crawler.speed=1.6f; crawler.damage=10;
            crawler.sprite=MakeIcon("CaveCrawler",new Color(0.4f,0.28f,0.46f),4); crawler.drop=inventory.Catalog.Find("copper"); EditorUtility.SetDirty(crawler);
            var enemies=GetOrAdd<EnemySystem>(world.gameObject); Set(enemies,"world",world); Set(enemies,"player",vitals); Set(enemies,"clock",clock);
            Set(enemies,"material",lit); SetArray(enemies,"types",new[] { shade,crawler });
            var hud=GetOrAdd<WorldStatusHud>(Object.FindAnyObjectByType<PrototypeHud>().gameObject);
            Set(hud,"world",world); Set(hud,"player",inventory.transform); Set(hud,"save",world.GetComponent<SaveSystem>());
            Set(hud,"clock",clock); Set(hud,"weather",weather); Set(hud,"vitals",vitals);
            Save(); Debug.Log("BROKEN HEART: Living world configured.");
        }
        [MenuItem("Broken Heart/Stages/4 - Crystal Energy")]
        public static void EnergyStage()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorSceneManager.OpenScene(PrototypeBuilder.ScenePath);
            var world = Object.FindAnyObjectByType<BlockWorld>();
            var inventory = Object.FindAnyObjectByType<PlayerInventory>();
            var catalog = inventory.Catalog;
            var generatorData = Asset<EnergyDeviceData>("Assets/Game/Data/CrystalGenerator.asset");
            generatorData.capacity = 40; generatorData.generationPerSecond = 3; generatorData.consumptionPerSecond = 0;
            generatorData.color = new Color(0.5f,0.6f,1); EditorUtility.SetDirty(generatorData);
            Set(catalog.Find("crystal_generator"), "device", generatorData);
            var lamp = CreateItem("Crystal Lamp", "crystal_lamp", MakeIcon("Lamp", new Color(0.94f,0.79f,0.4f), 1), null, ItemCategory.Mechanism);
            var lampData = Asset<EnergyDeviceData>("Assets/Game/Data/CrystalLamp.asset");
            lampData.capacity = 10; lampData.consumptionPerSecond = 1; lampData.generationPerSecond = 0;
            lampData.color = new Color(1,0.8f,0.5f); EditorUtility.SetDirty(lampData); Set(lamp, "device", lampData);
            SetArray(catalog, "items", catalog.Items.Concat(new[] { lamp }).Distinct().ToArray());
            var recipe = MakeRecipe("crystal_lamp", RecipeCategory.Mechanisms, lamp, 1,
                (catalog.Find("crystal"),1), (catalog.Find("copper"),2));
            var crafting = inventory.GetComponent<CraftingSystem>();
            var recipes = crafting.Recipes.Concat(new[] { recipe }).Distinct().ToArray();
            SetArray(crafting,"recipes",recipes); SetArray(inventory.GetComponent<MemorySystem>(),"recipes",recipes);
            var network = GetOrAdd<EnergyNetwork>(world.gameObject);
            Set(network,"world",world); Set(network,"material",AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/PixelUnlit.mat"));
            Set(network,"wireMaterial",AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/PixelUnlit.mat"));
            Set(network,"starterGenerator",catalog.Find("crystal_generator")); Set(network,"starterLamp",lamp);
            Set(inventory.GetComponent<BlockInteraction>(),"energy",network);
            Save(); Debug.Log("BROKEN HEART: Crystal energy configured.");
        }
        [MenuItem("Broken Heart/Stages/3 - Chunk World and Saves")]
        public static void WorldStage()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorSceneManager.OpenScene(PrototypeBuilder.ScenePath);
            var world = Object.FindAnyObjectByType<BlockWorld>();
            var inventory = Object.FindAnyObjectByType<PlayerInventory>();
            var extra = new[] {
                MakeBlock("Wood", new Color(0.43f,0.28f,0.17f), false, 0.7f),
                MakeBlock("Leaves", new Color(0.22f,0.46f,0.22f), false, 0.2f),
                MakeBlock("Copper", new Color(0.74f,0.43f,0.24f), true, 1.3f),
                MakeBlock("Iron", new Color(0.63f,0.69f,0.73f), true, 1.8f),
                MakeBlock("Crystal", new Color(0.35f,0.85f,0.88f), true, 1.5f),
                MakeBlock("Ruin", new Color(0.38f,0.39f,0.45f), true, 2f) };
            SetArray(world, "palette", world.Palette.Concat(extra).Distinct().ToArray());
            var catalog = inventory.Catalog;
            var items = extra.Select(b => CreateItem(b.name, b.Id, b.Sprite, b)).ToArray();
            SetArray(catalog, "items", catalog.Items.Concat(items).Distinct().ToArray());
            var generation = Asset<WorldGenerationData>("Assets/Game/Data/WorldGeneration.asset");
            generation.grass = world.Palette[0]; generation.dirt = world.Palette[1]; generation.stone = world.Palette[2];
            generation.wood = extra[0]; generation.leaves = extra[1]; generation.ruin = extra[5];
            generation.ores = new[] { new OreRule { block=extra[4], minimumDepth=18, threshold=0.69f },
                new OreRule { block=extra[3], minimumDepth=12, threshold=0.68f }, new OreRule { block=extra[2], minimumDepth=5, threshold=0.67f } };
            EditorUtility.SetDirty(generation);
            Set(world, "generation", generation); Set(world, "observer", inventory.transform);
            var so = new SerializedObject(world); so.FindProperty("bounds").rectIntValue = new RectInt(-256,-96,512,160); so.ApplyModifiedPropertiesWithoutUndo();
            var save = GetOrAdd<SaveSystem>(world.gameObject);
            Set(save, "world", world); Set(save, "inventory", inventory); Set(save, "memory", inventory.GetComponent<MemorySystem>());
            var crystal = catalog.Find("crystal");
            MakeRecipe("crystal_focus", RecipeCategory.Magic, catalog.Find("crystal_focus"), 1, (crystal, 2), (catalog.Find("stone"), 4));
            MakeRecipe("crystal_generator", RecipeCategory.Mechanisms, catalog.Find("crystal_generator"), 1,
                (crystal, 4), (catalog.Find("copper"), 6), (catalog.Find("stone"), 6));
            world.Map.ClearAllTiles();
            var generator = new TerrainGenerator(world.Seed, generation);
            for (int y = -32; y < 32; y++) for (int x = -64; x < 64; x++)
            {
                string id = generator.Generate(new Vector2Int(x,y));
                if (id != null) world.Map.SetTile(new Vector3Int(x,y,0), world.Palette.First(b => b.Id == id).Tile);
            }
            Save(); Debug.Log("BROKEN HEART: Chunk world and saves configured.");
        }
        public static BlockData MakeBlock(string name, Color color, bool solid, float hardness)
        {
            var sprite = MakeIcon(name, color, 3);
            var tile = Asset<Tile>("Assets/Game/Tiles/" + name + ".asset");
            tile.sprite = sprite; tile.colliderType = solid ? Tile.ColliderType.Grid : Tile.ColliderType.None; EditorUtility.SetDirty(tile);
            var block = Asset<BlockData>("Assets/Game/Data/" + name + ".asset");
            var so = new SerializedObject(block);
            so.FindProperty("id").stringValue = name.ToLowerInvariant(); so.FindProperty("tile").objectReferenceValue = tile;
            so.FindProperty("solid").boolValue = solid; so.FindProperty("hardness").floatValue = hardness; so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(block); return block;
        }
        [MenuItem("Broken Heart/Stages/2 - Crafting and Memory")]
        public static void CraftingStage()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorSceneManager.OpenScene(PrototypeBuilder.ScenePath);
            var inventory = Object.FindAnyObjectByType<PlayerInventory>();
            var catalog = inventory.Catalog;
            Directory.CreateDirectory("Assets/Game/Data/Recipes");
            var pickaxe = CreateItem("Stone Pickaxe", "stone_pickaxe", MakeIcon("Pickaxe", new Color(0.62f,0.70f,0.75f), 0), null, ItemCategory.Tool);
            var so = new SerializedObject(pickaxe);
            so.FindProperty("maxStack").intValue = 1;
            so.FindProperty("miningPower").floatValue = 2.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
            var focus = CreateItem("Crystal Focus", "crystal_focus", MakeIcon("Focus", new Color(0.53f,0.85f,0.9f), 1), null, ItemCategory.Magic);
            var generator = CreateItem("Crystal Generator", "crystal_generator", MakeIcon("Generator", new Color(0.7f,0.53f,0.9f), 2), null, ItemCategory.Mechanism);
            SetArray(catalog, "items", catalog.Items.Concat(new[] { pickaxe, focus, generator }).Distinct().ToArray());
            var dirt = catalog.Find("dirt"); var stone = catalog.Find("stone"); var grass = catalog.Find("grass");
            var recipes = new[] {
                MakeRecipe("stone_pickaxe", RecipeCategory.Tools, pickaxe, 1, (stone, 5), (dirt, 2)),
                MakeRecipe("grass_blocks", RecipeCategory.Blocks, grass, 4, (dirt, 4)),
                MakeRecipe("crystal_focus", RecipeCategory.Magic, focus, 1, (stone, 8), (grass, 2)),
                MakeRecipe("crystal_generator", RecipeCategory.Mechanisms, generator, 1, (stone, 10), (dirt, 4)) };
            var memory = GetOrAdd<MemorySystem>(inventory.gameObject);
            Set(memory, "inventory", inventory); SetArray(memory, "recipes", recipes);
            var crafting = GetOrAdd<CraftingSystem>(inventory.gameObject);
            Set(crafting, "inventory", inventory); Set(crafting, "memory", memory); SetArray(crafting, "recipes", recipes);
            var hud = Object.FindAnyObjectByType<PrototypeHud>();
            Set(hud, "memory", memory); Set(hud, "crafting", crafting);
            Save();
            Debug.Log("BROKEN HEART: Crafting and memory configured.");
        }

        public static RecipeData MakeRecipe(string id, RecipeCategory category, ItemData output, int count, params (ItemData item, int amount)[] costs)
        {
            var recipe = Asset<RecipeData>("Assets/Game/Data/Recipes/" + id + ".asset");
            var so = new SerializedObject(recipe);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("category").enumValueIndex = (int)category;
            so.FindProperty("output").objectReferenceValue = output;
            so.FindProperty("outputCount").intValue = count;
            var ingredients = so.FindProperty("ingredients"); ingredients.arraySize = costs.Length;
            for (int i = 0; i < costs.Length; i++)
            {
                ingredients.GetArrayElementAtIndex(i).FindPropertyRelative("item").objectReferenceValue = costs[i].item;
                ingredients.GetArrayElementAtIndex(i).FindPropertyRelative("count").intValue = costs[i].amount;
            }
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(recipe);
            return recipe;
        }

        public static Sprite MakeIcon(string name, Color color, int shape)
        {
            string path = "Assets/Game/Sprites/" + name + ".png";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
            {
                bool filled = shape == 0 ? (x > 13 && x < 18 && y > 3 && y < 25) || (y > 23 && y < 28 && x > 4 && x < 28) :
                    shape == 1 ? Mathf.Abs(x - 16) + Mathf.Abs(y - 16) < 14 : x > 4 && x < 28 && y > 3 && y < 29;
                if (shape == 3) filled = true;
                if (shape == 4) filled = y > 3 && y < 26 && x > 3 + (y > 22 ? 3 : 0) && x < 28 - (y > 22 ? 3 : 0);
                if (shape == 5) filled = y > 13 && y < 18 && ((x > 3 && x < 10) || (x > 21 && x < 28));
                Color pixel = filled ? color * (0.8f + ((x * 7 + y * 13 + x * y) % 5) * 0.05f) : Color.clear;
                if (shape == 4 && y > 17 && y < 21 && ((x > 8 && x < 12) || (x > 20 && x < 24))) pixel = new Color(0.95f,0.64f,0.3f);
                if (filled) pixel.a = 1;
                texture.SetPixel(x, y, pixel);
            }
            texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spritePixelsPerUnit = 32;
            importer.filterMode = FilterMode.Point; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false; importer.alphaIsTransparency = true; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        [MenuItem("Broken Heart/Stages/1 - Inventory")]
        public static void InventoryStage()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(PrototypeBuilder.ScenePath);
            var world = Object.FindAnyObjectByType<BlockWorld>();
            var player = Object.FindAnyObjectByType<PlayerMotor>().gameObject;
            var hud = Object.FindAnyObjectByType<PrototypeHud>();
            Directory.CreateDirectory("Assets/Game/Data/Items");
            AssetDatabase.Refresh();
            var items = world.Palette.Select(block => CreateItem(block.name, block.Id, block.Sprite, block)).ToArray();
            var catalog = Asset<ItemCatalog>("Assets/Game/Data/ItemCatalog.asset");
            SetArray(catalog, "items", items);
            var inventory = GetOrAdd<PlayerInventory>(player);
            Set(inventory, "catalog", catalog);
            var dimensions=new SerializedObject(inventory); dimensions.FindProperty("capacity").intValue=36;dimensions.FindProperty("hotbarSize").intValue=9;dimensions.ApplyModifiedPropertiesWithoutUndo();
            SetArray(inventory, "starterItems", items.Take(3).ToArray());
            var dropPath = "Assets/Game/Prefabs/ItemDrop.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<ItemDrop>(dropPath);
            if (prefab == null)
            {
                var drop = new GameObject("Item Drop", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(ItemDrop));
                drop.layer = 2;
                drop.transform.localScale = Vector3.one * 0.4f;
                var renderer = drop.GetComponent<SpriteRenderer>();
                renderer.sprite = items[0].Icon;
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/PixelUnlit.mat");
                renderer.sortingOrder = 12;
                var rb = drop.GetComponent<Rigidbody2D>();
                rb.gravityScale = 1.5f;
                rb.constraints = RigidbodyConstraints2D.FreezeRotation;
                rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                drop.AddComponent<CircleCollider2D>().radius = 0.4f;
                var trigger = drop.AddComponent<CircleCollider2D>();
                trigger.isTrigger = true;
                trigger.radius = 2.7f;
                prefab = PrefabUtility.SaveAsPrefabAsset(drop, dropPath).GetComponent<ItemDrop>();
                Object.DestroyImmediate(drop);
            }
            var spawner = GetOrAdd<DropSpawner>(world.gameObject);
            Set(spawner, "prefab", prefab);
            var interaction = player.GetComponent<BlockInteraction>();
            Set(interaction, "inventory", inventory);
            Set(interaction, "drops", spawner);
            Set(interaction, "hud", hud);
            Set(hud, "inventory", inventory);
            Save();
            Debug.Log("BROKEN HEART: Inventory stage configured.");
        }

        public static ItemData CreateItem(string name, string id, Sprite icon, BlockData block = null, ItemCategory category = ItemCategory.Block)
        {
            var item = Asset<ItemData>("Assets/Game/Data/Items/" + name + ".asset");
            var so = new SerializedObject(item);
            so.FindProperty("id").stringValue = id;
            so.FindProperty("displayName").stringValue = name;
            so.FindProperty("icon").objectReferenceValue = icon;
            so.FindProperty("placeableBlock").objectReferenceValue = block;
            so.FindProperty("category").enumValueIndex = (int)category;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(item);
            return item;
        }
        public static T Asset<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
        public static T GetOrAdd<T>(GameObject go) where T : Component => go.GetComponent<T>() ?? go.AddComponent<T>();
        public static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }
        public static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var array = so.FindProperty(field);
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }
        public static void Save()
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }
    }
}

