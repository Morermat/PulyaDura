using UnityEngine;

namespace PulyaDura
{
    /// <summary>
    /// Поведение босса поверх обычного ИИ:
    /// - каждые 4.5 с «рычит» — красная волна эха из его позиции, видно, где он;
    /// - на половине здоровья впадает в ярость: заряд рельсы короче, выстрел двумя лучами (см. TankWeapon).
    /// Вешается в Tank.Setup, если у врага стоит флаг boss.
    /// </summary>
    public class BossAura : MonoBehaviour
    {
        Tank tank;
        float timer = 2f;
        bool announcedRage;

        public bool Enraged => tank != null && tank.Hp * 2 <= tank.MaxHp;

        void Awake() => tank = GetComponent<Tank>();

        void Update()
        {
            if (tank == null || tank.IsDead || GameManager.I.State != GameState.Playing) return;
            if (Enraged && !announcedRage)
            {
                announcedRage = true;
                GameManager.I.UI.Banner("ЯРОСТЬ", "", UiKit.Danger, 1.6f);
                Announcer.I.Say("line_rage");
                CameraRig.I.Shake(0.5f);
            }
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = Enraged ? 3f : 4.5f;
            EchoSystem.I.Emit(transform.position, Enraged ? 7f : 5.5f, GameManager.Cfg.enemyEchoColor, 0.8f);
            Sfx.I.Play(SfxId.RailCharge, transform.position, 0.8f, 0.45f);
        }
    }
}
