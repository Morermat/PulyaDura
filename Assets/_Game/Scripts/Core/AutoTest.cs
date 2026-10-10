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

            // 2. Выбор корпуса и пушки клавишами мыши не проверяем, проверяем API
            gm.SelectHull(1);
            gm.SelectTurret(0);

            // 3. Enter запускает забег
            yield return Tap(Key.Enter);
            yield return Wait(0.3f);
            Check("Enter в гараже начинает забег", gm.State == GameState.Playing, $"state={gm.State}");
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
                Check("Охотник реагирует на выстрел (едет или стреляет)", hunter == null || d1 < d0 - 1f || enemyShots > 0, $"dist {d0:F1} → {d1:F1}, выстрелов врагов {enemyShots}");
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

            // 12. Проход всех комнат до победы
            gm.DebugLoadRoom(0);
            yield return Wait(0.5f);
            for (int room = 0; room < gm.RoomCount; room++)
            {
                if (gm.State != GameState.Playing) yield return Wait(1f);
                int before = gm.RoomIndex;
                KillEnemies();
                yield return Wait(2.6f);
                if (room < gm.RoomCount - 1)
                    Check($"Комната {before + 1} → {before + 2}", gm.RoomIndex == before + 1 && gm.State == GameState.Playing, $"room={gm.RoomIndex} state={gm.State}");
            }
            Check("После 8-й комнаты экран победы", gm.State == GameState.Result, $"state={gm.State}");
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
