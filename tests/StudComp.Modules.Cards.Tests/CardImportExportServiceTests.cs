using System.Text;
using StudComp.Core.Domain;
using StudComp.Modules.Cards.Services;

namespace StudComp.Modules.Cards.Tests;

/// <summary>
/// Импорт/экспорт картотеки (new_addons.md §7.6, DoD §12 Phase 12.9): двухфазный импорт
/// (Preview → Commit), JSON и CSV, режимы Add/ReplaceDeck/Merge.
/// </summary>
public sealed class CardImportExportServiceTests : CardsDatabaseTestBase
{
    private readonly List<string> _tempFiles = [];

    private string TempFile(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rubrica-cards-import-{Guid.NewGuid():N}.{extension}");
        _tempFiles.Add(path);
        return path;
    }

    private void CleanupTempFiles()
    {
        foreach (var path in _tempFiles)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>Стереть содержимое картотеки, оставив предметы — проверка «импорт на чистой базе».</summary>
    private async Task ClearCardsDataAsync()
    {
        await using var context = CreateContext();
        context.CardTagLinks.RemoveRange(context.CardTagLinks);
        context.Cards.RemoveRange(context.Cards);
        context.CardDecks.RemoveRange(context.CardDecks);
        context.CardTags.RemoveRange(context.CardTags);
        await context.SaveChangesAsync();
    }

    // ---- JSON round-trip (DoD: «идентичный набор карточек/колод/меток») ----------------------

    [Fact]
    public async Task Json_round_trip_reproduces_cards_decks_and_tags()
    {
        try
        {
            var subjectId = await SeedSubjectAsync("Матан", "МА");
            var deckId = await SeedDeckAsync("К экзамену", subjectId);
            await SeedCardAsync("Интеграл", "Предел интегральных сумм.", subjectId, deckId, ["формулы", "анализ"]);
            await SeedCardAsync("Производная", "Скорость изменения функции.", subjectId, null, ["анализ"]);

            var path = TempFile("json");
            var exported = await ImportExport.ExportJsonAsync(path, new CardExportScope.All());
            Assert.True(exported.IsSuccess);
            Assert.Equal(2, exported.Value);

            await ClearCardsDataAsync();

            var preview = await ImportExport.PreviewJsonImportAsync(path, CardImportMode.Add, null);
            Assert.True(preview.IsSuccess);

            var summary = await ImportExport.CommitImportAsync(preview.Value);
            Assert.True(summary.IsSuccess);
            Assert.Equal(2, summary.Value.Created);
            Assert.Equal(0, summary.Value.Skipped);

            var restored = await CardRepo.GetAllAsync();
            Assert.Equal(2, restored.Count);

            var integral = restored.Single(c => c.Front == "Интеграл");
            Assert.Equal("Предел интегральных сумм.", integral.Back);
            Assert.Equal(subjectId, integral.SubjectId);

            var deck = Assert.Single(await DeckRepo.GetAllAsync());
            Assert.Equal("К экзамену", deck.Name);
            Assert.Equal(subjectId, deck.SubjectId);
            Assert.Equal(deck.Id, integral.DeckId);

            var derivative = restored.Single(c => c.Front == "Производная");
            Assert.Null(derivative.DeckId);

            var tagMap = await Tags.GetForCardsAsync(restored.Select(c => c.Id).ToList());
            var integralTags = tagMap[integral.Id].Select(t => t.Name).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.Equal(["анализ", "формулы"], integralTags);
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    [Fact]
    public async Task Json_import_leaves_a_card_unlinked_when_its_subject_is_not_found()
    {
        try
        {
            var path = TempFile("json");
            var json = CardsJson.Serialize(new CardsExportPayload(
                [new CardExportDto { Front = "Ф", Back = "О", SubjectName = "Несуществующий предмет" }], [], []));
            await File.WriteAllTextAsync(path, json);

            var preview = await ImportExport.PreviewJsonImportAsync(path, CardImportMode.Add, null);
            Assert.True(preview.IsSuccess);
            Assert.Contains(preview.Value.Warnings, w => w.Contains("Несуществующий предмет"));

            var summary = await ImportExport.CommitImportAsync(preview.Value);
            Assert.True(summary.IsSuccess);
            Assert.Equal(1, summary.Value.Created);

            var card = Assert.Single(await CardRepo.GetAllAsync());
            Assert.Null(card.SubjectId);
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    // ---- Merge: «было N, стало N, изменилось M» -----------------------------------------------

    [Fact]
    public async Task Merge_updates_matching_fronts_and_creates_the_rest()
    {
        try
        {
            var subjectId = await SeedSubjectAsync();
            await SeedCardAsync("Интеграл", "Старое определение.", subjectId, tags: ["старое"]);
            await SeedCardAsync("Производная", "Не трогать.", subjectId);

            var path = TempFile("json");
            var json = CardsJson.Serialize(new CardsExportPayload(
                [
                    new CardExportDto { Front = "Интеграл", Back = "Новое определение.", Tags = ["новое"] },
                    new CardExportDto { Front = "Ряд Тейлора", Back = "Разложение функции в ряд." },
                ],
                [],
                []));
            await File.WriteAllTextAsync(path, json);

            var before = await CardRepo.CountAsync();

            var preview = await ImportExport.PreviewJsonImportAsync(path, CardImportMode.Merge, null);
            Assert.True(preview.IsSuccess);
            Assert.Equal(1, preview.Value.Rows.Count(r => r.Action == CardImportRowAction.UpdateExisting));
            Assert.Equal(1, preview.Value.Rows.Count(r => r.Action == CardImportRowAction.Create));

            var summary = await ImportExport.CommitImportAsync(preview.Value);
            Assert.True(summary.IsSuccess);
            Assert.Equal(1, summary.Value.Updated);
            Assert.Equal(1, summary.Value.Created);

            var after = await CardRepo.CountAsync();
            Assert.Equal(before + 1, after);

            var updated = (await CardRepo.GetBySubjectAsync(subjectId)).Single(c => c.Front == "Интеграл");
            Assert.Equal("Новое определение.", updated.Back);

            var tagMap = await Tags.GetForCardsAsync([updated.Id]);
            var tagNames = tagMap[updated.Id].Select(t => t.Name).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.Equal(["новое", "старое"], tagNames);

            var untouched = (await CardRepo.GetBySubjectAsync(subjectId)).Single(c => c.Front == "Производная");
            Assert.Equal("Не трогать.", untouched.Back);
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    [Fact]
    public async Task Merge_matches_regardless_of_case_and_yo_e_and_whitespace()
    {
        try
        {
            await SeedCardAsync("Ёмкость   конденсатора", "Старое.");

            var path = TempFile("json");
            await File.WriteAllTextAsync(
                path,
                CardsJson.Serialize(new CardsExportPayload(
                    [new CardExportDto { Front = "емкость конденсатора", Back = "Новое." }], [], [])));

            var preview = await ImportExport.PreviewJsonImportAsync(path, CardImportMode.Merge, null);
            Assert.Equal(CardImportRowAction.UpdateExisting, Assert.Single(preview.Value.Rows).Action);
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    // ---- CSV: кодировки --------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Csv_utf8_with_and_without_bom_parses_identically(bool withBom)
    {
        try
        {
            const string csv = "Front;Back;Tags;Deck\r\nИнтеграл;Предел сумм;формулы;\r\n";
            var path = TempFile("csv");
            await File.WriteAllTextAsync(path, csv, new UTF8Encoding(withBom));

            var sniff = await ImportExport.SniffCsvAsync(path);
            Assert.True(sniff.IsSuccess);
            Assert.Equal(';', sniff.Value.Delimiter);
            Assert.True(sniff.Value.HasHeader);

            var preview = await ImportExport.PreviewCsvImportAsync(
                path, ';', sniff.Value.Encoding, true, CardImportMode.Add, null);
            Assert.True(preview.IsSuccess);
            Assert.Equal("Интеграл", Assert.Single(preview.Value.Rows).Front);
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    [Fact]
    public async Task Csv_windows1251_without_bom_is_detected_and_parsed()
    {
        try
        {
            const string csv = "Front;Back;Tags;Deck\r\nИнтеграл;Предел сумм;формулы;\r\n";
            var path = TempFile("csv");
            var cp1251 = Encoding.GetEncoding(1251);
            await File.WriteAllBytesAsync(path, cp1251.GetBytes(csv));

            var sniff = await ImportExport.SniffCsvAsync(path);
            Assert.True(sniff.IsSuccess);
            Assert.Equal(1251, sniff.Value.Encoding.CodePage);

            var preview = await ImportExport.PreviewCsvImportAsync(
                path, sniff.Value.Delimiter, sniff.Value.Encoding, sniff.Value.HasHeader, CardImportMode.Add, null);
            Assert.True(preview.IsSuccess);
            Assert.Equal("Интеграл", Assert.Single(preview.Value.Rows).Front);
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    [Fact]
    public async Task Csv_tab_delimiter_is_supported()
    {
        try
        {
            const string csv = "Front\tBack\tTags\tDeck\r\nИнтеграл\tОпределение\t\t\r\n";
            var path = TempFile("csv");
            await File.WriteAllTextAsync(path, csv, new UTF8Encoding(false));

            var sniff = await ImportExport.SniffCsvAsync(path);
            Assert.True(sniff.IsSuccess);
            Assert.Equal('\t', sniff.Value.Delimiter);
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    [Fact]
    public async Task Csv_quoted_fields_with_delimiters_and_newlines_parse_correctly()
    {
        try
        {
            const string csv =
                "Front;Back;Tags;Deck\r\n\"Тест; с точкой с запятой\";\"Многострочный\nответ\";\"метка1,метка2\";\r\n";
            var path = TempFile("csv");
            await File.WriteAllTextAsync(path, csv, new UTF8Encoding(false));

            var preview = await ImportExport.PreviewCsvImportAsync(
                path, ';', new UTF8Encoding(false), true, CardImportMode.Add, null);

            Assert.True(preview.IsSuccess);
            var row = Assert.Single(preview.Value.Rows);
            Assert.Equal("Тест; с точкой с запятой", row.Front);
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    // ---- Битые файлы: без исключений и без частичной записи -----------------------------------

    [Fact]
    public async Task Foreign_json_is_rejected_without_writing_anything()
    {
        try
        {
            var path = TempFile("json");
            await File.WriteAllTextAsync(path, "{ \"schema\": \"not.ours\" }");

            var before = await CardRepo.CountAsync();
            var preview = await ImportExport.PreviewJsonImportAsync(path, CardImportMode.Add, null);

            Assert.True(preview.IsFailure);
            Assert.Equal(before, await CardRepo.CountAsync());
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    [Fact]
    public async Task Garbage_json_is_rejected_without_throwing()
    {
        try
        {
            var path = TempFile("json");
            await File.WriteAllTextAsync(path, "это не json вовсе {{{");

            var preview = await ImportExport.PreviewJsonImportAsync(path, CardImportMode.Add, null);

            Assert.True(preview.IsFailure);
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    [Fact]
    public async Task A_csv_row_with_an_unterminated_quote_is_skipped_with_a_reason_not_an_exception()
    {
        try
        {
            const string csv =
                "Front;Back;Tags;Deck\r\nХорошая;Строка;;\r\n\"Незакрытая;и весь остаток файла\r\nвторая часть";
            var path = TempFile("csv");
            await File.WriteAllTextAsync(path, csv, new UTF8Encoding(false));

            var preview = await ImportExport.PreviewCsvImportAsync(
                path, ';', new UTF8Encoding(false), true, CardImportMode.Add, null);

            Assert.True(preview.IsSuccess);
            Assert.Contains(
                preview.Value.Rows,
                r => r.Action == CardImportRowAction.Skip && r.Reason != null && r.Reason.Contains("кавычка"));
            // Первая, честная строка всё равно попадает в план.
            Assert.Contains(preview.Value.Rows, r => r.Front == "Хорошая" && r.Action == CardImportRowAction.Create);
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    // ---- ReplaceDeck: детерминизм независимо от файла -----------------------------------------

    [Fact]
    public async Task Replace_deck_without_a_chosen_target_is_refused()
    {
        try
        {
            var path = TempFile("json");
            await File.WriteAllTextAsync(path, CardsJson.Serialize(new CardsExportPayload([], [], [])));

            var preview = await ImportExport.PreviewJsonImportAsync(path, CardImportMode.ReplaceDeck, null);

            Assert.True(preview.IsFailure);
            Assert.Equal("cards.import_target_deck_required", preview.Error.Code);
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    [Fact]
    public async Task Replace_deck_reassigns_cards_regardless_of_the_files_deck_column()
    {
        try
        {
            var subjectId = await SeedSubjectAsync();
            var targetDeckId = await SeedDeckAsync("Экзамен", subjectId);
            await SeedDeckAsync("Коллоквиум", subjectId);
            var existingInTarget = await SeedCardAsync("Старая", "Оборот", subjectId, targetDeckId);

            var path = TempFile("json");
            var json = CardsJson.Serialize(new CardsExportPayload(
                [
                    new CardExportDto { Front = "Новая1", Back = "О1", DeckName = "Коллоквиум" },
                    new CardExportDto { Front = "Новая2", Back = "О2", DeckName = null },
                ],
                [],
                []));
            await File.WriteAllTextAsync(path, json);

            var preview = await ImportExport.PreviewJsonImportAsync(path, CardImportMode.ReplaceDeck, targetDeckId);
            Assert.True(preview.IsSuccess);
            Assert.Contains(preview.Value.Warnings, w => w.Contains("независимо"));

            var summary = await ImportExport.CommitImportAsync(preview.Value);
            Assert.True(summary.IsSuccess);
            Assert.Equal(2, summary.Value.Created);

            var targetCards = await CardRepo.GetByDeckAsync(targetDeckId);
            Assert.Equal(2, targetCards.Count);
            Assert.All(targetCards, c => Assert.Contains(c.Front, new[] { "Новая1", "Новая2" }));

            var oldCardAfter = await CardRepo.GetByIdAsync(existingInTarget);
            Assert.NotNull(oldCardAfter);
            Assert.Null(oldCardAfter!.DeckId);
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    // ---- Отмена на границе строки --------------------------------------------------------------

    [Fact]
    public async Task Cancelling_commit_stops_at_the_row_boundary_without_partial_rows()
    {
        try
        {
            var payload = new CardsExportPayload(
                Enumerable.Range(1, 5).Select(i => new CardExportDto { Front = $"К{i}", Back = "О" }).ToList(),
                [],
                []);
            var path = TempFile("json");
            await File.WriteAllTextAsync(path, CardsJson.Serialize(payload));

            var preview = await ImportExport.PreviewJsonImportAsync(path, CardImportMode.Add, null);
            Assert.True(preview.IsSuccess);

            using var cts = new CancellationTokenSource();
            var progress = new SyncProgress<CardImportProgress>(p =>
            {
                if (p.Processed == 2)
                {
                    cts.Cancel();
                }
            });

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => ImportExport.CommitImportAsync(preview.Value, progress, cts.Token));

            Assert.Equal(2, await CardRepo.CountAsync());
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    // ---- Область экспорта -----------------------------------------------------------------------

    [Fact]
    public async Task Export_by_selection_takes_exactly_the_given_cards()
    {
        try
        {
            var a = await SeedCardAsync("А", "оА");
            await SeedCardAsync("Б", "оБ");

            var path = TempFile("json");
            var exported = await ImportExport.ExportJsonAsync(path, new CardExportScope.Selection([a]));

            Assert.True(exported.IsSuccess);
            Assert.Equal(1, exported.Value);
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    [Fact]
    public async Task Export_by_deck_includes_only_that_decks_cards()
    {
        try
        {
            var subjectId = await SeedSubjectAsync();
            var deckId = await SeedDeckAsync("Колода", subjectId);
            await SeedCardAsync("В колоде", "о1", subjectId, deckId);
            await SeedCardAsync("Вне колоды", "о2", subjectId);

            var path = TempFile("json");
            var exported = await ImportExport.ExportJsonAsync(path, new CardExportScope.Deck(deckId));

            Assert.True(exported.IsSuccess);
            Assert.Equal(1, exported.Value);
        }
        finally
        {
            CleanupTempFiles();
        }
    }

    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
