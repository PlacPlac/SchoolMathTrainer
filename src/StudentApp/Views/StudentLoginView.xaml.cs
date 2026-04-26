using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using StudentApp.ViewModels;

namespace StudentApp.Views;

public partial class StudentLoginView : UserControl
{
    private StudentLoginViewModel? _viewModel;
    private bool _syncingPasswordBoxes;
    private bool _updatingViewModelFromPasswordBox;

    public StudentLoginView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as StudentLoginViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        SyncPasswordBoxesFromViewModel();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_updatingViewModelFromPasswordBox)
        {
            return;
        }

        if (e.PropertyName is nameof(StudentLoginViewModel.Pin) or nameof(StudentLoginViewModel.NewPin))
        {
            SyncPasswordBoxesFromViewModel();
        }
    }

    private void OnPinPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingPasswordBoxes || _viewModel is null)
        {
            return;
        }

        UpdateViewModelFromPasswordBox(() => _viewModel.Pin = PinPasswordBox.Password);
    }

    private void OnNewPinPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingPasswordBoxes || _viewModel is null)
        {
            return;
        }

        UpdateViewModelFromPasswordBox(() => _viewModel.NewPin = NewPinPasswordBox.Password);
    }

    private void UpdateViewModelFromPasswordBox(Action update)
    {
        _updatingViewModelFromPasswordBox = true;
        try
        {
            update();
        }
        finally
        {
            _updatingViewModelFromPasswordBox = false;
        }
    }

    private void SyncPasswordBoxesFromViewModel()
    {
        _syncingPasswordBoxes = true;
        try
        {
            PinPasswordBox.Password = _viewModel?.Pin ?? string.Empty;
            NewPinPasswordBox.Password = _viewModel?.NewPin ?? string.Empty;
        }
        finally
        {
            _syncingPasswordBoxes = false;
        }
    }
}
