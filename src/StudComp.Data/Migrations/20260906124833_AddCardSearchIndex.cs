using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudComp.Data.Migrations
{
    /// <summary>
    /// Полнотекстовый индекс картотеки: виртуальная таблица FTS5 и триггеры, держащие её в синхроне
    /// (new_addons.md §4.2). Единственная миграция проекта с сырым SQL — отдельной строкой в истории
    /// именно затем, чтобы это было видно.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Почему триггеры, а не поддержка индекса кодом репозитория: EF пишет в <c>Cards</c> тремя
    /// разными путями (<c>SaveChanges</c>, <c>ExecuteUpdate</c>, <c>ExecuteDelete</c>), и любой обход
    /// тихо разъехался бы с индексом. Триггер живёт ниже EF, ему всё равно, кто именно написал строку.
    /// </para>
    /// <para>
    /// Строка индекса привязана к <c>Cards.rowid</c>: обслуживание становится точечным, а не
    /// сканированием индекса на каждую правку метки. Обратная связь для выдачи — колонка
    /// <c>CardId</c>. Мягко удалённые карточки из индекса не выпиливаются: их отсекает <c>JOIN</c>
    /// с <c>Cards.DeletedAt IS NULL</c> в самом запросе — на одну ветку триггера меньше, и это же
    /// бесплатно даёт поиск внутри «Корзины».
    /// </para>
    /// <para>
    /// <b>Важно для будущих миграций.</b> Любая миграция, пересобирающая таблицу <c>Cards</c>,
    /// <c>CardDecks</c> или <c>Subjects</c> (SQLite-ребилд, который EF делает при изменении колонок
    /// или FK — в проекте он уже случался в <c>AddSemester</c>), снесёт эти триггеры вместе со старой
    /// таблицей и обязана пересоздать их в своём <c>Up()</c>. Против забывчивости стоит тест-страж
    /// <c>CardSearchIndexTests</c>.
    /// </para>
    /// </remarks>
    public partial class AddCardSearchIndex : Migration
    {
        /// <summary>
        /// Складывает «ё» с «е». Токенайзер <c>unicode61</c> с <c>remove_diacritics=2</c> честно
        /// складывает регистр кириллицы, но «ё» отдельной буквой и остаётся — проверено замером на
        /// той самой сборке SQLite, которая едет с приложением. Для русского это обязательное
        /// поведение, иначе «емкость» не находит «ёмкость», поэтому складываем руками — и здесь, и
        /// в выражении запроса.
        /// </summary>
        private static string Fold(string expression) =>
            $"replace(replace({expression}, 'ё', 'е'), 'Ё', 'Е')";

        /// <summary>Метки карточки, склеенные через пробел. Псевдоним карточки — <c>c</c>.</summary>
        private const string TagsExpression = """
            COALESCE((SELECT group_concat(t.Name, ' ')
                        FROM CardTagLinks l
                        JOIN CardTags t ON t.Id = l.TagId
                       WHERE l.CardId = c.Id), '')
            """;

        /// <summary>
        /// Хвост с низким весом: подсказка, источник, название колоды и предмета. Псевдоним — <c>c</c>.
        /// </summary>
        private const string ExtraExpression = """
            COALESCE(c.Hint, '') || ' ' || COALESCE(c.Source, '') || ' '
              || COALESCE((SELECT d.Name FROM CardDecks d WHERE d.Id = c.DeckId), '') || ' '
              || COALESCE((SELECT s.Name || ' ' || s.Code FROM Subjects s WHERE s.Id = c.SubjectId), '')
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // prefix='2 3 4' — поиск живёт со второй буквы; remove_diacritics=2 складывает регистр
            // кириллицы. «Ё» токенайзер отдельной буквой оставляет, поэтому её складывает Fold().
            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE CardSearch USING fts5(
                    Front,
                    Back,
                    Tags,
                    Extra,
                    CardId UNINDEXED,
                    tokenize = 'unicode61 remove_diacritics 2',
                    prefix = '2 3 4');
                """);

            migrationBuilder.Sql($"""
                CREATE TRIGGER Cards_ai AFTER INSERT ON Cards BEGIN
                    INSERT INTO CardSearch(rowid, Front, Back, Tags, Extra, CardId)
                    SELECT c.rowid, {Fold("c.Front")}, {Fold("c.Back")}, {Fold(TagsExpression)}, {Fold(ExtraExpression)}, c.Id
                      FROM Cards c WHERE c.rowid = new.rowid;
                END;
                """);

            migrationBuilder.Sql($"""
                CREATE TRIGGER Cards_au AFTER UPDATE ON Cards BEGIN
                    DELETE FROM CardSearch WHERE rowid = old.rowid;
                    INSERT INTO CardSearch(rowid, Front, Back, Tags, Extra, CardId)
                    SELECT c.rowid, {Fold("c.Front")}, {Fold("c.Back")}, {Fold(TagsExpression)}, {Fold(ExtraExpression)}, c.Id
                      FROM Cards c WHERE c.rowid = new.rowid;
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER Cards_ad AFTER DELETE ON Cards BEGIN
                    DELETE FROM CardSearch WHERE rowid = old.rowid;
                END;
                """);

            // Метку повесили или сняли — пересобираем колонку Tags затронутой карточки. Если карточки
            // уже нет (сработал каскад при её удалении), SELECT ничего не вернёт и вставки не будет.
            migrationBuilder.Sql($"""
                CREATE TRIGGER CardTagLinks_ai AFTER INSERT ON CardTagLinks BEGIN
                    DELETE FROM CardSearch WHERE rowid IN (SELECT rowid FROM Cards WHERE Id = new.CardId);
                    INSERT INTO CardSearch(rowid, Front, Back, Tags, Extra, CardId)
                    SELECT c.rowid, {Fold("c.Front")}, {Fold("c.Back")}, {Fold(TagsExpression)}, {Fold(ExtraExpression)}, c.Id
                      FROM Cards c WHERE c.Id = new.CardId;
                END;
                """);

            migrationBuilder.Sql($"""
                CREATE TRIGGER CardTagLinks_ad AFTER DELETE ON CardTagLinks BEGIN
                    DELETE FROM CardSearch WHERE rowid IN (SELECT rowid FROM Cards WHERE Id = old.CardId);
                    INSERT INTO CardSearch(rowid, Front, Back, Tags, Extra, CardId)
                    SELECT c.rowid, {Fold("c.Front")}, {Fold("c.Back")}, {Fold(TagsExpression)}, {Fold(ExtraExpression)}, c.Id
                      FROM Cards c WHERE c.Id = old.CardId;
                END;
                """);

            // Переименование колоды и предмета меняет колонку Extra их карточек. Без этих двух
            // триггеров индекс молча расходился бы с данными, а предмет переименовывает Органайзер —
            // модуль, до которого Картотека не дотягивается по своим границам.
            migrationBuilder.Sql($"""
                CREATE TRIGGER CardDecks_au AFTER UPDATE OF Name ON CardDecks
                WHEN old.Name IS NOT new.Name
                BEGIN
                    DELETE FROM CardSearch WHERE rowid IN (SELECT rowid FROM Cards WHERE DeckId = new.Id);
                    INSERT INTO CardSearch(rowid, Front, Back, Tags, Extra, CardId)
                    SELECT c.rowid, {Fold("c.Front")}, {Fold("c.Back")}, {Fold(TagsExpression)}, {Fold(ExtraExpression)}, c.Id
                      FROM Cards c WHERE c.DeckId = new.Id;
                END;
                """);

            migrationBuilder.Sql($"""
                CREATE TRIGGER Subjects_au_cards AFTER UPDATE OF Name, Code ON Subjects
                WHEN old.Name IS NOT new.Name OR old.Code IS NOT new.Code
                BEGIN
                    DELETE FROM CardSearch WHERE rowid IN (SELECT rowid FROM Cards WHERE SubjectId = new.Id);
                    INSERT INTO CardSearch(rowid, Front, Back, Tags, Extra, CardId)
                    SELECT c.rowid, {Fold("c.Front")}, {Fold("c.Back")}, {Fold(TagsExpression)}, {Fold(ExtraExpression)}, c.Id
                      FROM Cards c WHERE c.SubjectId = new.Id;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Subjects_au_cards;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS CardDecks_au;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS CardTagLinks_ad;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS CardTagLinks_ai;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Cards_ad;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Cards_au;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Cards_ai;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS CardSearch;");
        }
    }
}
