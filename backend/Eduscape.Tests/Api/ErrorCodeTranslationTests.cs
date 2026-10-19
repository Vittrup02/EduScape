using System.Reflection;
using System.Text.Json;
using Eduscape.Api.Errors;
using Eduscape.Domain.Common;
using Eduscape.Domain.Rooms;

namespace Eduscape.Tests.Api;

/// <summary>
/// Contract between backend and frontend: every error code the backend can return
/// must have a translation in every language file, so users never see a raw code.
/// </summary>
public class ErrorCodeTranslationTests
{
    public static TheoryData<string> LocaleFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(LocalesDirectory(), "*.json"))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LocaleFiles))]
    public void EveryBackendErrorCode_IsTranslated(string localeFile)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(LocalesDirectory(), localeFile)));
        var translated = json.RootElement.GetProperty("errors").EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet();

        var missing = BackendErrorCodes().Where(code => !translated.Contains(code)).ToList();

        Assert.True(missing.Count == 0, $"{localeFile} is missing errors.{string.Join(", errors.", missing)}");
    }

    [Theory]
    [MemberData(nameof(LocaleFiles))]
    public void EverySubjectAndGradeLevel_IsTranslated(string localeFile)
    {
        // The API sends these enums as keys (e.g. "Mathematics") for the frontend to translate.
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(LocalesDirectory(), localeFile)));

        foreach (var (section, keys) in new[] { ("subjects", Enum.GetNames<Subject>()), ("gradeLevels", Enum.GetNames<GradeLevel>()) })
        {
            var translated = json.RootElement.GetProperty(section).EnumerateObject().Select(p => p.Name).ToHashSet();
            var missing = keys.Where(key => !translated.Contains(key)).ToList();
            Assert.True(missing.Count == 0, $"{localeFile} is missing {section}.{string.Join($", {section}.", missing)}");
        }
    }

    [Fact]
    public void LocalesDirectory_ContainsDanishAndEnglish()
    {
        var files = Directory.GetFiles(LocalesDirectory(), "*.json").Select(Path.GetFileName);

        Assert.Contains("da.json", files);
        Assert.Contains("en.json", files);
    }

    private static IEnumerable<string> BackendErrorCodes() =>
        new[] { typeof(DomainErrors), typeof(ApiErrors) }
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f is { IsLiteral: true } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    private static string LocalesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "frontend", "src", "locales")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new DirectoryNotFoundException("Could not find frontend/src/locales above the test output directory.")
            : Path.Combine(directory.FullName, "frontend", "src", "locales");
    }
}
