using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Domain;
using StudComp.Modules.Cards.Services;

namespace StudComp.ViewModels.Cards;

/// <summary>Откуда читать импортируемые карточки — от этого зависит, какой <c>Preview*Async</c> вызывается.</summary>
public enum CardsImportSourceFormat
{
    Json,
    Csv,
}

/// <summary>
/// Диалог импорта картотеки (new_addons.md §7.6): выбор режима (и для CSV — кодировки/разделителя) →
/// построение плана → сводка «создано/обновлено/пропущено» до какой-либо записи.
/// </summary>
/// <remarks>
/// Кнопка диалога («Импортировать») активна только когда план уже построен — саму запись делает
/// вызывающий код (<c>CardLibraryViewModel</c>) через <see cref="Plan"/> уже после закрытия диалога,
/// вызовом <c>ICardImportExportService.CommitImportAsync</c>. Диалог сам в базу не пишет ни строки.
/// </remarks>
public sealed partial class CardsImportViewModel : ObservableObject
{
    private readonly ICardImportExportService _importExport;
    private readonly string _path;
    private readonly CardsImportSourceFormat _format;

    public CardsImportViewModel(
        ICardImportExportService importExport,
        string path,
        CardsImportSourceFormat format,
        IReadOnlyList<CardDeck> decks,
        CardCsvSniffResult? sniff)
    {
        _importExport = importExport;
        _path = path;
        _format = format;
        Decks = decks;

        _selectedMode = CardChoices.ImportModes[0];
        _selectedDelimiter = Delimiters[0];
        _selectedEncoding = Encodings[0];

        if (sniff is not null)
        {
            _selectedDelimiter = Delimiters.FirstOrDefault(x => x.Value == sniff.Delimiter) ?? Delimiters[0];
            _selectedEncoding = Encodings.FirstOrDefault(x => x.Value == sniff.Encoding.CodePage) ?? Encodings[0];
            _hasHeader = sniff.HasHeader;

            foreach (var row in sniff.PreviewRows)
            {
                PreviewRows.Add(string.Join("  ·  ", row));
            }
        }
    }

    public bool IsCsv => _format == CardsImportSourceFormat.Csv;

    public IReadOnlyList<CardDeck> Decks { get; }

    public IReadOnlyList<NamedChoice<CardImportMode>> Modes => CardChoices.ImportModes;

    public IReadOnlyList<NamedChoice<char>> Delimiters { get; } =
    [
        new(';', "; — точка с запятой"),
        new('\t', "Tab"),
        new(',', ", — запятая"),
    ];

    public IReadOnlyList<NamedChoice<int>> Encodings { get; } =
    [
        new(65001, "UTF-8"),
        new(1251, "Windows-1251"),
        new(1200, "Unicode (UTF-16)"),
    ];

    /// <summary>Первые строки файла — чтобы пользователь увидел, что вообще распозналось, до импорта.</summary>
    public ObservableCollection<string> PreviewRows { get; } = [];

    public bool HasPreviewRows => PreviewRows.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsTargetDeck))]
    private NamedChoice<CardImportMode> _selectedMode;

    [ObservableProperty]
    private CardDeck? _targetDeck;

    [ObservableProperty]
    private NamedChoice<char> _selectedDelimiter;

    [ObservableProperty]
    private NamedChoice<int> _selectedEncoding;

    [ObservableProperty]
    private bool _hasHeader = true;

    [ObservableProperty]
    private bool _isPreviewStage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _warningsText = string.Empty;

    /// <summary>«Заменить колоду» без выбранной цели ничего не значит.</summary>
    public bool NeedsTargetDeck => SelectedMode.Value == CardImportMode.ReplaceDeck;

    /// <summary>Готовый план — то, что закоммитит вызывающий код после закрытия диалога.</summary>
    public CardImportPlan? Plan { get; private set; }

    /// <summary>Кнопка диалога подтверждает импорт только когда план уже построен.</summary>
    public bool CanSave => IsPreviewStage && Plan is not null;

    partial void OnSelectedModeChanged(NamedChoice<CardImportMode> value) => ResetPlan();

    partial void OnTargetDeckChanged(CardDeck? value) => ResetPlan();

    [RelayCommand]
    private async Task BuildPlanAsync()
    {
        if (NeedsTargetDeck && TargetDeck is null)
        {
            StatusText = "Выберите колоду, которую нужно заменить.";
            return;
        }

        IsBusy = true;
        StatusText = string.Empty;
        try
        {
            var result = _format == CardsImportSourceFormat.Json
                ? await _importExport
                    .PreviewJsonImportAsync(_path, SelectedMode.Value, TargetDeck?.Id)
                    .ConfigureAwait(true)
                : await _importExport
                    .PreviewCsvImportAsync(
                        _path,
                        SelectedDelimiter.Value,
                        Encoding.GetEncoding(SelectedEncoding.Value),
                        HasHeader,
                        SelectedMode.Value,
                        TargetDeck?.Id)
                    .ConfigureAwait(true);

            if (result.IsFailure)
            {
                StatusText = result.Error.Message;
                Plan = null;
                IsPreviewStage = false;
                return;
            }

            Plan = result.Value;
            var created = Plan.Rows.Count(r => r.Action == CardImportRowAction.Create);
            var updated = Plan.Rows.Count(r => r.Action == CardImportRowAction.UpdateExisting);
            var skipped = Plan.Rows.Count(r => r.Action == CardImportRowAction.Skip);
            StatusText = $"Будет создано: {created} · обновлено: {updated} · пропущено: {skipped}.";
            WarningsText = string.Join(Environment.NewLine, Plan.Warnings);
            IsPreviewStage = true;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanSave));
        }
    }

    [RelayCommand]
    private void BackToOptions() => ResetPlan();

    private void ResetPlan()
    {
        IsPreviewStage = false;
        Plan = null;
        StatusText = string.Empty;
        WarningsText = string.Empty;
        OnPropertyChanged(nameof(CanSave));
    }
}
