using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace PulyaDura
{
    /// <summary>
    /// QA-прогон: билд, запущенный с флагом -autotest, сам играет через Input System
    /// (как живой игрок нажимает клавиши и мышь), проверяет результат и пишет PASS/FAIL в qa.txt,
    /// плюс скриншоты ключевых моментов. В обычной игре не активен.
    /// Пример: PulyaDura.exe -autotest -shots C:\temp\shots
    /// </summary>
    public class AutoTest : MonoBehaviour
    {
        string dir;
        readonly List<string> errors = new();
        readonly StringBuilder report = new();
        readonly List<Vector3> path = new();
        int passed, failed, enemyShots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(args, "-autotest") < 0) return;
            var go = new GameObject("AutoTest");
            DontDestroyOnLoad(go);
            var t = go.AddComponent<AutoTest>();
            int i = System.Array.IndexOf(args, "-shots");
            t.dir = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "autotest");
        }

        void Awake()
        {
            Application.logMessageReceived += OnLog;
            GameEvents.ShotFired += OnShot;
            // Окно теста может быть в фоне: ввод не должен отключаться
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            GameEvents.ShotFired -= OnShot;
        }

        void OnShot(Tank t) { if (t.Team == Team.Enemy) enemyShots++; }

        void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add($"[{type}] {msg}\n{stack}");
        }

        // ---------- Ввод ----------

        static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));

        static void MouseAt(Vector2 pos, bool left = false, bool right = false)
        {
            var st = new MouseState { position = pos };
            if (left) st = st.WithButton(MouseButton.Left);
            if (right) st = st.WithButton(MouseButton.Right);
            InputSystem.QueueStateEvent(Mouse.current, st);
        }

        IEnumerator Tap(params Key[] keys)
        {
            Keys(keys);
            yield return null;
            yield return null;
            Keys();
            yield return null;
        }

        IEnumerator Hold(float seconds, params Key[] keys)
        {
            Keys(keys);
            yield return new WaitForSecondsRealtime(seconds);
            Keys();
            yield return null;
        }

        static Vector2 ScreenOf(Vector3 world) => Camera.main.WorldToScreenPoint(world);

        // ---------- Отчёт ----------

        void Check(string name, bool ok, string details = "")
        {
            if (ok) passed++; else failed++;
            report.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}  {details}");
        }

        void Note(string text) => report.AppendLine("      " + text);

        void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));

        static IEnumerator Wait(float s) { yield return new WaitForSecondsRealtime(s); }

        // ---------- Сценарий ----------

        IEnumerator Start()
        {
            Directory.CreateDirectory(dir);
            var gm = GameManager.I;
            yield return Wait(1f);

            // 1. Гараж: танк на подиуме крутится
            var garageTank = gm.GarageTank;
            float yaw0 = garageTank != null ? garageTank.eulerAngles.y : 0f;
            yield return Wait(1f);
            float yaw1 = gm.GarageTank != null ? gm.GarageTank.eulerAngles.y : 0f;
            Check("Гараж: превью танка вращается", Mathf.Abs(Mathf.DeltaAngle(yaw0, yaw1)) > 10f, $"Δyaw={Mathf.DeltaAngle(yaw0, yaw1):F1}");
            Shot("01_garage");
            yield return null;

            // 1б. Лист моделей: все сочетания корпус × пушка и все типы врагов крупным планом
            yield return ModelSheet();

            // 2. Выбор корпуса и пушки клавишами мыши не проверяем, проверяем API
            gm.SelectHull(1);
            gm.SelectTurret(0);

            // 3. Enter запускает забег
            yield return Tap(Key.Enter);
            yield return Wait(0.3f);
            Check("Enter в гараже начинает забег", gm.State == GameState.Playing, $"state={gm.State}");
            Check("В первой комнате под танком подсказка клавиш", GameObject.Find("Tutorial") != null);
            Shot("02_room1_start");
            yield return Wait(0.6f);

            // 4. Движение WASD
            var p = gm.Player;
            Vector3 start = p.transform.position;
            yield return Hold(0.8f, Key.W);
            float dz = p.transform.position.z - start.z;
            Check("W двигает танк вверх", dz > 1.5f, $"Δz={dz:F2}");
            var pc = p.GetComponent<PlayerController>();

            start = p.transform.position;
            Keys(Key.D);
            yield return Wait(0.35f);
            bool loud = pc.IsLoud;
            yield return Wait(0.45f);
            Keys();
            yield return null;
            float dx = p.transform.position.x - start.x;
            Check("D двигает танк вправо", dx > 1.5f, $"Δx={dx:F2}");
            Check("Полный ход — шумно", loud);
            Check("Корпус развернулся по ходу", Mathf.Abs(Vector3.Dot(p.transform.forward, Vector3.right)) > 0.8f, $"fwd={p.transform.forward}");

            start = p.transform.position;
            Keys(Key.LeftShift, Key.S);
            yield return Wait(0.35f);
            bool quiet = !pc.IsLoud && pc.IsSneaking;
            yield return Wait(0.45f);
            Keys();
            yield return null;
            float sneakDist = (p.transform.position - start).magnitude;
            Check("Shift — тихий ход, медленнее", quiet && sneakDist < dx * 0.75f && sneakDist > 0.3f, $"dist={sneakDist:F2} vs {dx:F2}");

            // 5. Прицел мышью
            Vector3 target = p.transform.position + Vector3.right * 4f;
            MouseAt(ScreenOf(target));
            yield return Wait(0.15f);
            float aimDot = Vector3.Dot(Prims.Flat(p.Visual.TurretPivot.forward).normalized, Vector3.right);
            Check("Башня следит за мышью", aimDot > 0.9f, $"dot={aimDot:F2}");

            // 6. Выстрел ЛКМ
            int shots0 = gm.Stats.shots;
            MouseAt(ScreenOf(target), left: true);
            yield return Wait(0.1f);
            MouseAt(ScreenOf(target));
            yield return Wait(0.35f);
            Check("ЛКМ стреляет", gm.Stats.shots > shots0, $"shots={gm.Stats.shots}");
            Shot("03_shot_echo");

            // 7. Скилл на Пробел
            bool readyBefore = p.Ability.Ready;
            yield return Tap(Key.Space);
            Check("Пробел включает скилл", readyBefore && !p.Ability.Ready);
            yield return Wait(0.3f);
            Shot("04_ability");

            // 8. Пауза
            yield return Tap(Key.Escape);
            yield return null;
            Check("Esc ставит паузу", gm.Paused && Time.timeScale == 0f, $"ts={Time.timeScale}");
            Shot("05_pause");
            yield return null;
            yield return Tap(Key.Escape);
            yield return null;
            Check("Esc снимает паузу", !gm.Paused && Time.timeScale > 0f);

            // 9. Restart
            yield return Wait(0.5f);
            yield return Tap(Key.R);
            yield return Wait(0.3f);
            Check("R перезапускает забег", gm.State == GameState.Playing && gm.RoomIndex == 0 && gm.Lives == GameManager.Cfg.lives);

            Check("Музыка играет", MusicDirector.I != null && MusicDirector.I.IsPlaying);

            // 9б. Враги невидимы, эхо оставляет «призрака» на месте врага
            gm.DebugLoadRoom(0);
            yield return Wait(2.5f);
            Tank anyEnemy = null;
            foreach (var t in Tank.All) if (t.Team == Team.Enemy) { anyEnemy = t; break; }
            bool allHidden = true;
            foreach (var r in anyEnemy.GetComponentsInChildren<MeshRenderer>())
                if (r.enabled && r.name != "Sensor") allHidden = false;
            Check("Враги невидимы (видны только следы эха)", allHidden);
            float emitTime = Time.time;
            EchoSystem.I.Emit(anyEnemy.transform.position + Vector3.right * 2f, 4f, GameManager.Cfg.echoColor);
            yield return Wait(0.4f);
            Check("Эхо оставляет призрака врага", EchoGhosts.I.LastSnapshotOf(anyEnemy) >= emitTime && EchoGhosts.I.Count > 0, $"призраков на экране {EchoGhosts.I.Count}");
            Shot("05b_ghost");

            // 9в. Новичок в первой комнате видит фары игрока и стреляет
            p = gm.Player;
            p.GetComponent<PlayerController>().enabled = false;
            p.Invulnerable = true;
            p.Body.position = CellToWorld(12, 10, 21, 13);
            p.transform.position = p.Body.position;
            enemyShots = 0;
            yield return Wait(4f);
            Check("Новичок в комнате 1 стреляет, когда видит игрока", enemyShots > 0, $"выстрелов врагов {enemyShots}");
            Check("Музыка переходит в боевой слой", MusicDirector.I.Intensity > 0.5f, $"intensity={MusicDirector.I.Intensity:F2}");
            p.Invulnerable = false;

            // 10. ИИ охотников едет на звук (комната 4)
            gm.DebugLoadRoom(3);
            yield return Wait(2.6f);
            var hunter = FirstEnemy();
            p = gm.Player;
            if (hunter != null && p != null)
            {
                float d0 = Vector3.Distance(hunter.transform.position, p.transform.position);
                p.GetComponent<PlayerController>().enabled = false;
                p.Invulnerable = true;
                enemyShots = 0;
                Fire(p, Prims.Flat(hunter.transform.position - p.transform.position));
                yield return Wait(3f);
                float d1 = hunter != null ? Vector3.Distance(hunter.transform.position, p.transform.position) : 0f;
                bool alerted = hunter != null && hunter.Brain.Alerted;
                Check("Охотник услышал выстрел и вступил в бой", hunter == null || alerted, $"тревога={alerted}, dist {d0:F1} → {d1:F1}, выстрелов врагов {enemyShots}");
                p.Invulnerable = false;
                Shot("06_hunters");
            }

            // 11. Снайперы (комната 6). Точка появления безопасна, поэтому выезжаем в центр:
            // по карте простреливаемости клетка (10,6) открыта для выстрела с одним отскоком
            gm.DebugLoadRoom(5);
            yield return Wait(2.6f);
            p = gm.Player;
            p.GetComponent<PlayerController>().enabled = false;
            p.Body.position = CellToWorld(10, 6, 21, 13);
            p.transform.position = p.Body.position;
            yield return Wait(0.1f);
            p.Invulnerable = true;
            enemyShots = 0;
            Fire(p, Vector3.forward);
            yield return Wait(5f);
            p.Invulnerable = false;
            Check("Снайперы отвечают на выстрел", enemyShots > 0, $"enemyShots={enemyShots}");
            Shot("07_snipers");

            // 11б. Снайпер за стеной в комнате 1 должен найти выстрел через отскок от нижней стены
            gm.DebugLoadRoom(0);
            yield return Wait(0.5f);
            p = gm.Player;
            p.GetComponent<PlayerController>().enabled = false;
            p.Invulnerable = true;
            var sniperCfg = GameManager.Cfg.Enemy('s');
            Tank dummy = null;
            foreach (var t in Tank.All)
                if (t.Team == Team.Enemy && (dummy == null || t.transform.position.z > dummy.transform.position.z)) dummy = t;
            var sniper = Tank.Spawn(Team.Enemy, GameManager.Cfg.Hull(sniperCfg.hullId), GameManager.Cfg.Turret(sniperCfg.turretId), sniperCfg,
                dummy.transform.position + Vector3.right * 1.5f, Quaternion.identity, gm.LevelRoot);
            KillEnemiesExcept(sniper);
            yield return Wait(0.3f);
            enemyShots = 0;
            Fire(p, Vector3.back);
            yield return Wait(5f);
            Check("Снайпер стреляет через отскок из-за стены", enemyShots > 0, $"enemyShots={enemyShots}");
            Shot("07b_sniper_bank");
            p.Invulnerable = false;

            // 11в. Встречные снаряды гасят друг друга
            gm.DebugLoadRoom(0);
            yield return Wait(0.5f);
            p = gm.Player;
            p.GetComponent<PlayerController>().enabled = false;
            p.Invulnerable = true;
            Tank dummyOwner = null;
            foreach (var t in Tank.All) if (t.Team == Team.Enemy && t.Enemy.behaviour == EnemyBehaviour.Dummy) { dummyOwner = t; break; }
            KillEnemiesExcept(dummyOwner);
            yield return Wait(0.2f);
            var cannon = GameManager.Cfg.Turret("cannon");
            ProjectileSystem.I.Spawn(p, cannon, CellToWorld(4, 10, 21, 13) + Vector3.up * Ricochet.ShotHeight, Vector3.right, 0f);
            ProjectileSystem.I.Spawn(dummyOwner, cannon, CellToWorld(16, 10, 21, 13) + Vector3.up * Ricochet.ShotHeight, Vector3.left, 0f);
            int flying = ProjectileSystem.I.CountAlive();
            yield return Wait(1.2f);
            Check("Встречные снаряды гасят друг друга", flying == 2 && ProjectileSystem.I.CountAlive() == 0 && dummyOwner != null && !dummyOwner.IsDead,
                $"было {flying}, осталось {ProjectileSystem.I.CountAlive()}");
            p.Invulnerable = false;

            // 11д. Объекты на карте и бонусы
            yield return WorldObjectTests();

            // 11г. Враг стреляет за кадром — у края экрана стрелка в его сторону
            gm.DebugLoadRoom(6);
            yield return Wait(0.6f);
            p = gm.Player;
            p.GetComponent<PlayerController>().enabled = false;
            p.Invulnerable = true;
            Tank offscreen = null;
            foreach (var t in Tank.All)
            {
                if (t.Team != Team.Enemy) continue;
                Vector3 vp = Camera.main.WorldToViewportPoint(t.transform.position);
                if (vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) { offscreen = t; break; }
            }
            if (offscreen != null)
            {
                offscreen.Visual.SetTurretDirection(Vector3.back);
                offscreen.Weapon.TryFire();
                yield return Wait(0.6f);
                Check("Стрелка к врагу за кадром", gm.UI.ActiveEdgeMarks > 0, $"стрелок: {gm.UI.ActiveEdgeMarks}");
                Shot("07c_edge_mark");
            }
            else Note("нет врага за кадром в комнате 7 — проверка стрелки пропущена");
            p.Invulnerable = false;

            // 12. Проход всех комнат до победы
            gm.DebugLoadRoom(0);
            yield return Wait(0.5f);
            for (int room = 0; room < gm.RoomCount; room++)
            {
                if (gm.State != GameState.Playing) yield return Wait(1f);
                int before = gm.RoomIndex;
                KillEnemies();
                if (room == 0)
                {
                    yield return Wait(1.2f);
                    Check("Зачистка: распев «ПУЛЯ. ДУРА.» во весь экран", gm.UI.SlamVisible && Announcer.I.WordsSpoken > 0, $"слов {Announcer.I.WordsSpoken}");
                    Shot("07d_chant");
                    yield return Wait(2.8f);
                }
                else yield return Wait(room == gm.RoomCount - 1 ? 5.5f : 4f); // после последней комнаты распев длиннее
                if (room < gm.RoomCount - 1)
                    Check($"Комната {before + 1} → {before + 2}", gm.RoomIndex == before + 1 && gm.State == GameState.Playing, $"room={gm.RoomIndex} state={gm.State}");
            }
            Check("После 8-й комнаты экран победы", gm.State == GameState.Result, $"state={gm.State}");
            var board = Leaderboard.Top;
            Check("Забег попал в таблицу, очки > 0", gm.Stats.score > 0 && board.Count > 0 && (board.Exists(r => r.score == gm.Stats.score) || board.Count == 10),
                $"очки {gm.Stats.score}, записей {board.Count}");
            Shot("24_result_board");
            yield return Wait(0.1f);
            Shot("08_victory");
            yield return Wait(0.5f);
            yield return Tap(Key.R);
            yield return Wait(0.3f);
            Check("R с экрана итогов — новый забег", gm.State == GameState.Playing && gm.RoomIndex == 0);

            // 13. Три смерти — поражение
            for (int life = 0; life < GameManager.Cfg.lives; life++)
            {
                yield return Wait(0.6f);
                var pl = gm.Player;
                if (pl != null && !pl.IsDead)
                    pl.TakeDamage(new DamageInfo { amount = 999, source = pl, point = pl.transform.position, direction = Vector3.forward, bounces = 1 });
                if (life == 0) { yield return Wait(0.3f); Shot("09_self_kill"); }
                yield return Wait(2.2f);
            }
            Check("Три смерти — экран поражения", gm.State == GameState.Result, $"state={gm.State} lives={gm.Lives}");
            Shot("10_defeat");
            yield return Wait(0.3f);
            yield return Tap(Key.G);
            yield return Wait(0.3f);
            Check("G с экрана итогов — в гараж", gm.State == GameState.Garage);

            // 14. Все сочетания корпус × пушка: стрельба и скилл без ошибок
            int errorsBefore = errors.Count;
            float worstFrame = 0f;
            for (int h = 0; h < GameManager.Cfg.hulls.Count; h++)
            for (int t = 0; t < GameManager.Cfg.turrets.Count; t++)
            {
                gm.SelectHull(h);
                gm.SelectTurret(t);
                gm.DebugLoadRoom(7);
                yield return Wait(0.4f);
                var pl = gm.Player;
                if (pl == null) continue;
                pl.GetComponent<PlayerController>().enabled = false;
                pl.Ability.TryActivate(Vector3.forward, Vector3.right);
                if (t == 0)
                {
                    var ability = GameManager.Cfg.hulls[h].ability;
                    bool works = ability switch
                    {
                        AbilityType.Dash => pl.Invulnerable,
                        AbilityType.Mirror => pl.MirrorActive,
                        _ => FindAnyObjectByType<TempWall>() != null,
                    };
                    Check($"Скилл «{GameManager.Cfg.hulls[h].abilityTitle}» срабатывает", works);
                }
                float until = Time.realtimeSinceStartup + 1.6f;
                float a = 0f;
                while (Time.realtimeSinceStartup < until && pl != null && !pl.IsDead)
                {
                    a += 37f;
                    Fire(pl, Quaternion.Euler(0f, a, 0f) * Vector3.forward);
                    worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);
                    yield return null;
                }
                if (h == 2 && t == 3) Shot("11_heavy_rail");
                if (h == 0 && t == 1) Shot("12_light_plasma");
            }
            Check("Все 12 сочетаний корпус × пушка без ошибок", errors.Count == errorsBefore, $"новых ошибок: {errors.Count - errorsBefore}");
            Note($"худший кадр при стрельбе: {worstFrame * 1000f:F1} мс");

            // 15. Нагрузка: плазма без остановки 4 секунды
            gm.SelectHull(1);
            gm.SelectTurret(1);
            gm.DebugLoadRoom(6);
            yield return Wait(0.4f);
            var stress = gm.Player;
            stress.GetComponent<PlayerController>().enabled = false;
            stress.Invulnerable = true;
            int frames = 0, slow = 0;
            float total = 0f, worst = 0f;
            int maxProjectiles = 0;
            float end = Time.realtimeSinceStartup + 4f;
            float ang = 0f;
            while (Time.realtimeSinceStartup < end)
            {
                ang += 23f;
                Fire(stress, Quaternion.Euler(0f, ang, 0f) * Vector3.forward);
                frames++;
                total += Time.unscaledDeltaTime;
                worst = Mathf.Max(worst, Time.unscaledDeltaTime);
                if (Time.unscaledDeltaTime > 0.033f) slow++;
                maxProjectiles = Mathf.Max(maxProjectiles, ProjectileSystem.I.ActiveCount);
                yield return null;
            }
            float avg = total / Mathf.Max(1, frames);
            Check("Нагрузка: средний кадр < 16.7 мс", avg < 0.0167f, $"avg={avg * 1000f:F1} мс, worst={worst * 1000f:F1} мс, кадров > 33 мс: {slow} из {frames}, пуль={maxProjectiles}");
            Shot("13_stress");
            stress.Invulnerable = false;

            // 15б. Время загрузки комнаты (стройка уровня + NavMesh)
            float worstLoad = 0f;
            for (int r = 0; r < gm.RoomCount; r++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                gm.DebugLoadRoom(r);
                worstLoad = Mathf.Max(worstLoad, (float)sw.Elapsed.TotalMilliseconds);
                yield return Wait(0.2f);
            }
            Note($"самая долгая загрузка комнаты: {worstLoad:F1} мс (скрыта за баннером «Комната чиста»)");

            // 16. Разные соотношения сторон
            gm.EnterGarage();
            Screen.SetResolution(1024, 768, FullScreenMode.Windowed);
            yield return Wait(1f);
            Shot("14_garage_4x3");
            gm.DebugLoadRoom(1);
            yield return Wait(0.6f);
            Shot("15_room_4x3");
            Screen.SetResolution(1920, 820, FullScreenMode.Windowed);
            yield return Wait(1f);
            Shot("16_room_21x9");
            gm.EnterGarage();
            yield return Wait(0.6f);
            Shot("17_garage_21x9");
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            yield return Wait(0.6f);

            report.Insert(0, $"Итого: {passed} PASS, {failed} FAIL, ошибок в логе: {errors.Count}\n\n");
            if (errors.Count > 0) report.AppendLine("\nОшибки:\n" + string.Join("\n\n", errors));
            File.WriteAllText(Path.Combine(dir, "qa.txt"), report.ToString());
            yield return Wait(0.3f);
            Application.Quit();
        }

        /// <summary>Центр клетки (x, y) комнаты размером w×h в мировых координатах.</summary>
        static Vector3 CellToWorld(int x, int y, int w, int h)
        {
            float c = GameManager.Cfg.cellSize;
            return new Vector3(-(w - 1) * c * 0.5f + x * c, 0f, (h - 1) * c * 0.5f - y * c);
        }

        /// <summary>Расставляет витрину танков вдали от уровня и снимает её с двух ракурсов.</summary>
        IEnumerator ModelSheet()
        {
            var cfg = GameManager.Cfg;
            var ui = GameObject.Find("UI");
            ui.SetActive(false);
            var root = new GameObject("ModelSheet").transform;
            root.position = new Vector3(200f, 0f, 0f);
            var floor = Prims.Part("Floor", PrimitiveType.Cube, root, new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 30f), Mats.Lit(new Color(0.05f, 0.055f, 0.07f), 0.3f));
            var light = new GameObject("Key").AddComponent<Light>();
            light.transform.SetParent(root, false);
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            for (int h = 0; h < cfg.hulls.Count; h++)
            for (int t = 0; t < cfg.turrets.Count; t++)
            {
                var go = new GameObject($"T{h}{t}");
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3((t - 1.5f) * 2.4f, 0f, (1 - h) * 2.4f);
                go.transform.localRotation = Quaternion.Euler(0f, 200f, 0f);
                var v = go.AddComponent<TankVisual>();
                v.Build(cfg.hulls[h], cfg.turrets[t], Team.Player, null, false);
                v.SetTurretLocalYaw(-30f);
            }
            CameraRig.I.LookAt(root.position, 13f, 45f);
            yield return Wait(0.4f);
            Shot("00a_models_player");
            yield return null;
            yield return null;

            foreach (Transform child in root) if (child.name.StartsWith("T")) Destroy(child.gameObject);
            for (int i = 0; i < cfg.enemies.Count; i++)
            {
                var e = cfg.enemies[i];
                var go = new GameObject("E" + i);
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3((i % 3 - 1) * 2.8f, 0f, (i / 3 == 0 ? 1.4f : -1.4f));
                go.transform.localRotation = Quaternion.Euler(0f, 200f, 0f);
                var v = go.AddComponent<TankVisual>();
                v.Build(cfg.Hull(e.hullId), cfg.Turret(e.turretId), Team.Enemy, e, false);
                v.SetTurretLocalYaw(-30f);
                v.Reveal(0.35f);
            }
            CameraRig.I.LookAt(root.position, 10f, 40f);
            yield return Wait(0.4f);
            Shot("00b_models_enemies");
            yield return null;
            yield return null;

            // Вид из игры: тот же угол, что у игровой камеры
            CameraRig.I.LookAt(root.position, 16f, 58f);
            yield return Wait(0.3f);
            Shot("00c_models_gameplay_angle");
            yield return null;
            yield return null;
            Destroy(root.gameObject);
            ui.SetActive(true);
            GameManager.I.EnterGarage();
            yield return Wait(0.5f);
        }

        IEnumerator WorldObjectTests()
        {
            var gm = GameManager.I;
            var cannon = GameManager.Cfg.Turret("cannon");
            Vector3 up = Vector3.up * Ricochet.ShotHeight;

            // Звук: одни уши — над игроком; гамма отскоков растёт
            gm.DebugLoadRoom(0);
            yield return Wait(0.5f);
            var p = gm.Player;
            int listeners = 0;
            AudioListener ears = null;
            foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (l.isActiveAndEnabled) { listeners++; ears = l; }
            float earsOff = ears != null ? Prims.Flat(ears.transform.position - p.transform.position).magnitude : 99f;
            Check("Объёмный звук: один слушатель, он над игроком", listeners == 1 && earsOff < 0.5f, $"слушателей {listeners}, смещение {earsOff:F2}");
            Check("Отскоки звучат по гамме вверх", ProjectileSystem.BouncePitch(1, Team.Player) < ProjectileSystem.BouncePitch(2, Team.Player)
                && ProjectileSystem.BouncePitch(2, Team.Player) < ProjectileSystem.BouncePitch(4, Team.Player)
                && ProjectileSystem.BouncePitch(1, Team.Enemy) < ProjectileSystem.BouncePitch(1, Team.Player));
            int clips = 0;
            foreach (var v in Announcer.I.variants) if (v.first != null && v.second != null) clips++;
            Check("Голос: 3+ варианта распева со звуком", Announcer.I.VariantCount >= 3 && clips == Announcer.I.VariantCount, $"вариантов {Announcer.I.VariantCount}, с клипами {clips}");

            // Ваншот: мишень (2 брони) падает с одного выстрела, за убийство дают очки
            p.GetComponent<PlayerController>().enabled = false;
            p.Invulnerable = true;
            Tank target = null, rookie = null;
            foreach (var t in Tank.All)
            {
                if (t.Team != Team.Enemy) continue;
                if (t.Enemy.behaviour == EnemyBehaviour.Dummy) target = t; else rookie = t;
            }
            rookie.Brain.enabled = false;
            int score0 = gm.Stats.score;
            ProjectileSystem.I.Spawn(p, cannon, target.transform.position + Vector3.left * 2f + up, Vector3.right, 0f, new ShotMods { power = true });
            yield return Wait(0.6f);
            Check("Бонус «Ваншот»: враг погибает с одного попадания", target.IsDead, $"броня {target.Hp}/{target.MaxHp}");
            Check("Очки за убийство", gm.Stats.score > score0, $"{score0} → {gm.Stats.score}");

            // Метка: попавший враг подсвечен. Цель — турель (4 брони), чтобы пережила попадание
            var tCfg = GameManager.Cfg.Enemy('t');
            var marked = Tank.Spawn(Team.Enemy, GameManager.Cfg.Hull(tCfg.hullId), GameManager.Cfg.Turret(tCfg.turretId), tCfg,
                CellToWorld(6, 10, 21, 13), Quaternion.identity, gm.LevelRoot);
            yield return Wait(0.2f);
            marked.Brain.enabled = false;
            ProjectileSystem.I.Spawn(p, cannon, marked.transform.position + Vector3.left * 2f + up, Vector3.right, 0f, new ShotMods { mark = true });
            yield return Wait(0.4f);
            Check("Бонус «Метка»: враг виден после попадания", !marked.IsDead && marked.Visual.Marked && marked.Visual.Shown, $"marked={marked.Visual.Marked} shown={marked.Visual.Shown} hp={marked.Hp}");
            Shot("18_marked_enemy");
            yield return Wait(0.1f);

            // Аптечка и щит
            p.Invulnerable = false;
            p.TakeDamage(new DamageInfo { amount = 2, source = rookie, point = p.transform.position, direction = Vector3.forward });
            int hpHurt = p.Hp;
            Pickup.Spawn(PickupType.Heal, p.transform.position + Vector3.right * 0.3f, gm.LevelRoot);
            yield return Wait(0.3f);
            Check("Аптечка подбирается наездом и лечит", p.Hp > hpHurt && Pickup.All.Count == 0, $"{hpHurt} → {p.Hp}, на полу {Pickup.All.Count}");
            Pickup.Spawn(PickupType.Shield, p.transform.position, gm.LevelRoot);
            yield return Wait(0.3f);
            int hpShield = p.Hp;
            bool hadShield = p.ShieldCharges > 0;
            p.TakeDamage(new DamageInfo { amount = 1, source = rookie, point = p.transform.position, direction = Vector3.forward });
            Check("Щит гасит одно попадание", hadShield && p.Hp == hpShield && p.ShieldCharges == 0, $"щит={hadShield}, броня {hpShield} → {p.Hp}");
            p.Invulnerable = true;

            // Жёлтый ящик в комнате 2 роняет бонус
            gm.DebugLoadRoom(1);
            yield return Wait(0.5f);
            var box = FindAnyObjectByType<SupplyBox>();
            bool hasBox = box != null;
            for (int i = 0; i < 4 && box != null; i++)
                box.TakeDamage(new DamageInfo { amount = 1, source = gm.Player, point = box.transform.position, direction = Vector3.up });
            yield return Wait(0.3f);
            Check("Жёлтый ящик роняет бонус", hasBox && Pickup.All.Count == 1, $"ящик={hasBox}, бонусов {Pickup.All.Count}");
            Shot("19_pickup");
            yield return Wait(0.1f);

            // Бочка в комнате 3: взрыв, шрапнель, урон турели рядом
            gm.DebugLoadRoom(2);
            yield return Wait(0.5f);
            p = gm.Player;
            p.Invulnerable = true;
            p.GetComponent<PlayerController>().enabled = false;
            var barrel = FindAnyObjectByType<Barrel>();
            bool hasBarrel = barrel != null;
            Tank turret = null;
            foreach (var t in Tank.All) if (t.Team == Team.Enemy && t.Enemy.symbol == "t") turret = t;
            bool hadTurret = turret != null;
            int turretHp = hadTurret ? turret.Hp : 0;
            if (hasBarrel)
                ProjectileSystem.I.Spawn(p, cannon, barrel.transform.position + Vector3.left * 1.4f + up, Vector3.right, 0f);
            int maxAlive = 0;
            for (int i = 0; i < 30; i++) { maxAlive = Mathf.Max(maxAlive, ProjectileSystem.I.CountAlive()); yield return null; }
            Check("Бочка взрывается от пули", hasBarrel && barrel == null, hasBarrel ? (barrel == null ? "" : "бочка цела") : "бочки нет в комнате");
            Check("Шрапнель: кольцо осколков", maxAlive >= GameManager.Cfg.shrapnelCount / 2, $"пуль в воздухе до {maxAlive}");
            // Погибший танк уничтожается, и Unity считает ссылку на него равной null
            Check("Взрыв ранит врага рядом", hadTurret && (turret == null || turret.IsDead || turret.Hp < turretHp),
                !hadTurret ? "турели нет" : turret == null ? "турель уничтожена" : $"турель {turretHp} → {turret.Hp}");
            Shot("20_barrel");
            yield return Wait(0.1f);

            // Ловушка в комнате 4 зовёт охотников
            gm.DebugLoadRoom(3);
            yield return Wait(0.5f);
            p = gm.Player;
            p.Invulnerable = true;
            p.GetComponent<PlayerController>().enabled = false;
            var trap = FindAnyObjectByType<NoiseTrap>();
            var hunters = new List<Tank>();
            foreach (var t in Tank.All) if (t.Team == Team.Enemy && t.Enemy.behaviour == EnemyBehaviour.Hunter) hunters.Add(t);
            bool calmBefore = hunters.TrueForAll(h => !h.Brain.Alerted);
            if (trap != null) p.TeleportTo(trap.transform.position);
            yield return Wait(0.8f);
            bool allAlerted = hunters.Count > 0 && hunters.TrueForAll(h => h.IsDead || h.Brain.Alerted);
            Check("Ловушка-сирена поднимает всех охотников", trap != null && calmBefore && allAlerted, $"ловушка={trap != null}, спокойны до={calmBefore}, тревога после={allAlerted}");
            Shot("21_trap");
            yield return Wait(0.1f);

            // Порталы в комнате 5: пуля и танк
            gm.DebugLoadRoom(4);
            yield return Wait(0.5f);
            p = gm.Player;
            p.Invulnerable = true;
            var portals = new List<Teleporter>(Teleporter.All);
            portals.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
            bool paired = portals.Count == 2 && portals[0].Partner == portals[1];
            var dCfg = GameManager.Cfg.Enemy('d');
            var victim = Tank.Spawn(Team.Enemy, GameManager.Cfg.Hull(dCfg.hullId), GameManager.Cfg.Turret(dCfg.turretId), dCfg,
                CellToWorld(15, 6, 21, 13), Quaternion.identity, gm.LevelRoot);
            // Второй враг держит комнату «незачищенной», пока идёт тест
            var keeper = Tank.Spawn(Team.Enemy, GameManager.Cfg.Hull(dCfg.hullId), GameManager.Cfg.Turret(dCfg.turretId), dCfg,
                CellToWorld(7, 1, 21, 13), Quaternion.identity, gm.LevelRoot);
            foreach (var t in new List<Tank>(Tank.All))
                if (t.Team == Team.Enemy && t != victim && t != keeper) t.TakeDamage(new DamageInfo { amount = 999, source = victim, point = t.transform.position, direction = Vector3.forward });
            yield return Wait(0.3f);
            // Пуля летит влево в левый портал, выходит из правого — и попадает в мишень «за спиной»
            ProjectileSystem.I.Spawn(p, cannon, CellToWorld(6, 6, 21, 13) + up, Vector3.left, 0f);
            yield return Wait(1.2f);
            Check("Портал переносит пулю", paired && (victim.IsDead || victim.Hp < victim.MaxHp), $"порталов {portals.Count}, мишень {victim.Hp}/{victim.MaxHp}");
            p.GetComponent<PlayerController>().enabled = true;
            p.TeleportTo(CellToWorld(1, 6, 21, 13));
            yield return Wait(1f);
            float tp0 = p.LastTeleport;
            yield return Hold(1.2f, Key.D);
            Check("Портал переносит танк", p.LastTeleport > tp0 && p.transform.position.x > 5f, $"x={p.transform.position.x:F1}");
            Shot("22_portal");
            yield return Wait(0.1f);

            // Щитоносец: спереди пуля отражается, сзади — ранит
            gm.DebugLoadRoom(0);
            yield return Wait(0.5f);
            p = gm.Player;
            p.Invulnerable = true;
            p.GetComponent<PlayerController>().enabled = false;
            var qCfg = GameManager.Cfg.Enemy('q');
            var shield = Tank.Spawn(Team.Enemy, GameManager.Cfg.Hull(qCfg.hullId), GameManager.Cfg.Turret(qCfg.turretId), qCfg,
                CellToWorld(6, 9, 21, 13), Quaternion.identity, gm.LevelRoot);
            KillEnemiesExcept(shield);
            yield return Wait(0.3f);
            shield.Brain.enabled = false;
            shield.Visual.SetTurretDirection(Vector3.forward);
            yield return Wait(0.3f);
            Vector3 front = Prims.Flat(shield.Visual.TurretPivot.forward).normalized;
            int hp0 = shield.Hp;
            ProjectileSystem.I.Spawn(p, cannon, shield.transform.position + front * 2.2f + up, -front, 0f);
            yield return Wait(0.5f);
            int hpFront = shield.Hp;
            ProjectileSystem.I.Spawn(p, cannon, shield.transform.position - front * 2.2f + up, front, 0f);
            yield return Wait(0.5f);
            Check("Щитоносец: щит спереди держит пулю", shield.HasFrontShield && hpFront == hp0, $"броня {hp0} → {hpFront}");
            Check("Щитоносец: сзади пробивается", shield.IsDead || shield.Hp < hpFront, $"броня {hpFront} → {shield.Hp}");
            Shot("23_shield_enemy");
            yield return Wait(0.1f);
            p.Invulnerable = false;
        }

        static void Fire(Tank t, Vector3 dir)
        {
            if (t == null || t.IsDead) return;
            t.Visual.SetTurretDirection(dir);
            t.Weapon.TryFire();
        }

        static Tank FirstEnemy()
        {
            foreach (var t in Tank.All)
                if (t.Team == Team.Enemy && !t.IsDead && t.Enemy.behaviour == EnemyBehaviour.Hunter) return t;
            return null;
        }

        static void KillEnemiesExcept(Tank keep)
        {
            var list = new List<Tank>(Tank.All);
            foreach (var t in list)
                if (t.Team == Team.Enemy && !t.IsDead && t != keep)
                    t.TakeDamage(new DamageInfo { amount = 999, source = keep, point = t.transform.position, direction = Vector3.forward });
        }

        static void KillEnemies()
        {
            var list = new List<Tank>(Tank.All);
            foreach (var t in list)
                if (t.Team == Team.Enemy && !t.IsDead)
                    t.TakeDamage(new DamageInfo { amount = 999, source = GameManager.I.Player, point = t.transform.position, direction = Vector3.forward, bounces = 1 });
        }
    }
}
