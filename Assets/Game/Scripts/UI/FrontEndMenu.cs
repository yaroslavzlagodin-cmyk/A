using System;
using System.IO;
using System.Linq;
using BrokenHeart.Core.Profiles;
using BrokenHeart.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BrokenHeart.UI
{
    public sealed class FrontEndMenu : MonoBehaviour
    {
        [SerializeField] private HeroData[] heroes;
        [SerializeField] private WorldGenerationData generation;
        [SerializeField] private Texture2D mainBackdrop;
        private LibraryData library;
        private WorldGenerationData previewSettings;
        private string page="main",status="",search="",selectedHero,editingId;
        private string confirmText;
        private Action confirmed;
        private bool storageBlocked;
        private WorldProfile worldDraft;
        private CharacterProfile characterDraft;
        private Vector2 scroll;
        private AsyncOperation loading;
        private DisplaySettingsDraft display;
        private static readonly string[] Difficulties={"Мирна","Легка","Нормальна","Важка","Хардкор","Парад смерті"};
        private static readonly string[] Resources={"Захмарне","Багате","Середнє","Бідне","Тотальна нестача"};
        private static readonly int[] Widths={3000,5000,10000,20000},Depths={400,800,1200,1600};
        private static readonly string[] Sizes={"Малий","Середній","Великий","Гігантський"};
        public string Page=>page;
        private void Awake()
        {
            bool openCharacters = SessionContext.OpenCharacterSelection;
            Time.timeScale=1;SessionContext.Clear();previewSettings=Instantiate(generation);
            try { library=ProfileLibrary.Load();DisplaySettings.Apply(library);Localization.SetLanguage(library.language); }
            catch(Exception error){library=new LibraryData();status=error.Message;storageBlocked=true;}
            if (openCharacters && !storageBlocked) Navigate("characters");
        }
        private void OnDestroy(){if(previewSettings!=null)Destroy(previewSettings);}
        private void Navigate(string next){if(next=="settings")display=new DisplaySettingsDraft(library);page=next;scroll=Vector2.zero;search="";status="";if(Event.current!=null)GUI.FocusControl(null);}
        private bool Persist()
        {
            if(storageBlocked)return false;
            try {ProfileLibrary.Save(library);return true;}catch(Exception error){status="Не вдалося зберегти: "+error.Message;return false;}
        }
        private void OnGUI()
        {
            float scale=Mathf.Min(Screen.width/1280f,Screen.height/720f);
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            var viewport = new Rect((1280 - width) / 2, (720 - height) / 2, width, height);
            var old=GUI.matrix;
            bool isMain=page=="main";
            if (!isMain) MenuSkin.Fill(new Rect(0,0,Screen.width,Screen.height),MenuSkin.Background);
            GUI.matrix=Matrix4x4.TRS(new Vector3((Screen.width-1280*scale)/2,(Screen.height-720*scale)/2,0),Quaternion.identity,Vector3.one*scale);
            if (!isMain) MenuSkin.Backdrop();
            GUI.enabled=confirmed==null && !storageBlocked;
            switch(page)
            {
                case "main":MainPage(viewport);break;
                case "characters":CharacterLibrary();break;
                case "character_editor":CharacterEditor();break;
                case "worlds":WorldLibrary();break;
                case "world_editor":WorldEditor();break;
                case "settings":Settings();break;
                case "loading":MenuSkin.Label(new Rect(380,280,600,70),"Прокидається новий світ…",30,MenuSkin.Cyan);MenuSkin.Fill(new Rect(380,370,520*(loading?.progress??0),8),MenuSkin.Cyan);break;
            }
            GUI.enabled=true;
            if (!isMain) MenuSkin.Label(new Rect(55,679,1170,32),status,15,MenuSkin.Muted);
            if(confirmed!=null)
            {
                MenuSkin.Fill(viewport, new Color(0,0,0,0.7f));
                MenuSkin.Box(new Rect(365,245,550,220));
                MenuSkin.Label(new Rect(390,270,500,100),confirmText,22);
                if(MenuSkin.Button(new Rect(390,390,235,45),"Скасувати"))confirmed=null;
                if(MenuSkin.Button(new Rect(650,390,235,45),"Підтвердити",false,true)){var action=confirmed;confirmed=null;action?.Invoke();}
            }
            GUI.matrix=old;
        }
        private void Heading(string title)
        {MenuSkin.Label(new Rect(55,25,1170,40),title,27,MenuSkin.Cyan);MenuSkin.Box(new Rect(50,80,1180,580));}
        private void MainPage(Rect viewport)
        {
            if (mainBackdrop != null) GUI.DrawTexture(viewport, mainBackdrop, ScaleMode.ScaleAndCrop, false);
            else MenuSkin.Fill(viewport, new Color32(20, 15, 27, 255));
            MenuSkin.MainTitle(new Rect(viewport.xMin + 30, viewport.yMin + 33, 610, 82), "BROKEN HEART");
            MenuSkin.Label(new Rect(viewport.xMin + 34, viewport.yMin + 118, 260, 25), $"Alpha {Application.version}", 17);

            const float gap = 15;
            var button = new Rect(viewport.xMin + 34, viewport.yMax - 287, 228, 54);
            bool controlsEnabled = GUI.enabled;
            if (MenuSkin.MainButton(button, "Нова подорож  →", primary: true)) Navigate("characters");

            var lastHero = library.characters.Find(c => c.id == library.lastCharacter && !c.dead);
            var lastWorld = library.worlds.Find(w => w.id == library.lastWorld);
            bool canContinue = lastHero != null && lastWorld != null;
            button.y += button.height + gap;
            GUI.enabled = controlsEnabled && canContinue;
            if (MenuSkin.MainButton(button, "Продовжити подорож")) Launch(lastWorld, lastHero);
            GUI.enabled = controlsEnabled;

            button.y += button.height + gap;
            if (MenuSkin.MainButton(button, "Налаштування")) Navigate("settings");
            button.y += button.height + gap;
            if (MenuSkin.MainButton(button, "Вийти з гри")) Ask("Завершити гру?", () => {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying=false;
#else
                Application.Quit();
#endif
            });

            var journey = new Rect(viewport.xMax - 502, viewport.yMax - 66, 484, 52);
            string description = canContinue ? lastHero.name + "  /  " + lastWorld.name : "Створи героя. Досліджуй світ. Знайди себе.";
            MenuSkin.MainJourney(journey, description, canContinue);
            if (!string.IsNullOrEmpty(status))
                MenuSkin.Label(new Rect(button.x, viewport.yMax - 340, 700, 48), status, 16, new Color32(255, 207, 220, 255));
        }
        private void CharacterLibrary()
        {
            Heading("ГЕРОЇ");
            if(MenuSkin.Button(new Rect(70,96,220,44),"До головного меню"))Navigate("main");
            if(MenuSkin.Button(new Rect(930,96,280,44),"Створити персонажа",true))EditCharacter(null);
            search=MenuSkin.Input(new Rect(310,96,590,44),search);
            var list=library.characters.Where(c=>c.name.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0).OrderByDescending(c=>c.favorite).ToArray();
            scroll=GUI.BeginScrollView(new Rect(70,160,1140,475),scroll,new Rect(0,0,1110,Mathf.Max(470,list.Length*116)));
            if(list.Length==0)MenuSkin.Label(new Rect(20,35,1050,90),"Персонажів ще немає. Створи свого першого мандрівника.",24);
            for(int i=0;i<list.Length;i++)
            {
                var hero=list[i];float y=i*116;MenuSkin.Box(new Rect(0,y,1100,106));Portrait(new Rect(12,y+5,100,96),hero.race);
                MenuSkin.Label(new Rect(130,y+15,570,32),(hero.favorite?"★ ":"")+hero.name+(hero.dead?" — подорож завершено":""),24,MenuSkin.Cyan);
                MenuSkin.Label(new Rect(130,y+53,570,30),heroes[hero.race].displayName+" • Складність "+(hero.challenge+1)+" / 6",18,MenuSkin.Muted);
                GUI.enabled=!hero.dead && confirmed==null;
                if(MenuSkin.Button(new Rect(740,y+12,170,38),"Обрати",true)){selectedHero=hero.id;Navigate("worlds");}
                GUI.enabled=confirmed==null;
                if(MenuSkin.Button(new Rect(740,y+60,170,34),"Редагувати"))EditCharacter(hero);
                if(MenuSkin.Button(new Rect(925,y+12,75,38),hero.favorite?"★":"☆")){hero.favorite=!hero.favorite;Persist();}
                if(MenuSkin.Button(new Rect(1010,y+12,75,38),"×",false,true))Ask("Прибрати героя «"+hero.name+"» з бібліотеки?",()=>{library.characters.RemoveAll(c=>c.id==hero.id);Persist();});
            }
            GUI.EndScrollView();
        }
        private void EditCharacter(CharacterProfile profile)
        {editingId=profile?.id;characterDraft=profile==null?new CharacterProfile():JsonUtility.FromJson<CharacterProfile>(JsonUtility.ToJson(profile));Navigate("character_editor");}
        private void CharacterEditor()
        {
            Heading(editingId==null?"СТВОРЕННЯ ПЕРСОНАЖА":"НАЛАШТУВАННЯ ПЕРСОНАЖА");
            if(MenuSkin.Button(new Rect(70,96,240,44),"До бібліотеки"))Navigate("characters");
            if(MenuSkin.Button(new Rect(650,96,250,44),"Випадково"))
            {characterDraft.name=new[]{"Астра","Рун","Ейра","Торен","Нокс","Ліра"}[UnityEngine.Random.Range(0,6)];characterDraft.race=UnityEngine.Random.Range(0,heroes.Length);}
            if(MenuSkin.Button(new Rect(920,96,290,44),"Зберегти",true))
            {
                characterDraft.name=characterDraft.name.Trim();if(characterDraft.name.Length==0){status="Введи ім'я героя.";return;}
                int index=library.characters.FindIndex(c=>c.id==editingId);
                if(index<0)library.characters.Add(characterDraft);else library.characters[index]=characterDraft;
                editingId=characterDraft.id;
                if(Persist())Navigate("characters");
            }
            MenuSkin.Box(new Rect(75,166,340,465));
            MenuSkin.Landscape(new Rect(87,178,316,302));
            Portrait(new Rect(160,217+(float)Math.Sin(Time.unscaledTime*1.8)*3,170,235),characterDraft.race);
            MenuSkin.Fill(new Rect(132,465,226,4),heroes[characterDraft.race].color);
            MenuSkin.Label(new Rect(97,490,295,34),heroes[characterDraft.race].displayName,26,heroes[characterDraft.race].color);
            MenuSkin.Label(new Rect(97,533,295,84),heroes[characterDraft.race].description,17,MenuSkin.Muted);
            MenuSkin.Label(new Rect(448,168,740,25),"01  /  ІМ'Я МАНДРІВНИКА",15,MenuSkin.Cyan);
            characterDraft.name=MenuSkin.Input(new Rect(448,203,740,44),characterDraft.name,40);
            MenuSkin.Label(new Rect(448,268,740,25),"02  /  ПОХОДЖЕННЯ",15,MenuSkin.Cyan);
            for(int i=0;i<heroes.Length;i++)
            {
                var r=new Rect(448+i*150,304,140,52);
                if(MenuSkin.Button(r,heroes[i].displayName,characterDraft.race==i))characterDraft.race=i;
                MenuSkin.Fill(new Rect(r.x+12,r.yMax-6,r.width-24,2),heroes[i].color);
            }
            MenuSkin.Label(new Rect(448,382,740,25),"03  /  ВИПРОБУВАННЯ",15,MenuSkin.Cyan);
            string[] challenges={"Спокійна","Звичайна","Випробування","Сувора","Екстремальна","Одна спроба"};
            for(int i=0;i<6;i++)
                if(MenuSkin.Button(new Rect(448+(i%3)*250,418+(i/3)*53,240,44),challenges[i],characterDraft.challenge==i))characterDraft.challenge=i;
            MenuSkin.Box(new Rect(448,540,740,91));
            MenuSkin.Fill(new Rect(448,540,3,91),characterDraft.challenge==5?new Color(1,0.4f,0.5f):MenuSkin.Cyan);
            MenuSkin.Label(new Rect(466,557,703,60),ChallengeText(characterDraft.challenge),19);
        }        public static string ChallengeText(int level)=>new[]{"Спокійна подорож — менша шкода, речі зберігаються.","Звичайна подорож — речі зберігаються після смерті.","Випробування — після смерті випадає половина матеріалів.","Сувора подорож — після смерті випадають інструменти й механізми.","Екстремальна подорож — після смерті випадає весь інвентар.","Одна спроба — остаточна смерть героя."}[level];
        private void WorldLibrary()
        {
            Heading("СВІТИ / "+library.characters.Find(c=>c.id==selectedHero)?.name);
            if(MenuSkin.Button(new Rect(70,96,220,44),"До героїв"))Navigate("characters");
            if(MenuSkin.Button(new Rect(930,96,280,44),"Створити світ",true))EditWorld(null);
            search=MenuSkin.Input(new Rect(310,96,590,44),search);
            var list=library.worlds.Where(w=>w.name.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0).OrderByDescending(w=>w.favorite).ToArray();
            scroll=GUI.BeginScrollView(new Rect(70,160,1140,475),scroll,new Rect(0,0,1110,Mathf.Max(470,list.Length*116)));
            if(list.Length==0)MenuSkin.Label(new Rect(20,35,1050,90),"Світів ще немає. Створи нову подорож.",24);
            for(int i=0;i<list.Length;i++)
            {
                var world=list[i];float y=i*116;MenuSkin.Box(new Rect(0,y,1100,106));DrawPreview(new Rect(10,y+10,115,86),world);
                MenuSkin.Label(new Rect(145,y+15,565,32),(world.favorite?"★ ":"")+world.name,24,MenuSkin.Cyan);
                MenuSkin.Label(new Rect(145,y+53,565,30),Difficulties[world.difficulty]+" • "+world.width+" × "+world.height,18,MenuSkin.Muted);
                if(MenuSkin.Button(new Rect(740,y+12,170,38),"Грати",true))Launch(world,library.characters.Find(c=>c.id==selectedHero));
                if(MenuSkin.Button(new Rect(740,y+60,170,34),"Редагувати"))EditWorld(world);
                if(MenuSkin.Button(new Rect(925,y+12,75,38),world.favorite?"★":"☆")){world.favorite=!world.favorite;Persist();}
                if(MenuSkin.Button(new Rect(1010,y+12,75,38),"×",false,true))Ask("Прибрати світ «"+world.name+"» з бібліотеки?",()=>{library.worlds.RemoveAll(w=>w.id==world.id);Persist();});
            }
            GUI.EndScrollView();
        }
        private void EditWorld(WorldProfile profile)
        {editingId=profile?.id;worldDraft=profile==null?new WorldProfile{seed=UnityEngine.Random.Range(1,int.MaxValue).ToString()}:JsonUtility.FromJson<WorldProfile>(JsonUtility.ToJson(profile));Navigate("world_editor");}
        private void WorldEditor()
        {
            Heading(editingId==null?"СТВОРЕННЯ СВІТУ":"НАЛАШТУВАННЯ СВІТУ");
            if(MenuSkin.Button(new Rect(70,96,240,44),"До бібліотеки"))Navigate("worlds");
            if(MenuSkin.Button(new Rect(770,96,210,44),"Зберегти",true))SaveWorld(false);
            if(MenuSkin.Button(new Rect(995,96,215,44),"Зберегти й грати"))SaveWorld(true);
            bool locked=editingId!=null && File.Exists(ProfileLibrary.WorldPath(editingId));
            MenuSkin.Label(new Rect(75,170,245,30),"РЕЛЬЄФ І БІОМИ",21,MenuSkin.Cyan);DrawPreview(new Rect(75,210,245,145),worldDraft);
            MenuSkin.Label(new Rect(75,375,245,30),"Розмір світу",19,MenuSkin.Cyan);
            GUI.enabled=!locked && confirmed==null;
            for(int i=0;i<Widths.Length;i++)if(MenuSkin.Button(new Rect(75,414+i*40,245,34),Sizes[i]+" ("+Widths[i]+" × "+Depths[i]+")",worldDraft.width==Widths[i])){worldDraft.width=Widths[i];worldDraft.height=Depths[i];}
            GUI.enabled=confirmed==null;
            string terrainInfo = locked ? "● Безпечний спавн у центрі" : "● Безпечний спавн у центрі\n● Печери й підземні тунелі";
            MenuSkin.Label(new Rect(75,585,245,60),terrainInfo,16,MenuSkin.Muted);
            scroll=GUI.BeginScrollView(new Rect(345,165,865,475),scroll,new Rect(0,0,835,810));
            MenuSkin.Label(new Rect(0,0,820,28),"Назва світу",19,MenuSkin.Muted);worldDraft.name=MenuSkin.Input(new Rect(0,32,820,42),worldDraft.name);
            GUI.enabled=!locked && confirmed==null;
            MenuSkin.Label(new Rect(0,90,400,25),"Складність",19,MenuSkin.Cyan);MenuSkin.Label(new Rect(425,90,395,25),"Багатство ресурсів",19,MenuSkin.Cyan);
            for(int i=0;i<6;i++)if(MenuSkin.Button(new Rect(0,125+i*37,400,32),Difficulties[i],worldDraft.difficulty==i))worldDraft.difficulty=i;
            for(int i=0;i<5;i++)if(MenuSkin.Button(new Rect(425,125+i*37,395,32),Resources[i],worldDraft.resources==i))worldDraft.resources=i;
            if(worldDraft.difficulty==4)MenuSkin.Label(new Rect(425,315,395,40),"Хардкор: остаточна загибель героя.",17,new Color(1,0.4f,0.5f));
            MenuSkin.Label(new Rect(0,355,820,25),"Seed генерації — число або текст",19,MenuSkin.Muted);
            worldDraft.seed=MenuSkin.Input(new Rect(0,387,650,42),worldDraft.seed);
            if(MenuSkin.Button(new Rect(665,387,155,42),"Випадково"))worldDraft.seed=UnityEngine.Random.Range(1,int.MaxValue).ToString();
            if(MenuSkin.Button(new Rect(0,445,820,36),"Високі гори: "+(worldDraft.mountains?"Увімкнено":"Вимкнено"),worldDraft.mountains))worldDraft.mountains=!worldDraft.mountains;
            worldDraft.mountainHeight=Slider("Висота гір",worldDraft.mountainHeight,0,48,500);
            worldDraft.terrainScale=Slider("Ширина пагорбів",worldDraft.terrainScale,0.5f,2.5f,565);
            worldDraft.biomeScale=Slider("Розмір біомів",worldDraft.biomeScale,0.5f,2.5f,630);
            MenuSkin.Label(new Rect(0,700,820,25),"Розташування біомів",19,MenuSkin.Cyan);
            string[] layouts={"Мішані","Від центру","Хвилями"};for(int i=0;i<3;i++)if(MenuSkin.Button(new Rect(i*276,737,267,36),layouts[i],worldDraft.layout==i))worldDraft.layout=i;
            GUI.enabled=confirmed==null;GUI.EndScrollView();
            if(locked)status="Світ уже відвідано: можна змінити назву. Для іншої генерації створи новий світ.";
        }
        private float Slider(string label,float value,float min,float max,float y)
        {MenuSkin.Label(new Rect(0,y,820,25),label+": "+value.ToString("0.0"),18);return GUI.HorizontalSlider(new Rect(0,y+34,815,22),value,min,max);}
        private void SaveWorld(bool launch)
        {
            worldDraft.name=worldDraft.name.Trim();worldDraft.seed=worldDraft.seed.Trim();
            if(worldDraft.name.Length==0){status="Введи назву світу.";return;}
            if(worldDraft.seed.Length==0)worldDraft.seed=UnityEngine.Random.Range(1,int.MaxValue).ToString();
            int index=library.worlds.FindIndex(w=>w.id==editingId);if(index<0)library.worlds.Add(worldDraft);else library.worlds[index]=worldDraft;
            editingId=worldDraft.id;
            if(!Persist())return;
            if(launch)Launch(worldDraft,library.characters.Find(c=>c.id==selectedHero));else Navigate("worlds");
        }
        public void Launch(WorldProfile world,CharacterProfile character)
        {
            if(character==null || character.dead){status="Спочатку обери живого героя.";return;}
            library.lastWorld=world.id;library.lastCharacter=character.id;if(!Persist())return;
            SessionContext.Begin(world,character);page="loading";loading=SceneManager.LoadSceneAsync("Game");
        }
        private void Settings()
        {
            Heading("НАЛАШТУВАННЯ ЕКРАНА");
            DisplaySettingsDraft.DrawLanguage(new Vector2(810, 100), library.language, ChangeLanguage);
            if(MenuSkin.Button(new Rect(75,100,240,44),"← Назад"))Navigate("main");
            display.Draw(new Vector2(85, 175), () => ApplyDisplay());
            MenuSkin.Box(new Rect(805,175,383,361));
            MenuSkin.Label(new Rect(830,197,333,35),"КОМФОРТНА ПОДОРОЖ",19,MenuSkin.Cyan);
            MenuSkin.Label(new Rect(830,250,333,170),"У вікні — вибраний розмір.\n\nПовний екран — без рамки, у роздільності твого монітора.\n\nРозмір вікна запам'ятовується для повернення з повного екрана.",18);
            MenuSkin.Label(new Rect(830,441,333,73),Application.isEditor?"У редакторі розмір задає Game View. Режим вікна діє у зібраній грі.":"Налаштування збережуться для наступного запуску.",16,MenuSkin.Muted);
        }
        public void ChangeLanguage(string language){library.language=language;Localization.SetLanguage(language);Persist();}
        public bool ApplyDisplay()
        {
            display.WriteTo(library);
            if(!Persist())return false;
            DisplaySettings.Apply(library);status="Налаштування екрана збережено.";return true;
        }        private void Portrait(Rect rect,int race)
        {
            var sprite=heroes[race].sprite;if(sprite==null)return;
            float height=Mathf.Min(rect.height,rect.width*2)*0.85f;var target=new Rect(rect.center.x-height/4,rect.center.y-height/2,height/2,height);
            GUI.DrawTexture(target,sprite.texture,ScaleMode.ScaleToFit,true);
        }
        private void DrawPreview(Rect rect,WorldProfile profile)
        {
            profile.Apply(previewSettings);var terrain=new TerrainGenerator(profile.Seed,previewSettings);
            MenuSkin.Fill(rect,new Color32(8,25,42,255));int columns=Mathf.RoundToInt(rect.width);
            for(int i=0;i<columns;i++)
            {
                int x=Mathf.RoundToInt((i/(float)columns-0.5f)*profile.width);
                float top=rect.y+rect.height*0.38f-terrain.Surface(x)*rect.height/100;
                top=Mathf.Clamp(top,rect.y+2,rect.yMax-10);
                MenuSkin.Fill(new Rect(rect.x+i,top,1,rect.yMax-top),terrain.Biome(x)=="forest"?new Color32(58,68,42,255):new Color32(76,70,65,255));
                MenuSkin.Fill(new Rect(rect.x+i,top,1,2),new Color32(104,177,115,255));
            }
            MenuSkin.Fill(new Rect(rect.center.x-1,rect.y,2,7),MenuSkin.Cyan);
        }
        private void Ask(string text,Action action){confirmText=text;confirmed=action;}
        // Used by editor visual checks without saving fabricated user profiles.
        public void PreviewPage(string target)
        {
            if(target=="character_editor")EditCharacter(null);else if(target=="world_editor")EditWorld(null);else Navigate(target);
        }
    }
}


