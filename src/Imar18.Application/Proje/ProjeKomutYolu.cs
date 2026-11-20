using Imar18.Domain.Diagnostics;
using Imar18.Domain.Revizyon;

namespace Imar18.Application.Proje;

/// <summary>
/// CommandBus (P5.1, arch.md "Mutasyon, Onay ve UI Duyarlılığı"): tek yazıcı, sıralı komut işleme.
/// <list type="number">
/// <item>Komut doğrulanır; reddedilirse durum değişmez ve <see cref="AuditSonucu.Reddedildi"/> kaydı yazılır.</item>
/// <item>Başarılıysa yeni revizyon hesaplanır; onay yeni revizyona bağlı değilse otomatik düşer (<see cref="AuditSonucu.OnayDustu"/>,
/// AC-PILOT-06).</item>
/// <item>Yeni durum ve denetim kayıtları tek <see cref="IProjeDeposu.KaydetAsync"/> çağrısıyla (aynı transaction) yazılır;
/// yazma başarısızsa bellekteki durum değişmez (geri alma) ve <c>ERR-CIKTI-009</c> döner.</item>
/// </list>
/// </summary>
public sealed class ProjeKomutYolu : IDisposable
{
    private readonly IProjeDeposu _depo;
    private readonly TimeProvider _zaman;
    private readonly SemaphoreSlim _kilit = new(1, 1);
    private volatile Anlik _anlik;

    private ProjeKomutYolu(IProjeDeposu depo, TimeProvider zaman, Anlik anlik)
    {
        _depo = depo;
        _zaman = zaman;
        _anlik = anlik;
    }

    /// <summary>Başarılı her commit'ten sonra (UI yenilemesi için).</summary>
    public event EventHandler? Degisti;

    /// <summary>Güncel proje durumu.</summary>
    public ProjeBelgesi Belge => _anlik.Kayit.Belge;

    /// <summary>Güncel içerik revizyonu.</summary>
    public RevisionId Revizyon => _anlik.Revizyon;

    /// <summary>Kalıcı denetim kayıtları (sıra numarasıyla).</summary>
    public IReadOnlyList<AuditKaydi> Denetim => _anlik.Kayit.Denetim;

    /// <summary>Durum ve revizyonun tutarlı tek anlık görüntüsü.</summary>
    public (ProjeBelgesi Belge, RevisionId Revizyon) AnlikGoruntu()
    {
        var a = _anlik;
        return (a.Kayit.Belge, a.Revizyon);
    }

    /// <summary>
    /// Depoda kayıt varsa yükler, yoksa yeni proje oluşturup <c>ProjeOlustur</c> denetimiyle kaydeder. Yüklenen onay yeniden
    /// hesaplanan revizyonla eşleşmiyorsa düşürülür ve kaydedilir. Bozuk kayıtta depoya yazılmaz.
    /// </summary>
    public static async Task<Result<ProjeKomutYolu>> AcAsync(
        IProjeDeposu depo,
        string yeniProjeAdi,
        TimeProvider? zaman = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(depo);
        zaman ??= TimeProvider.System;

        try
        {
            if (await depo.VarMiAsync(cancellationToken).ConfigureAwait(false))
            {
                var yuklenen = await depo.YukleAsync(cancellationToken).ConfigureAwait(false);
                if (yuklenen.IsFailure)
                {
                    return Result<ProjeKomutYolu>.Failure(yuklenen.Error);
                }

                var kayit = yuklenen.Value;
                var rev = kayit.Belge.RevizyonKimligi();
                var yol = new ProjeKomutYolu(depo, zaman, new Anlik(kayit, rev));
                if (kayit.Belge.Onay is not null && !kayit.Belge.Onay.GecerliMi(rev))
                {
                    var dus = yol.Yeni("sistem", "ProjeAc", AuditSonucu.OnayDustu, null, kayit.Belge.Onay.RevizyonId.Deger, rev.Deger, "acilista revizyon uyusmadi", kayit.Denetim);
                    var commit = await yol.CommitAsync(new ProjeKaydi(kayit.Belge with { Onay = null }, [.. kayit.Denetim, dus]), rev, cancellationToken).ConfigureAwait(false);
                    if (commit.IsFailure)
                    {
                        return Result<ProjeKomutYolu>.Failure(commit.Error);
                    }
                }

                return Result<ProjeKomutYolu>.Success(yol);
            }

            var belge = ProjeBelgesi.Yeni(yeniProjeAdi);
            var ilkRev = belge.RevizyonKimligi();
            var yeni = new ProjeKomutYolu(depo, zaman, new Anlik(new ProjeKaydi(belge, []), ilkRev));
            var olustur = yeni.Yeni("sistem", "ProjeOlustur", AuditSonucu.Basarili, null, null, ilkRev.Deger, null, []);
            var ilk = await yeni.CommitAsync(new ProjeKaydi(belge, [olustur]), ilkRev, cancellationToken).ConfigureAwait(false);
            return ilk.IsFailure ? Result<ProjeKomutYolu>.Failure(ilk.Error) : Result<ProjeKomutYolu>.Success(yeni);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException)
        {
            // ArgumentException: yüklenen kayıt revizyon özeti için geçersiz (P5 reviewer M4) — açılış çökmez.
            return Result<ProjeKomutYolu>.Failure(KayitHatasi(ex.GetType().Name));
        }
    }

    /// <summary>Komutu uygular, denetler ve atomik kaydeder.</summary>
    /// <exception cref="ArgumentException">Kullanıcı kimliği boşsa.</exception>
    public async Task<Result<ProjeBelgesi>> GonderAsync(IProjeKomutu komut, string kullaniciId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(komut);
        KullaniciDogrula(kullaniciId);

        await _kilit.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var mevcut = _anlik;
            var sonuc = komut.Uygula(mevcut.Kayit.Belge);
            if (sonuc.IsSuccess && komut is not IOnayKomutu && !Equals(sonuc.Value.Onay, mevcut.Kayit.Belge.Onay))
            {
                sonuc = KomutHatasi.Gecersiz(komut.Ad, "approval may only be changed through ApprovalService");
            }

            // SAHA-014 (architect K2): Parseller = İleri(DepremOncesiParseller) değişmezi her komuttan sonra tek merkezde denetlenir.
            if (sonuc.IsSuccess && IleriDonusumDegismezi.Dogrula(sonuc.Value) is { IsFailure: true } bozuk)
            {
                sonuc = bozuk;
            }

            if (sonuc.IsFailure)
            {
                var red = Yeni(kullaniciId, komut.Ad, AuditSonucu.Reddedildi, sonuc.Error.Code, mevcut.Revizyon.Deger, mevcut.Revizyon.Deger, komut.Ayrinti, mevcut.Kayit.Denetim);
                var redCommit = await CommitAsync(mevcut.Kayit with { Denetim = [.. mevcut.Kayit.Denetim, red] }, mevcut.Revizyon, cancellationToken).ConfigureAwait(false);
                return redCommit.IsFailure ? Result<ProjeBelgesi>.Failure(redCommit.Error) : Result<ProjeBelgesi>.Failure(sonuc.Error);
            }

            var yeni = sonuc.Value with { Surum = mevcut.Kayit.Belge.Surum + 1 };
            var yeniRev = yeni.RevizyonKimligi();
            var kayitlar = new List<AuditKaydi>(mevcut.Kayit.Denetim)
            {
                Yeni(kullaniciId, komut.Ad, AuditSonucu.Basarili, null, mevcut.Revizyon.Deger, yeniRev.Deger, komut.Ayrinti, mevcut.Kayit.Denetim),
            };
            if (yeni.Onay is not null && !yeni.Onay.GecerliMi(yeniRev))
            {
                kayitlar.Add(Yeni(kullaniciId, komut.Ad, AuditSonucu.OnayDustu, null, yeni.Onay.RevizyonId.Deger, yeniRev.Deger, "veri degisti", kayitlar));
                yeni = yeni with { Onay = null };
            }

            var commit = await CommitAsync(new ProjeKaydi(yeni, kayitlar), yeniRev, cancellationToken).ConfigureAwait(false);
            return commit.IsFailure ? Result<ProjeBelgesi>.Failure(commit.Error) : Result<ProjeBelgesi>.Success(yeni);
        }
        finally
        {
            _kilit.Release();
        }
    }

    /// <summary>
    /// Durum değiştirmeyen denetim kaydı (çıktı girişimleri, onaysız denemeler) ekler ve atomik kaydeder (spec 14.2 madde 8).
    /// </summary>
    public async Task<Result<AuditKaydi>> DenetimKaydiEkleAsync(
        string kullaniciId,
        string islem,
        AuditSonucu sonuc,
        string? hataKodu,
        RevisionId? revizyon,
        string? ayrinti,
        CancellationToken cancellationToken = default)
    {
        KullaniciDogrula(kullaniciId);
        ArgumentException.ThrowIfNullOrWhiteSpace(islem);

        await _kilit.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var mevcut = _anlik;
            var kayit = Yeni(kullaniciId, islem, sonuc, hataKodu, revizyon?.Deger, revizyon?.Deger, ayrinti, mevcut.Kayit.Denetim);
            var commit = await CommitAsync(mevcut.Kayit with { Denetim = [.. mevcut.Kayit.Denetim, kayit] }, mevcut.Revizyon, cancellationToken).ConfigureAwait(false);
            return commit.IsFailure ? Result<AuditKaydi>.Failure(commit.Error) : Result<AuditKaydi>.Success(kayit);
        }
        finally
        {
            _kilit.Release();
        }
    }

    /// <summary>
    /// Başarılı çıktı denetim kaydı (P5 reviewer M2): kilit altında güncel revizyonun hâlâ <paramref name="beklenenRevizyon"/>
    /// olduğu ve geçerli onayın sürdüğü doğrulanır; aksi hâlde <see cref="AuditSonucu.OnaysizGirisim"/> yazılır ve
    /// <c>ERR-CIKTI-007</c> döner (üretim sırasında veri değişti veya onay geri alındı).
    /// </summary>
    public async Task<Result<AuditKaydi>> CiktiBasariKaydiEkleAsync(
        string kullaniciId,
        string islem,
        RevisionId beklenenRevizyon,
        string ayrinti,
        CancellationToken cancellationToken = default)
    {
        KullaniciDogrula(kullaniciId);
        ArgumentException.ThrowIfNullOrWhiteSpace(islem);
        ArgumentNullException.ThrowIfNull(beklenenRevizyon);

        await _kilit.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var mevcut = _anlik;
            var gecerli = mevcut.Revizyon == beklenenRevizyon && mevcut.Kayit.Belge.GecerliOnayVar(mevcut.Revizyon);
            var kayit = gecerli
                ? Yeni(kullaniciId, islem, AuditSonucu.Basarili, null, beklenenRevizyon.Deger, beklenenRevizyon.Deger, ayrinti, mevcut.Kayit.Denetim)
                : Yeni(kullaniciId, islem, AuditSonucu.OnaysizGirisim, ErrorCodes.CiktiGecerliOnayYok, beklenenRevizyon.Deger, mevcut.Revizyon.Deger, "uretim sirasinda onay/revizyon degisti", mevcut.Kayit.Denetim);
            var commit = await CommitAsync(mevcut.Kayit with { Denetim = [.. mevcut.Kayit.Denetim, kayit] }, mevcut.Revizyon, cancellationToken).ConfigureAwait(false);
            if (commit.IsFailure)
            {
                return Result<AuditKaydi>.Failure(commit.Error);
            }

            if (!gecerli)
            {
                var e = ErrorCatalog.Get(ErrorCodes.CiktiGecerliOnayYok);
                return Result<AuditKaydi>.Failure(e with
                {
                    TechnicalMessage = e.TechnicalMessage.Replace("{output}", islem, StringComparison.Ordinal).Replace("{revisionId}", beklenenRevizyon.Deger, StringComparison.Ordinal),
                });
            }

            return Result<AuditKaydi>.Success(kayit);
        }
        finally
        {
            _kilit.Release();
        }
    }

    public void Dispose() => _kilit.Dispose();

    private static void KullaniciDogrula(string kullaniciId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kullaniciId);
        if (kullaniciId.Length > 64)
        {
            throw new ArgumentException("Kullanıcı kimliği en fazla 64 karakter olabilir.", nameof(kullaniciId));
        }
    }

    private static AppError KayitHatasi(string neden)
    {
        var e = ErrorCatalog.Get(ErrorCodes.CiktiProjeKaydiHatasi);
        return e with { TechnicalMessage = e.TechnicalMessage.Replace("{reason}", neden, StringComparison.Ordinal) };
    }

    private AuditKaydi Yeni(
        string kullaniciId,
        string islem,
        AuditSonucu sonuc,
        string? kod,
        string? onceki,
        string? sonraki,
        string? ayrinti,
        IReadOnlyList<AuditKaydi> mevcutKayitlar) =>
        new((mevcutKayitlar.Count == 0 ? 0 : mevcutKayitlar[^1].Sira) + 1, _zaman.GetUtcNow(), kullaniciId, islem, sonuc, kod, onceki, sonraki, ayrinti);

    /// <summary>Atomik yazar; yalnızca başarılı yazımdan sonra bellekteki anlık görüntüyü değiştirir.</summary>
    private async Task<Result<ProjeKaydi>> CommitAsync(ProjeKaydi kayit, RevisionId revizyon, CancellationToken cancellationToken)
    {
        Result<ProjeKaydi> sonuc;
        try
        {
            sonuc = await _depo.KaydetAsync(kayit, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<ProjeKaydi>.Failure(KayitHatasi(ex.GetType().Name));
        }

        if (sonuc.IsSuccess)
        {
            _anlik = new Anlik(kayit, revizyon);
            Degisti?.Invoke(this, EventArgs.Empty);
        }

        return sonuc;
    }

    private sealed record Anlik(ProjeKaydi Kayit, RevisionId Revizyon);
}
