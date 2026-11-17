using System.Globalization;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;

namespace Imar18.Application.Cad;

/// <summary>Etiket eslestirme sonucu (P4.2).</summary>
public sealed record CadLabelMatchResult(
    IReadOnlyList<CadPolyline> Polylines,
    int MatchedCount,
    int UnlabeledCount,
    int AmbiguousCount,
    IReadOnlyList<AppError> Warnings);

/// <summary>
/// DXF/DWG parsel etiketi (TEXT/MTEXT) – poligon eslestirmesi (P4.2, STATUS SORU-033 c). Hedef: rolu
/// <paramref name="polygonRoles"/> icinde olan, kapali ve etiketsiz poligonlar. Kurallar:
/// <list type="bullet">
/// <item>yazinin ekleme noktasi poligonun kesin icindeyse adaydir (sinir uzerindeki nokta sayilmaz),</item>
/// <item>tek farkli metin ve o yazi baska bir hedef poligonda degilse etiket atanir,</item>
/// <item>hic aday yoksa "etiketsiz", birden cok metin veya paylasilan yazi varsa "belirsiz" sayilir (etiket bos kalir),</item>
/// <item>etiketsiz/belirsiz varsa ERR-GIRD-010 (Warning + muhendis incelemesi) uretilir; koordinat yazilmaz.</item>
/// <item>ondalik ayiracli salt sayisal yazilar (alan, kot: "523.45") parsel kodu sayilmaz; <c>textLayers</c> verilirse
/// yalnizca o katmanlardaki yazilar kullanilir,</item>
/// <item>eslestirme rol bazlidir: kadastro ve imar katmanlari ust uste bindigi icin her rol grubu ayri cagrilmalidir.</item>
/// </list>
/// Hesap <c>decimal</c> ve ilk koseye ötelemeli yerel koordinatla yapilir.
/// </summary>
public static class CadLabelMatcher
{
    public static CadLabelMatchResult Match(
        CadLayerDocument document,
        CadLayerRoleMap roles,
        IReadOnlyCollection<CadLayerRole> polygonRoles,
        IReadOnlyCollection<string>? textLayers = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(polygonRoles);

        var layerFilter = textLayers is null ? null : new HashSet<string>(textLayers.Select(l => l.Trim()), StringComparer.Ordinal);
        var texts = document.Texts
            .Where(t => layerFilter is null || layerFilter.Contains(t.LayerName.Trim()))
            .Select(t => (Text: t.Text.Trim(), t.Position))
            .Where(t => t.Text.Length > 0 && !IsNumericValue(t.Text))
            .ToList();

        var targets = new List<int>();
        for (var i = 0; i < document.Polylines.Count; i++)
        {
            var p = document.Polylines[i];
            if (p.IsClosed && p.Label.Length == 0 && polygonRoles.Contains(roles.Resolve(p.LayerName)))
            {
                targets.Add(i);
            }
        }

        // Her hedef poligonun icindeki yazi indeksleri (once sinir kutusu on elemesi) ve her yazinin kac hedefte oldugu.
        var inside = targets.ToDictionary(i => i, i =>
        {
            var v = document.Polylines[i].Vertices;
            decimal minX = v.Min(p => p.X), maxX = v.Max(p => p.X), minY = v.Min(p => p.Y), maxY = v.Max(p => p.Y);
            return texts.Select((t, ti) => (t, ti))
                .Where(x => x.t.Position.X > minX && x.t.Position.X < maxX && x.t.Position.Y > minY && x.t.Position.Y < maxY)
                .Where(x => StrictlyInside(v, x.t.Position))
                .Select(x => x.ti)
                .ToList();
        });
        var usage = new int[texts.Count];
        foreach (var list in inside.Values)
        {
            foreach (var ti in list)
            {
                usage[ti]++;
            }
        }

        var result = document.Polylines.ToArray();
        int matched = 0, unlabeled = 0, ambiguous = 0;
        foreach (var i in targets)
        {
            var candidates = inside[i];
            var distinct = candidates.Select(ti => texts[ti].Text).Distinct(StringComparer.Ordinal).ToList();
            if (candidates.Count == 0)
            {
                unlabeled++;
            }
            else if (distinct.Count == 1 && candidates.All(ti => usage[ti] == 1))
            {
                result[i] = result[i] with { Label = distinct[0] };
                matched++;
            }
            else
            {
                ambiguous++;
            }
        }

        IReadOnlyList<AppError> warnings = [];
        if (unlabeled + ambiguous > 0)
        {
            var w = ErrorCatalog.Get(ErrorCodes.GirdEtiketPoligonEslesmedi);
            warnings = [w with { TechnicalMessage = w.TechnicalMessage
                .Replace("{unlabeled}", unlabeled.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{ambiguous}", ambiguous.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{path}", $"{document.Format.ToString().ToUpperInvariant()} input", StringComparison.Ordinal) }];
        }

        return new CadLabelMatchResult(Array.AsReadOnly(result), matched, unlabeled, ambiguous, warnings);
    }

    /// <summary>Ondalik ayiracli salt sayi (alan/kot yazisi) mi? "523.45", "523,45", "-1.5" → true; "14", "101/1" → false.</summary>
    private static bool IsNumericValue(string text)
    {
        var t = text.TrimStart('+', '-');
        var sep = t.IndexOfAny(['.', ',']);
        return sep > 0 && sep < t.Length - 1 && t.Remove(sep, 1).All(char.IsAsciiDigit) && t.Count(c => c is '.' or ',') == 1;
    }

    /// <summary>Isin atma (ray casting) ile kesin iclilik; sinir veya kose uzerindeki nokta disarida sayilir.</summary>
    public static bool StrictlyInside(IReadOnlyList<GeodeticPoint> ring, GeodeticPoint point)
    {
        if (ring.Count < 4)
        {
            return false;
        }

        var o = ring[0];
        var px = point.X - o.X;
        var py = point.Y - o.Y;
        var inside = false;
        for (var i = 0; i < ring.Count - 1; i++)
        {
            var ax = ring[i].X - o.X;
            var ay = ring[i].Y - o.Y;
            var bx = ring[i + 1].X - o.X;
            var by = ring[i + 1].Y - o.Y;

            // Sinir denetimi: nokta kenar dogrusu uzerinde ve kenar kutusunun icindeyse.
            var cross = ((bx - ax) * (py - ay)) - ((by - ay) * (px - ax));
            if (cross == 0m && px >= Math.Min(ax, bx) && px <= Math.Max(ax, bx) && py >= Math.Min(ay, by) && py <= Math.Max(ay, by))
            {
                return false;
            }

            if ((ax > px) != (bx > px))
            {
                var kesisimY = ay + ((px - ax) * (by - ay) / (bx - ax));
                if (py < kesisimY)
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }
}
