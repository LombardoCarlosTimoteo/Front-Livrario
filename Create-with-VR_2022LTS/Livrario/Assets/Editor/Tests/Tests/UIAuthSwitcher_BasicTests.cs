using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

public class UIAuthSwitcher_BasicTests
{
    [Test]
    public void ShowMainMenu_ActivaSoloMainYPadres()
    {
        // Árbol
        var parent = new GameObject("Parent");
        parent.SetActive(false);

        var login = new GameObject("Login"); login.transform.SetParent(parent.transform);
        var reg = new GameObject("Registro"); reg.transform.SetParent(parent.transform);
        var main = new GameObject("Main"); main.transform.SetParent(parent.transform);

        login.SetActive(true);
        reg.SetActive(true);
        main.SetActive(false);

        // SUT
        var go = new GameObject("Switcher");
        var sw = go.AddComponent<UIAuthSwitcher>();
        sw.rootLogin = login;
        sw.rootRegistro = reg;
        sw.rootMainMenu = main;

        // Act
        sw.ShowMainMenu();

        // Assert
        Assert.IsTrue(parent.activeSelf, "El padre debe quedar activo por EnsureParentsActive()");
        Assert.IsFalse(login.activeInHierarchy);
        Assert.IsFalse(reg.activeInHierarchy);
        Assert.IsTrue(main.activeInHierarchy);
    }
}
