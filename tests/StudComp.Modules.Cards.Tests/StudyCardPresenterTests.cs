using StudComp.Core.Domain;
using StudComp.Modules.Cards.Services;

namespace StudComp.Modules.Cards.Tests;

/// <summary>
/// Автомат состояния карточки в сессии (new_addons.md §5.1, Phase 12.8).
/// </summary>
/// <remarks>
/// Здесь закрываются два жёстких пункта DoD: ответ невозможно увидеть до раскрытия, и оценку
/// невозможно поставить до него же. Автомат вынесен из вьюмодели в модуль именно ради этих тестов —
/// проекта <c>StudComp.App.Tests</c> в решении нет.
/// </remarks>
public sealed class StudyCardPresenterTests
{
    private const string Answer = "Связывает поток ротора через поверхность и циркуляцию по контуру.";

    private static StudyCardView View(
        StudyCheckMode mode = StudyCheckMode.SelfAssessment,
        string? hint = null,
        string? clozeSource = null,
        IReadOnlyList<string>? options = null) =>
        new(
            new Card { Id = Guid.NewGuid(), Front = "Теорема Стокса", Back = Answer },
            "Теорема Стокса",
            clozeSource ?? Answer,
            hint,
            mode,
            options ?? [],
            options is null ? -1 : 0,
            clozeSource is null ? null : ClozeParser.Parse(clozeSource),
            [],
            null,
            null,
            []);

    [Fact]
    public void The_answer_does_not_exist_until_the_card_is_revealed()
    {
        var presenter = new StudyCardPresenter(View());

        Assert.Equal(StudyCardState.Question, presenter.State);
        Assert.Equal(string.Empty, presenter.VisibleAnswer);

        presenter.Reveal();

        Assert.Equal(Answer, presenter.VisibleAnswer);
    }

    [Fact]
    public void Grading_is_impossible_before_the_answer_is_revealed()
    {
        var presenter = new StudyCardPresenter(View());

        Assert.False(presenter.CanGrade);
        Assert.True(presenter.CanReveal);

        presenter.Reveal();

        Assert.True(presenter.CanGrade);
        Assert.False(presenter.CanReveal);
    }

    [Fact]
    public void The_question_is_visible_from_the_start()
    {
        Assert.Equal("Теорема Стокса", new StudyCardPresenter(View()).Question);
    }

    [Fact]
    public void Options_of_a_self_assessed_card_stay_hidden_until_the_reveal()
    {
        var presenter = new StudyCardPresenter(View(options: ["а", "б", "в", "г"]));

        Assert.Empty(presenter.VisibleOptions);

        presenter.Reveal();

        Assert.Equal(4, presenter.VisibleOptions.Count);
    }

    [Fact]
    public void Options_of_a_multiple_choice_card_are_the_question_itself()
    {
        // В режиме «выбор варианта» сам выбор и есть способ проверки — прятать варианты бессмысленно.
        var presenter = new StudyCardPresenter(
            View(StudyCheckMode.MultipleChoice, options: ["а", "б", "в", "г"]));

        Assert.Equal(4, presenter.VisibleOptions.Count);
        Assert.Equal(string.Empty, presenter.VisibleAnswer);
    }

    [Fact]
    public void A_hint_does_not_reveal_the_answer()
    {
        var presenter = new StudyCardPresenter(View(hint: "Через ротор."));

        Assert.True(presenter.HasHint);
        Assert.Equal(string.Empty, presenter.VisibleHint);

        presenter.ShowHint();

        Assert.True(presenter.HintUsed);
        Assert.Equal("Через ротор.", presenter.VisibleHint);
        Assert.Equal(string.Empty, presenter.VisibleAnswer);
        Assert.Equal(StudyCardState.Question, presenter.State);
    }

    [Fact]
    public void Asking_for_a_hint_that_does_not_exist_changes_nothing()
    {
        var presenter = new StudyCardPresenter(View());

        presenter.ShowHint();

        Assert.False(presenter.HasHint);
        Assert.False(presenter.HintUsed);
    }

    [Fact]
    public void Gaps_are_revealed_one_at_a_time()
    {
        var presenter = new StudyCardPresenter(
            View(StudyCheckMode.Cloze, clozeSource: "{{Поток}} ротора равен {{циркуляции}}"));

        Assert.Equal("… ротора равен …", presenter.VisibleCloze);

        presenter.RevealNextGap();
        Assert.Equal("Поток ротора равен …", presenter.VisibleCloze);

        presenter.RevealNextGap();
        Assert.Equal("Поток ротора равен циркуляции", presenter.VisibleCloze);
        Assert.False(presenter.HasHiddenGaps);

        // Лишний вызов не ломается и не уходит за границу.
        presenter.RevealNextGap();
        Assert.Equal(2, presenter.RevealedGaps);
    }

    [Fact]
    public void Revealing_the_card_opens_every_remaining_gap()
    {
        var presenter = new StudyCardPresenter(
            View(StudyCheckMode.Cloze, clozeSource: "{{а}} и {{б}} и {{в}}"));

        presenter.Reveal();

        Assert.Equal(3, presenter.RevealedGaps);
        Assert.Equal("а и б и в", presenter.VisibleCloze);
    }

    [Fact]
    public void Revealing_twice_is_harmless()
    {
        var presenter = new StudyCardPresenter(View());

        presenter.Reveal();
        presenter.Reveal();

        Assert.Equal(StudyCardState.Revealed, presenter.State);
        Assert.True(presenter.CanGrade);
    }

    [Fact]
    public void A_card_cannot_be_graded_twice()
    {
        var presenter = new StudyCardPresenter(View());

        presenter.Reveal();
        presenter.MarkGraded();

        Assert.Equal(StudyCardState.Graded, presenter.State);
        Assert.False(presenter.CanGrade);
        Assert.False(presenter.CanReveal);

        presenter.MarkGraded();
        Assert.Equal(StudyCardState.Graded, presenter.State);
    }

    [Fact]
    public void The_answer_stays_visible_after_grading()
    {
        var presenter = new StudyCardPresenter(View());

        presenter.Reveal();
        presenter.MarkGraded();

        Assert.Equal(Answer, presenter.VisibleAnswer);
    }
}
