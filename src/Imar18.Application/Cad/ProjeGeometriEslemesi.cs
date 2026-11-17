using System.Globalization;
using Imar18.Domain.Geometry;

namespace Imar18.Application.Cad;

/// <summary>CAD belgesinden proje geometrisine dönüşüm sonucu.</summary>
public sealed record ProjeGeometriSonucu(IReadOnlyList<ParselGeometrisi> Parseller, int AtlananSayisi);

/// <summary>
/// İçe aktarılan CAD belgesini mühendis katman–rol eşlemesiyle (SORU-032 b) proje parsel geometrisine çevirir (P5 sihirbaz
/// bağlantısı). Rol eşlemesi: kadastro parseli → Kadastro, imar parseli → İmar, yola terk → YolTerk, park → KamuAlani; diğer
/// roller parsel değildir. Kapalı olmayan veya <see cref="PolygonRing.FromOpen"/> denetiminden geçmeyen parsel poligonları
/// atlanır ve sayılır (tek bozuk poligon içe aktarmayı düşürmez). Kimlik: etiket; etiket yoksa <c>KATMAN#sıra</c>; yinelenen
/// kimliğe <c>~2</c>, <c>~3</c>… eklenir. Sıra kaynak belge sırasıdır (deterministik).
/// </summary>
public static class ProjeGeometriEslemesi
{
    public static ProjeGeometriSonucu Olustur(CadLayerDocument belge, CadLayerRoleMap roller)
    {
        ArgumentNullException.ThrowIfNull(belge);
        ArgumentNullException.ThrowIfNull(roller);

        var parseller = new List<ParselGeometrisi>();
        var kullanilan = new Dictionary<string, int>(StringComparer.Ordinal);
        var katmanSayaci = new Dictionary<string, int>(StringComparer.Ordinal);
        var atlanan = 0;

        foreach (var c in belge.Polylines)
        {
            ParselRolu? rol = roller.Resolve(c.LayerName) switch
            {
                CadLayerRole.CadastralParcel => ParselRolu.Kadastro,
                CadLayerRole.ZoningParcel => ParselRolu.Imar,
                CadLayerRole.RoadDedication => ParselRolu.YolTerk,
                CadLayerRole.Park => ParselRolu.KamuAlani,
                _ => null,
            };
            if (rol is null)
            {
                continue;
            }

            var sira = katmanSayaci[c.LayerName] = katmanSayaci.GetValueOrDefault(c.LayerName) + 1;
            var koseler = c.Vertices.Count > 1 && c.Vertices[0] == c.Vertices[^1] ? c.Vertices.Take(c.Vertices.Count - 1).ToArray() : c.Vertices.ToArray();
            if (!c.IsClosed || PolygonRing.FromOpen(koseler).IsFailure)
            {
                atlanan++;
                continue;
            }

            var temel = string.IsNullOrWhiteSpace(c.Label) ? c.LayerName + "#" + sira.ToString(CultureInfo.InvariantCulture) : c.Label.Trim();
            var adet = kullanilan[temel] = kullanilan.GetValueOrDefault(temel) + 1;
            var id = adet == 1 ? temel : temel + "~" + adet.ToString(CultureInfo.InvariantCulture);
            parseller.Add(new ParselGeometrisi(id.Length > 64 ? id[..64] : id, rol.Value, koseler));
        }

        return new ProjeGeometriSonucu(parseller, atlanan);
    }
}
