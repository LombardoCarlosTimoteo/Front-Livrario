using NUnit.Framework;
using System.Reflection;
using Assert = NUnit.Framework.Assert;

public class LibraryMenuController_MapGenresToRoom_Basics
{
    [Test]
    public void MapGenresToRoom_DevuelveSalaEsperada()
    {
        var t = typeof(LibraryMenuController);
        var m = t.GetMethod("MapGenresToRoom", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(m, "No se encontró el método privado MapGenresToRoom");

        string Room(params string[] genres) => (string)m.Invoke(null, new object[] { genres });

        Assert.AreEqual("Room_cienciaFiccion", Room("Science Fiction"));
        Assert.AreEqual("Room_Policial", Room("Thriller policial"));
        Assert.AreEqual("Room_Fantasia2", Room("Fantasía Épica"));
    }
}
