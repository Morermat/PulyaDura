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
        public bool InputEnabled => State == GameState.Playing && !Paused;
        public Tank Player { get; private set; }
        public Transform LevelRoot => level.Root;
        public int RoomIndex { get; private set; }
        public int RoomCount => rooms.Count;
        public int Lives { get; private set; }
        public RunStats Stats { get; private set; } = new();
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

            ui = gameObject.AddComponent<GameUI>();
            ui.Build(this);
            feedback = gameObject.AddComponent<CombatFeedback>();
            feedback.Init(config, ui);
        }

        void Start() => EnterGarage();

        void OnEnable()
        {
            GameEvents.TankKilled += OnKilled;
            GameEvents.ShotFired += OnShot;
        }

        void OnDisable()
        {
            GameEvents.TankKilled -= OnKilled;
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

        public void StartRun() => BeginRun(0);
        public void RestartRun() => BeginRun(0);

        /// <summary>Новый забег с нужной комнаты. Для отладки, автотеста и демо на защите: F1–F8 в редакторе.</summary>
        public void DebugLoadRoom(int index)
        {
            if (index < 0 || index >= rooms.Count) return;
            if (State is GameState.Garage or GameState.Result) BeginRun(index);
            else LoadRoom(index);
        }

        void BeginRun(int room)
        {
            if (rooms.Count == 0) return;
            Stats = new RunStats();
            Lives = config.lives;
            garage.Hide();
            LoadRoom(room);
        }

        void LoadRoom(int index)
        {
            ResetWorld();
            RoomIndex = index;
            streak = 0;
            var room = rooms[index];
            level.Build(room);

            Player = Tank.Spawn(Team.Player, config.hulls[HullIndex], config.turrets[TurretIndex], null, level.PlayerSpawn, Quaternion.identity, level.Root);
            foreach (var (enemy, pos) in level.EnemySpawns)
            {
                Vector3 look = Prims.Flat(level.PlayerSpawn - pos);
                var rot = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity;
                Tank.Spawn(Team.Enemy, config.Hull(enemy.hullId), config.Turret(enemy.turretId), enemy, pos, rot, level.Root);
            }

            CameraRig.I.Fit(level.Bounds);
            CameraRig.I.Follow(Player, config.cameraZoom, config.cameraLean);
            EchoSystem.I.RevealAll(level.PlayerSpawn, 1f);
            Sfx.I.Play(SfxId.Mirror, level.PlayerSpawn, 0.5f, 0.6f);
            State = GameState.Playing;
            ui.ShowHud();
            ui.ShowRoom(index, rooms.Count, room.title, room.hint);
        }

        void Update()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || (pad != null && pad.startButton.wasPressedThisFrame);
            bool restart = (kb != null && kb.rKey.wasPressedThisFrame) || (pad != null && pad.selectButton.wasPressedThisFrame);
            bool back = kb != null && kb.escapeKey.wasPressedThisFrame;

            switch (State)
            {
                case GameState.Garage:
                    if (confirm) StartRun();
                    break;
                case GameState.Playing:
                    if (!Paused) Stats.time += Time.deltaTime;
                    if (back || (pad != null && pad.startButton.wasPressedThisFrame)) TogglePause();
                    if (restart) RestartRun();
                    break;
                case GameState.RoomClear:
                case GameState.Dead:
                    if (restart) RestartRun();
                    break;
                case GameState.Result:
                    if (restart || confirm) RestartRun();
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
                : !byPlayer ? KillKind.Betrayal
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
            if (byPlayer) AddScore(100 + 50 * d.bounces + 50 * (streak - 1), t.transform.position);
            else if (betrayal) AddScore(75, t.transform.position);

            if (EnemiesAlive() == 0 && State == GameState.Playing) RoomCleared();
        }

        static int EnemiesAlive()
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
            ui.Banner(self ? "ПУЛЯ — ДУРА" : "ПОДБИТ", Lives > 0 ? $"Осталось жизней: {Lives}" : "Жизни кончились", UiKit.Danger);
            // Убил сам себя — голос медленно и с издёвкой произносит название игры
            if (self) Announcer.I.Chant(2, slam: false, pitch: 0.85f, variant: 0);
            AddScore(-150, null);
            Sfx.I.Play(SfxId.Death);
            TimeController.I.SlowMo(0.25f, 0.8f);
            flow = StartCoroutine(After(1.7f, () =>
            {
                if (Lives > 0) LoadRoom(RoomIndex);
                else ShowResult(false);
            }));
        }

        void RoomCleared()
        {
            State = GameState.RoomClear;
            Stats.rooms = Mathf.Min(Stats.rooms + 1, RoomCount);
            AddScore(250, null);
            if (Player != null) Player.Invulnerable = true;
            TimeController.I.SlowMo(0.3f, 0.9f);
            Sfx.I.Play(SfxId.RoomClear);
            bool last = RoomIndex + 1 >= rooms.Count;
            if (Player != null) EchoSystem.I.RevealAll(Player.transform.position, 0.9f);
            // Распев в такт музыке, как в концовке SUPERHOT: после последней комнаты — длиннее
            string next = last ? "ВСЕ КОМНАТЫ ЧИСТЫ" : $"ДАЛЬШЕ: {rooms[RoomIndex + 1].title.ToUpper()}";
            float chant = Announcer.I.Chant(last ? 6 : 4, subtitle: next);
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
            float bestTime = PlayerPrefs.GetFloat("best_time", 0f);
            int bestRooms = PlayerPrefs.GetInt("best_rooms", 0);
            bool record = false;
            if (win && (bestTime <= 0f || Stats.time < bestTime))
            {
                bestTime = Stats.time;
                PlayerPrefs.SetFloat("best_time", bestTime);
                record = true;
            }
            if (Stats.rooms > bestRooms)
            {
                bestRooms = Stats.rooms;
                PlayerPrefs.SetInt("best_rooms", bestRooms);
                record |= !win;
            }
            PlayerPrefs.Save();
            if (win) AddScore(Mathf.Max(0, Mathf.RoundToInt((300f - Stats.time) * 10f)), null);
            int place = Leaderboard.Add(new RunRecord
            {
                score = Stats.score, time = Stats.time, rooms = Stats.rooms, win = win, kills = Stats.kills,
                hull = config.hulls[HullIndex].title, turret = config.turrets[TurretIndex].title, ticks = DateTime.Now.Ticks,
            });
            ui.ShowResult(win, Stats, record, bestTime, bestRooms, place);
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
