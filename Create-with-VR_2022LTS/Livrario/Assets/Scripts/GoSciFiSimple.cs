using UnityEngine;
using UnityEngine.SceneManagement;

public class GoSciFiSimple : MonoBehaviour
{
    public void OnBtnCienciaFiccion()
    {
        SceneManager.LoadScene("Room_cienciaFiccion"); // reemplaza la escena actual
    }
}
