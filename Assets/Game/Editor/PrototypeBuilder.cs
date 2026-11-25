using System.IO;
using System.Linq;
using BrokenHeart.Player;
using BrokenHeart.UI;
using BrokenHeart.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;
using UnityEngine.SceneManagement;

namespace BrokenHeart.Editor
{
    public static class PrototypeBuilder
    {
        public const string ScenePath = "Assets/Game/Scenes/Game.unity";

        [MenuItem("Broken Heart/Create Prototype")]
        public static void Build()
        {
            if (File.Exists(ScenePath))
            {
                Debug.Log("Game scene already exists; keeping your edits. Open Assets/Game/Scenes/Game.unity.");
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string[] folders = { "Scenes", "Scripts/Core", "Scripts/Player", "Scripts/World",
                "Scripts/Inventory", "Scripts/Crafting", "Scripts/Combat", "Scripts/Systems", "Scripts/UI",
                "Prefabs", "Sprites", "Tiles", "Animations", "Audio", "Data", "Materials" };
            foreach (var folder in folders) Directory.CreateDirectory("Assets/Game/" + folder);
            AssetDatabase.Refresh();

            var blocks = new BlockData[3];
            string[] names = { "Grass", "Dirt", "Stone" };
            float[] hardness = { 0.3f, 0.45f, 1.1f };
            for (int i = 0; i < blocks.Length; i++)
            {
                var sprite = CreateSprite(names[i], i);
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = sprite;
                tile.colliderType = Tile.ColliderType.Grid;
                AssetDatabase.CreateAsset(tile, "Assets/Game/Tiles/" + names[i] + ".asset");
                var block = ScriptableObject.CreateInstance<BlockData>();
                var data = new SerializedObject(block);
                data.FindProperty("id").stringValue = names[i].ToLowerInvariant();
                data.FindProperty("tile").objectReferenceValue = tile;
                data.FindProperty("hardness").floatValue = hardness[i];
                data.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(block, "Assets/Game/Data/" + names[i] + ".asset");
                blocks[i] = block;
            }
            var playerSprite = CreateSprite("Player", 3);
            var material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
            AssetDatabase.CreateAsset(material, "Assets/Game/Materials/PixelUnlit.mat");
            var physics = new PhysicsMaterial2D("PlayerFrictionless") { friction = 0, bounciness = 0 };
            AssetDatabase.CreateAsset(physics, "Assets/Game/Materials/PlayerFrictionless.physicsMaterial2D");

            AssetDatabase.SaveAssets();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // NewScene can unload unreferenced assets; resolve fresh persistent references afterwards.
            for (int i = 0; i < blocks.Length; i++)
                blocks[i] = AssetDatabase.LoadAssetAtPath<BlockData>("Assets/Game/Data/" + names[i] + ".asset");
            playerSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/Sprites/Player.png");
            material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/PixelUnlit.mat");
            physics = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>("Assets/Game/Materials/PlayerFrictionless.physicsMaterial2D");
            var grid = new GameObject("World Grid", typeof(Grid));
            grid.GetComponent<Grid>().cellSize = Vector3.one;
            var terrain = new GameObject("Terrain", typeof(Tilemap), typeof(TilemapRenderer),
                typeof(Rigidbody2D), typeof(TilemapCollider2D), typeof(CompositeCollider2D));
            terrain.transform.SetParent(grid.transform);
            terrain.layer = 0; // Default is the terrain mask; the player uses built-in Ignore Raycast.
            terrain.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
            terrain.GetComponent<TilemapCollider2D>().compositeOperation = Collider2D.CompositeOperation.Merge;
            terrain.GetComponent<CompositeCollider2D>().geometryType = CompositeCollider2D.GeometryType.Polygons;
            terrain.GetComponent<TilemapRenderer>().sharedMaterial = material;
            var map = terrain.GetComponent<Tilemap>();
            var world = grid.AddComponent<BlockWorld>();
            Assign(world, "tilemap", map);
            var worldData = new SerializedObject(world);
            var palette = worldData.FindProperty("palette");
            palette.arraySize = blocks.Length;
            for (int i = 0; i < blocks.Length; i++) palette.GetArrayElementAtIndex(i).objectReferenceValue = blocks[i];
            worldData.ApplyModifiedPropertiesWithoutUndo();
            var preview = new BlockStore(world.Bounds);
            preview.Changed += (cell, id) => map.SetTile((Vector3Int)cell, blocks.First(b => b.Id == id).Tile);
            TestTerrain.Fill(preview, world.Bounds, world.Seed, blocks[0].Id, blocks[1].Id, blocks[2].Id);

            var player = new GameObject("Player", typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(BoxCollider2D));
            player.layer = 2;
            player.transform.position = new Vector3(0.5f, 4, 0);
            var visual = player.GetComponent<SpriteRenderer>();
            visual.sprite = playerSprite;
            visual.sharedMaterial = material;
            visual.sortingOrder = 10;
            var body = player.GetComponent<Rigidbody2D>();
            body.gravityScale = 3;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var shape = player.GetComponent<BoxCollider2D>();
            shape.size = new Vector2(0.7f, 1.8f);
            shape.sharedMaterial = physics;
            var motor = player.AddComponent<PlayerMotor>();
            Assign(motor, "visual", visual);
            var motorData = new SerializedObject(motor);
            motorData.FindProperty("groundLayers").intValue = 1;
            motorData.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(player, "Assets/Game/Prefabs/Player.prefab");

            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener),
                typeof(UniversalAdditionalCameraData), typeof(PixelPerfectCamera), typeof(CameraFollow));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0.5f, 5.5f, -10);
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 7.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.40f, 0.68f, 0.82f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100;
            var pixel = cameraObject.GetComponent<PixelPerfectCamera>();
            pixel.assetsPPU = 32;
            pixel.refResolutionX = 854;
            pixel.refResolutionY = 480;
            Assign(cameraObject.GetComponent<CameraFollow>(), "target", player.transform);
            var lightObject = new GameObject("Daylight", typeof(Light2D));
            lightObject.GetComponent<Light2D>().lightType = Light2D.LightType.Global;

            var cursor = new GameObject("Block Cursor", typeof(LineRenderer));
            var line = cursor.GetComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.loop = true;
            line.widthMultiplier = 0.035f;
            line.sortingOrder = 20;
            line.positionCount = 4;
            line.SetPositions(new[] { new Vector3(-0.5f,-0.5f), new Vector3(-0.5f,0.5f),
                new Vector3(0.5f,0.5f), new Vector3(0.5f,-0.5f) });
            var interaction = player.AddComponent<BlockInteraction>();
            Assign(interaction, "world", world);
            Assign(interaction, "view", camera);
            Assign(interaction, "playerShape", shape);
            Assign(interaction, "outline", line);
            Assign(new GameObject("Prototype HUD", typeof(PrototypeHud)).GetComponent<PrototypeHud>(), "interaction", interaction);
            EditorSceneManager.SaveScene(scene, ScenePath);
            var existing = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            existing.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = existing.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("BROKEN HEART: Prototype scene and assets created successfully.");
        }

        [MenuItem("Broken Heart/Refresh Terrain Preview")]
        public static void RefreshTerrain()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var world = Object.FindAnyObjectByType<BlockWorld>();
            if (world == null) throw new System.InvalidOperationException("Open the Game scene first.");
            var serialized = new SerializedObject(world);
            var palette = serialized.FindProperty("palette");
            string[] names = { "Grass", "Dirt", "Stone" };
            if (palette.arraySize < 3) palette.arraySize = 3;
            for (int i = 0; i < 3; i++)
                if (palette.GetArrayElementAtIndex(i).objectReferenceValue == null)
                    palette.GetArrayElementAtIndex(i).objectReferenceValue =
                        AssetDatabase.LoadAssetAtPath<BlockData>("Assets/Game/Data/" + names[i] + ".asset");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            world.Map.ClearAllTiles();
            if (world.Generation != null)
            {
                var generator = new TerrainGenerator(world.Seed, world.Generation);
                for (int y = -32; y < 32; y++) for (int x = -64; x < 64; x++)
                {
                    var id = generator.Generate(new Vector2Int(x,y));
                    if (id != null) world.Map.SetTile(new Vector3Int(x,y,0), world.Palette.First(b => b.Id == id).Tile);
                }
                EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
                EditorSceneManager.SaveScene(world.gameObject.scene);
                return;
            }
            var preview = new BlockStore(world.Bounds);
            preview.Changed += (cell, id) => world.Map.SetTile((Vector3Int)cell, world.Palette.First(b => b.Id == id).Tile);
            TestTerrain.Fill(preview, world.Bounds, world.Seed, world.Palette[0].Id, world.Palette[1].Id, world.Palette[2].Id);
            EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
            EditorSceneManager.SaveScene(world.gameObject.scene);
            Debug.Log("BROKEN HEART: Terrain references and preview refreshed.");
        }

        private static void Assign(Object target, string property, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Sprite CreateSprite(string name, int kind)
        {
            int height = kind == 3 ? 64 : 32;
            var texture = new Texture2D(32, height, TextureFormat.RGBA32, false);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < 32; x++)
                {
                    int noise = (x * 17 + y * 31 + x * y * 3) % 13;
                    Color color;
                    if (kind == 3)
                    {
                        color = Color.clear;
                        if (x >= 6 && x < 26 && y >= 3 && y < 61)
                        {
                            color = y > 43 ? new Color(0.85f,0.65f,0.46f) :
                                y > 19 ? new Color(0.23f,0.42f,0.45f) : new Color(0.20f,0.23f,0.30f);
                            if (y > 55 || (x < 10 && y > 43)) color = new Color(0.22f,0.16f,0.18f);
                            if (x > 21 && y > 48 && y < 52) color = new Color(0.09f,0.1f,0.15f);
                            if (y < 18 && x > 13 && x < 18) color = Color.clear;
                        }
                    }
                    else
                    {
                        color = kind == 2 ? new Color(0.40f,0.44f,0.49f) : new Color(0.45f,0.29f,0.19f);
                        if (kind == 0 && y >= 26 - (x % 5 == 0 ? 2 : 0)) color = new Color(0.32f,0.54f,0.25f);
                        if (noise < 3) color *= 0.86f;
                        if (noise == 8) color *= 1.10f;
                        color.a = 1;
                    }
                    texture.SetPixel(x, y, color);
                }
            texture.Apply();
            string path = "Assets/Game/Sprites/" + name + ".png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 32;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
