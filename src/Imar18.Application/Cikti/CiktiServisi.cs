using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Mevzuat.Tahsis;
using Imar18.Domain.Numerics;
using Imar18.Domain.Revizyon;

namespace Imar18.Application.Cikti;

/// <summary>
/// Başarılı çıktı: dosya, SHA-256 checksum ve bağlı revizyon. <see cref="ChecksumDosyasi"/> yalnız
/// <see cref="CiktiSecenekleri.Sha256YanDosyasiUret"/> açıkken yazılan yan dosyanın yoludur, aksi hâlde <see langword="null"/>;
/// <see cref="Sha256"/> her zaman doludur (SORU-053).
/// </summary>
public sealed record CiktiSonucu(CiktiTuru Tur, string DosyaYolu, string? ChecksumDosyasi, string Sha256, long Boyut, RevisionId Revizyon);

/// <summary>
/// Onaylı revizyonun anlık görüntüsünden <see cref="CiktiPaketi"/> üretir. Nokta numaraları deterministiktir: parseller rol
/// (Kadastro → İmar → Yol → Kamu) ve kimlik sırasıyla gezilir; aynı koordinatlı köşe tek numara alır.
/// </summary>
public static class CiktiPaketiOlusturucu
{
    public static Result<CiktiPaketi> Olustur(ProjeBelgesi belge, ProjeDegerlendirmesi degerlendirme, IDonusumHesaplayici? donusumHesaplayici = null)
    {
        ArgumentNullException.ThrowIfNull(belge);
        ArgumentNullException.ThrowIfNull(degerlendirme);
        if (belge.Onay is null || !belge.Onay.GecerliMi(degerlendirme.Revizyon))
        {
            return Hata(ErrorCodes.CiktiGecerliOnayYok, ("output", "package"), ("revisionId", degerlendirme.Revizyon.Deger));
        }

        if (!degerlendirme.Tamamlandi || degerlendirme.Dop is null || degerlendirme.Hak is null || belge.Crs is null)
        {
            return Hata(ErrorCodes.CiktiOnayIcinHesapEksik, ("revisionId", degerlendirme.Revizyon.Deger), ("missing", string.Join(", ", degerlendirme.EksikGirdiler)));
        }

        if (degerlendirme.KritikVar)
        {
            return Hata(ErrorCodes.CiktiOnayKritikHataEngeli,
                ("count", degerlendirme.KritikSayisi.ToString(CultureInfo.InvariantCulture)), ("revisionId", degerlendirme.Revizyon.Deger));
        }

        var uyarilar = new List<string>();
        var eslesme = ParselKimlikEslemesi.Olustur(belge.KimlikCiftleri);
        // SAHA-017: eşleme yoksa "ada/parsel" biçimli geçici kod (kroki parselasyonu: 1/1, 2/3) doğrudan tescil no'dur.
        // Reviewer O6: geçersiz (yinelenen) eşleme listesinde ve başka parsele atanmış tescil no ile çakışmada uygulanmaz.
        var geriDonusSerbest = eslesme.IsSuccess || belge.KimlikCiftleri.Count == 0;
        string? GeriDonus(string gecici) => geriDonusSerbest && AdaParselBicimiMi(gecici) ? string.Join('/', gecici.Split('/').Select(x => x.Trim())) : null;
        bool Cakisiyor(string kod, string gecici) => eslesme.IsSuccess && eslesme.Value.TryGeciciKod(kod, out var diger) && diger != gecici;
        string Tescil(string gecici) =>
            eslesme.IsSuccess && eslesme.Value.TryTescilNo(gecici, out var t) ? t
            : GeriDonus(gecici) is { } kod && !Cakisiyor(kod, gecici) ? kod
            : "-";

        var dengelenmis = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (degerlendirme.Dengeleme is { } dz)
        {
            foreach (var k in dz.ImarParselleri.Append(dz.YolTerk).Concat(dz.DigerKamuAlanlari))
            {
                dengelenmis[k.Id] = k.DengelenmisAlanM2;
            }
        }

        // ---- Koordine özet ve noktalı / cepheli alan ----
        var noktalar = new Dictionary<GeodeticPoint, int>();
        var koordinatlar = new List<KoordinatSatiri>();
        var parseller = new List<ParselCetvelSatiri>();
        var kadastroKoseleri = new List<(GeodeticPoint Kose, int No)>();
        var oturtulan = new List<string>();
        foreach (var p in degerlendirme.Parseller.OrderBy(p => p.Geometri.Rol).ThenBy(p => p.Geometri.ParselId, StringComparer.Ordinal))
        {
            var v = p.Halka.Vertices;
            var nolar = new int[v.Count];
            var kadastro = p.Geometri.Rol == ParselRolu.Kadastro;
            for (var i = 0; i < v.Count; i++)
            {
                // SAHA-021: kadastro (TK_PARSEL) köşesiyle ≤ 0.02 m çakışan TK_Oluşan köşesi ayrı nokta değildir — kadastro köşesinin
                // numarasını (arşiv no'sunu) taşır; KOORDİNE ÖZET / RÖLÖVE'de iki kez yazılmaz (SORU-075, SORU-078).
                if (!noktalar.TryGetValue(v[i], out var no))
                {
                    if (!kadastro && KadastroKosesineCakisir(kadastroKoseleri, v[i], out no))
                    {
                        noktalar.Add(v[i], no); // yalnız arama önbelleği; koordinat satırı kadastro köşesininkidir

                        // Reviewer Y1: birebir olmayan (mm kaymış) çakışmada alan ve cephe gerçek köşeyle, KOORDİNE ÖZET kadastro köşesiyle
                        // hesaplanır; fark ≤ 0.02 m'dir ama mühendis CAD'de köşeyi oturtsun diye uyarılır (sessiz geçilmez).
                        var k = koordinatlar[no - 1];
                        oturtulan.Add(string.Create(CultureInfo.InvariantCulture,
                            $"{p.Geometri.ParselId} ({ArsivNoktalari.Uzaklik(new GeodeticPoint(k.X, k.Y), v[i]):0.000} m)"));
                    }
                    else
                    {
                        no = koordinatlar.Count + 1;
                        noktalar.Add(v[i], no);
                        koordinatlar.Add(new KoordinatSatiri(no, v[i].Y, v[i].X));
                        if (kadastro)
                        {
                            kadastroKoseleri.Add((v[i], no));
                        }
                    }
                }

                nolar[i] = no;
            }

            var kenarlar = v.Select((k, i) => new CepheSatiri(nolar[i], k.Y, k.X,
                Math.Round(DecimalMath.KenarUzunlugu(k, v[(i + 1) % v.Count]), 2, MidpointRounding.AwayFromZero))).ToArray();
            var tescil = p.Geometri.Rol == ParselRolu.Kadastro ? p.Geometri.ParselId : Tescil(p.Geometri.ParselId);
            if (p.Geometri.Rol != ParselRolu.Kadastro && tescil == "-")
            {
                uyarilar.Add(GeriDonus(p.Geometri.ParselId) is { } kod && Cakisiyor(kod, p.Geometri.ParselId)
                    ? "Geçici kod başka parselin tescil no'suyla çakışıyor, tescil no atanmadı: " + p.Geometri.ParselId
                    : "Tescil no eşlemesi yok: " + p.Geometri.ParselId);
            }

            var alan = p.Geometri.Rol != ParselRolu.Kadastro && dengelenmis.TryGetValue(p.Geometri.ParselId, out var da) ? da : p.AlanM2;
            parseller.Add(new ParselCetvelSatiri(p.Geometri.ParselId, tescil, p.Geometri.Rol, nolar, kenarlar, p.AlanM2, alan, HeM2Dm2.Ayir(alan), v));
        }

        // ---- Dengeleme ----
        DengelemeCetveli? dengeleme = null;
        if (degerlendirme.Dengeleme is { } d)
        {
            var satirlar = d.ImarParselleri.Append(d.YolTerk).Concat(d.DigerKamuAlanlari)
                .Select(k => new DengelemeSatiri(k.Id, Tescil(k.Id), k.HesapAlaniM2, k.DuzeltmeM2, k.DengelenmisAlanM2))
                .ToArray();
            dengeleme = new DengelemeCetveli(satirlar, d.TapuToplamiM2, d.HesapToplamiM2, d.FarkM2, d.TecvizSiniriM2);
        }
        else
        {
            uyarilar.Add("Dengeleme cetveli üretilmedi (yol terk parseli yok).");
        }

        // ---- İki yönlü dağıtım ----
        var haklar = degerlendirme.Hak.MalikHaklari.ToDictionary(h => (h.KadastroParselId, h.MalikId));
        var malikSirali = belge.HakEslestirme!.Eslestirme
            .OrderBy(s => s.KadastroParselId, ParselNoKarsilastirici.Instance)
            .ThenBy(s => s.MalikId, StringComparer.Ordinal)
            .ThenBy(s => Tescil(s.ImarParselKodu), ParselNoKarsilastirici.Instance)
            .Select(s =>
            {
                var h = haklar[(s.KadastroParselId, s.MalikId)];
                return new MalikDagitimSatiri(s.KadastroParselId, s.MalikId, h.GirenAlanM2, h.DopKesintisiM2, h.NetHakM2, Tescil(s.ImarParselKodu), s.AlanM2);
            })
            .ToArray();

        var imarSirali = degerlendirme.Hak.ImarParselleri
            .OrderBy(p => Tescil(p.GeciciKod), ParselNoKarsilastirici.Instance)
            .ThenBy(p => p.GeciciKod, StringComparer.Ordinal)
            .SelectMany(p => p.Paydaslar.Select(s => new TescilDagitimSatiri(
                Tescil(p.GeciciKod), p.GeciciKod, p.AlanM2, HeM2Dm2.Ayir(p.AlanM2), s.MalikId, string.Join(", ", s.KadastroParselleri), s.AlanM2, s.Hisse.ToString())))
            .ToArray();

        // Reviewer M7: "ada/parsel" biçiminde olmayan imar parseli tescil sayfasına alınamaz — sessiz atlama yerine uyarı.
        foreach (var ip in parseller.Where(x => x.Rol == ParselRolu.Imar && x.TescilNo != "-" && !x.TescilNo.Contains('/', StringComparison.Ordinal)))
        {
            uyarilar.Add("Tescil no ada/parsel biçiminde değil, tescil sayfasına alınmadı: " + ip.TescilNo);
        }

        // ---- P7.3-C1: kadastro parselleri ve malik hisseleri ----
        var kadastroOzeti = belge.HakEslestirme.KadastroParselleri
            .OrderBy(k => k.ParselId, ParselNoKarsilastirici.Instance)
            .Select(k => new KadastroParselOzeti(k.ParselId, k.TapuAlaniM2, k.Malikler
                .OrderBy(m => m.MalikId, StringComparer.Ordinal)
                .Select(m =>
                {
                    var h = haklar.TryGetValue((k.ParselId, m.MalikId), out var hk) ? hk : null;
                    return new KadastroMalikOzeti(m.MalikId, HisseMetni(m.Hisse), h?.GirenAlanM2 ?? 0m, h?.DopKesintisiM2 ?? 0m, h?.NetHakM2 ?? 0m);
                })
                .ToArray()))
            .ToArray();

        // ---- SAHA-014: .DNS ileri dönüşüm (aktif = Deprem Sonrası; Deprem Öncesi = orijinal CAD köşeleri, ters hesap yok) ----
        // ---- P7.3-C2: ortak noktalı dönüşüm (proje noktaları deprem sonrası; deprem öncesi ters dönüşümle) ----
        KoordinatDonusumu.DonusumOzeti? donusum = null;
        AppError? donusumHatasi = null;
        var nczSablonu = belge.NczSablonu;
        DonusumKatsayilari? nczSablonIleri = null;
        if (belge.IleriDonusum is { } ik && belge.Donusum is { } idg)
        {
            var ileri = KoordinatDonusumu.DonusumOzeti.Ileri(idg, ik, belge.DepremOncesiParseller ?? [], koordinatlar);
            if (ileri.IsFailure)
            {
                donusumHatasi = ileri.Error;
                uyarilar.Add("Deprem öncesi koordinatlar eşleştirilemedi; Fen Klasörü .zip üretilemez.");
            }
            else
            {
                donusum = ileri.Value;
            }

            if (nczSablonu is not null)
            {
                // SAHA-017: şablon korunur; kayıtları yazıcıda aynı .DNS katsayılarıyla ileri dönüştürülür.
                nczSablonIleri = ik;
                uyarilar.Add("Kaynak NCZ Deprem Öncesi koordinatlarda; PARSELASYON HARİTASI.NCZ şablon korunarak, nokta/çizgi/yazı/poligon kayıtları .DNS katsayılarıyla Deprem Sonrası'na ileri dönüştürülerek üretildi; şablonda iç düzeni çözülmemiş kayıt (tip 6, 10 vb.) varsa iki koordinat sistemi karışmasın diye haritaya alınmaz.");
            }
        }
        else if (belge.Donusum is { } dg)
        {
            if (donusumHesaplayici is null)
            {
                uyarilar.Add("Dönüşüm hesaplayıcısı bağlı değil; deprem öncesi koordinatlar üretilmedi.");
            }
            else
            {
                var ds = new KoordinatDonusumu.DonusumServisi(donusumHesaplayici).Hesapla(dg, koordinatlar);
                if (ds.IsFailure)
                {
                    donusumHatasi = ds.Error;
                    uyarilar.Add("Dönüşüm hesaplanamadı (ortak noktalar çakışık/eşdoğrusal olabilir); Fen Klasörü .zip üretilemez.");
                }
                else
                {
                    donusum = ds.Value;
                }
            }
        }

        if (oturtulan.Count > 0)
        {
            uyarilar.Add("TK_Oluşan köşesi kadastro (TK_PARSEL) köşesine ≤ " + ArsivNoktalari.CakismaToleransiM.ToString("0.00", CultureInfo.InvariantCulture)
                + " m yakın, birebir değil; aynı nokta sayılıp kadastro nokta no'su ve KOORDİNE ÖZET'te kadastro koordinatı basıldı (alan / cephe CAD köşesiyle hesaplandı). CAD'de köşeyi kadastro köşesine oturtun: "
                + string.Join(", ", oturtulan.Distinct(StringComparer.Ordinal).Take(10))
                + (oturtulan.Distinct(StringComparer.Ordinal).Count() > 10
                    ? string.Create(CultureInfo.InvariantCulture, $" (+{oturtulan.Distinct(StringComparer.Ordinal).Count() - 10})")
                    : string.Empty));
        }

        var (noktaAdlari, arsivKoordinatlari) = ArsivNoktaAdlari(belge, parseller, koordinatlar, donusum, uyarilar);

        var crs = belge.Crs;
        var epok = crs.Epok is { } e ? " · epok " + e.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty;
        return Result<CiktiPaketi>.Success(new CiktiPaketi(
            belge.ProjeAdi,
            degerlendirme.Revizyon.Deger,
            belge.Onay.OnaylayanId,
            belge.Onay.Zaman,
            PilotDamgasi.Metin,
            PilotDamgasi.FenKlasoruSablonSurumu,
            $"{crs.Datum.ToString().ToUpperInvariant()} TM3 · orta meridyen {crs.OrtaMeridyenDerece.ToString(CultureInfo.InvariantCulture)}°{epok} · {crs.CrsId}",
            crs.OlcekPaydasi,
            koordinatlar,
            parseller,
            dengeleme,
            DopOzeti.From(degerlendirme.Dop),
            malikSirali,
            imarSirali,
            uyarilar.Concat(degerlendirme.Bulgular.Where(b => b.Severity == ErrorSeverity.Warning).Select(b => b.UserMessage)).Distinct(StringComparer.Ordinal).ToArray())
        {
            Kunye = belge.Kunye,
            Donusum = donusum,
            DonusumHatasi = donusumHatasi,
            Rehber = belge.MalikRehberi,
            KadastroParselleri = kadastroOzeti,
            NczSablonu = nczSablonu,
            NczSablonIleriDonusumu = nczSablonIleri,
            NoktaAdlari = noktaAdlari,
            ArsivKoordinatlari = arsivKoordinatlari,
            ArsivNoktalariVar = noktaAdlari.Count > 0, // reviewer D4: yalnız en az bir arşiv kaydı köşeye eşlendiyse
        });
    }

    /// <summary>
    /// SAHA-021: köşe bir kadastro köşesiyle <see cref="ArsivNoktalari.CakismaToleransiM"/> (0.02 m) içinde çakışıyor mu; çakışıyorsa
    /// en yakın (eşitte ilk kaydedilen) kadastro köşesinin nokta no'su döner.
    /// </summary>
    public static bool KadastroKosesineCakisir(IReadOnlyList<(GeodeticPoint Kose, int No)> kadastro, GeodeticPoint v, out int no)
    {
        ArgumentNullException.ThrowIfNull(kadastro);
        no = 0;
        var enYakin = decimal.MaxValue;
        foreach (var (k, n) in kadastro)
        {
            if (Math.Abs(k.X - v.X) > ArsivNoktalari.CakismaToleransiM || Math.Abs(k.Y - v.Y) > ArsivNoktalari.CakismaToleransiM)
            {
                continue;
            }

            var d = ArsivNoktalari.Uzaklik(k, v);
            if (d <= ArsivNoktalari.CakismaToleransiM && d < enYakin)
            {
                (enYakin, no) = (d, n);
            }
        }

        return no > 0;
    }

    /// <summary>SAHA-020 (reviewer Y4): arşiv koordinatı ile Deprem Öncesi köşe arasındaki uyarı eşiği (m).</summary>
    public const decimal ArsivSapmaUyarisiM = 0.50m;

    /// <summary>
    /// SAHA-020 / SAHA-021: arşiv onaylı kadastro noktalarını iç nokta numaralarına eşler. Her kadastro köşesinin kaynak koordinatı
    /// (<c>.DNS</c> ileri dönüşümünde Deprem Öncesi orijinal CAD köşesi — dönüşüm özetinin hesaplanıp hesaplanamamasından bağımsız olarak
    /// <see cref="ProjeBelgesi.DepremOncesiParseller"/>'in ileri dönüşümünden kurulan sözlükle; aksi halde aktif köşe) önce kaydın eşleme
    /// anahtarıyla (<see cref="ArsivNoktaKaydi.Kose"/>) birebir, eşleşmeyenler sonra <see cref="ArsivNoktalari.CakismaToleransiM"/> (0.02 m)
    /// içinde en yakın kullanılmamış kayıtla eşlenir. Eşlenen köşe mühendisin arşiv no'sunu ve arşiv koordinatını alır (elle ters kipte
    /// yalnız mühendis girdiyse) — hiçbir zaman otomatik numara / CAD koordinatıyla ezilmez. Kalan noktalar sıralı <c>1, 2, 3…</c>
    /// numaralanır: önce yeni (TK_Oluşan) köşeler, sonra arşiv kaydı olmayan kadastro köşeleri (arşiv no'suyla çakışan sıra atlanır).
    /// Eşlenmeyen kayıt (geometri değişmiş) ve Deprem Öncesi köşeden <see cref="ArsivSapmaUyarisiM"/>'den fazla sapan arşiv koordinatı
    /// uyarılır. Kayıt yoksa boş sözlükler.
    /// </summary>
    private static (IReadOnlyDictionary<int, string>, IReadOnlyDictionary<int, KoordinatSatiri>) ArsivNoktaAdlari(
        ProjeBelgesi belge, IReadOnlyList<ParselCetvelSatiri> parseller, IReadOnlyList<KoordinatSatiri> koordinatlar,
        KoordinatDonusumu.DonusumOzeti? donusum, List<string> uyarilar)
    {
        var adlar = new Dictionary<int, string>();
        var arsiv = new Dictionary<int, KoordinatSatiri>();
        if (belge.ArsivKadastroNoktalari.Count == 0)
        {
            return (adlar, arsiv);
        }

        var kaynak = KaynakKoseler(belge, koordinatlar);
        var kayitlar = new Dictionary<GeodeticPoint, ArsivNoktaKaydi>();
        foreach (var k in belge.ArsivKadastroNoktalari)
        {
            kayitlar.TryAdd(k.Kose, k);
        }

        // 1. geçiş: birebir anahtar; 2. geçiş: ≤ 0.02 m en yakın kullanılmamış kayıt (en yakın çiftler önce; deterministik).
        var kadastroNolari = parseller.Where(x => x.Rol == ParselRolu.Kadastro).SelectMany(x => x.NoktaNolari).Distinct().Order().ToList();
        var eslesme = new Dictionary<int, ArsivNoktaKaydi>();
        var eslenen = new HashSet<ArsivNoktaKaydi>();
        foreach (var no in kadastroNolari)
        {
            if (kaynak.TryGetValue(no, out var k) && kayitlar.TryGetValue(k, out var kayit) && eslenen.Add(kayit))
            {
                eslesme[no] = kayit;
            }
        }

        var adaylar = kadastroNolari.Where(no => !eslesme.ContainsKey(no) && kaynak.ContainsKey(no))
            .SelectMany(no => belge.ArsivKadastroNoktalari.Where(r => !eslenen.Contains(r))
                .Select(r => (No: no, Kayit: r, Uzaklik: ArsivNoktalari.Uzaklik(kaynak[no], r.Kose))))
            .Where(x => x.Uzaklik <= ArsivNoktalari.CakismaToleransiM)
            .OrderBy(x => x.Uzaklik).ThenBy(x => x.No).ThenBy(x => x.Kayit.Sira)
            .ToList();
        foreach (var (no, kayit, _) in adaylar)
        {
            if (!eslesme.ContainsKey(no) && eslenen.Add(kayit))
            {
                eslesme[no] = kayit;
            }
        }

        var sapmali = new List<string>();
        var elleTers = belge.IleriDonusum is null && belge.Donusum is not null;
        foreach (var (no, kayit) in eslesme.OrderBy(x => x.Key))
        {
            adlar[no] = kayit.NoktaNo;

            // Reviewer Y1: elle ortak noktalı (ters) kipte anahtar Deprem Sonrası köşedir; arşiv Y, X anahtarla aynıysa (mühendis
            // girmediyse; ≤ 1 mm gösterim yuvarlaması payıyla) Deprem Öncesi ters dönüşümle hesaplanan değer korunur — Deprem Sonrası
            // koordinat "Öncesi"ne basılmaz.
            if (elleTers && ArsivNoktalari.Uzaklik(new GeodeticPoint(kayit.ArsivX, kayit.ArsivY), kayit.Kose) <= ArsivNoktalari.AyniKoordinatToleransiM)
            {
                continue;
            }

            arsiv[no] = new KoordinatSatiri(no, kayit.ArsivY, kayit.ArsivX);
            var referans = donusum?.DepremOncesi.FirstOrDefault(d => d.NoktaNo == no) is { } r ? new GeodeticPoint(r.X, r.Y) : kaynak[no];
            var sapma = ArsivNoktalari.Uzaklik(new GeodeticPoint(kayit.ArsivX, kayit.ArsivY), referans);
            if (sapma > ArsivSapmaUyarisiM)
            {
                sapmali.Add(string.Create(CultureInfo.InvariantCulture, $"{kayit.NoktaNo} ({sapma:0.00} m)"));
            }
        }

        if (adlar.Count == 0)
        {
            uyarilar.Add("Arşiv onaylı kadastro noktaları projedeki kadastro köşeleriyle eşleşmedi (geometri değişmiş olabilir); otomatik nokta numaraları basıldı. 1. adımda arşiv noktalarını yeniden uygulayın.");
            return (adlar, arsiv);
        }

        var eksik = belge.ArsivKadastroNoktalari.Count - eslenen.Count;
        if (eksik > 0)
        {
            uyarilar.Add(string.Create(CultureInfo.InvariantCulture,
                $"{eksik} arşiv noktası projedeki kadastro köşeleriyle eşleşmedi (geometri değişmiş olabilir); 1. adımda arşiv noktalarını yeniden uygulayın."));
        }

        if (sapmali.Count > 0)
        {
            uyarilar.Add("Arşiv koordinatı Deprem Öncesi kadastro köşesinden " + ArsivSapmaUyarisiM.ToString("0.00", CultureInfo.InvariantCulture)
                + " m'den fazla farklı (rakam veya Y/X hatası olabilir; mühendis kontrol etmeli): " + string.Join(", ", sapmali));
        }

        // Reviewer Y2 / SAHA-021: önce yeni (TK_Oluşan) noktalar, sonra arşiv kaydı olmayan kadastro köşeleri sıralı 1, 2, 3…
        // numaralanır; bir arşiv no'suyla çakışan sıra atlanır (iki nokta aynı adı taşımaz).
        var arsivNolari = adlar.Values.ToHashSet(StringComparer.Ordinal);
        var kadastroKume = kadastroNolari.ToHashSet();
        var sira = 0;
        foreach (var k in koordinatlar.Where(k => !adlar.ContainsKey(k.NoktaNo)).OrderBy(k => kadastroKume.Contains(k.NoktaNo)).ThenBy(k => k.NoktaNo))
        {
            string ad;
            do
            {
                ad = (++sira).ToString(CultureInfo.InvariantCulture);
            }
            while (arsivNolari.Contains(ad));

            adlar[k.NoktaNo] = ad;
        }

        return (adlar, arsiv);
    }

    /// <summary>
    /// SAHA-021: nokta no → arşiv eşleme koordinatı. <c>.DNS</c> ileri dönüşümünde aktif (Deprem Sonrası) nokta, Deprem Öncesi
    /// parsellerin <see cref="DonusumKatsayilari.Uygula"/> ile ileri dönüşümünden kurulan sözlükle orijinal CAD köşesine geri bulunur
    /// (dönüşüm özeti hesaplanamasa da arşiv numaraları kaybolmaz); aksi halde aktif köşenin kendisidir.
    /// </summary>
    private static Dictionary<int, GeodeticPoint> KaynakKoseler(ProjeBelgesi belge, IReadOnlyList<KoordinatSatiri> koordinatlar)
    {
        var sonuc = new Dictionary<int, GeodeticPoint>();
        if (belge.IleriDonusum is { } ik && belge.DepremOncesiParseller is { } orj)
        {
            var eslem = new Dictionary<GeodeticPoint, GeodeticPoint>();
            foreach (var o in orj.SelectMany(p => p.Koseler))
            {
                eslem.TryAdd(ik.Uygula(o), o);
            }

            foreach (var k in koordinatlar)
            {
                if (eslem.TryGetValue(new GeodeticPoint(k.X, k.Y), out var o))
                {
                    sonuc[k.NoktaNo] = o;
                }
            }

            return sonuc;
        }

        foreach (var k in koordinatlar)
        {
            sonuc[k.NoktaNo] = new GeodeticPoint(k.X, k.Y);
        }

        return sonuc;
    }

    /// <summary>SAHA-017: <c>"1/1"</c>, <c>" 2 / 3 "</c> gibi rakamlardan oluşan ada/parsel kodu mu?</summary>
    public static bool AdaParselBicimiMi(string kod)
    {
        var parca = kod.Split('/');
        return parca.Length == 2 && parca.All(x => x.Trim() is { Length: > 0 } t && t.All(char.IsAsciiDigit));
    }

    /// <summary>Kadastro hissesi: 1 ise "TAM", değilse sadeleştirilmiş "pay/payda".</summary>
    public static string HisseMetni(Fraction hisse) =>
        hisse.Numerator == hisse.Denominator
            ? "TAM"
            : hisse.Numerator.ToString(CultureInfo.InvariantCulture) + "/" + hisse.Denominator.ToString(CultureInfo.InvariantCulture);

    internal static Result<CiktiPaketi> Hata(string kod, params (string Alan, string Deger)[] alanlar)
    {
        var e = ErrorCatalog.Get(kod);
        var m = e.TechnicalMessage;
        foreach (var (alan, deger) in alanlar)
        {
            m = m.Replace("{" + alan + "}", deger, StringComparison.Ordinal);
        }

        return Result<CiktiPaketi>.Failure(e with { TechnicalMessage = m });
    }
}

/// <summary>"101/2" &lt; "101/10" &lt; "102/1": '/' ile ayrılmış parçalar sayıysa sayısal, değilse ordinal karşılaştırılır.</summary>
public sealed class ParselNoKarsilastirici : IComparer<string>
{
    public static ParselNoKarsilastirici Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null || y is null)
        {
            return x is null ? -1 : 1;
        }

        var a = x.Split('/');
        var b = y.Split('/');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var c = long.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out var na)
                && long.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out var nb)
                ? na.CompareTo(nb)
                : string.CompareOrdinal(a[i], b[i]);
            if (c != 0)
            {
                return c;
            }
        }

        return a.Length.CompareTo(b.Length) is var u && u != 0 ? u : string.CompareOrdinal(x, y);
    }
}

/// <summary>
/// Çıktı servisi (P5.3, spec 14.2): (1) güncel revizyon için geçerli onay yoksa <see cref="AuditSonucu.OnaysizGirisim"/> kaydı
/// ve <c>ERR-CIKTI-007</c>; (2) paket ve dosya arka planda üretilir (50 ms kuralı); (3) SHA-256 hesaplanır, çıktı (ve yalnız
/// <see cref="CiktiSecenekleri.Sha256YanDosyasiUret"/> açıkken <c>.sha256</c> yan dosyası, SORU-053) atomik yazılır; (4) yalnızca
/// bundan sonra özeti (<c>sha256=…</c>) taşıyan başarılı denetim kaydı eklenir (AC-FEN-04). Herhangi bir
/// adım başarısızsa reddedilen girişim denetime yazılır ve başarılı kayıt oluşmaz (AC-FEN-05). Denetim kaydı yazılamazsa
/// üretilen dosyalar silinir (her çıktının denetim izi olması kuralı). Pilot checksum'u imza değildir (Ed25519 Faz 12).
/// </summary>
public sealed class CiktiServisi
{
    private readonly ProjeKomutYolu _yol;
    private readonly Dictionary<CiktiTuru, ICiktiUretici> _ureticiler;
    private readonly ICiktiYazici _yazici;
    private readonly IParselTopolojiDenetcisi _topoloji;

    private readonly IDonusumHesaplayici? _donusum;

    public CiktiServisi(
        ProjeKomutYolu yol,
        IEnumerable<ICiktiUretici> ureticiler,
        ICiktiYazici yazici,
        IParselTopolojiDenetcisi topoloji,
        IDonusumHesaplayici? donusum = null)
    {
        _donusum = donusum;
        ArgumentNullException.ThrowIfNull(yol);
        ArgumentNullException.ThrowIfNull(ureticiler);
        ArgumentNullException.ThrowIfNull(yazici);
        ArgumentNullException.ThrowIfNull(topoloji);
        _yol = yol;
        _ureticiler = ureticiler.ToDictionary(u => u.Tur);
        _yazici = yazici;
        _topoloji = topoloji;
    }

    /// <summary>Kayıtlı üretici varsa noktalı dosya uzantısı.</summary>
    public string? Uzanti(CiktiTuru tur) => _ureticiler.TryGetValue(tur, out var u) ? u.Uzanti : null;

    public async Task<Result<CiktiSonucu>> UretAsync(
        CiktiTuru tur,
        string kullaniciId,
        string hedefYol,
        CiktiSecenekleri? secenekler = null,
        IProgress<int>? ilerleme = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hedefYol);
        secenekler ??= CiktiSecenekleri.Varsayilan;
        var islem = ApprovalService.CiktiIslemOneki + tur;
        var (belge, rev) = _yol.AnlikGoruntu();
        ilerleme?.Report(0);

        if (!belge.GecerliOnayVar(rev))
        {
            var red = CiktiPaketiOlusturucu.Hata(ErrorCodes.CiktiGecerliOnayYok, ("output", tur.ToString()), ("revisionId", rev.Deger)).Error;
            return await ReddetAsync(kullaniciId, islem, AuditSonucu.OnaysizGirisim, red, rev, null).ConfigureAwait(false);
        }

        if (!_ureticiler.TryGetValue(tur, out var uretici))
        {
            var e = ErrorCatalog.Get(ErrorCodes.CiktiFenKlasoruSablon);
            return await ReddetAsync(kullaniciId, islem, AuditSonucu.Reddedildi,
                e with { TechnicalMessage = e.TechnicalMessage.Replace("{templateId}", tur + " (no generator registered)", StringComparison.Ordinal) },
                rev, null).ConfigureAwait(false);
        }

        var checksumYolu = secenekler.Sha256YanDosyasiUret ? hedefYol + ".sha256" : null;
        var yazildi = false;
        var basarili = false;
        try
        {
            var uretim = await Task.Run(
                () =>
                {
                    var paket = CiktiPaketiOlusturucu.Olustur(belge, ProjeDegerlendirici.Degerlendir(belge, _topoloji), _donusum);
                    ilerleme?.Report(30);
                    return paket.IsFailure ? Result<byte[]>.Failure(paket.Error) : uretici.Uret(paket.Value, secenekler, cancellationToken);
                },
                cancellationToken).ConfigureAwait(false);
            if (uretim.IsFailure)
            {
                return await ReddetAsync(kullaniciId, islem, AuditSonucu.Reddedildi, uretim.Error, rev, null).ConfigureAwait(false);
            }

            ilerleme?.Report(80);
            var bytes = uretim.Value;
            var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
            cancellationToken.ThrowIfCancellationRequested();
            yazildi = true;
            await _yazici.YazAsync(hedefYol, bytes, cancellationToken).ConfigureAwait(false);
            if (checksumYolu is not null)
            {
                await _yazici.YazAsync(checksumYolu, Encoding.UTF8.GetBytes(sha + "  " + Path.GetFileName(hedefYol) + "\n"), cancellationToken).ConfigureAwait(false);
            }

            // Başarılı kayıt iptal edilmez; kilit altında revizyon ve onay yeniden doğrulanır (AC-FEN-04, reviewer M2).
            // SORU-053: yan dosya isteğe bağlıdır; bütünlük özeti her iki modda da bu kayıttadır.
            var ayrinti = string.Create(CultureInfo.InvariantCulture,
                $"sha256={sha};boyut={bytes.Length};uzanti={Path.GetExtension(hedefYol)};yanDosya={(checksumYolu is null ? "hayir" : "evet")}");
            var kayit = await _yol.CiktiBasariKaydiEkleAsync(kullaniciId, islem, rev, ayrinti, CancellationToken.None).ConfigureAwait(false);
            if (kayit.IsFailure)
            {
                return Result<CiktiSonucu>.Failure(kayit.Error);
            }

            basarili = true;
            if (checksumYolu is null)
            {
                // Reviewer M1 (SORU-053): aynı hedefe önceki yan dosyalı üretimden kalan .sha256 yeni dosyayla uyuşmaz; başarılı
                // kayıttan sonra kaldırılır (başarısız üretim önceki uyumlu çifte dokunmaz). Özet audit kaydındadır.
                await SessizSilAsync(hedefYol + ".sha256").ConfigureAwait(false);
            }

            ilerleme?.Report(100);
            return Result<CiktiSonucu>.Success(new CiktiSonucu(tur, hedefYol, checksumYolu, sha, bytes.Length, rev));
        }
        catch (OperationCanceledException)
        {
            await ReddetAsync(kullaniciId, islem, AuditSonucu.Reddedildi, null, rev, "iptal edildi").ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var e = ErrorCatalog.Get(ErrorCodes.CiktiProjeKaydiHatasi);
            return await ReddetAsync(kullaniciId, islem, AuditSonucu.Reddedildi,
                e with { TechnicalMessage = e.TechnicalMessage.Replace("{reason}", "output write failed: " + ex.GetType().Name, StringComparison.Ordinal) },
                rev, null).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException or FormatException or ArithmeticException)
        {
            var e = ErrorCatalog.Get(ErrorCodes.CiktiFenKlasoruSablon);
            return await ReddetAsync(kullaniciId, islem, AuditSonucu.Reddedildi,
                e with { TechnicalMessage = e.TechnicalMessage.Replace("{templateId}", tur + " (" + ex.GetType().Name + ")", StringComparison.Ordinal) },
                rev, null).ConfigureAwait(false);
        }
        finally
        {
            // Başarılı denetim kaydı yoksa diskte iz bırakılmaz (reviewer M1).
            if (yazildi && !basarili)
            {
                await SessizSilAsync(hedefYol).ConfigureAwait(false);
                if (checksumYolu is not null)
                {
                    await SessizSilAsync(checksumYolu).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>Reddedilen girişimi iptalden bağımsız (<see cref="CancellationToken.None"/>) denetime yazar.</summary>
    private async Task<Result<CiktiSonucu>> ReddetAsync(
        string kullaniciId,
        string islem,
        AuditSonucu sonuc,
        AppError? hata,
        RevisionId rev,
        string? ayrinti)
    {
        var kayit = await _yol.DenetimKaydiEkleAsync(kullaniciId, islem, sonuc, hata?.Code, rev, ayrinti, CancellationToken.None).ConfigureAwait(false);
        return kayit.IsFailure ? Result<CiktiSonucu>.Failure(kayit.Error) : Result<CiktiSonucu>.Failure(hata ?? ErrorCatalog.Get(null));
    }

    private async Task SessizSilAsync(string yol)
    {
        try
        {
            await _yazici.SilAsync(yol, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Silinemeyen dosya: denetim kaydı yoktur; kullanıcıya hata zaten döner.
        }
    }
}
