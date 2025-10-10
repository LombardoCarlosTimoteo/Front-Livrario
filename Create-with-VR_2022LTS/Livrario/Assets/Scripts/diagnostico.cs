using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem; // Mouse, Touchscreen (nuevo Input System)
#endif

public class UIRaycastDebugger : MonoBehaviour
{
    void Update()
    {
        Vector2? screenPos = null;

        // --- Nuevo Input System ---
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            screenPos = Mouse.current.position.ReadValue();
        else if (Touchscreen.current != null)
        {
            var t = Touchscreen.current.primaryTouch;
            if (t.press.wasPressedThisFrame) screenPos = t.position.ReadValue();
        }
#else
        // --- Input antiguo (por si tenés Both) ---
        if (Input.GetMouseButtonDown(0))
            screenPos = Input.mousePosition;
#endif

        if (!screenPos.HasValue) return;
        RaycastUI(screenPos.Value);
    }

    void RaycastUI(Vector2 screenPos)
    {
        if (EventSystem.current == null)
        {
            Debug.LogWarning("[UI Raycast] No hay EventSystem en la escena.");
            return;
        }

        var data = new PointerEventData(EventSystem.current) { position = screenPos };
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(data, results);

        if (results.Count == 0)
        {
            Debug.Log($"[UI Raycast] (nada) en {screenPos}");
            return;
        }

        // Lista completa de lo que está recibiendo el raycast (de arriba a abajo)
        string chain = string.Join(" > ", results.Select(r => r.gameObject.name));
        Debug.Log($"[UI Raycast] {screenPos} :: {chain}");
    }
}
