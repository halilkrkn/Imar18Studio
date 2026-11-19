using System.Buffers.Binary;
using System.Security.Cryptography;
using Imar18.Domain.Diagnostics;

namespace Imar18.Application.Cad;

/// <summary>
/// P7.3-C3 (SAHA-007): 1. adımda yüklenen kaynak Netcad <c>.NCZ</c> dosyasının şablon baytları (başlık, katman tablosu, çizgi
/// tipleri, XML ve kadastro kayıtları). <c>PARSELASYON HARİTASI.NCZ</c> bu baytlardan üretilir. Değişmezdir: baytlar kopyalanır,
/// SHA-256 özeti yalnızca baytlardan hesaplanır (dışarıdan verilen özete güvenilmez) ve bir kez hesaplanıp saklanır.
/// Burada yalnız hızlı başlık denetimi yapılır (imza, sürüm 120/125, bildirilen uzunluk); tam ayrıştırma Infrastructure
/// yazıcısındadır. Kaynak dosya kullanıcı adı ve XML kişi alanları taşıyabilir: denetime yalnız boyut ve özet yazılır (KVKK).
/// </summary>
public sealed class NczSablonu : IEquatable<NczSablonu>
{
    /// <summary>Uygulama güvenlik sınırı (gerçek parselasyon dosyaları 65–80 KB; proje kaydı her komutta yeniden yazılır).</summary>
    public const int EnBuyukBoyut = 8 * 1024 * 1024;

    /// <summary>Netcad 8.5 başlığı: 1 imza baytı + 12 × int32 (spike P4.0 §2.1).</summary>
    public const int BaslikBoyutu = 49;

    private const byte Imza = 0x01;
    private const int Surum0 = 120;
    private const int Surum2 = 125;

    private readonly byte[] _icerik;

    private NczSablonu(byte[] icerik, string sha256)
    {
        _icerik = icerik;
        Sha256 = sha256;
    }

    /// <summary>Şablon baytları (salt okunur görünüm).</summary>
    public ReadOnlyMemory<byte> Icerik => _icerik;

    /// <summary>Bayt sayısı.</summary>
    public int Uzunluk => _icerik.Length;

    /// <summary>Küçük harf onaltılık SHA-256 özeti (RevisionId'ye <c>ncz.sha256</c> olarak girer).</summary>
    public string Sha256 { get; }

    /// <summary>Baytları kopyalayıp doğrular; Netcad başlığı yoksa <c>ERR-GIRD-NCZ-001</c>.</summary>
    public static Result<NczSablonu> Olustur(ReadOnlySpan<byte> icerik)
    {
        if (BaslikHatasi(icerik) is { } neden)
        {
            return Hata(neden);
        }

        var kopya = icerik.ToArray();
        return Result<NczSablonu>.Success(new NczSablonu(kopya, Convert.ToHexStringLower(SHA256.HashData(kopya))));
    }

    /// <summary>Kalıcı kayıttan yükleme: baytların özeti saklanan özetle eşleşmezse reddedilir (hash doğrulaması).</summary>
    public static Result<NczSablonu> Yukle(ReadOnlySpan<byte> icerik, string beklenenSha256)
    {
        var s = Olustur(icerik);
        if (s.IsFailure)
        {
            return s;
        }

        return string.Equals(s.Value.Sha256, beklenenSha256, StringComparison.Ordinal) ? s : Hata("template checksum mismatch");
    }

    public bool Equals(NczSablonu? other) => other is not null && string.Equals(Sha256, other.Sha256, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as NczSablonu);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Sha256);

    private static string? BaslikHatasi(ReadOnlySpan<byte> d)
    {
        if (d.Length < BaslikBoyutu)
        {
            return "template header truncated";
        }

        if (d.Length > EnBuyukBoyut)
        {
            return "template too large";
        }

        if (d[0] != Imza
            || BinaryPrimitives.ReadInt32LittleEndian(d[1..]) != Surum0
            || BinaryPrimitives.ReadInt32LittleEndian(d[9..]) != Surum2)
        {
            return "template is not a Netcad 8.5 NCZ";
        }

        return BinaryPrimitives.ReadInt32LittleEndian(d[5..]) != d.Length ? "template declared length mismatch" : null;
    }

    private static Result<NczSablonu> Hata(string neden)
    {
        var e = ErrorCatalog.Get(ErrorCodes.GirdNczOkunamadi);
        return Result<NczSablonu>.Failure(e with
        {
            TechnicalMessage = e.TechnicalMessage.Replace("{path}", "ncz template (" + neden + ")", StringComparison.Ordinal),
        });
    }
}
