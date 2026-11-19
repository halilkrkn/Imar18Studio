using System.Buffers.Binary;
using System.Security.Cryptography;
using FluentAssertions;
using Imar18.Application.Cad;
using Imar18.Application.Onay;
using Imar18.Application.Proje;
using Imar18.Domain.Diagnostics;
using Xunit;

namespace Imar18.Application.Tests.Proje;

/// <summary>
/// P7.3-C3 (SAHA-007): 1. adımda yüklenen kaynak <c>.NCZ</c> şablonu proje çalışma alanında SHA-256 ile doğrulanarak saklanır
/// (<see cref="NczSablonuAyarlaKomutu"/>, CommandBus + denetim). Şablon <c>PARSELASYON HARİTASI.NCZ</c> içeriğini belirlediği
/// için yalnız varsa RevisionId'ye girer (SORU-046 emsali; şablonsuz projelerin kimliği değişmez). Baytlar sentetiktir.
/// </summary>
public sealed class NczSablonuTests
{
    /// <summary>Netcad 8.5 başlık imzalı en küçük sentetik dosya (1 + 12 × int32; h[0]=120, h[1]=uzunluk, h[2]=125).</summary>
    internal static byte[] SentetikNcz(int dolgu = 16, byte isaret = 0)
    {
        var b = new byte[49 + dolgu];
        b[0] = 0x01;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(1), 120);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(5), b.Length);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(9), 125);
        b[^1] = isaret;
        return b;
    }

    [Fact]
    public void NczSablonu_Gecerli_Baytlardan_SHA256_Ozetiyle_Olusmali_Ve_Kopya_Tutmali()
    {
        var bayt = SentetikNcz();

        var s = NczSablonu.Olustur(bayt);

        s.IsSuccess.Should().BeTrue();
        s.Value.Sha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(bayt)));
        s.Value.Uzunluk.Should().Be(bayt.Length);
        bayt[^1] = 0x7F; // çağıranın dizisi değişse de şablon değişmemeli
        s.Value.Icerik.ToArray().Should().NotEqual(bayt);
        SHA256.HashData(s.Value.Icerik.Span).Should().Equal(Convert.FromHexString(s.Value.Sha256));
    }

    [Theory]
    [InlineData("bos")]
    [InlineData("imza")]
    [InlineData("surum")]
    [InlineData("uzunluk")]
    public void NczSablonu_Netcad_Basligi_Olmayan_Baytlari_Reddetmeli(string bozulma)
    {
        var b = bozulma == "bos" ? new byte[10] : SentetikNcz();
        switch (bozulma)
        {
            case "imza": b[0] = 0x02; break;
            case "surum": BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(1), 121); break;
            case "uzunluk": BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(5), b.Length + 1); break;
        }

        var s = NczSablonu.Olustur(b);

        s.IsFailure.Should().BeTrue();
        s.Error.Code.Should().Be(ErrorCodes.GirdNczOkunamadi);
    }

    [Fact]
    public void NczSablonu_Yukle_Ozet_Uyusmazsa_Reddetmeli()
    {
        var b = SentetikNcz();
        var dogru = Convert.ToHexStringLower(SHA256.HashData(b));

        NczSablonu.Yukle(b, dogru).IsSuccess.Should().BeTrue();
        NczSablonu.Yukle(b, new string('0', 64)).IsFailure.Should().BeTrue("hash doğrulaması başarısız şablon kullanılamaz");
        NczSablonu.Yukle(b, "kisa").IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task NczSablonuAyarla_Sablonu_Saklamali_Denetime_Yalniz_Boyut_Ve_Ozet_Yazmali()
    {
        var (yol, _, _) = await OrnekProje.TamProjeAsync();
        var bayt = SentetikNcz();

        var r = await yol.GonderAsync(new NczSablonuAyarlaKomutu(bayt), OrnekProje.Muhendis);

        r.IsSuccess.Should().BeTrue();
        yol.Belge.NczSablonu!.Sha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(bayt)));
        var d = yol.Denetim[^1];
        d.Islem.Should().Be("NczSablonuAyarla");
        d.Ayrinti.Should().Be("bayt=" + bayt.Length + " sha256=" + yol.Belge.NczSablonu.Sha256[..12]);
    }

    [Fact]
    public async Task NczSablonuAyarla_Gecersiz_Baytlari_Reddetmeli_Ve_Durumu_Degistirmemeli()
    {
        var (yol, _, _) = await OrnekProje.TamProjeAsync();
        var once = yol.Revizyon;

        var r = await yol.GonderAsync(new NczSablonuAyarlaKomutu([1, 2, 3]), OrnekProje.Muhendis);

        r.IsFailure.Should().BeTrue();
        yol.Belge.NczSablonu.Should().BeNull();
        yol.Revizyon.Should().Be(once);
        yol.Denetim[^1].Sonuc.Should().Be(AuditSonucu.Reddedildi);
    }

    [Fact]
    public async Task NczSablonu_Yalniz_Varsa_RevisionIdye_Girmeli_Degisince_Onay_Dusmeli()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        var sablonsuz = yol.Revizyon;
        (await yol.GonderAsync(new NczSablonuAyarlaKomutu(null), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        yol.Revizyon.Should().Be(sablonsuz, "şablonsuz projelerin RevisionId'si değişmez");

        (await yol.GonderAsync(new NczSablonuAyarlaKomutu(SentetikNcz(isaret: 1)), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        var birinci = yol.Revizyon;
        birinci.Should().NotBe(sablonsuz);
        (await new ApprovalService(yol, zaman, OrnekProje.Topoloji).OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();

        (await yol.GonderAsync(new NczSablonuAyarlaKomutu(SentetikNcz(isaret: 2)), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        yol.Revizyon.Should().NotBe(birinci, "farklı şablon farklı PARSELASYON HARİTASI.NCZ üretir");
        yol.Belge.Onay.Should().BeNull("AC-PILOT-06: şablon değişince onay düşer");
    }

    [Fact]
    public async Task NczSablonuAyarla_Null_Sablonu_Temizlemeli_ProjeyiSifirla_Da_Temizlemeli()
    {
        var (yol, _, _) = await OrnekProje.TamProjeAsync();
        (await yol.GonderAsync(new NczSablonuAyarlaKomutu(SentetikNcz()), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();

        (await yol.GonderAsync(new NczSablonuAyarlaKomutu(null), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        yol.Belge.NczSablonu.Should().BeNull();
        yol.Denetim[^1].Ayrinti.Should().Be("temizlendi");

        (await yol.GonderAsync(new NczSablonuAyarlaKomutu(SentetikNcz()), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        (await yol.GonderAsync(new ProjeyiSifirlaKomutu(), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        yol.Belge.NczSablonu.Should().BeNull();
    }

    [Fact]
    public async Task CiktiPaketi_Onayli_Projenin_NCZ_Sablonunu_Tasimali()
    {
        var (yol, _, zaman) = await OrnekProje.TamProjeAsync();
        (await yol.GonderAsync(new NczSablonuAyarlaKomutu(SentetikNcz()), OrnekProje.Muhendis)).IsSuccess.Should().BeTrue();
        (await new ApprovalService(yol, zaman, OrnekProje.Topoloji).OnaylaAsync(OrnekProje.Muhendis, ApprovalService.OnayMetni)).IsSuccess.Should().BeTrue();

        var p = Imar18.Application.Cikti.CiktiPaketiOlusturucu.Olustur(yol.Belge, ProjeDegerlendirici.Degerlendir(yol.Belge, OrnekProje.Topoloji));

        p.IsSuccess.Should().BeTrue();
        p.Value.NczSablonu.Should().BeSameAs(yol.Belge.NczSablonu);
    }
}
