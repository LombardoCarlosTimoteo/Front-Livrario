using UnityEngine;

public class UIAuthSwitcher : MonoBehaviour
{
    [Header("Roots (objetos de primer nivel)")]
    public GameObject rootLogin;     // GameObject raíz del login
    public GameObject rootRegistro;  // GameObject raíz del registro
    public GameObject rootMainMenu;  // GameObject raíz del main menu

    public void ShowRegister()
    {
        Debug.Log("[UIAuthSwitcher] ShowRegister()");
        SetOnly(rootRegistro);
    }

    public void ShowLogin()
    {
        Debug.Log("[UIAuthSwitcher] ShowLogin()");
        SetOnly(rootLogin);
    }

    public void ShowMainMenu()
    {
        Debug.Log("[UIAuthSwitcher] ShowMainMenu()");
        SetOnly(rootMainMenu);
    }

    // Activa 'target' (y toda su jerarquía) y desactiva por completo los otros roots
    void SetOnly(GameObject target)
    {
        if (!rootLogin || !rootRegistro || !rootMainMenu)
        {
            Debug.LogError("[UIAuthSwitcher] Faltan referencias en el Inspector (rootLogin/rootRegistro/rootMainMenu).");
            return;
        }

        if (target == null)
        {
            Debug.LogError("[UIAuthSwitcher] Target nulo en SetOnly.");
            return;
        }

        // Apagar completamente los otros (incl. hijos)
        if (target != rootLogin) SetActiveRecursively(rootLogin, false);
        if (target != rootRegistro) SetActiveRecursively(rootRegistro, false);
        if (target != rootMainMenu) SetActiveRecursively(rootMainMenu, false);

        // Encender el objetivo y todos sus hijos
        int turnedOn = SetActiveRecursively(target, true);
        // Asegurar que todos los padres del target también estén activos
        EnsureParentsActive(target);

        Debug.Log($"[UIAuthSwitcher] States -> Login:{rootLogin.activeInHierarchy} Reg:{rootRegistro.activeInHierarchy} Main:{rootMainMenu.activeInHierarchy}. Activados en target: {turnedOn}");
    }

    // Activa/desactiva toda la jerarquía por debajo de 'go'
    int SetActiveRecursively(GameObject go, bool on)
    {
        if (go == null) return 0;
        int count = 0;

        // Encender/apagar este
        if (go.activeSelf != on)
        {
            go.SetActive(on);
            count++;
        }

        // Encender/apagar hijos
        for (int i = 0; i < go.transform.childCount; i++)
        {
            var child = go.transform.GetChild(i).gameObject;
            count += SetActiveRecursively(child, on);
        }
        return count;
    }

    // Sube por los padres activando cada uno (por si algún padre estaba desactivado)
    void EnsureParentsActive(GameObject go)
    {
        var t = go.transform.parent;
        while (t != null)
        {
            if (!t.gameObject.activeSelf)
                t.gameObject.SetActive(true);
            t = t.parent;
        }
    }
}
