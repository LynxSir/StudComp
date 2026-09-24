namespace StudComp.Core.Abstractions.Cards;

/// <summary>
/// Просьба открыть вкладку «Повторение». Шлётся из модуля Картотеки (напоминание о повторении) и
/// принимается в App-слое, который единственный умеет навигацию.
/// </summary>
/// <remarks>
/// Место в <c>Core.Abstractions</c> — тот же приём, что у <see cref="Archivist.FileSortedMessage"/>
/// (ADR §16.52): запись читают оба слоя, а <c>CommunityToolkit.Mvvm</c> в <c>Core</c> при этом не
/// тянется — <c>CoreDependenciesTests</c> остаётся зелёным.
/// </remarks>
/// <param name="SubjectId">Предмет, которым стоит ограничить очередь; <see langword="null"/> — вся картотека.</param>
public sealed record OpenReviewRequestedMessage(Guid? SubjectId = null);
