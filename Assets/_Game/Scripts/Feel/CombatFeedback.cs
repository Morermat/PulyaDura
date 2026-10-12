using UnityEngine;

namespace PulyaDura
{
    public enum KillKind { Self, Betrayal, Direct, Ricochet, DoubleRicochet, TripleRicochet, Explosion }

    /// <summary>
    /// «Сок» боя: взрывы, тряска, hit-stop, всплывающие надписи. Только реакция, без правил игры:
    /// счёт и состояние комнаты ведёт GameManager.
    /// </summary>
    public class CombatFeedback : MonoBehaviour
    {
        GameConfig cfg;
        GameUI ui;

        public void Init(GameConfig config, GameUI gameUi)
        {
            cfg = config;
            ui = gameUi;
        }

        void OnEnable() => GameEvents.TankDamaged += OnDamaged;
        void OnDisable() => GameEvents.TankDamaged -= OnDamaged;

        void OnDamaged(Tank t, DamageInfo d)
        {
            if (t.IsDead) return;
            if (t.Team == Team.Player)
            {
                PostFx.I.Hurt();
                ui.DamageFlash();
                CameraRig.I.Shake(0.45f);
                TimeController.I.HitStop(0.07f);
                Sfx.I.Play(SfxId.Hurt, t.transform.position);
            }
            else
            {
                TimeController.I.HitStop(0.035f);
                Sfx.I.Play(SfxId.Hit, t.transform.position, 0.8f, Random.Range(0.9f, 1.1f));
            }
        }

        /// <summary>Вызывается GameManager после подсчёта: какой это был килл и какая серия.</summary>
        public void OnKill(Tank t, KillKind kind, int streak)
        {
            Vector3 pos = t.transform.position;
            bool player = t.Team == Team.Player;
            Fx.I.Explosion(pos + Vector3.up * 0.4f, player ? cfg.bulletDanger : cfg.enemyAccent);
            Sfx.I.Play(SfxId.Explode, pos);
            EchoSystem.I.Emit(pos, 7f, player ? cfg.bulletDanger : cfg.enemyEchoColor, 1f);
            CameraRig.I.Shake(0.5f);
            TimeController.I.HitStop(0.09f);
            if (player) return;

            string label = kind switch
            {
                KillKind.Self => "САМ СЕБЯ!",
                KillKind.Betrayal => "СВОЙ СВОЕГО!",
                KillKind.Explosion => "БАБАХ!",
                _ when streak == 2 => "ДВОЙНОЕ!",
                _ when streak == 3 => "ТРОЙНОЕ!",
                _ when streak > 3 => $"СЕРИЯ ×{streak}!",
                KillKind.TripleRicochet => "ТРОЙНОЙ РИКОШЕТ!",
                KillKind.DoubleRicochet => "ДВОЙНОЙ РИКОШЕТ!",
                KillKind.Ricochet => "РИКОШЕТ!",
                _ => "",
            };
            Color color = kind is KillKind.Self or KillKind.Betrayal ? UiKit.Enemy
                : kind is KillKind.Ricochet or KillKind.DoubleRicochet or KillKind.TripleRicochet or KillKind.Explosion || streak > 1 ? UiKit.Danger : UiKit.Accent;
            if (label.Length > 0) ui.Floating(pos + Vector3.up * 1.5f, label, color, 40);

            if (kind is KillKind.DoubleRicochet or KillKind.TripleRicochet or KillKind.Explosion || (t.Enemy != null && t.Enemy.boss))
            {
                // Награда за главный навык: замедление и наезд камеры на попадание
                TimeController.I.SlowMo(0.2f, 0.7f);
                CameraRig.I.Focus(pos, 0.3f, 1.1f);
            }
        }
    }
}
