using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StudentApp.ViewModels;

namespace StudentApp.Views;

public partial class AdvancedDragDropView : UserControl
{
    private Button? _dragSourceButton;
    private Point _dragStartPoint;
    private int? _suppressedClickValue;

    public AdvancedDragDropView()
    {
        InitializeComponent();
    }

    private void NumberTile_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragSourceButton = sender as Button;
        _dragStartPoint = e.GetPosition(this);
    }

    private void NumberTile_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not Button button || !ReferenceEquals(button, _dragSourceButton))
        {
            return;
        }

        var currentPosition = e.GetPosition(this);
        var horizontalDistance = Math.Abs(currentPosition.X - _dragStartPoint.X);
        var verticalDistance = Math.Abs(currentPosition.Y - _dragStartPoint.Y);
        if (horizontalDistance < SystemParameters.MinimumHorizontalDragDistance &&
            verticalDistance < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (button.DataContext is int selectedValue)
        {
            _suppressedClickValue = selectedValue;
            DragDrop.DoDragDrop(button, selectedValue, DragDropEffects.Copy);
            e.Handled = true;
        }

        _dragSourceButton = null;
    }

    private void NumberTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: int selectedValue })
        {
            return;
        }

        if (_suppressedClickValue == selectedValue)
        {
            _suppressedClickValue = null;
            e.Handled = true;
            return;
        }

        if (DataContext is AdvancedDragDropViewModel viewModel)
        {
            viewModel.SubmitTappedValue(selectedValue);
        }
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not AdvancedDragDropViewModel viewModel)
        {
            return;
        }

        if (e.Data.GetData(typeof(int)) is int droppedValue)
        {
            viewModel.SubmitDroppedValue(droppedValue);
        }
    }
}
