using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Imar18.Application.Cad;
using Imar18.Domain.Diagnostics;

namespace Imar18.Infrastructure.Cad.Cks;

/// <summary>
/// Netcad <c>.CKS</c> DOP ozet okuyucusu (spec §25.1, ncz-netcad skill).
/// <para>
/// Kodlama: Netcad dosyasi Turkce harfleri Windows-1254, cerceve karakterlerini OEM (cp437) baytlariyla yazar
/// (P4.1 gozlemi); public ornek UTF-8'dir. Okuyucu once kati UTF-8 dener, olmazsa bayt-koruyan Latin-1 ile cozer.
/// Satir etiketleri yalnizca ASCII "iskeletiyle" (ASCII disi karakterler atilmis, kucuk harf) eslenir; boylece
/// karisik kodlama, mojibake veya dogru Turkce metin ayni sonucu verir. Sayilar ASCII'dir ve kultur bagimsiz okunur.
/// </para>
/// Tum zorunlu alanlar tam bir kez bulunmali ve "Hesap" bolumu aritmetigi tutmalidir; aksi halde <c>ERR-GIRD-007</c>.
/// </summary>
public sealed partial class CksFileReader : ICksFileReader
{
    /// <summary>Kabul edilen en buyuk dosya boyutu (CKS ozet dosyasi birkac KB'dir).</summary>
    public const long MaxFileSizeBytes = 1024L * 1024;

    private const int DopoScale = 7;

    private static readonly string[] SimpleFields =
    [
        "Toplam Tahsis",
        "Toplam İmar Ada Alanı",
        "Toplam Ortak Katılım Alanı",
        "Toplam Düzenlemeye Giren (Dopal)",
        "Toplam Düzenlemeye Giren (Dopalma)",
        "Toplam Kamulaştırılan Alan",
        "Toplam Bağışlanan Alan",
        "Toplam Kadastro Parsel Alanı",
        "Toplam Pay",
        "Ortak Katılım Oranı",
        "Düzenleme Ortaklık Payı Oranı (DOPO)",
        "DOPO (OKO ile birlikte)",
        "Maksimum Dop Oranı",
    ];

    private const string KamuyaAyrilanHesap = "Kamuya Ayrılan Alan";
    private const string DopoHesap = "Düzenleme Ortaklık Payı";

    private static readonly Dictionary<string, string> SkeletonToField =
        SimpleFields.Append(KamuyaAyrilanHesap).Append(DopoHesap).ToDictionary(Skeleton, f => f, StringComparer.Ordinal);

    /// <inheritdoc />
    public Result<CksDopOzeti> Read(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        if (!CadInputBuffer.TryReadAll(source, MaxFileSizeBytes, cancellationToken, out var data))
        {
            return Fail("file too large (limit 1 MiB)");
        }

        var simple = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var expressions = new Dictionary<string, (decimal A, char Op, decimal B, decimal C)>(StringComparer.Ordinal);

        foreach (var rawLine in Decode(data).Split('\n'))
        {
            var eq = rawLine.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0 || !SkeletonToField.TryGetValue(Skeleton(rawLine[..eq]), out var field))
            {
                continue; // cerceve, baslik veya bilinmeyen satir
            }

            var value = AsciiOnly(rawLine[(eq + 1)..]).Trim();
            if (field is KamuyaAyrilanHesap or DopoHesap)
            {
                var m = ExpressionPattern().Match(value);
                if (!m.Success || !TryNumber(m.Groups["a"].Value, out var a) || !TryNumber(m.Groups["b"].Value, out var b)
                    || !TryNumber(m.Groups["c"].Value, out var c))
                {
                    return Fail("invalid calculation expression or number format");
                }

                if (!expressions.TryAdd(field, (a, m.Groups["op"].Value[0], b, c)))
                {
                    return Fail("duplicate field");
                }

                continue;
            }

            if (!TryNumber(value, out var number))
            {
                return Fail("invalid number format");
            }

            if (!simple.TryAdd(field, number))
            {
                return Fail("duplicate field");
            }
        }

        if (simple.Count != SimpleFields.Length || expressions.Count != 2)
        {
            return Fail("missing required field");
        }

        var kamuya = expressions[KamuyaAyrilanHesap];
        var dopo = expressions[DopoHesap];
        if (kamuya.Op != '-' || dopo.Op != '/' || dopo.B == 0m
            || kamuya.A - kamuya.B != kamuya.C
            || decimal.Round(dopo.A / dopo.B, DopoScale, MidpointRounding.AwayFromZero) != dopo.C
            || dopo.A != kamuya.C
            || dopo.C != simple["Düzenleme Ortaklık Payı Oranı (DOPO)"])
        {
            return Fail("calculation section arithmetic mismatch");
        }

        return Result<CksDopOzeti>.Success(new CksDopOzeti(
            simple["Toplam Tahsis"],
            simple["Toplam İmar Ada Alanı"],
            simple["Toplam Ortak Katılım Alanı"],
            simple["Toplam Düzenlemeye Giren (Dopal)"],
            simple["Toplam Düzenlemeye Giren (Dopalma)"],
            simple["Toplam Kamulaştırılan Alan"],
            simple["Toplam Bağışlanan Alan"],
            simple["Toplam Kadastro Parsel Alanı"],
            simple["Toplam Pay"],
            simple["Ortak Katılım Oranı"],
            simple["Düzenleme Ortaklık Payı Oranı (DOPO)"],
            simple["DOPO (OKO ile birlikte)"],
            simple["Maksimum Dop Oranı"],
            kamuya.C));
    }

    /// <summary>ASCII disi karakterleri atar, harf/rakam/bosluk/parantezi korur, kucuk harfe cevirir, bosluklari teklestirir.</summary>
    internal static string Skeleton(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '(' or ')')
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (c == ' ' && sb.Length > 0 && sb[^1] != ' ')
            {
                sb.Append(' ');
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string AsciiOnly(string text) => string.Concat(text.Where(c => c < 128 && !char.IsControl(c)));

    private static string Decode(byte[] data)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(data).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(data);
        }
    }

    private static bool TryNumber(string text, out decimal value)
    {
        value = 0m;
        return NumberPattern().IsMatch(text)
            && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    private static Result<CksDopOzeti> Fail(string reason)
    {
        var error = ErrorCatalog.Get(ErrorCodes.GirdCksOkunamadi);
        return Result<CksDopOzeti>.Failure(error with
        {
            TechnicalMessage = error.TechnicalMessage
                .Replace("{path}", "CKS input", StringComparison.Ordinal)
                .Replace("{reason}", reason, StringComparison.Ordinal),
        });
    }

    // CKS alanlari ve oranlari negatif olamaz (P4.1 reviewer M6); isaret kabul edilmez.
    [GeneratedRegex(@"^\d{1,15}(\.\d{1,10})?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"^(?<a>\S+)\s*(?<op>[-/])\s*(?<b>\S+)\s*=\s*(?<c>\S+)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ExpressionPattern();
}
