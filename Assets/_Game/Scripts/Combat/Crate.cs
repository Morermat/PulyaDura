using UnityEngine;

namespace PulyaDura
{
    /// <summary>Ящик: поглощает снаряды и ломается после двух попаданий.</summary>
    public class Crate : MonoBehaviour, IDamageable
    {
        public int hp = 2;

        public void TakeDamage(DamageInfo info)
        {
            if (hp <= 0) return;
            hp -= Mathf.Max(1, info.amount);
            GetComponent<WallBlock>().Flash();
            Sfx.I.Play(SfxId.Hit, transform.position, 0.6f, 0.7f);
            if (hp > 0) return;

            Fx.I.Debris(transform.position, transform.localScale, Mats.Crate, info.direction, 6);
            Fx.I.Sparks(transform.position + Vector3.up * 0.4f, Vector3.up, GameManager.Cfg.tempWallColor, 16, 5f);
            Sfx.I.Play(SfxId.CrateBreak, transform.position);
            EchoSystem.I.Emit(transform.position + Vector3.up * Ricochet.ShotHeight, 3.5f, GameManager.Cfg.tempWallColor, 0.8f);
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
    }
}
