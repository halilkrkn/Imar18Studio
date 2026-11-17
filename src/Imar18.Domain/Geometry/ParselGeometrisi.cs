namespace Imar18.Domain.Geometry;

/// <summary>Proje parselinin düzenlemedeki rolü (CAD katman rolünden mühendis eşlemesiyle gelir — SORU-032 b).</summary>
public enum ParselRolu
{
    /// <summary>Düzenlemeye giren kadastro parseli.</summary>
    Kadastro,

    /// <summary>Oluşan imar parseli (R2'ye girer).</summary>
    Imar,

    /// <summary>Kamuya terk edilen yol (Y1; tecviz içi fark burada dengelenir — spec 25.4).</summary>
    YolTerk,

    /// <summary>Diğer kamu alanı (PARK, dini tesis vb.; R2'ye girmez — spec 25.1).</summary>
    KamuAlani,
}

/// <summary>
/// Proje parsel geometrisi: kimlik (geçici kod veya kadastro ada/parsel), rol ve jeodezik (X = Kuzey, Y = Doğu) köşe dizisi
/// (kapanış tekrarı yok). Geçerlilik <see cref="PolygonRing.FromOpen"/> ile denetlenir; revizyon kimliğine dahildir (AC-PILOT-06).
/// </summary>
public sealed record ParselGeometrisi(string ParselId, ParselRolu Rol, IReadOnlyList<GeodeticPoint> Koseler);
