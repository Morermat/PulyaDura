using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PulyaDura
{
    /// <summary>
    /// Смоук-тест: билд, запущенный с флагом -autotest, сам проходит ключевые сценарии,
    /// сохраняет скриншоты и лог ошибок, затем закрывается. В обычной игре не активен.
    /// Пример: PulyaDura.exe -autotest -shots C:\temp\shots
    /// </summary>
    public class AutoTest : MonoBehaviour
    {
        string dir;
        readonly List<string> errors = new();
        readonly List<Vector3> path = new();

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

        void Awake() => Application.logMessageReceived += OnLog;
        void OnDestroy() => Application.logMessageReceived -= OnLog;

        void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add($"[{type}] {msg}\n{stack}");
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(dir);
            var gm = GameManager.I;

            yield return Wait(1.5f);
            Shot("01_garage");

            gm.StartRun();
            yield return Wait(0.45f);
            Shot("02_room1_reveal");
            yield return Wait(3.2f);
            Shot("03_room1_dark");

            var player = gm.Player;
            player.GetComponent<PlayerController>().enabled = false;
            Fire(player, new Vector3(1f, 0f, 0.15f));
            yield return Wait(0.32f);
            Shot("04_first_bounce");
            yield return Wait(0.5f);
            Shot("05_echo_spread");

            yield return Wait(1f);
            var target = FirstEnemy();
            if (target != null)
            {
                var dir = BankShot(player, target.transform.position) ?? Prims.Flat(target.transform.position - player.transform.position).normalized;
                Fire(player, dir);
                yield return Wait(0.25f);
                Shot("06_bank_shot");
                yield return Wait(1.0f);
                Shot("07_after_bank");
            }

            KillEnemies();
            yield return Wait(0.5f);
            Shot("08_room_clear");
            yield return Wait(2.4f);
            Shot("09_room2_start");

            var p2 = gm.Player;
            if (p2 != null)
            {
                p2.GetComponent<PlayerController>().enabled = false;
                for (int k = 0; k < 4; k++)
                {
                    Fire(p2, Quaternion.Euler(0f, k * 70f + 20f, 0f) * Vector3.forward);
                    yield return Wait(0.6f);
                }
                Shot("10_room2_echoes");
                yield return Wait(3.2f);
                Shot("10b_echo_memory");
            }

            gm.DebugLoadRoom(7);
            yield return Wait(0.6f);
            Shot("11_boss_reveal");
            var p3 = gm.Player;
            p3.GetComponent<PlayerController>().enabled = false;
            Fire(p3, Vector3.right);
            yield return Wait(3.5f);
            Shot("12_boss_fight");

            for (int life = 0; life < 4 && gm.State != GameState.Result; life++)
            {
                var p = gm.Player;
                if (p != null && !p.IsDead && gm.State == GameState.Playing)
                {
                    p.TakeDamage(new DamageInfo { amount = 999, source = p, point = p.transform.position, direction = Vector3.forward, bounces = 1 });
                    yield return Wait(0.35f);
                    if (life == 0) Shot("13_self_kill");
                }
                yield return Wait(2.2f);
            }
            yield return Wait(0.5f);
            Shot("14_result");

            gm.EnterGarage();
            gm.SelectHull(2);
            gm.SelectTurret(3);
            yield return Wait(0.8f);
            Shot("15_garage_heavy_rail");

            File.WriteAllText(Path.Combine(dir, "log.txt"),
                errors.Count == 0 ? "OK: ошибок нет" : string.Join("\n\n", errors));
            yield return Wait(0.5f);
            Application.Quit();
        }

        static IEnumerator Wait(float s)
        {
            yield return new WaitForSecondsRealtime(s);
        }

        void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));

        static void Fire(Tank t, Vector3 dir)
        {
            if (t == null || t.IsDead) return;
            t.Visual.SetTurretDirection(dir);
            t.Weapon.TryFire();
        }

        static Tank FirstEnemy()
        {
            foreach (var t in Tank.All)
                if (t.Team == Team.Enemy && !t.IsDead) return t;
            return null;
        }

        static void KillEnemies()
        {
            var list = new List<Tank>(Tank.All);
            foreach (var t in list)
                if (t.Team == Team.Enemy && !t.IsDead)
                    t.TakeDamage(new DamageInfo { amount = 999, source = GameManager.I.Player, point = t.transform.position, direction = Vector3.forward, bounces = 1 });
        }

        Vector3? BankShot(Tank shooter, Vector3 target)
        {
            for (int a = 0; a < 360; a += 2)
            {
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                Ricochet.TracePath(shooter.ShotOrigin, dir, 40f, 1, shooter.Turret.projectileRadius, Layers.WallMask, path);
                if (path.Count >= 3 && Ricochet.DistanceToSegment(target, path[1], path[2], out _) < 0.5f) return dir;
            }
            return null;
        }
    }
}
