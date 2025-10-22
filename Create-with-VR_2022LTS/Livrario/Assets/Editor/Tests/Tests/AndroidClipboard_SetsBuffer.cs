using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

public class AndroidClipboard_SetsBuffer
{
    [Test]
    public void SetText_EnEditor_UsaSystemCopyBuffer()
    {
        AndroidClipboard.SetText("hola mundo");
        Assert.AreEqual("hola mundo", GUIUtility.systemCopyBuffer);
    }
}
