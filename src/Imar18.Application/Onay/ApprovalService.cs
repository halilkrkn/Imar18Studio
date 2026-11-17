using System.Globalization;
using Imar18.Application.Cikti;
using Imar18.Application.Proje;
using Imar18.Domain.Diagnostics;
using Imar18.Domain.Geometry;
using Imar18.Domain.Revizyon;

namespace Imar18.Application.Onay;

/// <summary>Onay ekranı içeriği (spec 11.3).</summary>
public sealed record OnayEkrani(
    string ProjeAdi,
    string RevizyonId,
    int ParselSayisi,
    decimal ToplamKadastroAlaniM2,
    decimal? Dopo,
    int TecvizUyariSayisi,
    int KritikHataSayisi,
    string MevzuatProfiliSurumu,
    string ParsidSemaDurumu,
    string FenKlasoruSablonSurumu,
    string OnayMetni,
    bool OnayVerilebilir,
    bool GecerliOnayVar,
    IReadOnlyList<string> Engeller)
{
    /// <summary>SAHA-004: topoloji bulgularının Türkçe parsel / alan ayrıntısı (engel, sınır toleransı ve imar yolu bilgisi).</summary>
    public IReadOnlyList<TopolojiNotu> TopolojiNotlari { get; init; } = [];
}

/// <summary>Geri alma sonucu (spec 11.5): bu revizyon için nihai çıktı üretilmişse "revizyon gerekli" uyarısı gösterilir.</summary>
public sealed record OnayGeriAlmaSonucu(RevisionId Revizyon, bool NihaiCiktiUretilmisti);

/// <summary>
/// ApprovalService (P5.2, FR-F5.10, spec 11): onay ve geri alma <see cref="ProjeKomutYolu"/> komutlarıdır; bu sayede onay,
/// reddedilen girişimler ve geri alma aynı atomik commit'le denetlenir (AC-ONAY-03/05). Kurallar:
/// <list type="number">
/// <item>Kritik (Critical) bulgu varsa onay engellenir — <c>ERR-CIKTI-004</c> (AC-PILOT-05). Bu kontrol eksik girdiden önce yapılır.</item>
/// <item>Hesap tamamlanmadıysa — <c>ERR-CIKTI-006</c>.</item>
/// <item>Kabul edilen metin <see cref="OnayMetni"/> ile birebir aynı olmalıdır — <c>ERR-CIKTI-005</c> (AC-ONAY-02).</item>
/// <item>Onay güncel <see cref="RevisionId"/>'ye bağlanır; veri değişince CommandBus onayı düşürür (AC-PILOT-06).</item>
/// <item>Geri alma onay zamanından itibaren en fazla 24 saat içinde mümkündür — aksi <c>ERR-CIKTI-008</c> (AC-ONAY-04).</item>
/// </list>
/// </summary>
public sealed class ApprovalService
{
    /// <summary>Spec 11.4 onay metni.</summary>
    public const string OnayMetni =
        "Bu çıktıyı nihai olarak onaylıyorum. Tecviz, DOP, tahsis ve geometri kontrollerini inceledim. Kritik hata bulunmadığını teyit ediyorum.";

    /// <summary>Pilot mevzuat profili (tek uzman onaylı profil — spec 18).</summary>
    public const string MevzuatProfiliSurumu = ProjeRevizyonu.MevzuatProfiliSurumu;

    /// <summary>Geri alma süresi (spec 11.5).</summary>
    public static readonly TimeSpan GeriAlmaSuresi = TimeSpan.FromHours(24);

    /// <summary>Başarılı nihai çıktı denetim kaydının işlem öneki (<see cref="CiktiServisi"/>).</summary>
    internal const string CiktiIslemOneki = "Cikti:";

    private readonly ProjeKomutYolu _yol;
    private readonly TimeProvider _zaman;
    private readonly IParselTopolojiDenetcisi _topoloji;

    public ApprovalService(ProjeKomutYolu yol, TimeProvider zaman, IParselTopolojiDenetcisi topoloji)
    {
        ArgumentNullException.ThrowIfNull(yol);
        ArgumentNullException.ThrowIfNull(zaman);
        ArgumentNullException.ThrowIfNull(topoloji);
        _yol = yol;
        _zaman = zaman;
        _topoloji = topoloji;
    }

    /// <summary>Güncel revizyon için geçerli onay var mı?</summary>
    public bool GecerliOnayVar
    {
        get
        {
            var (belge, rev) = _yol.AnlikGoruntu();
            return belge.GecerliOnayVar(rev);
        }
    }

    /// <summary>Güncel durum değerlendirmesi (salt okunur).</summary>
    public ProjeDegerlendirmesi Degerlendir() => ProjeDegerlendirici.Degerlendir(_yol.AnlikGoruntu().Belge, _topoloji);

    /// <summary>Onay ekranı özetini hazırlar (spec 11.3).</summary>
    public OnayEkrani OnayEkraniHazirla()
    {
        var (belge, rev) = _yol.AnlikGoruntu();
        var d = ProjeDegerlendirici.Degerlendir(belge, _topoloji);
        var engeller = new List<string>();
        if (d.KritikVar)
        {
            engeller.AddRange(d.Bulgular.Where(b => b.Severity == ErrorSeverity.Critical).Select(b => b.UserMessage).Distinct(StringComparer.Ordinal));

            // SAHA-004: topoloji engelinin hangi parsel çiftinde / parselde kaç m² olduğu ayrı satırda yazılır.
            engeller.AddRange(d.TopolojiNotlari.Where(n => n.Seviye == ErrorSeverity.Critical).Select(n => "↳ " + n.Metin));
        }

        if (!d.Tamamlandi)
        {
            engeller.Add("Eksik veya hesaplanamayan girdi: " + string.Join(", ", d.EksikGirdiler));
        }

        return new OnayEkrani(
            belge.ProjeAdi,
            rev.Deger,
            belge.Parseller.Count,
            d.Parseller.Where(p => p.Geometri.Rol == ParselRolu.Kadastro).Sum(p => p.AlanM2),
            d.Dop?.Dopo,
            d.Bulgular.Count(b => b.Category == ErrorCategory.Tecviz && b.Severity != ErrorSeverity.Critical),
            d.KritikSayisi,
            MevzuatProfiliSurumu,
            PilotDamgasi.ParsidSemaDurumu,
            PilotDamgasi.FenKlasoruSablonSurumu,
            OnayMetni,
            engeller.Count == 0,
            belge.GecerliOnayVar(rev),
            engeller)
        {
            TopolojiNotlari = d.TopolojiNotlari,
        };
    }

    /// <summary>Güncel revizyonu onaylar.</summary>
    public async Task<Result<MuhendisOnayi>> OnaylaAsync(string muhendisId, string kabulEdilenOnayMetni, CancellationToken cancellationToken = default)
    {
        var sonuc = await _yol.GonderAsync(new OnayVerKomutu(muhendisId, kabulEdilenOnayMetni, _zaman.GetUtcNow(), _topoloji), muhendisId, cancellationToken).ConfigureAwait(false);
        return sonuc.IsFailure ? Result<MuhendisOnayi>.Failure(sonuc.Error) : Result<MuhendisOnayi>.Success(sonuc.Value.Onay!);
    }

    /// <summary>Mevcut onayı 24 saat içinde geri alır; geri alma ayrı denetim kaydıdır ve eski çıktıları silmez.</summary>
    public async Task<Result<OnayGeriAlmaSonucu>> GeriAlAsync(string muhendisId, CancellationToken cancellationToken = default)
    {
        var komut = new OnayGeriAlKomutu(_zaman.GetUtcNow());
        var sonuc = await _yol.GonderAsync(komut, muhendisId, cancellationToken).ConfigureAwait(false);
        if (sonuc.IsFailure)
        {
            return Result<OnayGeriAlmaSonucu>.Failure(sonuc.Error);
        }

        // Geri alınan onay komut içinde, CommandBus kilidi altında yakalanır (P5 reviewer M3).
        var rev = komut.GeriAlinan!.RevizyonId;
        var uretildi = _yol.Denetim.Any(k => k.Sonuc == AuditSonucu.Basarili
            && k.Islem.StartsWith(CiktiIslemOneki, StringComparison.Ordinal)
            && k.SonrakiRevizyon == rev.Deger);
        return Result<OnayGeriAlmaSonucu>.Success(new OnayGeriAlmaSonucu(rev, uretildi));
    }

    private static AppError Hata(string kod, params (string Alan, string Deger)[] alanlar)
    {
        var e = ErrorCatalog.Get(kod);
        var m = e.TechnicalMessage;
        foreach (var (alan, deger) in alanlar)
        {
            m = m.Replace("{" + alan + "}", deger, StringComparison.Ordinal);
        }

        return e with { TechnicalMessage = m };
    }

    private sealed record OnayVerKomutu(string MuhendisId, string KabulEdilenMetin, DateTimeOffset Zaman, IParselTopolojiDenetcisi Topoloji) : IOnayKomutu
    {
        public string Ad => "OnayVer";

        public Result<Proje.ProjeBelgesi> Uygula(Proje.ProjeBelgesi mevcut)
        {
            var d = ProjeDegerlendirici.Degerlendir(mevcut, Topoloji);
            var rev = d.Revizyon.Deger;
            if (d.KritikVar)
            {
                return Result<Proje.ProjeBelgesi>.Failure(Hata(ErrorCodes.CiktiOnayKritikHataEngeli,
                    ("count", d.KritikSayisi.ToString(CultureInfo.InvariantCulture)), ("revisionId", rev)));
            }

            if (!d.Tamamlandi)
            {
                return Result<Proje.ProjeBelgesi>.Failure(Hata(ErrorCodes.CiktiOnayIcinHesapEksik,
                    ("revisionId", rev), ("missing", string.Join(", ", d.EksikGirdiler))));
            }

            if (!string.Equals(KabulEdilenMetin, OnayMetni, StringComparison.Ordinal))
            {
                return Result<Proje.ProjeBelgesi>.Failure(Hata(ErrorCodes.CiktiOnayMetniKabulEdilmedi, ("revisionId", rev)));
            }

            return Result<Proje.ProjeBelgesi>.Success(mevcut with { Onay = MuhendisOnayi.Olustur(d.Revizyon, MuhendisId, Zaman) });
        }
    }

    private sealed class OnayGeriAlKomutu(DateTimeOffset simdi) : IOnayKomutu
    {
        private DateTimeOffset Simdi { get; } = simdi;

        /// <summary>Başarılı uygulamada geri alınan onay.</summary>
        public MuhendisOnayi? GeriAlinan { get; private set; }

        public string Ad => "OnayGeriAl";

        public Result<Proje.ProjeBelgesi> Uygula(Proje.ProjeBelgesi mevcut)
        {
            if (mevcut.Onay is null)
            {
                return Result<Proje.ProjeBelgesi>.Failure(Hata(ErrorCodes.CiktiOnayGeriAlinamaz, ("reason", "no approval to revoke")));
            }

            if (Simdi - mevcut.Onay.Zaman > GeriAlmaSuresi || Simdi < mevcut.Onay.Zaman)
            {
                return Result<Proje.ProjeBelgesi>.Failure(Hata(ErrorCodes.CiktiOnayGeriAlinamaz, ("reason", "24 hour revocation window elapsed")));
            }

            GeriAlinan = mevcut.Onay;
            return Result<Proje.ProjeBelgesi>.Success(mevcut with { Onay = null });
        }
    }
}
