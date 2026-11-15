using System.Reflection;
using FluentAssertions;
using Imar18.Application;
using Imar18.Domain;
using Imar18.GeometryEngine;
using Imar18.Infrastructure;
using Imar18.Presentation.Avalonia;
using NetArchTest.Rules;
using Xunit;

namespace Imar18.Architecture.Tests;

/// <summary>
/// Imar18Studio v2.19 (§3, §9.4 ve D7) taviz verilmeyecek Clean Architecture,
/// Decimal/Fraction tip izolasyonu, GeometryEngine public NTS sizinti yasagi
/// ve bos assembly vakuum korumasi testleri.
/// </summary>
public sealed class CleanArchitectureRulesTests
{
    private static readonly Assembly DomainAssembly = typeof(Imar18DomainAssemblyMarker).Assembly;
    private static readonly Assembly GeometryAssembly = typeof(Imar18GeometryEngineAssemblyMarker).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Imar18ApplicationAssemblyMarker).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Imar18InfrastructureAssemblyMarker).Assembly;
    private static readonly Assembly PresentationAssembly = typeof(Imar18PresentationAvaloniaAssemblyMarker).Assembly;

    [Fact]
    public void CI_NetArchTest_Kontrol_Edilen_Her_Assemblyde_En_Az_Bir_Tip_Bulunmali()
    {
        var assemblies = new[]
        {
            DomainAssembly,
            GeometryAssembly,
            ApplicationAssembly,
            InfrastructureAssembly,
            PresentationAssembly
        };

        foreach (var asm in assemblies)
        {
            asm.GetTypes().Should().NotBeEmpty(
                $"{asm.GetName().Name} assembly'si bos olmamalidir (bos assembly vakuum korumasi — D7).");
        }
    }

    [Fact]
    public void CI_NetArchTest_Domain_Hicbir_Dis_Katmana_Veya_Geometri_Kutuphanesine_Bagimli_Olmamali()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Imar18.GeometryEngine",
                "Imar18.Application",
                "Imar18.Infrastructure",
                "Imar18.Presentation.Avalonia",
                "NetTopologySuite",
                "ProjNet",
                "ACadSharp",
                "Avalonia",
                "SkiaSharp",
                "ClosedXML",
                "QuestPDF")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Imar18.Domain saf C# ve .NET BCL kullanmalidir; UI, I/O veya NTS bagimliligi alamaz (§3.2).");
    }

    [Fact]
    public void CI_NetArchTest_Application_Katmani_NetTopologySuite_Veya_Infrastructure_Tiplerini_Sizdirmamali()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "NetTopologySuite",
                "NetTopologySuite.Geometries",
                "Imar18.Infrastructure",
                "Imar18.Presentation.Avalonia",
                "ACadSharp",
                "Avalonia")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "NetTopologySuite.Geometries tipleri Application sozlesmelerine veya dis DTO'lara sizamaz (§3.2 Madde 2).");
    }

    [Fact]
    public void CI_NetArchTest_GeometryEngine_Public_Imzalarinda_NetTopologySuite_Tipi_Olmamali()
    {
        var leakingSignatures = new List<string>();

        static bool IsNtsType(Type? type)
        {
            if (type is null) return false;
            if (type.IsGenericType)
            {
                if (type.GetGenericArguments().Any(IsNtsType)) return true;
            }
            if (type.IsArray && IsNtsType(type.GetElementType())) return true;
            return type.Namespace is not null &&
                   type.Namespace.StartsWith("NetTopologySuite", StringComparison.Ordinal);
        }

        foreach (var type in GeometryAssembly.GetExportedTypes())
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsNtsType(prop.PropertyType))
                {
                    leakingSignatures.Add($"{type.FullName}.{prop.Name} -> {prop.PropertyType.FullName}");
                }
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsNtsType(field.FieldType))
                {
                    leakingSignatures.Add($"{type.FullName}.{field.Name} -> {field.FieldType.FullName}");
                }
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (IsNtsType(method.ReturnType))
                {
                    leakingSignatures.Add($"{type.FullName}.{method.Name}() return -> {method.ReturnType.FullName}");
                }

                foreach (var param in method.GetParameters())
                {
                    if (IsNtsType(param.ParameterType))
                    {
                        leakingSignatures.Add($"{type.FullName}.{method.Name}({param.Name}: {param.ParameterType.FullName})");
                    }
                }
            }
        }

        leakingSignatures.Should().BeEmpty(
            "Imar18.GeometryEngine public tip ve uye imzalarinda NetTopologySuite tipleri disariya sizdirilamaz (D7 / AC-PILOT-02). Ihlaller: " +
            string.Join(", ", leakingSignatures));
    }

    [Fact]
    public void CI_NetArchTest_Presentation_Katmani_Infrastructure_Katmanina_Dogrudan_Erisememeli()
    {
        var result = Types.InAssembly(PresentationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Imar18.Infrastructure",
                "NetTopologySuite",
                "ACadSharp",
                "Microsoft.Data.Sqlite")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Imar18.Presentation.Avalonia yalnizca ViewModel ve CommandBus (Application) ile calismalidir.");
    }

    [Fact]
    public void CI_NetArchTest_Domain_Ve_Application_Katmanlarinda_Double_Veya_Float_Alan_Yada_Ozellik_Bulunmamali()
    {
        var domainAndAppTypes = DomainAssembly.GetTypes()
            .Concat(ApplicationAssembly.GetTypes())
            .Where(t => t.IsClass || t.IsValueType);

        var forbiddenMembers = new List<string>();

        static bool IsFloatingPoint(Type? t)
        {
            if (t is null) return false;
            if (t.IsByRef) t = t.GetElementType();
            if (t is null) return false;
            if (t == typeof(double) || t == typeof(double?) || t == typeof(float) || t == typeof(float?))
                return true;
            if (t.IsArray && IsFloatingPoint(t.GetElementType()))
                return true;
            if (t.IsGenericType && t.GetGenericArguments().Any(IsFloatingPoint))
                return true;
            return false;
        }

        foreach (var type in domainAndAppTypes)
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsFloatingPoint(prop.PropertyType))
                {
                    forbiddenMembers.Add($"{type.FullName}.{prop.Name} ({prop.PropertyType.Name})");
                }
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsFloatingPoint(field.FieldType))
                {
                    forbiddenMembers.Add($"{type.FullName}.{field.Name} ({field.FieldType.Name})");
                }
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (IsFloatingPoint(method.ReturnType))
                {
                    forbiddenMembers.Add($"{type.FullName}.{method.Name}() return ({method.ReturnType.Name})");
                }

                foreach (var param in method.GetParameters())
                {
                    if (IsFloatingPoint(param.ParameterType))
                    {
                        forbiddenMembers.Add($"{type.FullName}.{method.Name}({param.Name}: {param.ParameterType.Name})");
                    }
                }
            }
        }

        forbiddenMembers.Should().BeEmpty(
            "Mulkiyet, tapu alani, tecviz, DOP, mahsup ve hisse hesaplarinda (alan, ozellik, metot parametresi ve donus tipi dahil) yalnizca System.Decimal ve Fraction(BigInteger) kullanilabilir; double/float yasaktir (§3.2 Madde 1 & D7). Ihlaller: " +
            string.Join(", ", forbiddenMembers));
    }

    [Fact]
    public void CI_NetArchTest_GeometryEngine_UI_Veya_Infrastructure_Bagimliligi_Almamali()
    {
        var result = Types.InAssembly(GeometryAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Imar18.Application",
                "Imar18.Infrastructure",
                "Imar18.Presentation.Avalonia",
                "Avalonia",
                "ClosedXML",
                "QuestPDF")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Imar18.GeometryEngine yalnizca geometri ve eksen/kuantizasyon hesabi yapar; I/O veya UI bilemez.");
    }
}
