using System.Buffers.Binary;
using System.Text;

namespace Imar18.Infrastructure.Tests.Cad.Ncz;

/// <summary>
/// Test icin tamamen sentetik Netcad 8.5 <c>.NCZ</c> ikili dosyasi uretir (gercek veri icermez, AC-PILOT-08).
/// Yerlesim P4.0 spike raporundaki (docs/spikes/P4.0_NCZ_Fizibilite_Raporu.md) cozumlenmis yapiyi izler:
/// 1 bayt imza + 12 adet int32 baslik, katman tablosu (22 bayt baslik + 29 baytlik kayitlar) ve
/// [bayrak][int32 boyut][yuk] kayit akisi. Koordinatlar (X=Kuzey, Y=Dogu, Z) little-endian double'dir.
/// </summary>
internal sealed class NczSentetikDosya
{
    private static readonly Encoding Turkce = CodePagesEncodingProvider.Instance.GetEncoding(1254)!;

    private readonly List<string> _katmanlar = [];
    private readonly List<byte[]> _kayitlar = [];

    public byte Imza { get; set; } = 0x01;

    public int Baslik0 { get; set; } = 120;

    public int Baslik2 { get; set; } = 125;

    /// <summary>Baslik ile katman tablosu arasindaki bolge (null: 200 bayt sifir). Kullanici adi/PNG benzetimi icin.</summary>
    public byte[]? OnBolge { get; set; }

    /// <summary>Katman tablosu ile varlik akisi arasindaki bolge (null: 64 bayt sifir). NetmapManager XML benzetimi icin.</summary>
    public byte[]? KatmanSonrasiBolge { get; set; }

    /// <summary>Null ise gercek dosya uzunlugu yazilir.</summary>
    public int? BildirilenUzunluk { get; set; }

    /// <summary>
    /// SAHA-017: gercek Netcad 8.5 blok duzeni — h[3]'te [0x06][int32 uzunluk = 13 + 29n][13 bayt on baslik (16: sayac)]
    /// [n x (FD 80 12 00 + 21 bayt Pascal ad + 4 bayt renk)], blok sonunda (= h[7]) font blogu [0x0B][84]["ARIAL.NCF"],
    /// h[8] = h[7] + 89'da sembol blogu [0x0C]["PLAN.NCS"], sonra <see cref="KatmanSonrasiBolge"/>, h[10]'da [0x0D][0] ve h[11]'de [0x17][0].
    /// Varsayilan (false) eski sentetik duzendir (diger P4/P6 testlerinin baytlari degismez).
    /// </summary>
    public bool NetcadBloklari { get; set; }

    public NczSentetikDosya Katman(string ad)
    {
        _katmanlar.Add(ad);
        return this;
    }

    /// <summary>Netcad cizgi (tip 2) kaydi: okuyucu tarafindan atlanmalidir.</summary>
    public NczSentetikDosya Cizgi(int katman, (double X, double Y) p1, (double X, double Y) p2)
    {
        var yuk = new byte[101];
        yuk[0] = 2;
        yuk[1] = 2;
        yuk[2] = (byte)katman;
        Double(yuk, 3, p1.X);
        Double(yuk, 11, p1.Y);
        BinaryPrimitives.WriteInt16LittleEndian(yuk.AsSpan(30), (short)katman);
        Double(yuk, 85, p2.X);
        Double(yuk, 93, p2.Y);
        _kayitlar.Add(Kayit(0x15, yuk));
        return this;
    }

    /// <summary>Yazi (tip 5) kaydi benzetimi: metin yukte duz durur (anonimlestiricide atlanmalidir).</summary>
    public NczSentetikDosya Yazi(int katman, string metin)
    {
        var yuk = new List<byte> { 5, 5, (byte)katman };
        yuk.AddRange(new byte[40]);
        yuk.AddRange(PascalAlan(metin, 64, null));
        _kayitlar.Add(Kayit(0x15, [.. yuk]));
        return this;
    }

    /// <summary>
    /// SAHA-015: gercek Netcad duzeninde tip 5 yazi kaydi (P7.3 spike §2.4): yuk = 93 + metin uzunlugu bayt; 3..18 ekleme noktasi
    /// (Kuzey, Dogu), 30 int16 katman, 41 float 1.0, 45..76 sinir kutusu (minK, minD, maxK, maxD), 81 float yukseklik, 85 float
    /// donukluk (radyan), 89 stil 'M', 92 Pascal metin (Windows-1254).
    /// </summary>
    public NczSentetikDosya YaziTip5(
        int katman,
        string metin,
        (double Kuzey, double Dogu) ekleme,
        (double MinK, double MinD, double MaxK, double MaxD)? kutu = null,
        float yukseklik = 1.5f,
        float donukluk = 0f,
        int? uzunlukOverride = null,
        int fazlaBayt = 0,
        byte bayrak = 0x15,
        int? katmanAlaniOverride = null)
    {
        var b = Turkce.GetBytes(metin);
        var yuk = new byte[93 + b.Length + fazlaBayt];
        yuk[0] = 5;
        yuk[1] = 5;
        yuk[2] = (byte)katman;
        Double(yuk, 3, ekleme.Kuzey);
        Double(yuk, 11, ekleme.Dogu);
        BinaryPrimitives.WriteInt16LittleEndian(yuk.AsSpan(30), (short)(katmanAlaniOverride ?? katman));
        BinaryPrimitives.WriteSingleLittleEndian(yuk.AsSpan(41), 1f);
        var k = kutu ?? (ekleme.Kuzey, ekleme.Dogu, ekleme.Kuzey + yukseklik, ekleme.Dogu + (0.8 * yukseklik * metin.Length));
        Double(yuk, 45, k.MinK);
        Double(yuk, 53, k.MinD);
        Double(yuk, 61, k.MaxK);
        Double(yuk, 69, k.MaxD);
        BinaryPrimitives.WriteSingleLittleEndian(yuk.AsSpan(81), yukseklik);
        BinaryPrimitives.WriteSingleLittleEndian(yuk.AsSpan(85), donukluk);
        yuk[89] = (byte)'M';
        yuk[92] = (byte)(uzunlukOverride ?? b.Length);
        b.CopyTo(yuk, 93);
        _kayitlar.Add(Kayit(bayrak, yuk));
        return this;
    }

    /// <summary>Poligon/coklu cizgi (tip 7) kaydi.</summary>
    /// <param name="sinif">Null degilse genisletilmis (0x16) kayit ve 28 baytlik sinif alani yazilir.</param>
    /// <param name="bildirilenAlan">Null degilse alan bayragi (0x02) set edilir.</param>
    public NczSentetikDosya Poligon(
        int katman,
        string etiket,
        IReadOnlyList<(double X, double Y, double Z)> koseler,
        double? bildirilenAlan = null,
        string? sinif = null,
        int? katmanAlaniOverride = null,
        int fazlaBayt = 0,
        int? etiketUzunlukOverride = null,
        int? sinifUzunlukOverride = null)
    {
        var basliksiz = new List<byte>();
        var yuk = new byte[81];
        yuk[0] = 7;
        yuk[1] = 7;
        yuk[2] = (byte)katman;
        BinaryPrimitives.WriteInt16LittleEndian(yuk.AsSpan(30), (short)(katmanAlaniOverride ?? katman));
        basliksiz.AddRange(yuk);

        if (sinif is not null)
        {
            basliksiz.AddRange(PascalAlan(sinif, 28, sinifUzunlukOverride));
        }

        basliksiz.AddRange(PascalAlan(etiket, 15, etiketUzunlukOverride));
        var bayrak = 0x11 | (bildirilenAlan.HasValue ? 0x02 : 0);
        var tmp = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(tmp, bayrak);
        Double(tmp, 4, bildirilenAlan ?? 0d);
        basliksiz.AddRange(tmp);

        foreach (var (x, y, z) in koseler)
        {
            var k = new byte[24];
            Double(k, 0, x);
            Double(k, 8, y);
            Double(k, 16, z);
            basliksiz.AddRange(k);
        }

        basliksiz.AddRange(new byte[fazlaBayt]);
        _kayitlar.Add(Kayit(sinif is null ? (byte)0x15 : (byte)0x16, [.. basliksiz]));
        return this;
    }

    /// <summary>Ham kayit (bozuk dosya testleri icin).</summary>
    public NczSentetikDosya HamKayit(byte bayrak, byte[] yuk)
    {
        _kayitlar.Add(Kayit(bayrak, yuk));
        return this;
    }

    public byte[] Olustur(int kirpilacakBayt = 0)
    {
        var dosya = new List<byte>();
        dosya.Add(Imza);
        dosya.AddRange(new byte[48]); // 12 x int32 baslik, sonra doldurulur
        dosya.AddRange(OnBolge ?? new byte[200]); // onizleme/cizgi tipi bolgesi yerine sentetik dolgu

        var katmanOfseti = dosya.Count;
        int katmanSonu;
        var sembolOfseti = 0;
        if (NetcadBloklari)
        {
            dosya.Add(0x06);
            dosya.AddRange(BitConverter.GetBytes(13 + (29 * _katmanlar.Count)));
            dosya.AddRange(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0 });
            dosya.AddRange(BitConverter.GetBytes((short)_katmanlar.Count));
            foreach (var ad in _katmanlar)
            {
                var kayit = new byte[29];
                new byte[] { 0xFD, 0x80, 0x12, 0x00 }.CopyTo(kayit, 0);
                var b = Turkce.GetBytes(ad);
                kayit[4] = (byte)b.Length;
                b.CopyTo(kayit, 5);
                new byte[] { 0x01, 0x00, 0x01, 0x00 }.CopyTo(kayit, 25);
                dosya.AddRange(kayit);
            }

            katmanSonu = dosya.Count;
            dosya.AddRange(Blok(0x0B, 84, [1, 0, 0, 0, 9, .. Encoding.ASCII.GetBytes("ARIAL.NCF")]));
            sembolOfseti = dosya.Count;
            dosya.AddRange(Blok(0x0C, 16, [3, 0, 0, 0, 8, .. Encoding.ASCII.GetBytes("PLAN.NCS")]));
            dosya.AddRange(KatmanSonrasiBolge ?? new byte[64]);
            dosya.AddRange(new byte[] { 0x0D, 0, 0, 0, 0 });
        }
        else
        {
            var katmanBasligi = new byte[22];
            BinaryPrimitives.WriteInt16LittleEndian(katmanBasligi.AsSpan(16), (short)_katmanlar.Count);
            dosya.AddRange(katmanBasligi);
            foreach (var ad in _katmanlar)
            {
                var kayit = new byte[29];
                var b = Turkce.GetBytes(ad);
                kayit[0] = (byte)b.Length;
                b.CopyTo(kayit, 1);
                dosya.AddRange(kayit);
            }

            katmanSonu = dosya.Count - 4;
            dosya.AddRange(KatmanSonrasiBolge ?? new byte[64]); // diger bolumler yerine sentetik dolgu
        }

        var varlikOfseti = dosya.Count;
        dosya.Add(0x17);
        dosya.AddRange(new byte[4]);
        foreach (var k in _kayitlar)
        {
            dosya.AddRange(k);
        }

        var bayt = dosya.ToArray();
        int[] baslik = [Baslik0, BildirilenUzunluk ?? bayt.Length, Baslik2, katmanOfseti, 0, 0, 0, katmanSonu, sembolOfseti, 562, varlikOfseti - 5, varlikOfseti];
        for (var i = 0; i < baslik.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bayt.AsSpan(1 + (4 * i)), baslik[i]);
        }

        return kirpilacakBayt == 0 ? bayt : bayt[..^kirpilacakBayt];
    }

    /// <summary>[tip][int32 uzunluk][icerik + sifir dolgu] blogu.</summary>
    private static byte[] Blok(byte tip, int uzunluk, byte[] icerik)
    {
        var b = new byte[5 + uzunluk];
        b[0] = tip;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(1), uzunluk);
        icerik.CopyTo(b, 5);
        return b;
    }

    /// <summary>Uretilen dosyanin baslik int32 alanini (h[k]) degistirir.</summary>
    public static void BaslikYaz(byte[] dosya, int k, int deger) =>
        BinaryPrimitives.WriteInt32LittleEndian(dosya.AsSpan(1 + (4 * k)), deger);

    /// <summary>Uretilen dosyanin baslik int32 alanini (h[k]) okur.</summary>
    public static int BaslikOku(byte[] dosya, int k) =>
        BinaryPrimitives.ReadInt32LittleEndian(dosya.AsSpan(1 + (4 * k)));

    public MemoryStream Akis(int kirpilacakBayt = 0) => new(Olustur(kirpilacakBayt), writable: false);

    private static byte[] Kayit(byte bayrak, byte[] yuk)
    {
        var k = new byte[5 + yuk.Length];
        k[0] = bayrak;
        BinaryPrimitives.WriteInt32LittleEndian(k.AsSpan(1), yuk.Length);
        yuk.CopyTo(k, 5);
        return k;
    }

    private static byte[] PascalAlan(string metin, int genislik, int? uzunlukOverride)
    {
        var alan = new byte[genislik];
        var b = Turkce.GetBytes(metin);
        alan[0] = (byte)(uzunlukOverride ?? b.Length);
        b.AsSpan(0, Math.Min(b.Length, genislik - 1)).CopyTo(alan.AsSpan(1));
        return alan;
    }

    private static void Double(byte[] hedef, int ofset, double deger) =>
        BinaryPrimitives.WriteDoubleLittleEndian(hedef.AsSpan(ofset), deger);
}
