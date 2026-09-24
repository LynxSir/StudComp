using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Controls;
using StudComp.Core.Abstractions.Organizer;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.Organizer.Services;
using StudComp.Modules.Organizer.Services.ForecastStrategies;
using StudComp.Services;

namespace StudComp.ViewModels.Organizer;

/// <summary>Вкладка «Зачётка»: список оценок по предмету + прогноз итога и обратная задача
/// «сколько нужно набрать на оставшемся» (ARCHITECTURE §9.4).</summary>
public sealed partial class GradeBookViewModel(
    IGradeBookService grades,
    ISubjectService subjects,
    IDialogService dialogs,
    IToastService toasts) : ObservableObject
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>Ключ стратегии «линейная регрессия» — совпадает с <c>OrganizerChoices.ForecastStrategies</c>.</summary>
    private const string LinearRegressionKey = "linear-regression";

    private bool _suppressReload;

    /// <summary>Строки зачётки выбранного предмета — источник для графика тренда.</summary>
    private IReadOnlyList<GradeEntry> _entries = [];

    public ObservableCollection<Subject> Subjects { get; } = [];

    public ObservableCollection<GradeRowViewModel> Items { get; } = [];

    [ObservableProperty]
    private Subject? _selectedSubject;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWarning))]
    [NotifyPropertyChangedFor(nameof(HasTargetGrade))]
    private string _targetGradeText = string.Empty;

    [ObservableProperty]
    private bool _hasSubjects;

    /// <summary>
    /// Показывать ли пикер предмета. В разделе «Зачётка» — да; в Хабе предмета зачётка открыта на
    /// одном фиксированном предмете, и выбирать там нечего (new_addons.md §5).
    /// </summary>
    [ObservableProperty]
    private bool _isSubjectPickerVisible = true;

    [ObservableProperty]
    private bool _hasForecast;

    [ObservableProperty]
    private string _predictedScoreText = string.Empty;

    [ObservableProperty]
    private string _scaleHintText = string.Empty;

    [ObservableProperty]
    private string _confidenceText = string.Empty;

    [ObservableProperty]
    private string _passVerdictText = string.Empty;

    [ObservableProperty]
    private string _requiredScoreText = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWarning))]
    private bool _isTargetAchievable = true;

    /// <summary>Данные графика тренда оценок; <see langword="null"/> — рисовать нечего.</summary>
    [ObservableProperty]
    private GradeTrendData? _trendData;

    /// <summary>Показывать карточку с графиком: есть прогноз и хотя бы две оценённые строки.</summary>
    [ObservableProperty]
    private bool _showTrendChart;

    /// <summary>Фраза «Средневзвешенная … · линейная регрессия …» под прогнозом (DoD Phase 11).</summary>
    [ObservableProperty]
    private string _strategyComparisonText = string.Empty;

    /// <summary>Показывать честное «уже не дотянуть»: цель задана и по прогнозу недостижима.</summary>
    public bool ShowWarning => HasForecast && !IsTargetAchievable && ParseTarget(TargetGradeText) is not null;

    /// <summary>Целевая оценка уже введена — блок с ней стоит показывать раскрытым (Phase 13.9).</summary>
    public bool HasTargetGrade => !string.IsNullOrWhiteSpace(TargetGradeText);

    partial void OnSelectedSubjectChanged(Subject? value)
    {
        if (!_suppressReload)
        {
            _ = ReloadForSubjectAsync();
        }
    }

    partial void OnTargetGradeTextChanged(string value)
    {
        if (!_suppressReload)
        {
            _ = RecomputeForecastAsync();
        }
    }

    /// <summary>Перечитать предметы (сохранив выбранный) и данные по нему. Зовётся при показе вкладки.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        var all = await subjects.GetAllAsync();
        var keepId = SelectedSubject?.Id;

        _suppressReload = true;
        Subjects.Clear();
        foreach (var subject in all)
        {
            Subjects.Add(subject);
        }

        HasSubjects = Subjects.Count > 0;
        SelectedSubject = Subjects.FirstOrDefault(s => s.Id == keepId) ?? Subjects.FirstOrDefault();
        _suppressReload = false;

        await ReloadForSubjectAsync();
    }

    /// <summary>
    /// Режим Хаба: зачётка одного предмета без пикера. Вся остальная механика (список, прогноз,
    /// обратная задача, график тренда) переиспользуется как есть.
    /// </summary>
    public async Task LockToSubjectAsync(Guid subjectId)
    {
        IsSubjectPickerVisible = false;

        var subject = (await subjects.GetAllAsync()).FirstOrDefault(s => s.Id == subjectId);

        _suppressReload = true;
        Subjects.Clear();
        if (subject is not null)
        {
            Subjects.Add(subject);
        }

        HasSubjects = Subjects.Count > 0;
        SelectedSubject = subject;
        _suppressReload = false;

        await ReloadForSubjectAsync();
    }

    private async Task ReloadForSubjectAsync()
    {
        IsBusy = true;
        try
        {
            if (SelectedSubject is null)
            {
                Items.Clear();
                _entries = [];
                HasForecast = false;
                ShowTrendChart = false;
                TrendData = null;
                StrategyComparisonText = string.Empty;
                return;
            }

            // Список заменяется одним синхронным блоком после await: запросы идут на пуле, и
            // наложившиеся перезагрузки (смена предмета + показ вкладки) иначе давали бы дубли.
            var entries = await grades.GetBySubjectAsync(SelectedSubject.Id);
            _entries = entries;
            Items.Clear();
            foreach (var entry in entries.OrderByDescending(e => e.IsPlanned).ThenBy(e => e.Date))
            {
                Items.Add(new GradeRowViewModel(entry));
            }

            await RecomputeForecastAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RecomputeForecastAsync()
    {
        if (SelectedSubject is null)
        {
            HasForecast = false;
            return;
        }

        var target = ParseTarget(TargetGradeText);
        var result = await grades.ForecastAsync(SelectedSubject.Id, target);
        if (result.IsFailure)
        {
            HasForecast = false;
            ShowTrendChart = false;
            TrendData = null;
            StrategyComparisonText = string.Empty;
            toasts.Show("Не удалось посчитать прогноз", result.Error.Message, ToastKind.Error);
            return;
        }

        var forecast = result.Value;
        var scale = ScaleFor(SelectedSubject);

        PredictedScoreText = forecast.PredictedFinalScore.ToString("0.##", Ru);
        ScaleHintText = $"из {scale.Max.ToString("0.##", Ru)}";
        ConfidenceText = forecast.Confidence switch
        {
            ConfidenceLevel.High => "высокая",
            ConfidenceLevel.Medium => "средняя",
            _ => "низкая",
        };
        PassVerdictText = forecast.PredictedFinalScore >= scale.PassThreshold
            ? "прогноз — сдаёт"
            : "прогноз — не сдаёт";
        RequiredScoreText = forecast.RequiredScoreOnNextAssessment is { } required
            ? required.ToString("0.##", Ru)
            : "—";
        IsTargetAchievable = forecast.IsAchievable;
        HasForecast = true;

        await UpdateTrendAndComparisonAsync(SelectedSubject, forecast, scale);
    }

    /// <summary>
    /// Строит данные графика тренда из загруженных оценок и, если данных достаточно, фразу сравнения
    /// стратегий (считает прогноз обеими стратегиями через <see cref="IGradeBookService.ForecastWithAsync"/>).
    /// </summary>
    private async Task UpdateTrendAndComparisonAsync(Subject subject, ForecastResult forecast, GradeScale scale)
    {
        var graded = _entries.Where(e => !e.IsPlanned).OrderBy(e => e.Date).ToList();
        if (graded.Count < 2)
        {
            ShowTrendChart = false;
            TrendData = null;
            StrategyComparisonText = string.Empty;
            return;
        }

        var planned = _entries.Where(e => e.IsPlanned).OrderBy(e => e.Date).ToList();
        var first = graded[0].Date;
        var span = scale.Max - scale.Min;
        var passFraction = scale.Max > 0m ? (double)(scale.PassThreshold / scale.Max) : 0d;

        static double Fraction(GradeEntry e) =>
            e.MaxScore > 0m ? (double)Math.Clamp(e.RawScore / e.MaxScore, 0m, 1m) : 0d;

        var gradedPoints = graded
            .Select(e => new TrendPoint(
                (e.Date - first).TotalDays,
                Fraction(e),
                IsPass: passFraction > 0d && Fraction(e) >= passFraction))
            .ToList();
        var pendingPoints = planned
            .Select(e => new TrendPoint((e.Date - first).TotalDays, 0d, IsPass: false))
            .ToList();

        var xs = gradedPoints.Select(p => p.TimeDays).ToList();
        var ys = gradedPoints.Select(p => p.Fraction).ToList();
        var ws = graded.Select(e => (double)Math.Max(0m, e.Weight)).ToList();
        var fit = LinearRegression.Fit(xs, ys, ws);

        var totalWeight = ws.Sum();
        var weightedMean = totalWeight > 0d
            ? ys.Zip(ws, (f, w) => f * w).Sum() / totalWeight
            : ys.Average();

        var predictedFraction = span > 0m
            ? (double)((forecast.PredictedFinalScore - scale.Min) / span)
            : 0d;
        var lastDate = pendingPoints.Count > 0 ? planned[^1].Date : graded[^1].Date;

        TrendData = new GradeTrendData(
            gradedPoints,
            pendingPoints,
            fit,
            weightedMean,
            predictedFraction,
            passFraction,
            first.ToString("dd.MM.yy", Ru),
            lastDate.ToString("dd.MM.yy", Ru),
            $"прогноз {forecast.PredictedFinalScore.ToString("0.##", Ru)}");
        ShowTrendChart = true;

        var weighted = await grades.ForecastWithAsync(subject.Id, string.Empty);
        var linear = await grades.ForecastWithAsync(subject.Id, LinearRegressionKey);
        StrategyComparisonText = weighted.IsSuccess && linear.IsSuccess
            ? ForecastComparison.Describe(weighted.Value, linear.Value, scale, fit.Slope)
            : string.Empty;
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (SelectedSubject is null)
        {
            return;
        }

        var editor = new GradeEntryEditorViewModel(SelectedSubject.Id, existing: null);
        if (await dialogs.ShowEditorAsync(editor, editor.HeaderText))
        {
            var result = await grades.CreateAsync(editor.ToModel());
            if (result.IsFailure)
            {
                toasts.Show("Не удалось добавить оценку", result.Error.Message, ToastKind.Error);
                return;
            }

            await ReloadForSubjectAsync();
        }
    }

    [RelayCommand]
    private async Task EditAsync(GradeRowViewModel? row)
    {
        if (row is null || SelectedSubject is null)
        {
            return;
        }

        var editor = new GradeEntryEditorViewModel(SelectedSubject.Id, row.Entry);
        if (await dialogs.ShowEditorAsync(editor, editor.HeaderText))
        {
            var result = await grades.UpdateAsync(editor.ToModel());
            if (result.IsFailure)
            {
                toasts.Show("Не удалось сохранить оценку", result.Error.Message, ToastKind.Error);
                return;
            }

            await ReloadForSubjectAsync();
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(GradeRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var confirmed = await dialogs.ConfirmAsync("Удалить оценку?", $"«{row.Title}» будет удалена.");
        if (!confirmed)
        {
            return;
        }

        var result = await grades.DeleteAsync(row.Entry.Id);
        if (result.IsFailure)
        {
            toasts.Show("Не удалось удалить оценку", result.Error.Message, ToastKind.Error);
            return;
        }

        await ReloadForSubjectAsync();
    }

    /// <summary>Шкала предмета — та же логика, что в сервисе; здесь нужна лишь для подписи «из N» и вердикта.</summary>
    private static GradeScale ScaleFor(Subject subject) => subject.GradeScaleKind switch
    {
        GradeScaleKind.Custom => new GradeScale(
            0m,
            subject.GradeScaleMax ?? 100m,
            subject.GradeScalePassThreshold ?? 60m,
            GradeScaleKind.Custom),
        var kind => GradeScale.For(kind),
    };

    private static decimal? ParseTarget(string? text)
    {
        text = text?.Trim().Replace(',', '.');
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
