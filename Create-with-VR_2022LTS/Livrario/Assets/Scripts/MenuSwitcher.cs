using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;

public class MenuSwitcher : MonoBehaviour
{
    [SerializeField] GameObject menuPrincipal;   // ← arrastrá aquí "GameObject (2)"
    [SerializeField] GameObject goCargarLibro;   // ← arrastrá aquí "GoCargarLibro"

    public void OnCargarLibro()
    {
        if (goCargarLibro) goCargarLibro.SetActive(true);

        // Limpia la selección actual del botón (opcional pero recomendado)
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);

        // Desactiva el menú en el PRÓXIMO frame (deja terminar el click actual)
        StartCoroutine(DisableNextFrame(menuPrincipal));
    }

    IEnumerator DisableNextFrame(GameObject go)
    {
        yield return null;           // espera 1 frame
        if (go) go.SetActive(false); // ahora sí, lo ocultamos
    }
}
