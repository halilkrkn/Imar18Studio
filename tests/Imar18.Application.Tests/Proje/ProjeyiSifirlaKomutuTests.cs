using FluentAssertions;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Xunit;

namespace Imar18.Application.Tests.Proje;

/// <summary>
/// P7 "Projeyi Temizle / Sıfırla": tüm hesap girdileri CommandBus üzerinden boşaltılır, proje adı korunur, işlem denetime
/// yazılır ve mevcut mühendis onayı (içerik revizyonu değiştiği için) otomatik düşer (AC-PILOT-06).
/// </summary>
public sealed class ProjeyiSifirlaKomutuTests
{
    [Fact]
    public async Task ProjeyiSifirla_Tum_Girdileri_Bosaltmali_Proje_Adini_Korumali_Ve_Denetime_Yazmali()
    {
        var (yol, _, _) = await OrnekProje.TamProjeAsync();
        var ad = yol.Belge.ProjeAdi;

        var sonuc = await yol.GonderAsync(new ProjeyiSifirlaKomutu(), OrnekProje.Muhendis);

        sonuc.IsSuccess.Should().BeTrue();
        var b = yol.Belge;
        b.ProjeAdi.Should().Be(ad);
        b.Crs.Should().BeNull();
        b.Parseller.Should().BeEmpty();
        b.Dop.Should().BeNull();
        b.TecvizParselleri.Should().BeEmpty();
        b.DuzenlemeToplamTecvizSiniriM2.Should().BeNull();
        b.HakEslestirme.Should().BeNull();
        b.KimlikCiftleri.Should().BeEmpty();
        b.RevizyonKimligi().Should().Be(ProjeBelgesi.Yeni(ad).RevizyonKimligi());
        yol.Denetim[^1].Islem.Should().Be("ProjeyiSifirla");
        yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.Basarili);
    }

    [Fact]
    public async Task ProjeyiSifirla_Onayli_Projede_Onayi_Otomatik_Dusurmeli()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var onay = new ApprovalService(yol, zaman, OrnekProje.Topoloji);
        (await onay.OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();
        onay.GecerliOnayVar.Should().BeTrue();

        (await yol.GonderAsync(new ProjeyiSifirlaKomutu(), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        onay.GecerliOnayVar.Should().BeFalse();
        yol.Denetim.Should().Contain(k => k.Sonuc == AuditSonucu.OnayDustu);
    }

    [Fact]
    public async Task ProjeyiSifirla_Bos_Projede_De_Basarili_Olmali()
    {
        var zaman = new SabitZaman(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));
        var yol = (await ProjeKomutYolu.AcAsync(new BellekProjeDeposu(), "Boş", zaman)).Value;

        (await yol.GonderAsync(new ProjeyiSifirlaKomutu(), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        (await yol.GonderAsync(new ProjeyiSifirlaKomutu(), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        yol.Belge.Parseller.Should().BeEmpty();
    }
}
