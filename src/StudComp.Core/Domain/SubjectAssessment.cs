namespace StudComp.Core.Domain;

public enum SubjectAssessment
{
    Unspecified = 0,
    Credit = 1,
    GradedCredit = 2,
    Exam = 3,
    CourseWork = 4,
    IndependentWork = 5,
}

public static class SubjectAssessmentNames
{
    public static string DisplayName(this SubjectAssessment value) => value switch
    {
        SubjectAssessment.Credit => "Зачёт",
        SubjectAssessment.GradedCredit => "Дифференцированный зачёт",
        SubjectAssessment.Exam => "Экзамен",
        SubjectAssessment.CourseWork => "Курсовая работа / проект",
        SubjectAssessment.IndependentWork => "Самостоятельная работа",
        _ => "Контроль не указан",
    };
}
