using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools; // para silenciar logs esporádicos del Editor
using Assert = NUnit.Framework.Assert;

public class WebPanelToggle_BasicTests
{
    [Test]
    public void OpenHide_ToggleActivo_yMantienePose()
    {
        // Silencia mensajes tipo "ShouldRunBehaviour()" que pueden aparecer en EditMode
        LogAssert.ignoreFailingMessages = true;
        try
        {
            var canvas = new GameObject("WebCanvas");
            canvas.transform.position = new Vector3(1, 2, 3);
            canvas.transform.rotation = Quaternion.Euler(10, 20, 30);
            canvas.transform.localScale = new Vector3(2, 2, 2);

            var toggleGO = new GameObject("Toggle");
            var tog = toggleGO.AddComponent<WebPanelToggle>();
            tog.webCanvas = canvas;

            // Simula Awake (el script guarda la pose inicial y oculta el canvas)
            tog.SendMessage("Awake");

            // Guardamos la pose que el componente dejó registrada en Awake
            var pos0 = canvas.transform.position;
            var rot0 = canvas.transform.rotation;
            var scl0 = canvas.transform.localScale;

            // Open: debe activarse sin mover la pose
            tog.Open();
            Assert.IsTrue(canvas.activeSelf);
            Assert.Less(Vector3.Distance(pos0, canvas.transform.position), 1e-6f);
            Assert.Less(Quaternion.Angle(rot0, canvas.transform.rotation), 1e-5f);
            Assert.Less(Vector3.Distance(scl0, canvas.transform.localScale), 1e-6f);

            // Hide: sólo desactiva
            tog.Hide();
            Assert.IsFalse(canvas.activeSelf);
        }
        finally
        {
            LogAssert.ignoreFailingMessages = false;
        }
    }
}
