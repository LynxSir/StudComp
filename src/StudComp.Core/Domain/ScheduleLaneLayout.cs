namespace StudComp.Core.Domain;

/// <summary>
/// Место пары в «дорожках» одной ячейки сетки: какая по счёту и сколько их всего рядом.
/// </summary>
/// <param name="EntryId">Пара, к которой относится раскладка.</param>
/// <param name="Lane">Номер дорожки, начиная с нуля.</param>
/// <param name="LaneCount">Сколько дорожек делят между собой ширину дня.</param>
public readonly record struct ScheduleLane(Guid EntryId, int Lane, int LaneCount);

/// <summary>
/// Раскладка накладывающихся пар по вертикальным дорожкам внутри дня (new_addons.md §5, режим
/// «показать обе недели»): числитель и знаменатель в одно и то же время должны стоять рядом, а не
/// друг на друге.
/// </summary>
/// <remarks>
/// Чистая геометрия без WPF — живёт в <c>Core</c>, чтобы её можно было проверить тестами, а не
/// глазами по экрану (тот же довод, что у <see cref="WeekParityCalculator"/>, ADR §16.21).
/// </remarks>
public static class ScheduleLaneLayout
{
    /// <summary>
    /// Раздаёт дорожки: пары одного дня, пересекающиеся по времени, попадают в разные дорожки,
    /// непересекающиеся — переиспользуют освободившуюся. <c>LaneCount</c> одинаков внутри группы
    /// пересекающихся пар, поэтому соседние плитки имеют равную ширину.
    /// </summary>
    public static IReadOnlyList<ScheduleLane> Assign(IReadOnlyList<ScheduleEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var result = new List<ScheduleLane>(entries.Count);

        foreach (var day in entries.GroupBy(x => x.DayOfWeek))
        {
            AssignWithinDay([.. day.OrderBy(x => x.StartTime).ThenBy(x => x.EndTime)], result);
        }

        return result;
    }

    private static void AssignWithinDay(List<ScheduleEntry> ordered, List<ScheduleLane> result)
    {
        // Конец текущей занятости каждой дорожки — по нему видно, освободилась ли она.
        var laneEnds = new List<TimeOnly>();

        // Индексы записей текущей «грозди» пересекающихся пар: им всем выставляется общий LaneCount.
        var cluster = new List<int>();
        var clusterEnd = TimeOnly.MinValue;

        foreach (var entry in ordered)
        {
            if (cluster.Count > 0 && entry.StartTime >= clusterEnd)
            {
                // Гроздь закончилась: до неё ничто не дотягивается — фиксируем ширину и начинаем новую.
                FlushCluster(cluster, laneEnds.Count, result);
                laneEnds.Clear();
                clusterEnd = TimeOnly.MinValue;
            }

            var lane = laneEnds.FindIndex(end => end <= entry.StartTime);
            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(entry.EndTime);
            }
            else
            {
                laneEnds[lane] = entry.EndTime;
            }

            result.Add(new ScheduleLane(entry.Id, lane, LaneCount: 0));
            cluster.Add(result.Count - 1);

            if (entry.EndTime > clusterEnd)
            {
                clusterEnd = entry.EndTime;
            }
        }

        FlushCluster(cluster, laneEnds.Count, result);
    }

    private static void FlushCluster(List<int> cluster, int laneCount, List<ScheduleLane> result)
    {
        foreach (var index in cluster)
        {
            result[index] = result[index] with { LaneCount = Math.Max(1, laneCount) };
        }

        cluster.Clear();
    }
}
