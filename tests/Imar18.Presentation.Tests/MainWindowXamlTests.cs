using System.Runtime.CompilerServices;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Imar18.Presentation.Tests;

/// <summary>
/// P6 reviewer H1 regresyonu: Avalonia'da aynı elemandaki diğer bağlamalar o elemanın yeni <c>DataContext</c>'ine göre çözülür.
/// Sihirbaz adım panelinde <c>DataContext="{Binding Hesap}"</c> ile <c>IsVisible="{Binding Wizard.IsStepN}"</c> aynı elemanda olursa
/// yol <c>Hesap.Wizard...</c> olur, bağlama sessizce başarısız olur ve panel her adımda görünür. Bu statik denetim görünümü
/// (headless çalıştırmadan) korur.
/// </summary>
public sealed class MainWindowXamlTests
{
    private static XDocument Belge([CallerFilePath] string buDosya = "")
    {
        var kok = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(buDosya)!, "..", ".."));
        return XDocument.Load(Path.Combine(kok, "src", "Imar18.Presentation.Avalonia", "Views", "MainWindow.axaml"));
    }

    [Fact]
    public void AnaPencere_Adim_Panelinde_DataContext_Degisen_Elemanda_Ust_Baglamli_IsVisible_Olmamali()
    {
        var hatali = Belge().Descendants()
            .Where(e => e.Attribute("DataContext") is not null
                && (e.Attribute("IsVisible")?.Value.Contains("Wizard.", StringComparison.Ordinal) ?? false))
            .Select(e => e.Name.LocalName)
            .ToList();

        hatali.Should().BeEmpty();
    }

    [Fact]
    public void AnaPencere_Her_Sihirbaz_Adimi_Icin_Tek_Gorunurluk_Kapisi_Olmali()
    {
        var kapilar = Belge().Descendants().Select(e => e.Attribute("IsVisible")?.Value ?? string.Empty)
            .Where(v => v.Contains("Wizard.IsStep", StringComparison.Ordinal)).ToList();

        kapilar.Should().BeEquivalentTo(Enumerable.Range(1, 5).Select(i => $"{{Binding Wizard.IsStep{i}}}"));
    }
}
