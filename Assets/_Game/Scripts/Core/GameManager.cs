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
        public int kills, ricochetKills, shots, deaths, bestStreak, rooms;
    }

    /// <summary>
    /// Точка входа и состояния игры: Гараж → Комнаты → Результат.
    /// Сцена почти пустая: камера и этот объект. Всё остальное собирается из GameConfig.
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
        public RoomData CurrentRoom => rooms[RoomIndex];
        public int Lives { get; private set; }
        public RunStats Stats { get; private set; } = new();
        public int HullIndex { get; private set; }
        public int TurretIndex { get; private set; }

        List<RoomData> rooms;
        LevelBuilder level;
        GameUI ui;
        Transform garage;
        GameObject garageTank;
        Coroutine flow;
        int streak;
        float streakTime;
        Vignette vignette;
        ChromaticAberration aberration;
        float hurtPulse;

        static readonly Vector3 GaragePos = new(0f, 0f, -300f);

        void Awake()
        {
            I = this;
            Mats.Init(config);
            rooms = RoomParser.Parse(config.rooms != null ? config.rooms.text : "");
            if (rooms.Count == 0) Debug.LogError("Нет комнат: проверьте GameConfig → rooms");
            Physics.IgnoreLayerCollision(Layers.Debris, Layers.Tank);

            SetupEnvironment();
            gameObject.AddComponent<TimeController>();
            if (GetComponent<Sfx>() == null) gameObject.AddComponent<Sfx>();
            gameObject.AddComponent<Fx>().Init();
            new GameObject("Echo").AddComponent<EchoSystem>().Init(config);
            new GameObject("Projectiles").AddComponent<ProjectileSystem>().Init(config);
            level = new LevelBuilder(config);

            HullIndex = Mathf.Clamp(PlayerPrefs.GetInt("hull", 1), 0, config.hulls.Count - 1);
            TurretIndex = Mathf.Clamp(PlayerPrefs.GetInt("turret", 0), 0, config.turrets.Count - 1);

            ui = gameObject.AddComponent<GameUI>();
            ui.Build(this);
        }

        void Start() => EnterGarage();

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

            var volume = new GameObject("PostFX").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(1.3f);
            bloom.threshold.Override(0.8f);
            bloom.scatter.Override(0.72f);
            vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.38f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(Color.black);
            aberration = profile.Add<ChromaticAberration>(true);
            aberration.intensity.Override(0.08f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var grain = profile.Add<FilmGrain>(true);
            grain.intensity.Override(0.18f);
            volume.profile = profile;

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
            ui.ShowPause(false);
        }

        public void EnterGarage()
        {
            ResetWorld();
            level.Clear();
            Player = null;
            State = GameState.Garage;
            BuildGarage();
            garage.gameObject.SetActive(true);
            RebuildGarageTank();
            CameraRig.I.LookAt(GaragePos + Vector3.up * 0.45f, 9f, 30f);
            ui.ShowGarage();
        }

        public void SelectHull(int i)
        {
            HullIndex = Mathf.Clamp(i, 0, config.hulls.Count - 1);
            PlayerPrefs.SetInt("hull", HullIndex);
            Sfx.I.Play(SfxId.Select);
            RebuildGarageTank();
            ui.RefreshGarage();
        }

        public void SelectTurret(int i)
        {
            TurretIndex = Mathf.Clamp(i, 0, config.turrets.Count - 1);
            PlayerPrefs.SetInt("turret", TurretIndex);
            Sfx.I.Play(SfxId.Select);
            RebuildGarageTank();
            ui.RefreshGarage();
        }

        public void StartRun()
        {
            if (rooms.Count == 0) return;
            Stats = new RunStats();
            Lives = config.lives;
            streak = 0;
            if (garage != null) garage.gameObject.SetActive(false);
            LoadRoom(0);
        }

        public void RestartRun() => StartRun();

        /// <summary>Переход в комнату напрямую: для отладки, автотеста и демо на защите (F1–F8 в редакторе).</summary>
        public void DebugLoadRoom(int index)
        {
            if (index < 0 || index >= rooms.Count) return;
            if (State == GameState.Garage || State == GameState.Result)
            {
                Stats = new RunStats();
                Lives = config.lives;
                if (garage != null) garage.gameObject.SetActive(false);
            }
            LoadRoom(index);
        }

        void LoadRoom(int index)
        {
            ResetWorld();
            RoomIndex = index;
            var room = rooms[index];
            level.Build(room);

            var hull = config.hulls[HullIndex];
            var turret = config.turrets[TurretIndex];
            Player = Tank.Spawn(Team.Player, hull, turret, null, level.PlayerSpawn, Quaternion.identity, level.Root);
            foreach (var (enemy, pos) in level.EnemySpawns)
            {
                Vector3 look = Prims.Flat(level.PlayerSpawn - pos);
                var rot = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity;
                Tank.Spawn(Team.Enemy, config.Hull(enemy.hullId), config.Turret(enemy.turretId), enemy, pos, rot, level.Root);
            }

            CameraRig.I.Fit(level.Bounds);
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
                    if (garageTank != null) garageTank.transform.Rotate(0f, 25f * Time.unscaledDeltaTime, 0f, Space.World);
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
            // Отладка: F1–F8 — сразу в нужную комнату
            if (kb != null && State != GameState.Result)
                for (int i = 0; i < Mathf.Min(8, rooms.Count); i++)
                    if (kb[Key.F1 + i].wasPressedThisFrame) DebugLoadRoom(i);
#endif

            hurtPulse = Mathf.Max(0f, hurtPulse - Time.unscaledDeltaTime * 3f);
            vignette.intensity.value = 0.38f + hurtPulse * 0.12f;
            vignette.color.value = Color.Lerp(Color.black, new Color(0.35f, 0.02f, 0f), hurtPulse);
            aberration.intensity.value = 0.08f + hurtPulse * 0.8f;
        }

        public void TogglePause()
        {
            if (State != GameState.Playing) return;
            TimeController.I.Paused = !TimeController.I.Paused;
            ui.ShowPause(TimeController.I.Paused);
        }

        // ---------- События боя ----------

        void OnShot(Tank t)
        {
            if (t.Team == Team.Player) Stats.shots++;
        }

        void OnDamaged(Tank t, DamageInfo d)
        {
            if (t.IsDead) return;
            ui.Floating(t.transform.position + Vector3.up * 1.1f, "-" + d.amount, t.Team == Team.Player ? UiKit.Danger : UiKit.TextColor, 30);
            if (t.Team == Team.Player)
            {
                hurtPulse = 1f;
                ui.DamageFlash();
                CameraRig.I.Shake(0.45f);
                TimeController.I.HitStop(0.07f);
                Sfx.I.Play(SfxId.Hurt, t.transform.position);
            }
            else
            {
                TimeController.I.HitStop(0.035f);
                Sfx.I.Play(SfxId.Hit, t.transform.position, 0.8f, UnityEngine.Random.Range(0.9f, 1.1f));
            }
        }

        void OnKilled(Tank t, DamageInfo d)
        {
            Vector3 pos = t.transform.position;
            Fx.I.Explosion(pos + Vector3.up * 0.4f, t.Team == Team.Player ? config.bulletDanger : config.enemyAccent);
            Sfx.I.Play(SfxId.Explode, pos);
            EchoSystem.I.Emit(pos, 7f, t.Team == Team.Player ? config.bulletDanger : config.enemyEchoColor, 1f);
            CameraRig.I.Shake(0.5f);
            TimeController.I.HitStop(0.09f);

            if (t.Team == Team.Player)
            {
                PlayerDied(d);
                return;
            }

            Stats.kills++;
            bool byPlayer = d.source != null && d.source.Team == Team.Player;
            string label;
            Color color = UiKit.Accent;
            if (d.source == t) { label = "САМ СЕБЯ!"; color = UiKit.Enemy; }
            else if (!byPlayer) { label = "ПРЕДАТЕЛЬ!"; color = UiKit.Enemy; }
            else
            {
                streak = Time.time - streakTime < 2.2f ? streak + 1 : 1;
                streakTime = Time.time;
                Stats.bestStreak = Mathf.Max(Stats.bestStreak, streak);
                if (d.bounces > 0) Stats.ricochetKills++;
                label = streak == 2 ? "ДУПЛЕТ!" : streak == 3 ? "ТРИПЛЕТ!" : streak > 3 ? $"СЕРИЯ x{streak}!"
                    : d.bounces >= 2 ? "ДВОЙНОЙ РИКОШЕТ!" : d.bounces == 1 ? "РИКОШЕТ!" : "ПОПАЛ!";
                if (d.bounces > 0 || streak > 1) color = UiKit.Danger;
                if (d.bounces >= 2)
                {
                    // Награда за главный навык: замедление и наезд камеры на попадание
                    TimeController.I.SlowMo(0.2f, 0.7f);
                    CameraRig.I.Focus(pos, 0.3f, 1.1f);
                }
            }
            ui.Floating(pos + Vector3.up * 1.5f, label, color, 46);

            int alive = 0;
            foreach (var other in Tank.All)
                if (other.Team == Team.Enemy && !other.IsDead) alive++;
            if (alive == 0 && State == GameState.Playing) RoomCleared();
        }

        void PlayerDied(DamageInfo d)
        {
            if (State != GameState.Playing) return;
            Stats.deaths++;
            Lives--;
            State = GameState.Dead;
            bool self = d.source != null && d.source == Player;
            ui.Banner(self ? "ПУЛЯ — ДУРА" : "ПОДБИТ",
                Lives > 0 ? $"Осталось жизней: {Lives}" : "Жизни кончились", UiKit.Danger);
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
            Stats.rooms++;
            if (Player != null) Player.Invulnerable = true;
            TimeController.I.SlowMo(0.3f, 0.9f);
            Sfx.I.Play(SfxId.RoomClear);
            bool last = RoomIndex + 1 >= rooms.Count;
            ui.Banner("КОМНАТА ЧИСТА", last ? "Это была последняя!" : $"Дальше: {rooms[RoomIndex + 1].title}", UiKit.Accent);
            if (Player != null) EchoSystem.I.RevealAll(Player.transform.position, 0.9f);
            flow = StartCoroutine(After(1.9f, () =>
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
            ui.ShowResult(win, Stats, record, bestTime, bestRooms);
        }

        IEnumerator After(float seconds, Action action)
        {
            yield return new WaitForSecondsRealtime(seconds);
            flow = null;
            action();
        }

        // ---------- Гараж ----------

        void BuildGarage()
        {
            if (garage != null) return;
            garage = new GameObject("Garage").transform;
            garage.position = GaragePos;

            Prims.Part("Pedestal", PrimitiveType.Cylinder, garage, new Vector3(0f, -0.05f, 0f), new Vector3(4.2f, 0.05f, 4.2f), Mats.Lit(new Color(0.08f, 0.09f, 0.11f), 0.6f));
            var ring = Prims.Part("Ring", PrimitiveType.Cylinder, garage, new Vector3(0f, -0.06f, 0f), new Vector3(4.4f, 0.04f, 4.4f), Mats.Unlit(config.echoColor * 0.7f));
            ring.shadowCastingMode = ShadowCastingMode.Off;
            Prims.Part("Floor", PrimitiveType.Cube, garage, new Vector3(0f, -0.6f, 0f), new Vector3(40f, 1f, 40f), Mats.Lit(new Color(0.02f, 0.025f, 0.035f), 0.2f));

            var key = new GameObject("KeyLight").AddComponent<Light>();
            key.transform.SetParent(garage, false);
            key.transform.localPosition = new Vector3(1.5f, 6f, -2f);
            key.transform.LookAt(GaragePos);
            key.type = LightType.Spot;
            key.spotAngle = 45f;
            key.range = 15f;
            key.intensity = 30f;
            key.color = new Color(1f, 0.92f, 0.8f);
            key.shadows = LightShadows.Soft;

            var rim = new GameObject("RimLight").AddComponent<Light>();
            rim.transform.SetParent(garage, false);
            rim.transform.localPosition = new Vector3(-2.5f, 1.5f, 2.5f);
            rim.type = LightType.Point;
            rim.range = 7f;
            rim.intensity = 4f;
            rim.color = EchoSystem.Ldr(config.echoColor);
        }

        void RebuildGarageTank()
        {
            if (garage == null) return;
            float yaw = garageTank != null ? garageTank.transform.eulerAngles.y : 210f;
            if (garageTank != null) Destroy(garageTank);
            garageTank = new GameObject("GarageTank");
            garageTank.transform.SetParent(garage, false);
            garageTank.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var visual = garageTank.AddComponent<TankVisual>();
            visual.Build(config.hulls[HullIndex], config.turrets[TurretIndex], Team.Player, false);
            visual.SetTurretYaw(yaw + 25f);
            visual.Reveal(0.6f);
        }
    }
}
