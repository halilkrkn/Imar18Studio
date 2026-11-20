using System.Globalization;
using Imar18.Application.KoordinatDonusumu;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Infrastructure.Cad.Ncz;

namespace Imar18.Infrastructure.Cad.Donusum;

/// <summary>
/// SAHA-014: Netcad <c>.DNS</c> dönüşüm dosyası okuyucusu (Windows-1254 düz metin, <c>\r\n</c> veya <c>\n</c>).
/// <list type="bullet">
/// <item><c>$AMAT</c>: sonraki dolu satırda boşlukla ayrılmış tam 6 sayı (<see cref="CultureInfo.InvariantCulture"/>, bilimsel gösterim
/// <c>9.99996197049100E-0001</c> dahil): <c>A, B, CY, CX, C, D</c>. İleri formül <c>Y2 = A·Y1 + C·X1 + CY</c>,
/// <c>X2 = B·Y1 + D·X1 + CX</c> (<see cref="DonusumKatsayilari"/>).</item>
/// <item><c>$NOK</c>: <c>NoktaNo, Y1, X1, Y2, X2,</c> (sondaki virgül isteğe bağlı). Boş numaralı veya dört koordinatı da 0 olan
/// dolgu satırları atlanır; yalnız bir kısmı 0 olan satır, yinelenen / 32 karakteri aşan no ve virgüllü ondalık reddedilir.</item>
/// <item><c>$SON</c> zorunludur; sonrası yok sayılır. Bilinmeyen <c>$</c> bölümlerinin satırları atlanır.</item>
/// </list>
/// Katsayılar <see cref="DonusumKatsayilari.Dogrula"/>'dan geçmeli (det &gt; 0, |det − 1| ≤ 0.01; aksi <c>ERR-CRS-004</c>); ortak nokta
/// sayısı modelin en azı ile 500 arasında olmalıdır. Hata: <c>ERR-GIRD-011</c> (Türkçe mesaj, yalnız satır numarası — koordinat,
/// nokta adı veya dosya adı yazılmaz). İstisna atmaz (iptal hariç).
/// </summary>
public sealed class NetcadDnsOkuyucu : IDnsDosyaOkuyucu
{
    /// <summary>Kabul edilen en büyük dosya (gerçek DNS birkaç on KB'dir).</summary>
    public const int MaxBoyut = 1024 * 1024;

    /// <summary>Ortak nokta koordinatı mutlak sınırı (m; uygulama güvenlik sınırı — taşmasız artık hesabı).</summary>
    public const decimal MaxKoordinatM = 10_000_000m;

    private const string Komut = "NetcadDnsOkuyucu";

    private const NumberStyles SayiBicimi = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite
        | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    private enum Bolum
    {
        Yok,
        Amat,
        Nok,
        Bilinmeyen,
    }

    public Result<DnsDosyasi> Oku(Stream kaynak, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kaynak);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] bayt;
        try
        {
            using var ms = new MemoryStream();
            var tampon = new byte[81920];
            int n;
            while ((n = kaynak.Read(tampon, 0, tampon.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ms.Write(tampon, 0, n);
                if (ms.Length > MaxBoyut)
                {
                    return Hata("file exceeds 1 MiB", "dosya 1 MB sınırını aşıyor", null);
                }
            }

            bayt = ms.ToArray();
        }
        catch (IOException)
        {
            return Hata("stream could not be read", "dosya okunamadı", null);
        }

        var satirlar = NczParser.Turkish.GetString(bayt).Split('\n');
        DonusumKatsayilari? katsayilar = null;
        var nokVar = false;
        var sonVar = false;
        var bolum = Bolum.Yok;
        var noktalar = new List<DonusumOrtakNoktasi>();
        var nolar = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < satirlar.Length && !sonVar; i++)
        {
            if ((i & 0x3FF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var satirNo = i + 1;
            var s = satirlar[i].TrimEnd('\r').Trim();
            if (s.Length == 0)
            {
                continue;
            }

            if (s[0] == '$')
            {
                switch (s.ToUpperInvariant())
                {
                    case "$AMAT":
                        if (katsayilar is not null || bolum == Bolum.Amat)
                        {
                            return Hata("duplicate $AMAT section", "birden çok $AMAT bölümü var", satirNo);
                        }

                        bolum = Bolum.Amat;
                        break;
                    case "$NOK":
                        if (nokVar)
                        {
                            return Hata("duplicate $NOK section", "birden çok $NOK bölümü var", satirNo);
                        }

                        nokVar = true;
                        bolum = Bolum.Nok;
                        break;
                    case "$SON":
                        sonVar = true;
                        break;
                    default:
                        bolum = Bolum.Bilinmeyen;
                        break;
                }

                continue;
            }

            if (bolum == Bolum.Amat)
            {
                if (katsayilar is not null)
                {
                    return Hata("$AMAT must have a single line of 6 numbers", "$AMAT bölümü tek satırda 6 sayı olmalı", satirNo);
                }

                var p = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                var d = new decimal[6];
                if (p.Length != 6 || !p.Select((t, j) => decimal.TryParse(t, SayiBicimi, CultureInfo.InvariantCulture, out d[j])).All(x => x))
                {
                    return Hata("$AMAT must have exactly 6 numbers (A B CY CX C D)", "$AMAT satırında tam 6 sayı (A B CY CX C D) olmalı", satirNo);
                }

                katsayilar = new DonusumKatsayilari(A: d[0], B: d[1], C: d[4], D: d[5], Cy: d[2], Cx: d[3]);
            }
            else if (bolum == Bolum.Nok)
            {
                var alanlar = s.Split(',');
                if (alanlar.Length == 6 && alanlar[5].Trim().Length == 0)
                {
                    alanlar = alanlar[..5];
                }

                var k = new decimal[4];
                if (alanlar.Length != 5 || !Enumerable.Range(0, 4).All(j => decimal.TryParse(alanlar[j + 1], SayiBicimi, CultureInfo.InvariantCulture, out k[j])))
                {
                    return Hata("$NOK line must be 'NoktaNo, Y1, X1, Y2, X2,' with '.' decimals", "$NOK satırı 'NoktaNo, Y1, X1, Y2, X2' biçiminde ve ondalık ayırıcı '.' olmalı", satirNo);
                }

                var no = alanlar[0].Trim();
                var sifir = k.Count(v => v == 0m);
                if (no.Length == 0 || sifir == 4)
                {
                    continue; // Netcad dolgu satırı
                }

                if (sifir > 0)
                {
                    return Hata("$NOK line has partially zero coordinates", "$NOK satırında eksik (0) koordinat var", satirNo);
                }

                // Reviewer M2: aşırı büyük koordinat artık/istatistik hesabında taşma üretmesin (CAD kesinlik zarfıyla uyumlu sınır).
                if (k.Any(v => Math.Abs(v) > MaxKoordinatM || decimal.Round(v, PolygonRing.MaxScale) != v))
                {
                    return Hata("$NOK coordinate is out of range or has more than 6 decimals", "$NOK koordinatı desteklenen aralığın (±10 000 km) veya 6 ondalık hassasiyetin dışında", satirNo);
                }

                if (no.Length > 32 || !nolar.Add(no))
                {
                    return Hata("$NOK point no is too long or duplicate", "$NOK nokta numarası 32 karakteri aşıyor veya yineleniyor", satirNo);
                }

                noktalar.Add(new DonusumOrtakNoktasi(no, k[0], k[1], k[2], k[3]));
            }
        }

        if (katsayilar is null || !nokVar || !sonVar)
        {
            return Hata("$AMAT, $NOK and $SON sections are required", "$AMAT, $NOK ve $SON bölümleri bulunmalı", null);
        }

        var dogru = DonusumKatsayilari.Dogrula(katsayilar);
        if (dogru.IsFailure)
        {
            return Result<DnsDosyasi>.Failure(dogru.Error with
            {
                UserMessage = "Netcad dönüşüm dosyası (.DNS) katsayıları kullanılamaz: matris tekil/yansımalı ya da katsayı, öteleme veya ölçek (±%0,5) uygulama güvenlik sınırlarının dışında.",
            });
        }

        var enAz = DonusumGirdisi.EnAzNokta(katsayilar.HelmertMi ? DonusumModeli.Helmert2B : DonusumModeli.Afin2B);
        if (noktalar.Count < enAz || noktalar.Count > 500)
        {
            return Hata("$NOK must contain enough common points (Helmert 2, affine 3, at most 500)",
                $"$NOK bölümünde en az {enAz.ToString(CultureInfo.InvariantCulture)}, en çok 500 ortak nokta olmalı", null);
        }

        return Result<DnsDosyasi>.Success(new DnsDosyasi(katsayilar, noktalar.AsReadOnly(), DonusumParametreleri.Katsayilardan(katsayilar, noktalar)));
    }

    private static Result<DnsDosyasi> Hata(string neden, string kullaniciNedeni, int? satir)
    {
        var e = ErrorCatalog.Get(ErrorCodes.GirdProjeGirdisiGecersiz);
        var s = satir is { } n ? " (satır " + n.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty;
        var t = satir is { } m ? " (line " + m.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty;
        return Result<DnsDosyasi>.Failure(e with
        {
            TechnicalMessage = e.TechnicalMessage.Replace("{command}", Komut, StringComparison.Ordinal).Replace("{reason}", neden + t, StringComparison.Ordinal),
            UserMessage = "Netcad dönüşüm dosyası (.DNS) okunamadı: " + kullaniciNedeni + s + ".",
        });
    }
}
