using UnityEngine;

public class UIAuthSwitcher : MonoBehaviour
{
    [Header("Panels")]
    public GameObject panelLogin;
    public GameObject panelRegistro;

    public void ShowRegister()
    {
        if (panelLogin) panelLogin.SetActive(false);
        if (panelRegistro) panelRegistro.SetActive(true);
    }

    public void ShowLogin()
    {
        if (panelRegistro) panelRegistro.SetActive(false);
        if (panelLogin) panelLogin.SetActive(true);
    }
}
