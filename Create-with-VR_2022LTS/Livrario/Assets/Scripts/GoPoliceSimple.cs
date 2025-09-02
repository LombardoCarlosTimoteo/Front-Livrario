using UnityEngine;
using UnityEngine.SceneManagement;

public class GoPoliceSimple : MonoBehaviour
{
    public void OnBtnPolicial()
    {
        SceneManager.LoadScene("Room_Policial"); // reemplaza la escena actual
    }
}
