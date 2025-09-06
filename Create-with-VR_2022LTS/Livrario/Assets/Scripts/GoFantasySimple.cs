using UnityEngine;
using UnityEngine.SceneManagement;

public class GoFantasySimple : MonoBehaviour
{
    public void OnBtnFantasia()
    {
        SceneManager.LoadScene("Room_Fantasia2"); // reemplaza la escena actual
    }
}
