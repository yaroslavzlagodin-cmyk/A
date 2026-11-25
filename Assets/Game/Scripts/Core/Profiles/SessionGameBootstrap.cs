using System;
using System.Collections;
using BrokenHeart.Combat;
using BrokenHeart.Inventory;
using BrokenHeart.Player;
using BrokenHeart.Systems;
using BrokenHeart.UI;
using BrokenHeart.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
namespace BrokenHeart.Core.Profiles
{
    [DefaultExecutionOrder(-200)]
    public sealed class SessionGameBootstrap : MonoBehaviour
    {
        [SerializeField] private BlockWorld world;
        [SerializeField] private PlayerInventory player;
        [SerializeField] private HeroData[] heroes;
        private WorldGenerationData settings;
        private bool paused,loadFailed,deathSavePending;
        private bool savedWhilePaused;
        private DisplaySettingsDraft pauseSettings;
        private readonly PauseBackdrop backdrop = new();
        private Coroutine capture;
        private enum ExitDestination { None, Lobby, Characters }
        private ExitDestination exit;
        private string status="";
        private void Awake()
        {
            Time.timeScale=1;
            if(!SessionContext.Active)return;
            var profile=SessionContext.World;settings=Instantiate(world.Generation);profile.Apply(settings);
            world.Configure(profile.Seed,new RectInt(-profile.width/2,64-profile.height,profile.width,profile.height),settings);
            player.GetComponent<SpriteRenderer>().sprite=heroes[SessionContext.Character.race].sprite;
            player.GetComponent<PlayerMotor>().FallBoundary=world.Bounds.yMin-16;
            player.GetComponent<PlayerVitals>().DamageMultiplier=new[]{0.75f,1,1.15f,1.3f,1.5f,1.75f}[SessionContext.Character.challenge];
            player.GetComponent<PlayerVitals>().Died+=OnDeath;
            var enemies=world.GetComponent<EnemySystem>();enemies.Peaceful=profile.difficulty==0;
            enemies.DamageMultiplier=new[]{0,0.75f,1,1.3f,1.6f,2}[profile.difficulty];
        }
        private IEnumerator Start()
        {
            if(!SessionContext.Active)yield break;
            yield return null; // All scene systems (including starter devices) finish initialization first.
            if(!world.GetComponent<SaveSystem>().Load())
            {loadFailed=true;status=world.GetComponent<SaveSystem>().Status;SetPaused(true);}
        }
        private void Update()
        {
            var keys = Keyboard.current;
            if (keys == null || !keys.escapeKey.wasPressedThisFrame) return;
            if (pauseSettings != null) pauseSettings = null;
            else if (exit != ExitDestination.None) exit = ExitDestination.None;
            else if (!player.IsOpen && !player.IsMapOpen && !loadFailed && !deathSavePending) SetPaused(!paused);
        }
        public void SetPaused(bool value)
        {
            if (paused == value) return;
            paused = value;
            Time.timeScale = paused ? 0 : 1;
            bool resting = player.TryGetComponent<SleepSystem>(out var sleep) && sleep.Resting;
            player.GetComponent<PlayerMotor>().InputSuppressed = paused || resting;
            if (paused)
            {
                savedWhilePaused = false;
                if (!loadFailed) status = "";
                capture = StartCoroutine(CaptureBackdrop());
            }
            else
            {
                if (capture != null) StopCoroutine(capture);
                capture = null;
                exit = ExitDestination.None;
                pauseSettings = null;
                backdrop.ReleaseImage();
            }
        }
        private IEnumerator CaptureBackdrop()
        {
            yield return new WaitForEndOfFrame();
            capture = null;
            if (paused) backdrop.Capture();
        }
        public bool ReturnToMenu(bool save, bool chooseCharacter = false)
        {
            if(save && !loadFailed && !world.GetComponent<SaveSystem>().Save())
            {SetPaused(true);status=world.GetComponent<SaveSystem>().Status;return false;}
            SetPaused(false);
            SessionContext.Clear();
            SessionContext.OpenCharacterSelection = chooseCharacter;
            SceneManager.LoadScene("Menu");
            return true;
        }
        private void RequestExit(ExitDestination destination)
        {
            if (savedWhilePaused || loadFailed) ReturnToMenu(false, destination == ExitDestination.Characters);
            else if (!ReturnToMenu(true, destination == ExitDestination.Characters)) exit = destination;
        }
        private void SaveWhilePaused()
        {
            var save = world.GetComponent<SaveSystem>();
            savedWhilePaused = save.Save();
            status = save.Status;
        }
        private void OpenPauseSettings()
        {
            try
            {
                pauseSettings = new DisplaySettingsDraft(ProfileLibrary.Load());
                status = "";
            }
            catch (Exception error) { status = "Не вдалося завантажити: " + error.Message; }
        }
        private void ApplyPauseDisplay()
        {
            try
            {
                var library = ProfileLibrary.Load();
                pauseSettings.WriteTo(library);
                ProfileLibrary.Save(library);
                DisplaySettings.Apply(library);
                status = "Налаштування екрана збережено.";
            }
            catch (Exception error) { status = "Не вдалося зберегти: " + error.Message; }
        }
        private void ChangePauseLanguage(string language)
        {
            try
            {
                var library = ProfileLibrary.Load();
                library.language = language;
                ProfileLibrary.Save(library);
                Localization.SetLanguage(language);
            }
            catch (Exception error) { status = "Не вдалося зберегти: " + error.Message; }
        }
        private void OnDeath(Vector3 position)
        {
            if(!SessionContext.Active)return;
            int challenge=SessionContext.Character.challenge;
            bool permanent=challenge==5 || SessionContext.World.difficulty==4;
            var drops=world.GetComponent<DropSpawner>();
            for(int i=0;i<player.Model.Capacity;i++)
            {
                var slot=player.Model[i];if(slot.Empty)continue;
                int amount=0;
                if(permanent || challenge==4)amount=slot.Count;
                else if(challenge==3 && slot.Item.Category is ItemCategory.Tool or ItemCategory.Mechanism or ItemCategory.Weapon)amount=slot.Count;
                else if(challenge==2 && (slot.Item.Category==ItemCategory.Block || slot.Item.Category==ItemCategory.Material))amount=Mathf.CeilToInt(slot.Count*0.5f);
                if(amount>0){drops.Spawn(slot.Item,amount,position,player.GetComponent<Collider2D>());player.Model.RemoveAt(i,amount);}
            }
            if (permanent || challenge == 4)
            {
                for (int i = 0; i < PlayerInventory.EquipmentCount; i++)
                {
                    var slot = player.Equipped(i);
                    if (!slot.Empty) drops.Spawn(slot.Item, slot.Count, position, player.GetComponent<Collider2D>());
                }
                player.RestoreLoadout(new ItemStack[player.BaseCapacity], new ItemStack[PlayerInventory.EquipmentCount]);
            }
            if (permanent)
            {
                SessionContext.Character.dead = true;
                deathSavePending = true;
                RequestExit(ExitDestination.Lobby);
            }
        }
        private void OnGUI()
        {
            if (!paused || capture != null) return;
            backdrop.Draw(new Rect(0, 0, Screen.width, Screen.height), pauseSettings != null);
            var old = GUI.matrix;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1280 * scale) / 2,
                (Screen.height - 720 * scale) / 2, 0), Quaternion.identity, Vector3.one * scale);
            if (pauseSettings != null) DrawPauseSettings();
            else if (exit != ExitDestination.None) DrawExitPrompt();
            else DrawPauseButtons();
            GUI.matrix = old;
        }

        private void DrawPauseButtons()
        {
            if (loadFailed)
                HudSkin.Label(new Rect(360, 149, 560, 34), Localization.T("НЕ ВДАЛОСЯ ЗАВАНТАЖИТИ"), 21,
                    MenuSkin.Text, TextAnchor.MiddleCenter, true);
            if (!loadFailed && !deathSavePending && MenuSkin.PauseButton(new Rect(420, 204, 440, 50), "Продовжити гру", true)) SetPaused(false);
            if (MenuSkin.PauseButton(new Rect(420, 269, 440, 50), "Налаштування")) OpenPauseSettings();
            if (!loadFailed && MenuSkin.PauseButton(new Rect(420, 334, 440, 50), "Зберегти")) SaveWhilePaused();
            if (MenuSkin.PauseButton(new Rect(420, 399, 440, 50), "Вийти на головний екран")) RequestExit(ExitDestination.Lobby);
            if (MenuSkin.PauseButton(new Rect(420, 464, 440, 50), "Вийти до вибору персонажа")) RequestExit(ExitDestination.Characters);
            DrawStatus(532);
        }

        private void DrawPauseSettings()
        {
            if (MenuSkin.PauseButton(new Rect(302, 83, 202, 44), "← Назад"))
            {
                pauseSettings = null;
                return;
            }
            DisplaySettingsDraft.DrawLanguage(new Vector2(600, 83), Localization.Language, ChangePauseLanguage);
            pauseSettings.Draw(new Vector2(302, 156), ApplyPauseDisplay);
            DrawStatus(624);
        }

        private void DrawExitPrompt()
        {
            HudSkin.Label(new Rect(360, 227, 560, 36), Localization.T("Зберегти перед виходом?"), 21,
                MenuSkin.Text, TextAnchor.MiddleCenter, true);
            bool chooseCharacter = exit == ExitDestination.Characters;
            if (MenuSkin.PauseButton(new Rect(420, 275, 440, 50), "Зберегти й вийти", true)) ReturnToMenu(true, chooseCharacter);
            if (!deathSavePending && MenuSkin.PauseButton(new Rect(420, 340, 440, 50), "Вийти без збереження")) ReturnToMenu(false, chooseCharacter);
            if (MenuSkin.PauseButton(new Rect(420, 405, 440, 50), "Скасувати")) exit = ExitDestination.None;
            DrawStatus(475);
        }

        private void DrawStatus(float y)
        {
            if (!string.IsNullOrEmpty(status))
                HudSkin.Label(new Rect(245, y, 790, 28), Localization.T(status), 16, MenuSkin.Text, TextAnchor.MiddleCenter);
        }
        private void OnDestroy()
        {
            backdrop.Dispose();
            if(player!=null)player.GetComponent<PlayerVitals>().Died-=OnDeath;
            if(settings!=null)Destroy(settings);
            Time.timeScale=1;
        }
    }
}
