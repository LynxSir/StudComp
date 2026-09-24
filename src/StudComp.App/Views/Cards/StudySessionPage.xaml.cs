using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StudComp.ViewModels.Cards;

namespace StudComp.Views.Cards;

/// <summary>
/// Экран сессии (new_addons.md §8.3, §8.4).
/// </summary>
/// <remarks>
/// Код-behind здесь только про клавиатуру: сессия обязана целиком проходиться без мыши, а
/// перехватывать нажатия на уровне страницы — единственный способ отдать <c>Space</c> и цифры
/// командам, а не фокусированной кнопке. Приём тот же, что в <c>CardLibraryPage</c>.
/// </remarks>
public partial class StudySessionPage : UserControl
{
    public StudySessionPage()
    {
        InitializeComponent();

        // Без фокуса на самой странице клавиши уходили бы в первый попавшийся элемент.
        Loaded += (_, _) => Focus();
    }

    private StudySessionViewModel? ViewModel => DataContext as StudySessionViewModel;

    /// <inheritdoc />
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (ViewModel is not { } vm)
        {
            return;
        }

        // В поле ввода ответа клавиатура принадлежит пользователю, а не сессии.
        var typing = Keyboard.FocusedElement is TextBox;

        switch (e.Key)
        {
            case Key.Escape:
                Run(vm.ExitCommand, e);
                return;

            case Key.Space or Key.Enter when !typing:
                RevealOrGrade(vm, e);
                return;

            case Key.Enter when typing:
                Run(vm.SubmitTypedCommand, e);
                return;

            case >= Key.D1 and <= Key.D4 when !typing:
                Grade(vm, e.Key - Key.D1, e);
                return;

            case >= Key.NumPad1 and <= Key.NumPad4 when !typing:
                Grade(vm, e.Key - Key.NumPad1, e);
                return;

            case Key.H when !typing:
                Run(vm.ShowHintCommand, e);
                return;

            case Key.S when !typing:
                Run(vm.SuspendCommand, e);
                return;
        }
    }

    private static void RevealOrGrade(StudySessionViewModel vm, KeyEventArgs e)
    {
        if (vm.RevealCommand.CanExecute(null))
        {
            Run(vm.RevealCommand, e);
        }
        else if (vm.RevealGapCommand.CanExecute(null))
        {
            Run(vm.RevealGapCommand, e);
        }
    }

    private static void Grade(StudySessionViewModel vm, int index, KeyEventArgs e)
    {
        // Оценка недоступна до раскрытия — команда сама этого не даст, но и молчать незачем.
        if (index < 0 || index >= vm.Grades.Count)
        {
            return;
        }

        var grade = vm.Grades[index];

        if (vm.GradeCommand.CanExecute(grade))
        {
            vm.GradeCommand.Execute(grade);
            e.Handled = true;
        }
    }

    private static void Run(ICommand command, KeyEventArgs e)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
            e.Handled = true;
        }
    }
}
