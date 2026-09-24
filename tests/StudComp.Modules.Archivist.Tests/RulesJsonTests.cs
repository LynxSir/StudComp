using StudComp.Core.Domain;
using StudComp.Modules.Archivist.Services;

namespace StudComp.Modules.Archivist.Tests;

/// <summary>Формат экспорта/импорта правил (ARCHITECTURE §8.7): версионированный конверт, предмет по имени.</summary>
public sealed class RulesJsonTests
{
    [Fact]
    public void Round_trip_preserves_every_field()
    {
        var source = new List<RuleExportDto>
        {
            new()
            {
                Pattern = @"^ЛР\d+.*\.docx$",
                MatchType = RuleMatchType.Regex,
                Priority = 42,
                Enabled = false,
                WorkType = "ЛР",
                WatchedFolder = @"C:\Загрузки",
                RenameTemplate = "{Subject}_{Type}{Ext}",
                SubjectName = "Матан",
            },
        };

        var json = RulesJson.Serialize(source);
        var parsed = RulesJson.Deserialize(json);

        Assert.True(parsed.IsSuccess);
        var dto = Assert.Single(parsed.Value);
        Assert.Equal(source[0].Pattern, dto.Pattern);
        Assert.Equal(RuleMatchType.Regex, dto.MatchType);
        Assert.Equal(42, dto.Priority);
        Assert.False(dto.Enabled);
        Assert.Equal("ЛР", dto.WorkType);
        Assert.Equal("{Subject}_{Type}{Ext}", dto.RenameTemplate);
        Assert.Equal("Матан", dto.SubjectName);
        Assert.Equal(@"C:\Загрузки", dto.WatchedFolder);
    }

    /// <summary>
    /// Конверт версии не менял: файл, выгруженный до Phase 10, просто не несёт поля папки —
    /// читаться он обязан по-прежнему.
    /// </summary>
    [Fact]
    public void File_exported_before_folder_scope_still_loads()
    {
        const string legacy = """
            {
              "schema": "rubrica.archivist.rules",
              "version": 1,
              "rules": [
                { "pattern": ".docx", "matchType": "Extension", "priority": 10, "enabled": true }
              ]
            }
            """;

        var parsed = RulesJson.Deserialize(legacy);

        Assert.True(parsed.IsSuccess);
        Assert.Null(Assert.Single(parsed.Value).WatchedFolder);
    }

    [Fact]
    public void Enum_is_written_as_a_name_not_a_number()
    {
        var json = RulesJson.Serialize([new RuleExportDto { Pattern = ".pdf", MatchType = RuleMatchType.Extension }]);

        Assert.Contains("\"Extension\"", json);
        Assert.DoesNotContain("\"matchType\": 0", json);
    }

    [Fact]
    public void Unknown_and_missing_fields_are_tolerated()
    {
        var json = """
        { "schema": "rubrica.archivist.rules", "version": 1, "extra": "ignored", "rules": [
          { "pattern": ".zip", "matchType": "Extension" }
        ] }
        """;

        var parsed = RulesJson.Deserialize(json);

        Assert.True(parsed.IsSuccess);
        var dto = Assert.Single(parsed.Value);
        Assert.Equal(".zip", dto.Pattern);
        Assert.True(dto.Enabled); // дефолт, поля в файле не было
        Assert.Null(dto.SubjectName);
    }

    [Theory]
    [InlineData("{ \"not\": \"ours\" }")]
    [InlineData("не json вовсе")]
    [InlineData("{ \"schema\": \"something.else\", \"rules\": [] }")]
    public void Foreign_or_broken_content_is_rejected(string json)
    {
        var parsed = RulesJson.Deserialize(json);

        Assert.True(parsed.IsFailure);
        Assert.Equal("archivist.import_bad_format", parsed.Error.Code);
    }
}
