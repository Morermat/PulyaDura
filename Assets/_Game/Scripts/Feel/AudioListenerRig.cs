using UnityEngine;

namespace PulyaDura
{
    /// <summary>
    /// «Уши» игры стоят не в камере (она высоко и сбоку), а над танком игрока и смотрят как экран:
    /// правое ухо — вправо по экрану. Поэтому выстрел справа слышно справа, далёкий — тише и с эхом комнаты.
    /// </summary>
    [RequireComponent(typeof(AudioListener))]
    public class AudioListenerRig : MonoBehaviour
    {
        public static AudioListenerRig I { get; private set; }
        [Tooltip("Высота ушей над танком")] public float height = 2.5f;

        void Awake() => I = this;

        void LateUpdate()
        {
            var gm = GameManager.I;
            Vector3 target;
            if (gm != null && gm.Player != null && !gm.Player.IsDead) target = gm.Player.transform.position + Vector3.up * height;
            else if (Camera.main != null) target = Camera.main.transform.position;
            else return;
            transform.SetPositionAndRotation(target, Quaternion.identity);
        }
    }
}
