using System;
using System.IO;
using System.Linq;
using BrokenHeart.World;
using BrokenHeart.Inventory;
using BrokenHeart.Crafting;
using BrokenHeart.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace BrokenHeart.Editor
{
    // Native-resolution crops from the user's approved sheet. Never regenerate these images.
    public static class UserApprovedTextures
    {
        const string Root = "Assets/Game/Sprites/UserApproved/";
        static readonly string[] Ids = { "leaves", "grass_cover", "stone", "wood", "grass", "ruin", "mossy_rubble", "planks", "dirt", "stone_bricks", "mossy_stone_bricks" };
        static readonly string[] Images = { "Leaves", "GrassCover", "Stone", "Wood", "GrassSoil", "Rubble", "MossyRubble", "Planks", "Dirt", "StoneBricks", "MossyStoneBricks" };
        static readonly string[] Names = { "Leaves", "Grass Cover", "Stone", "Wood", "Grass", "Ruin", "Mossy Rubble", "Oak Planks", "Dirt", "Stone Bricks", "Mossy Stone Bricks" };

        [MenuItem("Broken Heart/Apply User Approved Textures")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(PrototypeBuilder.ScenePath);
            var world = UnityEngine.Object.FindAnyObjectByType<BlockWorld>();
            var inventory = UnityEngine.Object.FindAnyObjectByType<PlayerInventory>();
            var blocks = world.Palette.ToList();
            var items = inventory.Catalog.Items.ToList();
            for (int i = 0; i < Ids.Length; i++)
            {
                string path = Root + Images[i] + ".png";
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = width;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false; // Do not dilate or alter source RGB.
                importer.isReadable = true;
                importer.SaveAndReimport();
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) throw new Exception("Missing sprite: " + path);
                var block = blocks.FirstOrDefault(b => b.Id == Ids[i]);
                var tile = block != null ? block.Tile : FoundationUpgrade.Asset<Tile>("Assets/Game/Tiles/" + Images[i] + ".asset");
                tile.sprite = sprite;
                tile.color = Color.white;
                // The supplied screenshot has 140/141-pixel crops. Fit their native pixels to one cell without resampling PNGs.
                tile.transform = Matrix4x4.Scale(new Vector3(1, (float)width / height, 1));
                tile.flags = TileFlags.LockColor | TileFlags.LockTransform;
                if (block == null)
                {
                    block = FoundationUpgrade.Asset<BlockData>("Assets/Game/Data/" + Images[i] + ".asset");
                    var data = new SerializedObject(block);
                    data.FindProperty("id").stringValue = Ids[i];
                    data.FindProperty("tile").objectReferenceValue = tile;
                    data.FindProperty("hardness").floatValue = Ids[i] == "planks" ? 0.6f : 1f;
                    data.FindProperty("solid").boolValue = true;
                    data.FindProperty("breakable").boolValue = true;
                    data.ApplyModifiedPropertiesWithoutUndo();
                    blocks.Add(block);
                }
                tile.colliderType = block.Solid ? Tile.ColliderType.Grid : Tile.ColliderType.None;
                EditorUtility.SetDirty(tile);
                EditorUtility.SetDirty(block);
                var item = items.FirstOrDefault(it => it.Id == Ids[i]);
                if (item == null) items.Add(FoundationUpgrade.CreateItem(Names[i], Ids[i], sprite, block));
                else FoundationUpgrade.Set(item, "icon", sprite);
            }
            FoundationUpgrade.SetArray(world, "palette", blocks.ToArray());
            FoundationUpgrade.SetArray(inventory.Catalog, "items", items.ToArray());
            var catalog = inventory.Catalog;
            var additions = new[] {
                FoundationUpgrade.MakeRecipe("oak_planks", RecipeCategory.Blocks, catalog.Find("planks"), 4, (catalog.Find("wood"), 1)),
                FoundationUpgrade.MakeRecipe("stone_bricks", RecipeCategory.Blocks, catalog.Find("stone_bricks"), 4, (catalog.Find("stone"), 4)),
                FoundationUpgrade.MakeRecipe("mossy_rubble", RecipeCategory.Blocks, catalog.Find("mossy_rubble"), 1, (catalog.Find("ruin"), 1), (catalog.Find("leaves"), 1)),
                FoundationUpgrade.MakeRecipe("mossy_stone_bricks", RecipeCategory.Blocks, catalog.Find("mossy_stone_bricks"), 1, (catalog.Find("stone_bricks"), 1), (catalog.Find("leaves"), 1))
            };
            var crafting = inventory.GetComponent<CraftingSystem>();
            var recipes = crafting.Recipes.Concat(additions).Distinct().ToArray();
            FoundationUpgrade.SetArray(crafting, "recipes", recipes);
            FoundationUpgrade.SetArray(inventory.GetComponent<MemorySystem>(), "recipes", recipes);
            PrototypeBuilder.RefreshTerrain();
            FoundationUpgrade.Save();
            Verify();
        }

        public static void Verify()
        {
            EditorSceneManager.OpenScene(PrototypeBuilder.ScenePath);
            var world = UnityEngine.Object.FindAnyObjectByType<BlockWorld>();
            var inventory = UnityEngine.Object.FindAnyObjectByType<PlayerInventory>();
            for (int i = 0; i < Ids.Length; i++)
            {
                string path = Root + Images[i] + ".png";
                var block = world.Palette.Single(b => b.Id == Ids[i]);
                var item = inventory.Catalog.Find(Ids[i]);
                if (AssetDatabase.GetAssetPath(block.Sprite) != path || item.Icon != block.Sprite || item.PlaceableBlock != block)
                    throw new Exception("Incorrect block/icon binding: " + Ids[i]);
                var texture = block.Sprite.texture;
                var source = new Texture2D(2, 2);
                ImageConversion.LoadImage(source, File.ReadAllBytes(path));
                if (!texture.GetPixels32().SequenceEqual(source.GetPixels32())) throw new Exception("Imported pixels differ: " + path);
                UnityEngine.Object.DestroyImmediate(source);
                if (texture.filterMode != FilterMode.Point || texture.mipmapCount != 1 || block.Tile.color != Color.white)
                    throw new Exception("Incorrect pixel rendering settings: " + path);
                if (Ids[i] == "leaves" && !texture.GetPixels32().Any(p => p.a == 0)) throw new Exception("Leaves need transparent holes.");
            }
            foreach (string id in new[] { "planks", "stone_bricks", "mossy_rubble", "mossy_stone_bricks" })
                if (!inventory.GetComponent<CraftingSystem>().Recipes.Any(r => r.Output == inventory.Catalog.Find(id)))
                    throw new Exception("Missing recipe: " + id);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/UserApprovedTextures.txt", "PASS: 11 native-resolution source crops bound to blocks and inventory icons; imported pixels match PNGs exactly; point filtering, no mipmaps, transparent leaves; 4 new blocks craftable.\n");
            Debug.Log("USER APPROVED TEXTURES: PASS");
        }
    }
}
