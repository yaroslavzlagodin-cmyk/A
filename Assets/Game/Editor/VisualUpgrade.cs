using System;
using System.Linq;
using System.IO;
using BrokenHeart.World;
using BrokenHeart.Inventory;
using BrokenHeart.Crafting;
using BrokenHeart.Systems;
using BrokenHeart.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object=UnityEngine.Object;
namespace BrokenHeart.Editor
{
    public static class VisualUpgrade
    {
        [MenuItem("Broken Heart/Apply Terrain Textures")]
        public static void Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play Mode before upgrading.");
            EditorSceneManager.OpenScene(PrototypeBuilder.ScenePath);
            var world=Object.FindAnyObjectByType<BlockWorld>();var inventory=Object.FindAnyObjectByType<PlayerInventory>();
            string[] blocks={"grass","dirt","stone","wood","leaves","copper","iron","crystal","ruin"};
            string[] textures={"GrassSoil","Dirt","Stone","Wood","Leaves","Copper","Iron","Crystal","Rubble"};
            for(int i=0;i<blocks.Length;i++)
            {
                var sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/Sprites/DarkFantasy32/"+textures[i]+".png");
                if(sprite==null)throw new Exception("Missing texture: "+textures[i]);
                var block=world.Palette.First(b=>b.Id==blocks[i]);block.Tile.sprite=sprite;EditorUtility.SetDirty(block.Tile);
                FoundationUpgrade.Set(inventory.Catalog.Find(block.Id),"icon",sprite);
            }
            var cover=FoundationUpgrade.Asset<BlockData>("Assets/Game/Data/GrassCover.asset");
            var tile=FoundationUpgrade.Asset<Tile>("Assets/Game/Tiles/GrassCover.asset");
            tile.sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Game/Sprites/DarkFantasy32/GrassCover.png");tile.colliderType=Tile.ColliderType.Grid;EditorUtility.SetDirty(tile);
            var data=new SerializedObject(cover);data.FindProperty("id").stringValue="grass_cover";data.FindProperty("tile").objectReferenceValue=tile;data.FindProperty("hardness").floatValue=0.3f;data.FindProperty("solid").boolValue=true;data.ApplyModifiedPropertiesWithoutUndo();
            FoundationUpgrade.SetArray(world,"palette",world.Palette.Concat(new[]{cover}).Distinct().ToArray());
            var item=FoundationUpgrade.CreateItem("Grass Cover","grass_cover",tile.sprite,cover);
            FoundationUpgrade.SetArray(inventory.Catalog,"items",inventory.Catalog.Items.Concat(new[]{item}).Distinct().ToArray());
            var recipe=FoundationUpgrade.MakeRecipe("grass_cover",RecipeCategory.Blocks,item,4,(inventory.Catalog.Find("leaves"),1),(inventory.Catalog.Find("dirt"),1));
            var crafting=inventory.GetComponent<CraftingSystem>();var recipes=crafting.Recipes.Concat(new[]{recipe}).Distinct().ToArray();
            FoundationUpgrade.SetArray(crafting,"recipes",recipes);FoundationUpgrade.SetArray(inventory.GetComponent<MemorySystem>(),"recipes",recipes);
            var rubble=inventory.Catalog.Find("ruin");
            var rubbleRecipe=FoundationUpgrade.MakeRecipe("rubble",RecipeCategory.Blocks,rubble,1,(inventory.Catalog.Find("stone"),1));
            recipes=recipes.Concat(new[]{rubbleRecipe}).Distinct().ToArray();FoundationUpgrade.SetArray(crafting,"recipes",recipes);FoundationUpgrade.SetArray(inventory.GetComponent<MemorySystem>(),"recipes",recipes);
            PrototypeBuilder.RefreshTerrain();FoundationUpgrade.Save();
            File.WriteAllText("Logs/VisualUpgrade.txt","COMPLETE: 10 textures connected, grass cover and rubble recipes.\n");
            EditorSceneManager.OpenScene("Assets/Game/Scenes/Menu.unity");
        }
    }
}
