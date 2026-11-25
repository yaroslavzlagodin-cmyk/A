using System.IO;
using System.Linq;
using BrokenHeart.Core.Profiles;
using BrokenHeart.Inventory;
using BrokenHeart.UI;
using BrokenHeart.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BrokenHeart.Editor
{
    public static class MenuSetup
    {
        public const string Path="Assets/Game/Scenes/Menu.unity";
        [MenuItem("Broken Heart/Setup Imported Menu")]
        public static void Build()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            Directory.CreateDirectory("Assets/Game/Data/Heroes");AssetDatabase.Refresh();
            string[] ids={"human","dwarf","golem","orc","elf"},names={"Людина","Гном","Голем","Орк","Ельф"};
            string[] colors={"#55c7f3","#e9a34e","#929cac","#79bf65","#bd8bea"};
            string[] lore={"Людина — допитливий мандрівник, що шукає загублені таємниці.","Гном — мешканець підземних залів і знавець давніх копалень.","Голем — кам'яний вартовий, пробуджений енергією глибин.","Орк — відважний воїн, якого кличуть невідомі землі.","Ельф — уважний дослідник лісів і забутих стежок."};
            for(int i=0;i<5;i++)
            {
                ColorUtility.TryParseHtmlString(colors[i],out var color);
                var hero=FoundationUpgrade.Asset<HeroData>("Assets/Game/Data/Heroes/"+ids[i]+".asset");
                hero.id=ids[i];hero.displayName=names[i];hero.description=lore[i];hero.color=color;hero.sprite=HeroSprite(ids[i],color);EditorUtility.SetDirty(hero);
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(PrototypeBuilder.ScenePath);
            var heroes=ids.Select(id=>AssetDatabase.LoadAssetAtPath<HeroData>("Assets/Game/Data/Heroes/"+id+".asset")).ToArray();
            var world=Object.FindAnyObjectByType<BlockWorld>();var player=Object.FindAnyObjectByType<PlayerInventory>();
            var bootstrap=FoundationUpgrade.GetOrAdd<SessionGameBootstrap>(world.gameObject);
            FoundationUpgrade.Set(bootstrap,"world",world);FoundationUpgrade.Set(bootstrap,"player",player);FoundationUpgrade.SetArray(bootstrap,"heroes",heroes);
            PrototypeBuilder.RefreshTerrain();FoundationUpgrade.Save();
            if(File.Exists(Path))EditorSceneManager.OpenScene(Path);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var menu=Object.FindAnyObjectByType<FrontEndMenu>();
            if(menu==null)menu=new GameObject("Start Menu",typeof(FrontEndMenu)).GetComponent<FrontEndMenu>();
            heroes=ids.Select(id=>AssetDatabase.LoadAssetAtPath<HeroData>("Assets/Game/Data/Heroes/"+id+".asset")).ToArray();
            FoundationUpgrade.SetArray(menu,"heroes",heroes);
            FoundationUpgrade.Set(menu,"generation",AssetDatabase.LoadAssetAtPath<WorldGenerationData>("Assets/Game/Data/WorldGeneration.asset"));
            FoundationUpgrade.Set(menu, "mainBackdrop", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Sprites/MenuBackdrop.png"));
            if(Object.FindAnyObjectByType<Camera>()==null)
            {
                var camera=new GameObject("Menu Camera",typeof(Camera)).GetComponent<Camera>();camera.orthographic=true;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=MenuSkin.Background;camera.transform.position=new Vector3(0,0,-10);
            }
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(),Path);
            var scenes=EditorBuildSettings.scenes.Where(s=>s.path!=Path).ToList();scenes.Insert(0,new EditorBuildSettingsScene(Path,true));EditorBuildSettings.scenes=scenes.ToArray();
            LobbyStartup.UseLobby();
            AssetDatabase.SaveAssets();Debug.Log("BROKEN HEART: Into the Depths menu and five heroes adapted to Unity.");
        }
        private static Sprite HeroSprite(string id,Color color)
        {
            string path="Assets/Game/Sprites/Hero_"+id+".png";
            var existing=AssetDatabase.LoadAssetAtPath<Sprite>(path);if(existing!=null)return existing;
            var texture=new Texture2D(32,64,TextureFormat.RGBA32,false);
            for(int y=0;y<64;y++)for(int x=0;x<32;x++)
            {
                Color pixel=x>=4 && x<28 && y>=2 && y<62?color:Color.clear;
                if(y>=44 && y<51 && ((x>=9 && x<15)||(x>=21 && x<27)))pixel=Color.white;
                if(y>=45 && y<49 && ((x>=12 && x<15)||(x>=24 && x<27)))pixel=new Color32(22,37,61,255);
                texture.SetPixel(x,y,pixel);
            }
            texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());Object.DestroyImmediate(texture);AssetDatabase.ImportAsset(path);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.spritePixelsPerUnit=32;
            importer.filterMode=FilterMode.Point;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=false;importer.alphaIsTransparency=true;
            importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
