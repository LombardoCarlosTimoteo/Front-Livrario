using UnityEngine;
using UnityEngine.SceneManagement;

public class ExitToDefaultSimple : MonoBehaviour
{
    [SerializeField] string defaultSceneName = "Prueba-1"; // cambialo si tu sala se llama distinto

    public void OnBtnSalir()
    {
        SceneManager.LoadScene(defaultSceneName);
    }
}
