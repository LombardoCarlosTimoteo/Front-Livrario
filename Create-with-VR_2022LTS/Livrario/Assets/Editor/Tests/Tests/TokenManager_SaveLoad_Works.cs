using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert; // evita conflicto con UnityEngine.Assertions

public class TokenManager_SaveLoad_Works
{
    [SetUp] public void Setup() => PlayerPrefs.DeleteAll();

    [Test]
    public void GuardaYLeeTokens_Roundtrip()
    {
        // ACT
        TokenManager.SaveTokens("access-123", "refresh-456");

        // ASSERT
        Assert.IsTrue(TokenManager.TryLoadTokens(out var acc, out var rfr));
        Assert.AreEqual("access-123", acc);
        Assert.AreEqual("refresh-456", rfr);

        // Verificación básica: lo guardado no es texto plano
        var enc = PlayerPrefs.GetString("enc_access", "");
        Assert.IsNotEmpty(enc);
        Assert.AreNotEqual("access-123", enc);
    }
}
