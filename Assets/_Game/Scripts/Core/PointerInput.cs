using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace PulyaDura
{
    /// <summary>
    /// Мышь с запасным путём. Обычно мышь читается через Input System. Но при запуске Windows-билда
    /// на Linux через Wine/Proton Input System иногда не получает мышь, а старый UnityEngine.Input — получает
    /// (он работает через обычные оконные сообщения). Если старый ввод видит движение мыши, а новый нет,
    /// переключаемся на старый: и для прицела и стрельбы, и для кнопок меню (StandaloneInputModule).
    /// Для этого в Project Settings → Player → Active Input Handling стоит «Both».
    /// </summary>
    public class PointerInput : MonoBehaviour
    {
        public static bool UsingLegacy { get; private set; }

        Vector2 lastNew, lastLegacy;
        float legacyOnlyTime;

        /// <summary>Позиция курсора в пикселях экрана.</summary>
        public static Vector2 Position
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                if (UsingLegacy || Mouse.current == null) return Input.mousePosition;
#endif
                return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            }
        }

        public static bool Available
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                if (UsingLegacy) return true;
#endif
                return Mouse.current != null;
            }
        }

        public static bool LeftHeld
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                if (UsingLegacy) return Input.GetMouseButton(0);
#endif
                return Mouse.current != null && Mouse.current.leftButton.isPressed;
            }
        }

        public static bool RightPressed
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                if (UsingLegacy) return Input.GetMouseButtonDown(1);
#endif
                return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
            }
        }

        void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (UsingLegacy) return;
            Vector2 legacy = Input.mousePosition;
            Vector2 now = Mouse.current != null ? Mouse.current.position.ReadValue() : lastNew;
            bool legacyMoved = (legacy - lastLegacy).sqrMagnitude > 4f;
            bool newMoved = (now - lastNew).sqrMagnitude > 0.5f;
            lastLegacy = legacy;
            lastNew = now;
            // Старый ввод видит движение, новый — нет: суммарно полсекунды движения — переключаемся
            if (legacyMoved && !newMoved) legacyOnlyTime += Time.unscaledDeltaTime;
            else if (newMoved) legacyOnlyTime = 0f;
            if (legacyOnlyTime > 0.5f) SwitchToLegacy();
#endif
        }

        void SwitchToLegacy()
        {
            UsingLegacy = true;
            Debug.Log("[PointerInput] Input System не видит мышь — переключаемся на UnityEngine.Input");
            var es = EventSystem.current != null ? EventSystem.current : FindAnyObjectByType<EventSystem>();
            if (es == null) return;
            var module = es.GetComponent<InputSystemUIInputModule>();
            if (module != null) module.enabled = false;
            if (es.GetComponent<StandaloneInputModule>() == null) es.gameObject.AddComponent<StandaloneInputModule>();
        }
    }
}
