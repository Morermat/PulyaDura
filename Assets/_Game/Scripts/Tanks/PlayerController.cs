using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PulyaDura
{
    /// <summary>
    /// Управление: WASD — ехать (относительно экрана), мышь — целиться, ЛКМ — огонь, Пробел/ПКМ — скилл.
    /// Геймпад: левый стик, правый стик, RT, LT/A.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        Tank tank;
        Vector3 move;
        Vector3 aimDir = Vector3.forward;
        float dashTime;
        Vector3 dashVelocity;
        float touchTimer;
        AimLine aimLine;
        readonly Collider[] touchBuffer = new Collider[24];

        public Vector3 AimDirection => aimDir;

        public void Init(Tank owner)
        {
            tank = owner;
            aimDir = transform.forward;
            aimLine = gameObject.AddComponent<AimLine>();
            aimLine.Init(owner);
        }

        public void Dash(Vector3 dir, float speed, float duration)
        {
            dashVelocity = Prims.Flat(dir).normalized * speed;
            dashTime = duration;
        }

        void Update()
        {
            if (!GameManager.I.InputEnabled)
            {
                move = Vector3.zero;
                aimLine.SetVisible(false);
                return;
            }
            aimLine.SetVisible(true);

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;

            Vector2 mv = Vector2.zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) mv.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) mv.y -= 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) mv.x -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) mv.x += 1f;
            }
            if (pad != null) mv += pad.leftStick.ReadValue();
            move = Vector3.ClampMagnitude(new Vector3(mv.x, 0f, mv.y), 1f);

            Vector2 rs = pad != null ? pad.rightStick.ReadValue() : Vector2.zero;
            if (rs.sqrMagnitude > 0.15f)
            {
                aimDir = new Vector3(rs.x, 0f, rs.y).normalized;
            }
            else if (mouse != null && Camera.main != null)
            {
                Ray ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
                var plane = new Plane(Vector3.up, new Vector3(0f, Ricochet.ShotHeight, 0f));
                if (plane.Raycast(ray, out float enter))
                {
                    Vector3 d = Prims.Flat(ray.GetPoint(enter) - transform.position);
                    if (d.sqrMagnitude > 0.04f) aimDir = d.normalized;
                }
            }
            tank.Visual.SetTurretDirection(aimDir);

            bool fire = (mouse != null && mouse.leftButton.isPressed) || (pad != null && pad.rightTrigger.isPressed);
            if (fire) tank.Weapon.TryFire();

            bool ability = (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.leftShiftKey.wasPressedThisFrame))
                           || (mouse != null && mouse.rightButton.wasPressedThisFrame)
                           || (pad != null && (pad.leftTrigger.wasPressedThisFrame || pad.buttonSouth.wasPressedThisFrame));
            if (ability) tank.Ability.TryActivate(move, aimDir);

            // «На ощупь»: стены вплотную к танку слегка проступают из темноты
            touchTimer -= Time.deltaTime;
            if (touchTimer <= 0f)
            {
                touchTimer = 0.12f;
                float r = GameManager.Cfg.touchRadius;
                int n = Physics.OverlapSphereNonAlloc(transform.position, r, touchBuffer, Layers.WallMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    if (!touchBuffer[i].TryGetComponent<WallBlock>(out var wall)) continue;
                    float d = Vector3.Distance(touchBuffer[i].ClosestPoint(transform.position), transform.position);
                    wall.Show(Mathf.Lerp(0.5f, 0.1f, d / r), GameManager.Cfg.echoColor);
                }
            }
        }

        void FixedUpdate()
        {
            var rb = tank.Body;
            rb.angularVelocity = Vector3.zero;
            if (dashTime > 0f)
            {
                dashTime -= Time.fixedDeltaTime;
                rb.linearVelocity = dashVelocity;
                return;
            }

            Vector3 target = move * tank.Hull.speed;
            rb.linearVelocity = Vector3.MoveTowards(rb.linearVelocity, target, tank.Hull.speed * 8f * Time.fixedDeltaTime);

            if (move.sqrMagnitude > 0.01f)
            {
                // Танк может ехать задом: разворачиваемся к ближайшему из двух направлений
                Vector3 fwd = transform.forward;
                Vector3 want = Vector3.Dot(fwd, move) >= -0.2f ? move : -move;
                var rot = Quaternion.RotateTowards(rb.rotation, Quaternion.LookRotation(want), tank.Hull.turnSpeed * Time.fixedDeltaTime);
                rb.MoveRotation(rot);
            }
        }
    }

    /// <summary>
    /// Линия прицела. Через стену, которую ты ещё не видел, она не подсказывает:
    /// просто короткий луч. Если стена подсвечена эхом, видно, куда уйдёт отскок.
    /// </summary>
    public class AimLine : MonoBehaviour
    {
        const float BlindLength = 3.2f;
        Tank tank;
        LineRenderer line;
        readonly List<Vector3> path = new();
        readonly List<Collider> hits = new();
        readonly List<Vector3> shown = new();

        public void Init(Tank owner)
        {
            tank = owner;
            var go = new GameObject("AimLine");
            go.transform.SetParent(transform, false);
            line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = Mats.Fx(GameManager.Cfg.bulletPlayer * 0.5f);
            line.widthMultiplier = 0.05f;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.useWorldSpace = true;
        }

        public void SetVisible(bool v)
        {
            if (line.enabled != v) line.enabled = v;
        }

        void LateUpdate()
        {
            if (!line.enabled || tank.IsDead) return;
            var cfg = tank.Turret;
            Vector3 dir = Prims.Flat(tank.Visual.TurretPivot.forward).normalized;
            Vector3 origin = tank.ShotOrigin;
            Ricochet.TracePath(origin, dir, cfg.range, Mathf.Min(cfg.bounces, 1), Mathf.Max(0.05f, cfg.projectileRadius), Layers.WallMask, path, hits);

            shown.Clear();
            shown.Add(origin);
            for (int i = 1; i < path.Count; i++)
            {
                bool known = i - 1 < hits.Count && hits[i - 1] != null
                             && hits[i - 1].TryGetComponent<WallBlock>(out var wall) && (wall.Reveal > 0.12f || wall.baseGlow > 0f);
                Vector3 a = path[i - 1], b = path[i];
                if (known)
                {
                    shown.Add(b);
                    continue;
                }
                // Неизвестная стена: луч фиксированной длины, не выдаёт, где она
                shown.Add(a + (b - a).normalized * BlindLength);
                break;
            }

            line.positionCount = shown.Count;
            for (int i = 0; i < shown.Count; i++) line.SetPosition(i, shown[i]);

            float charge = tank.Weapon.Charge01;
            float alpha = tank.Weapon.Ready || tank.Weapon.Charging ? 0.9f : 0.35f;
            var c = new Color(1f, 1f, 1f, alpha);
            line.startColor = c;
            c.a = alpha * 0.15f;
            line.endColor = c;
            line.widthMultiplier = 0.05f + charge * 0.08f;
        }
    }
}
