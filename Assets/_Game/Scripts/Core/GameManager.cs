using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PulyaDura
{
    public enum GameState { Garage, Playing, RoomClear, Dead, Result }

    public class RunStats
    {
        public float time;
        public int kills, ricochetKills, shots, deaths, bestStreak, rooms, score, pickups;
    }

    /// <summary>
    /// Точка входа и правила забега: Гараж → Комнаты → Итоги, жизни, счёт, рекорды.
    /// Вид и «сок» живут отдельно: GarageStage, CombatFeedback, PostFx, GameUI.
    /// Сцена почти пустая: камера и этот объект, остальное собирается из GameConfig.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }
        public static GameConfig Cfg => I.config;

        public GameConfig config;

        public GameState State { get; private set; }
        public bool Paused => TimeController.I.Paused;
        public bool InputEnabled => Ready && State == GameState.Playing && !Paused;
        /// <summary>Прогрев при запуске закончен, игра в гараже (автотест ждёт этого).</summary>
        public bool Ready { get; private set; }
        /// <summary>Сколько секунд занял прогрев (для отчёта автотеста).</summary>
        public float WarmupSeconds { get; private set; }
        public Tank Player { get; private set; }
        public Transform LevelRoot => level.Root;
        public int RoomIndex { get; private set; }
        public int RoomCount => rooms.Count;
        public int Lives { get; private set; }
        public RunStats Stats { get; private set; } = new();
        /// <summary>Бесконечный режим: комнаты генерируются, сложность растёт, забег идёт до последней жизни.</summary>
        public bool Endless { get; private set; }
        public RoomData CurrentRoom { get; private set; }
        /// <summary>Сколько врагов было в комнате в начале (для счётчика в HUD).</summary>
        public int EnemiesTotal { get; private set; }
        public int EnemiesLeft => EnemiesAlive();
        /// <summary>Босс текущей комнаты (для полоски здоровья в HUD) или null.</summary>
        public Tank Boss { get; private set; }
        /// <summary>С какой комнаты можно продолжить после провала (−1 — нельзя).</summary>
        public int ContinueRoom { get; private set; } = -1;
        public const int MaxLives = 5;
        public int HullIndex { get; private set; }
        public int TurretIndex { get; private set; }
        public Transform GarageTank => garage.Tank;
        public GameUI UI => ui;

        List<RoomData> rooms;
        LevelBuilder level;
        GameUI ui;
        GarageStage garage;
        CombatFeedback feedback;
        Coroutine flow;
        int streak;
        float streakTime;
        readonly List<RoomData> endlessRooms = new();
        int endlessSeed;
        bool recordRun = true;
        readonly HashSet<int> hintsShown = new();
        bool hurtThisRoom;
        bool lifeBonusPending;
        int scaledForHeight;

        void Awake()
        {
            I = this;
            Mats.Init(config);
            rooms = RoomParser.Parse(config.rooms != null ? config.rooms.text : "");
            if (rooms.Count == 0) Debug.LogError("Нет комнат: проверьте GameConfig → rooms");
            Physics.IgnoreLayerCollision(Layers.Debris, Layers.Tank);
#if UNITY_EDITOR
            // В редакторе клавиатура доходит до игры, даже если фокус в другой вкладке
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            SetupEnvironment();

            gameObject.AddComponent<TimeController>();
            gameObject.AddComponent<PointerInput>();
            if (GetComponent<Sfx>() == null) gameObject.AddComponent<Sfx>();
            if (GetComponent<MusicDirector>() == null) gameObject.AddComponent<MusicDirector>();
            if (GetComponent<Announcer>() == null) gameObject.AddComponent<Announcer>();
            gameObject.AddComponent<Fx>().Init();
            new GameObject("PostFX").AddComponent<PostFx>();
            new GameObject("Echo").AddComponent<EchoSystem>().Init(config);
            new GameObject("Projectiles").AddComponent<ProjectileSystem>().Init(config);
            garage = new GameObject("Garage").AddComponent<GarageStage>();
            garage.Init(config);
            level = new LevelBuilder(config);

            HullIndex = Mathf.Clamp(PlayerPrefs.GetInt("hull", 1), 0, config.hulls.Count - 1);
            TurretIndex = Mathf.Clamp(PlayerPrefs.GetInt("turret", 0), 0, config.turrets.Count - 1);
            Endless = PlayerPrefs.GetInt("endless", 0) == 1;
            // Весь экран по умолчанию (выбор запоминается). Автотест работает в окне — ему нужен фиксированный размер
            if (System.Array.IndexOf(Environment.GetCommandLineArgs(), "-autotest") < 0)
                ApplyFullscreen(PlayerPrefs.GetInt("fullscreen", 1) == 1);

            ui = gameObject.AddComponent<GameUI>();
            ui.Build(this);
            feedback = gameObject.AddComponent<CombatFeedback>();
            feedback.Init(config, ui);
        }

        IEnumerator Start()
        {
            yield return Warmup();
            EnterGarage();
            Ready = true;
        }

        /// <summary>
        /// Прогрев видеокарты под экраном загрузки. Шейдер компилируется под видеокарту при первом показе
        /// (на Mac с Metal — особенно долго): без прогрева первая комната «грузится» рывками, а стартовая
        /// вспышка эха всё это время стоит ярко-голубой. Поэтому один раз при запуске, без звука, проходим
        /// гараж и все комнаты и показываем всё, что бывает в бою: выстрелы всех пушек, рельсу, эхо, взрыв,
        /// искры, стенку и зеркало. Дальше всё рисуется из готового.
        /// </summary>
        IEnumerator Warmup()
        {
            float start = Time.realtimeSinceStartup;
            ui.ShowLoading(true);
            float volume = AudioListener.volume;
            AudioListener.volume = 0f;
            bool endless = Endless;
            Endless = false;

            EnterGarage();
            yield return null;
            for (int i = 0; i < rooms.Count; i++)
            {
                BeginRun(i, false);
                var p = Player;
                p.Invulnerable = true;
                Vector3 o = p.ShotOrigin, f = p.transform.forward;
                foreach (var t in config.turrets)
                {
                    if (t.mode == FireMode.Rail) ProjectileSystem.I.FireRail(p, t, o, f);
                    else ProjectileSystem.I.Spawn(p, t, o, f, 0f);
                }
                EchoSystem.I.Emit(o, 12f, config.echoColor);
                Fx.I.Explosion(o + f * 2f, config.echoColor);
                p.Visual.SetMirror(true);
                if (i == 0) TempWall.Spawn(o + f * 2.5f, f, 1f);
                yield return null;
                yield return null;
            }

            // Обломки живут без родителя — убираем, чтобы не долетали до гаража
            foreach (var d in FindObjectsByType<Debris>()) Destroy(d.gameObject);
            Endless = endless;
            AudioListener.volume = volume;
            ui.ShowLoading(false);
            WarmupSeconds = Time.realtimeSinceStartup - start;
        }

        /// <summary>
        /// 3D рисуем не выше 1440 строк (на Mac — 1080): на Retina-экране полное разрешение — это в 3–4 раза
        /// больше пикселей при той же картинке. Интерфейс остаётся в родном разрешении и не мылится.
        /// </summary>
        void UpdateRenderScale()
        {
            if (Application.isEditor || Screen.height == scaledForHeight) return;
            scaledForHeight = Screen.height;
            if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset urp) return;
            float maxHeight = Application.platform == RuntimePlatform.OSXPlayer ? 1080f : 1440f;
            urp.renderScale = Mathf.Clamp(maxHeight / Mathf.Max(1, Screen.height), 0.5f, 1f);
        }

        void OnEnable()
        {
            GameEvents.TankKilled += OnKilled;
            GameEvents.TankDamaged += OnDamaged;
            GameEvents.ShotFired += OnShot;
        }

        void OnDisable()
        {
            GameEvents.TankKilled -= OnKilled;
            GameEvents.TankDamaged -= OnDamaged;
            GameEvents.ShotFired -= OnShot;
        }

        void SetupEnvironment()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = config.ambient;
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.reflectionIntensity = 0f;

            var cam = Camera.main;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.006f, 0.008f, 0.014f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 400f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            if (cam.GetComponent<CameraRig>() == null) cam.gameObject.AddComponent<CameraRig>();

            // Уши — над танком игрока, а не в камере: так работает объёмный звук
            if (cam.TryGetComponent<AudioListener>(out var camListener)) camListener.enabled = false;
            var ears = new GameObject("Listener");
            ears.AddComponent<AudioListener>();
            ears.AddComponent<AudioListenerRig>();

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
                es.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
        }

        // ---------- Поток игры ----------

        void ResetWorld()
        {
            if (flow != null) StopCoroutine(flow);
            flow = null;
            TimeController.I.ResetAll();
            ProjectileSystem.I.ClearAll();
            EchoSystem.I.Clear();
            Fx.I.Clear();
            Announcer.I.Stop();
            ui.EndSlam();
            ui.ShowPause(false);
        }

        public void EnterGarage()
        {
            ResetWorld();
            level.Clear();
            Player = null;
            State = GameState.Garage;
            garage.Show(config.hulls[HullIndex], config.turrets[TurretIndex]);
            ui.ShowGarage();

        }

        public void SelectHull(int i)
        {
            HullIndex = Mathf.Clamp(i, 0, config.hulls.Count - 1);
            PlayerPrefs.SetInt("hull", HullIndex);
            OnSelectionChanged();
        }

        public void SelectTurret(int i)
        {
            TurretIndex = Mathf.Clamp(i, 0, config.turrets.Count - 1);
            PlayerPrefs.SetInt("turret", TurretIndex);
            OnSelectionChanged();
        }

        void OnSelectionChanged()
        {
            Sfx.I.Play(SfxId.Select);
            if (State == GameState.Garage) garage.Show(config.hulls[HullIndex], config.turrets[TurretIndex]);
            ui.RefreshGarage();
        }

        public bool Fullscreen => Screen.fullScreenMode != FullScreenMode.Windowed;

        /// <summary>Переключить весь экран / окно (F11 или кнопка в гараже). Выбор сохраняется.</summary>
        public void ToggleFullscreen()
        {
            ApplyFullscreen(!Fullscreen);
            Sfx.I.Play(SfxId.Select);
        }

        static void ApplyFullscreen(bool on)
        {
            PlayerPrefs.SetInt("fullscreen", on ? 1 : 0);
            if (on)
            {
                var r = Screen.currentResolution;
                Screen.SetResolution(r.width, r.height, FullScreenMode.FullScreenWindow);
            }
            else Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
        }

        /// <summary>Выход из игры (в редакторе — остановка Play).</summary>
        public void Quit()
        {
            PlayerPrefs.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void SetEndless(bool on)
        {
            Endless = on;
            PlayerPrefs.SetInt("endless", on ? 1 : 0);
            Sfx.I.Play(SfxId.Select);
            ui.RefreshGarage();
        }

        public void StartRun() => BeginRun(0, true);
        public void RestartRun() => BeginRun(0, true);

        /// <summary>Переиграть текущую комнату: жизни и очки сохраняются. Это R в бою.</summary>
        public void RestartRoom()
        {
            if (State != GameState.Playing) return;
            LoadRoom(RoomIndex);
        }

        /// <summary>Продолжить с комнаты, где кончились жизни. Такой забег не попадает в таблицу.</summary>
        public void ContinueRun()
        {
            if (ContinueRoom < 0 || Endless) return;
            BeginRun(ContinueRoom, false);
        }

        /// <summary>Новый забег с нужной комнаты сюжета. Для отладки, автотеста и демо на защите: F1–F8 в редакторе.</summary>
        public void DebugLoadRoom(int index)
        {
            if (index < 0 || index >= rooms.Count) return;
            Endless = false;
            if (State is GameState.Garage or GameState.Result) BeginRun(index, false);
            else LoadRoom(index);
        }

        void BeginRun(int room, bool record)
        {
            if (rooms.Count == 0) return;
            Stats = new RunStats();
            Lives = config.lives;
            recordRun = record;
            ContinueRoom = -1;
            endlessRooms.Clear();
            hintsShown.Clear();
            endlessSeed = Environment.TickCount;
            garage.Hide();
            LoadRoom(room);
        }

        /// <summary>Комната по номеру: из rooms.txt или из генератора (бесконечный режим). Сгенерированные запоминаются,
        /// чтобы после смерти вернуться в ту же комнату.</summary>
        RoomData RoomAt(int index)
        {
            if (!Endless) return rooms[index];
            while (endlessRooms.Count <= index) endlessRooms.Add(RoomGenerator.Generate(endlessRooms.Count + 1, endlessSeed));
            return endlessRooms[index];
        }

        void LoadRoom(int index)
        {
            ResetWorld();
            hurtThisRoom = false;
            RoomIndex = index;
            streak = 0;
            var room = RoomAt(index);
            CurrentRoom = room;
            level.Build(room);
            EnemiesTotal = level.EnemySpawns.Count;

            Player = Tank.Spawn(Team.Player, config.hulls[HullIndex], config.turrets[TurretIndex], null, level.PlayerSpawn, Quaternion.identity, level.Root);
            foreach (var (enemy, pos) in level.EnemySpawns)
            {
                Vector3 look = Prims.Flat(level.PlayerSpawn - pos);
                var rot = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity;
                Tank.Spawn(Team.Enemy, config.Hull(enemy.hullId), config.Turret(enemy.turretId), enemy, pos, rot, level.Root);
            }

            if (lifeBonusPending)
            {
                lifeBonusPending = false;
                ui.Floating(level.PlayerSpawn + Vector3.up * 1.6f, "+1 ЖИЗНЬ", UiKit.Accent, 40);
                Sfx.I.Play(SfxId.Pickup, level.PlayerSpawn);
            }
            // Босс: имя во весь экран при входе
            Boss = null;
            foreach (var t in Tank.All)
                if (t.Enemy != null && t.Enemy.boss)
                {
                    Boss = t;
                    ui.Banner(t.Enemy.title.ToUpper(), "", UiKit.Danger, 2.6f);
                    CameraRig.I.Shake(0.4f);
                    break;
                }
            CameraRig.I.Fit(level.Bounds);
            CameraRig.I.Follow(Player, config.cameraZoom, config.cameraLean);
            EchoSystem.I.RevealAll(level.PlayerSpawn, 1f);
            Sfx.I.Play(SfxId.Mirror, level.PlayerSpawn, 0.5f, 0.6f);
            State = GameState.Playing;
            ui.ShowHud();
            ui.ShowRoom(index, Endless ? 0 : rooms.Count, room.title, room.hint);
            // Подсказка комнаты — короткий субтитр внизу, один раз за забег (после смерти не повторяется)
            if (!string.IsNullOrEmpty(room.hint) && hintsShown.Add(index)) ui.Subtitle(room.hint, 6f);
        }

        void Update()
        {
            UpdateRenderScale();
            if (!Ready) return;
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || (pad != null && pad.startButton.wasPressedThisFrame);
            bool restart = (kb != null && kb.rKey.wasPressedThisFrame) || (pad != null && pad.selectButton.wasPressedThisFrame);
            bool back = kb != null && kb.escapeKey.wasPressedThisFrame;

            if (kb != null && kb.f11Key.wasPressedThisFrame) ToggleFullscreen();

            switch (State)
            {
                case GameState.Garage:
                    if (confirm) StartRun();
                    if (kb != null && kb.tabKey.wasPressedThisFrame) SetEndless(!Endless);
                    break;
                case GameState.Playing:
                    if (!Paused) Stats.time += Time.deltaTime;
                    if (back || (pad != null && pad.startButton.wasPressedThisFrame)) TogglePause();
                    if (restart)
                    {
                        if (Paused) RestartRun();
                        else RestartRoom();
                    }
                    break;
                case GameState.Result:
                    if (restart || confirm) RestartRun();
                    if (kb != null && kb.cKey.wasPressedThisFrame) ContinueRun();
                    if (back || (kb != null && kb.gKey.wasPressedThisFrame)) EnterGarage();
                    break;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (kb != null && State != GameState.Result)
                for (int i = 0; i < Mathf.Min(8, rooms.Count); i++)
                    if (kb[Key.F1 + i].wasPressedThisFrame) DebugLoadRoom(i);
#endif
        }

        public void TogglePause()
        {
            if (State != GameState.Playing) return;
            TimeController.I.Paused = !TimeController.I.Paused;
            ui.ShowPause(TimeController.I.Paused);
        }

        // ---------- Счёт ----------

        void OnDamaged(Tank t, DamageInfo d)
        {
            if (t.Team == Team.Player) hurtThisRoom = true;
        }

        void OnShot(Tank t)
        {
            if (t.Team == Team.Player) Stats.shots++;
        }

        void OnKilled(Tank t, DamageInfo d)
        {
            if (t.Team == Team.Player)
            {
                feedback.OnKill(t, KillKind.Direct, 0);
                PlayerDied(d);
                return;
            }

            Stats.kills++;
            bool byPlayer = d.source != null && d.source.Team == Team.Player;
            bool betrayal = d.source != null && d.source.Team == Team.Enemy && d.source != t;
            KillKind kind = d.source == t ? KillKind.Self
                : d.explosion && byPlayer ? KillKind.Explosion
                : !byPlayer ? KillKind.Betrayal
                : d.bounces >= 3 ? KillKind.TripleRicochet
                : d.bounces >= 2 ? KillKind.DoubleRicochet
                : d.bounces == 1 ? KillKind.Ricochet
                : KillKind.Direct;
            if (byPlayer)
            {
                streak = Time.time - streakTime < 2.2f ? streak + 1 : 1;
                streakTime = Time.time;
                Stats.bestStreak = Mathf.Max(Stats.bestStreak, streak);
                if (d.bounces > 0) Stats.ricochetKills++;
            }
            feedback.OnKill(t, kind, byPlayer ? streak : 0);
            if (byPlayer)
            {
                // 100 за убийство, +50 за каждый отскок, +50 за каждое убийство в серии, +150 за взрыв бочки,
                // +150 за тройной рикошет, +1000 за босса
                int bonus = (kind == KillKind.Explosion ? 150 : 0) + (kind == KillKind.TripleRicochet ? 150 : 0) + (t.Enemy != null && t.Enemy.boss ? 1000 : 0);
                AddScore(100 + 50 * Mathf.Min(d.bounces, 4) + 50 * (streak - 1) + bonus, t.transform.position);
            }
            else if (betrayal) AddScore(75, t.transform.position);

            if (EnemiesAlive() == 0 && State == GameState.Playing) RoomCleared();
        }

        public static int EnemiesAlive()
        {
            int alive = 0;
            foreach (var other in Tank.All)
                if (other.Team == Team.Enemy && !other.IsDead) alive++;
            return alive;
        }

        void PlayerDied(DamageInfo d)
        {
            if (State != GameState.Playing) return;
            Stats.deaths++;
            Lives--;
            State = GameState.Dead;
            bool self = d.source != null && d.source == Player;
            // Причина смерти: враги невидимы, поэтому говорим, кто и как попал
            string cause = self ? "Своя же пуля"
                : d.explosion ? "Взрыв бочки"
                : d.source != null && d.source.Enemy != null ? d.source.Enemy.title + (d.bounces > 0 ? ", рикошетом" : "")
                : "Взрыв";
            ui.Banner(self ? "ПУЛЯ — ДУРА" : "ПОДБИТ", Lives > 0 ? $"жизней: {Lives}" : "", UiKit.Danger);
            ui.Subtitle(cause, 2.5f);
            AddScore(-500, null);
            Sfx.I.Play(SfxId.Death);
            TimeController.I.SlowMo(0.25f, 0.8f);
            flow = StartCoroutine(After(1.7f, () =>
            {
                if (Lives > 0) LoadRoom(RoomIndex);
                else
                {
                    ContinueRoom = Endless ? -1 : RoomIndex;
                    ShowResult(false);
                }
            }));
        }

        void RoomCleared()
        {
            State = GameState.RoomClear;
            Stats.rooms = Endless ? Stats.rooms + 1 : Mathf.Min(Stats.rooms + 1, RoomCount);
            AddScore(250, null);
            // Комната без единого попадания по игроку — бонус
            if (!hurtThisRoom)
            {
                AddScore(250, null);
                if (Player != null) ui.Floating(Player.transform.position + Vector3.up * 1.6f, "БЕЗ ЦАРАПИНЫ +250", UiKit.Accent, 34);
            }
            if (Player != null) Player.Invulnerable = true;
            TimeController.I.SlowMo(0.3f, 0.9f);
            Sfx.I.Play(SfxId.RoomClear);
            bool last = !Endless && RoomIndex + 1 >= rooms.Count;
            if (Player != null) EchoSystem.I.RevealAll(Player.transform.position, 0.9f);
            // Зачистка — короткий баннер; голос «ПУЛЯ. ДУРА.» звучит только на победе
            string next = last ? "ВСЕ КОМНАТЫ ЗАЧИЩЕНЫ" : $"ДАЛЬШЕ: {RoomAt(RoomIndex + 1).title.ToUpper()}";
            // В бесконечном режиме каждые 5 комнат — жизнь в награду
            if (Endless && (RoomIndex + 1) % 5 == 0 && Lives < MaxLives)
            {
                Lives++;
                lifeBonusPending = true;
                next += "  ·  +1 ЖИЗНЬ";
            }
            float chant;
            if (last) chant = Announcer.I.Chant(4, subtitle: next);
            else
            {
                ui.Banner("ЧИСТО", next, UiKit.Accent, 1.7f);
                chant = 1.6f;
            }
            flow = StartCoroutine(After(chant + 0.25f, () =>
            {
                if (!last) LoadRoom(RoomIndex + 1);
                else ShowResult(true);
            }));
        }

        void ShowResult(bool win)
        {
            ResetWorld();
            State = GameState.Result;
            // Забег «вне зачёта» (продолжение после провала) в таблицу не попадает
            if (!recordRun)
            {
                ui.ShowResult(win, Stats, 0);
                return;
            }
            // Победа: 1000 за каждую оставшуюся жизнь и 15 очков за каждую секунду быстрее 6 минут
            if (win) AddScore(Lives * 1000 + Mathf.Max(0, Mathf.RoundToInt((360f - Stats.time) * 15f)), null);
            int place = Stats.score <= 0 ? 0 : Leaderboard.Add(new RunRecord
            {
                score = Stats.score, time = Stats.time, rooms = Stats.rooms, win = win, kills = Stats.kills, endless = Endless,
                hull = config.hulls[HullIndex].title, turret = config.turrets[TurretIndex].title, ticks = DateTime.Now.Ticks,
            });
            ui.ShowResult(win, Stats, place);
        }

        /// <summary>Очки (см. Leaderboard). Если задана точка — над ней всплывает «+N».</summary>
        public void AddScore(int points, Vector3? at)
        {
            Stats.score = Mathf.Max(0, Stats.score + points);
            if (at.HasValue && points > 0) ui.Floating(at.Value + Vector3.up * 0.6f, "+" + points, UiKit.TextColor, 26);
        }

        public void OnPickup(Pickup pickup)
        {
            Stats.pickups++;
            AddScore(25, null);
        }

        IEnumerator After(float seconds, Action action)
        {
            yield return new WaitForSecondsRealtime(seconds);
            flow = null;
            action();
        }
    }
}
