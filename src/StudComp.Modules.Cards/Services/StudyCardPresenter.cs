using StudComp.Core.Domain;

namespace StudComp.Modules.Cards.Services;

/// <summary>
/// Конечный автомат карточки в сессии (new_addons.md §5.1). Держит состояние одной показываемой
/// карточки и решает, что вообще можно увидеть и нажать прямо сейчас.
/// </summary>
/// <remarks>
/// <para>
/// Главное правило раздела: <b>ответа не существует</b>, пока карточка не раскрыта. Не «скрыт
/// стилем», не «прозрачен», не «ниже по прокрутке» — <see cref="VisibleAnswer"/> возвращает пустую
/// строку, и разметке нечего показать даже при большом желании. Оценка недоступна до раскрытия по
/// той же причине: иначе её можно проставить не глядя.
/// </para>
/// <para>
/// Автомат живёт в модуле, а не во вьюмодели, ровно затем, чтобы эти два правила проверялись
/// машинно: проекта <c>StudComp.App.Tests</c> в решении нет, и на UI-слое такой тест повис бы.
/// Вьюмодель экрана — тонкая обёртка над этим классом.
/// </para>
/// </remarks>
public sealed class StudyCardPresenter
{
    private readonly StudyCardView _view;

    public StudyCardPresenter(StudyCardView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        _view = view;
    }

    /// <summary>Текущее состояние карточки.</summary>
    public StudyCardState State { get; private set; } = StudyCardState.Question;

    /// <summary>Вопрос — он виден всегда.</summary>
    public string Question => _view.QuestionText;

    /// <summary>Способ проверки, фактически применённый к этой карточке.</summary>
    public StudyCheckMode CheckMode => _view.CheckMode;

    /// <summary>Есть ли подсказка.</summary>
    public bool HasHint => !string.IsNullOrWhiteSpace(_view.Hint);

    /// <summary>Подсказку уже смотрели — итоги честнее, когда это видно.</summary>
    public bool HintUsed { get; private set; }

    /// <summary>Текст подсказки; пусто, пока её не попросили.</summary>
    public string VisibleHint => HintUsed ? _view.Hint ?? string.Empty : string.Empty;

    /// <summary>
    /// Оборот карточки. Пустая строка, пока состояние — <see cref="StudyCardState.Question"/>.
    /// </summary>
    public string VisibleAnswer => State == StudyCardState.Question ? string.Empty : _view.AnswerText;

    /// <summary>
    /// Варианты ответа для теста. До раскрытия они видны только в режиме «выбор варианта» — там сам
    /// выбор и есть способ проверки; в остальных режимах показывать нечего.
    /// </summary>
    public IReadOnlyList<string> VisibleOptions =>
        _view.CheckMode == StudyCheckMode.MultipleChoice || State != StudyCardState.Question
            ? _view.Options
            : [];

    /// <summary>Сколько пропусков уже раскрыто (режим «пропуски»).</summary>
    public int RevealedGaps { get; private set; }

    /// <summary>Текст с пропусками в текущем состоянии раскрытия.</summary>
    public string VisibleCloze => _view.Cloze is null
        ? string.Empty
        : _view.Cloze.ToMaskedText(State == StudyCardState.Question ? RevealedGaps : _view.Cloze.GapCount);

    /// <summary>Можно ли раскрыть ответ.</summary>
    public bool CanReveal => State == StudyCardState.Question;

    /// <summary>Можно ли поставить оценку. До раскрытия — нельзя, и это не косметика.</summary>
    public bool CanGrade => State == StudyCardState.Revealed;

    /// <summary>Осталось ли что раскрывать в режиме пропусков.</summary>
    public bool HasHiddenGaps => _view.Cloze is not null && RevealedGaps < _view.Cloze.GapCount;

    /// <summary>Показать подсказку. Ответ она не раскрывает.</summary>
    public void ShowHint()
    {
        if (HasHint)
        {
            HintUsed = true;
        }
    }

    /// <summary>Раскрыть следующий пропуск.</summary>
    public void RevealNextGap()
    {
        if (HasHiddenGaps)
        {
            RevealedGaps++;
        }
    }

    /// <summary>Раскрыть ответ. Повторный вызов ничего не меняет.</summary>
    public void Reveal()
    {
        if (State != StudyCardState.Question)
        {
            return;
        }

        State = StudyCardState.Revealed;
        RevealedGaps = _view.Cloze?.GapCount ?? 0;
    }

    /// <summary>Отметить, что оценка поставлена. Дважды оценить одну карточку нельзя.</summary>
    public void MarkGraded()
    {
        if (State == StudyCardState.Revealed)
        {
            State = StudyCardState.Graded;
        }
    }
}
