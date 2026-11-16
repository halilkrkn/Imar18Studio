using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Imar18.Domain.Numerics;

namespace Imar18.Domain.Revizyon;

/// <summary>
/// Hesap revizyonu kimliği (D16, AC-PILOT-06): kanonik girdi özetinin SHA-256 değeri, 64 haneli küçük harf hex.
/// Private yapıcı: yalnızca <see cref="RevizyonOzetiOlusturucu"/> veya <see cref="TryParse"/> ile kurulur.
/// </summary>
public sealed record RevisionId
{
    private RevisionId(string deger) => Deger = deger;

    /// <summary>64 haneli küçük harf hex SHA-256.</summary>
    public string Deger { get; }

    /// <summary>Kanonik biçimdeki (64 küçük harf hex) değeri ayrıştırır.</summary>
    public static bool TryParse(string? deger, out RevisionId id)
    {
        if (deger is { Length: 64 } && deger.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            id = new RevisionId(deger);
            return true;
        }

        id = null!;
        return false;
    }

    internal static RevisionId FromHash(byte[] hash) => new(Convert.ToHexStringLower(hash));

    public override string ToString() => Deger;
}

/// <summary>
/// Kanonik revizyon özeti: her alan uzunluk önekli UTF-8 (anahtar, değer) çifti olarak SHA-256'ya yazılır; alan sınırları
/// belirsizlik üretmez. Ondalık değerler <see cref="Fraction"/> sadeleştirilmiş biçimiyle yazılır (400.0 ≡ 400.00).
/// Alan sırası çağıranın sorumluluğundadır (deterministik sıralama ile eklenmelidir).
/// </summary>
public sealed class RevizyonOzetiOlusturucu
{
    private const string SemaSurumu = "imar18-revizyon-v1";
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public RevizyonOzetiOlusturucu() => Yaz("sema", SemaSurumu);

    public RevizyonOzetiOlusturucu Ekle(string anahtar, string deger)
    {
        ArgumentNullException.ThrowIfNull(anahtar);
        ArgumentNullException.ThrowIfNull(deger);
        Yaz(anahtar, deger);
        return this;
    }

    public RevizyonOzetiOlusturucu Ekle(string anahtar, decimal deger) => Ekle(anahtar, Fraction.FromDecimal(deger).ToString());

    public RevizyonOzetiOlusturucu Ekle(string anahtar, Fraction deger) => Ekle(anahtar, deger.ToString());

    public RevizyonOzetiOlusturucu Ekle(string anahtar, bool deger) => Ekle(anahtar, deger ? "1" : "0");

    public RevizyonOzetiOlusturucu Ekle(string anahtar, int deger) => Ekle(anahtar, deger.ToString(CultureInfo.InvariantCulture));

    /// <summary>Özeti tamamlar (oluşturucu sonrasında yeniden kullanılmamalıdır).</summary>
    public RevisionId Olustur() => RevisionId.FromHash(_hash.GetHashAndReset());

    private void Yaz(string anahtar, string deger)
    {
        Parca(anahtar);
        Parca(deger);
    }

    private void Parca(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        Span<byte> uzunluk = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(uzunluk, bytes.Length);
        _hash.AppendData(uzunluk);
        _hash.AppendData(bytes);
    }
}

/// <summary>
/// Revizyona bağlı mühendis onayı (D16, AC-PILOT-06): onay yalnızca bağlandığı <see cref="RevisionId"/> için geçerlidir;
/// veri değişip yeni revizyon üretildiğinde onay otomatik düşer. Kritik hata engeli ve audit kaydı P5.2 ApprovalService'tedir.
/// </summary>
public sealed record MuhendisOnayi
{
    private MuhendisOnayi(RevisionId revizyonId, string onaylayanId, DateTimeOffset zaman)
    {
        RevizyonId = revizyonId;
        OnaylayanId = onaylayanId;
        Zaman = zaman;
    }

    public RevisionId RevizyonId { get; }

    public string OnaylayanId { get; }

    public DateTimeOffset Zaman { get; }

    /// <exception cref="ArgumentException">Onaylayan kimliği boşsa.</exception>
    public static MuhendisOnayi Olustur(RevisionId revizyonId, string onaylayanId, DateTimeOffset zaman)
    {
        ArgumentNullException.ThrowIfNull(revizyonId);
        ArgumentException.ThrowIfNullOrWhiteSpace(onaylayanId);
        return new MuhendisOnayi(revizyonId, onaylayanId, zaman);
    }

    /// <summary>Güncel revizyon onaylanan revizyonla aynıysa true.</summary>
    public bool GecerliMi(RevisionId guncelRevizyon)
    {
        ArgumentNullException.ThrowIfNull(guncelRevizyon);
        return RevizyonId == guncelRevizyon;
    }
}
