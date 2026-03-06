using BloxManager.Models;
using BloxManager.ViewModels;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace BloxManager.Views
{
    public partial class MainWindow : Window
    {
        // ── Dark titlebar via DWM ────────────────────────────────────────────
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private MainViewModel? _backgroundVm;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            SourceInitialized += MainWindow_SourceInitialized;
            ContentRendered += MainWindow_ContentRendered;
            KeyDown += MainWindow_KeyDown;
            StateChanged += MainWindow_StateChanged;
            Loaded += MainWindow_Loaded;
            DataContextChanged += MainWindow_DataContextChanged;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            AttachBackgroundBindings();
            RefreshBackgroundImage();
        }

        private void MainWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            AttachBackgroundBindings();
            RefreshBackgroundImage();
        }

        private void AttachBackgroundBindings()
        {
            if (_backgroundVm != null)
            {
                _backgroundVm.PropertyChanged -= BackgroundVm_PropertyChanged;
                if (_backgroundVm.SettingsViewModel != null)
                    _backgroundVm.SettingsViewModel.PropertyChanged -= SettingsVm_PropertyChanged;
            }
            _backgroundVm = DataContext as MainViewModel;
            if (_backgroundVm != null)
            {
                _backgroundVm.PropertyChanged += BackgroundVm_PropertyChanged;
                if (_backgroundVm.SettingsViewModel != null)
                    _backgroundVm.SettingsViewModel.PropertyChanged += SettingsVm_PropertyChanged;
            }
        }

        private void BackgroundVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.BackgroundImagePath) ||
                e.PropertyName == nameof(MainViewModel.BackgroundImageStretch) ||
                e.PropertyName == nameof(MainViewModel.BackgroundImageAlignment) ||
                e.PropertyName == nameof(MainViewModel.BackgroundImageOpacity))
            {
                Dispatcher.Invoke(RefreshBackgroundImage);
            }
        }

        private void SettingsVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsViewModel.LowMemoryMode))
            {
                Dispatcher.Invoke(RefreshBackgroundImage);
            }
        }

        private void RefreshBackgroundImage()
        {
            System.Diagnostics.Debug.WriteLine("RefreshBackgroundImage called");
            try
            {
                if (_backgroundVm == null) 
                {
                    System.Diagnostics.Debug.WriteLine("RefreshBackgroundImage: _backgroundVm is null");
                    return;
                }
                if (BackgroundImage == null)
                {
                    System.Diagnostics.Debug.WriteLine("RefreshBackgroundImage: BackgroundImage control is null");
                    return;
                }

                var rawPath = _backgroundVm.BackgroundImagePath ?? string.Empty;
                System.Diagnostics.Debug.WriteLine($"RefreshBackgroundImage: rawPath = '{rawPath}'");
                var path = rawPath.Trim().Trim('"');
                path = Environment.ExpandEnvironmentVariables(path);
                if (!string.IsNullOrWhiteSpace(path) && !System.IO.Path.IsPathRooted(path))
                    path = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path));

                System.Diagnostics.Debug.WriteLine($"RefreshBackgroundImage: resolved path = '{path}'");

                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    System.Diagnostics.Debug.WriteLine("RefreshBackgroundImage: path is empty or file not found");
                    BackgroundImage.Source = null;
                    BackgroundImage.Visibility = Visibility.Collapsed;
                    return;
                }

                System.Diagnostics.Debug.WriteLine("RefreshBackgroundImage: loading bitmap...");
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();
                if (bitmap.CanFreeze) bitmap.Freeze();

                BackgroundImage.Source = bitmap;
                BackgroundImage.Visibility = Visibility.Visible;
                BackgroundImage.Opacity = Math.Max(0.0, Math.Min(1.0, _backgroundVm.BackgroundImageOpacity));
                System.Diagnostics.Debug.WriteLine("RefreshBackgroundImage completed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshBackgroundImage failed: {ex}");
                if (BackgroundImage != null)
                {
                    BackgroundImage.Source = null;
                    BackgroundImage.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                // Minimize all owned windows
                foreach (Window window in OwnedWindows)
                {
                    if (window.WindowState != WindowState.Minimized)
                    {
                        window.WindowState = WindowState.Minimized;
                    }
                }
            }
            else if (WindowState == WindowState.Normal || WindowState == WindowState.Maximized)
            {
                // Restore all owned windows
                foreach (Window window in OwnedWindows)
                {
                    if (window.WindowState == WindowState.Minimized)
                    {
                        window.WindowState = WindowState.Normal;
                    }
                }
            }
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && DataContext is MainViewModel viewModel)
            {
                viewModel.IsSettingsOpen = false;
            }

            // Ctrl+A to select all accounts
            if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                AccountList.SelectAll();
                e.Handled = true;
            }
        }

        private void AddAccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                if (button.ContextMenu != null)
                {
                    button.ContextMenu.PlacementTarget = button;
                    button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                    button.ContextMenu.IsOpen = true;
                }
            }
        }

        // ── Drag-to-select logic ────────────────────────────────────────────
        private Point _startPoint;
        private bool _isDragging;

        private void AccountList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                // Only start drag if clicking on empty space in the ScrollViewer
                // or if we want to support rubber-band selection starting from anywhere.
                // We'll check if we hit a ListBoxItem.
                var hit = VisualTreeHelper.HitTest(AccountList, e.GetPosition(AccountList));
                if (hit != null)
                {
                    DependencyObject? parent = hit.VisualHit;
                    while (parent != null && !(parent is ListBoxItem))
                    {
                        parent = VisualTreeHelper.GetParent(parent);
                    }

                    if (parent == null) // Clicked on empty space
                    {
                        _isDragging = true;
                        _startPoint = e.GetPosition(SelectionCanvas);
                        SelectionRectangle.Visibility = Visibility.Visible;
                        Canvas.SetLeft(SelectionRectangle, _startPoint.X);
                        Canvas.SetTop(SelectionRectangle, _startPoint.Y);
                        SelectionRectangle.Width = 0;
                        SelectionRectangle.Height = 0;
                        
                        AccountScrollViewer.CaptureMouse();
                        
                        // Clear selection if not holding Ctrl
                        if (Keyboard.Modifiers != ModifierKeys.Control)
                        {
                            AccountList.UnselectAll();
                        }
                        
                        e.Handled = true;
                    }
                }
            }
        }

        private void AccountList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point currentPoint = e.GetPosition(SelectionCanvas);

                double x = Math.Min(currentPoint.X, _startPoint.X);
                double y = Math.Min(currentPoint.Y, _startPoint.Y);
                double width = Math.Abs(currentPoint.X - _startPoint.X);
                double height = Math.Abs(currentPoint.Y - _startPoint.Y);

                Canvas.SetLeft(SelectionRectangle, x);
                Canvas.SetTop(SelectionRectangle, y);
                SelectionRectangle.Width = width;
                SelectionRectangle.Height = height;

                UpdateSelection(new Rect(x, y, width, height));
            }
        }

        private void AccountList_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                SelectionRectangle.Visibility = Visibility.Collapsed;
                AccountScrollViewer.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void UpdateSelection(Rect selectionRect)
        {
            // Convert selectionRect to coordinates relative to AccountList
            Point listOrigin = SelectionCanvas.TranslatePoint(new Point(0, 0), AccountList);
            Rect listSelectionRect = new Rect(selectionRect.X + listOrigin.X, selectionRect.Y + listOrigin.Y, selectionRect.Width, selectionRect.Height);

            for (int i = 0; i < AccountList.Items.Count; i++)
            {
                var item = AccountList.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                if (item != null)
                {
                    Point itemOrigin = item.TranslatePoint(new Point(0, 0), AccountList);
                    Rect itemRect = new Rect(itemOrigin.X, itemOrigin.Y, item.ActualWidth, item.ActualHeight);

                    if (listSelectionRect.IntersectsWith(itemRect))
                    {
                        item.IsSelected = true;
                    }
                    else if (Keyboard.Modifiers != ModifierKeys.Control)
                    {
                        item.IsSelected = false;
                    }
                }
            }
        }

        // Sync ListBox.SelectedItems → ViewModel.SelectedAccounts
        private void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                // We update the SelectedAccounts collection to reflect the ListBox's SelectedItems
                // This is needed because ListBox.SelectedItems isn't easily bindable in WPF without complexity
                vm.SelectedAccounts.Clear();
                foreach (var item in AccountList.SelectedItems)
                {
                    if (item is Account account)
                    {
                        vm.SelectedAccounts.Add(account);
                    }
                }
            }
        }

        private void GroupHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Check if the click is on a button (arrow button) - if so, don't handle it
            if (e.OriginalSource is Button)
            {
                // Don't handle the click - let it go to the arrow button
                return;
            }

            if (sender is FrameworkElement header && header.DataContext is AccountGroup group)
            {
                // If not holding Ctrl, clear previous selection
                if (Keyboard.Modifiers != ModifierKeys.Control)
                {
                    AccountList.UnselectAll();
                }

                // Select all accounts that belong to this group
                foreach (var account in group.Accounts)
                {
                    AccountList.SelectedItems.Add(account);
                }

                // Mark as handled to prevent the ListBox from clicking the individual ListBoxItem
                // which might clear the selection we just made.
                e.Handled = true;
            }
        }

        // ── Drag and Drop Reordering ─────────────────────────────────────────
        private int _draggedIndex = -1;
        private Point _dragStartPoint;

        private void ListBoxItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Don't start drag if clicking on an Expander's toggle button
            if (e.OriginalSource is DependencyObject dep && VisualTreeHelper.GetParent(dep) is System.Windows.Controls.Primitives.ToggleButton)
                return;

            if (sender is ListBoxItem item)
            {
                _dragStartPoint = e.GetPosition(null);
                _draggedIndex = AccountList.ItemContainerGenerator.IndexFromContainer(item);
            }
        }

        private void ListBoxItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedIndex != -1)
            {
                Point currentPosition = e.GetPosition(null);
                Vector diff = _dragStartPoint - currentPosition;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is ListBoxItem item)
                    {
                        DragDrop.DoDragDrop(item, item.DataContext, DragDropEffects.Move);
                    }
                    else if (sender is FrameworkElement element)
                    {
                        var lbi = FindAncestor<ListBoxItem>(element);
                        if (lbi != null)
                        {
                            DragDrop.DoDragDrop(lbi, lbi.DataContext, DragDropEffects.Move);
                        }
                    }
                }
            }
        }

        private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            do
            {
                if (current is T t) return t;
                current = VisualTreeHelper.GetParent(current);
            } while (current != null);
            return null;
        }

        private void ListBoxItem_Drop(object sender, DragEventArgs e)
        {
            if (_draggedIndex != -1 && sender is ListBoxItem droppedOnItem)
            {
                int targetIndex = AccountList.ItemContainerGenerator.IndexFromContainer(droppedOnItem);
                if (_draggedIndex != targetIndex && DataContext is MainViewModel vm)
                {
                    _ = vm.ReorderItemsAsync(_draggedIndex, targetIndex);
                }
                _draggedIndex = -1;
            }
        }

        private void ArrowButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is AccountGroup group && DataContext is MainViewModel vm)
            {
                System.Diagnostics.Debug.WriteLine($"Arrow button clicked for group: {group.Name}, IsExpanded: {group.IsExpanded}");
                
                vm.ToggleGroupExpansion(group);
                
                System.Diagnostics.Debug.WriteLine($"After toggle - IsExpanded: {group.IsExpanded}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("ArrowButton_Click failed - null checks");
            }
        }

        private void AccountsArea_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                var w = Math.Max(0, e.NewSize.Width);
                var h = Math.Max(0, e.NewSize.Height);
                vm.BackgroundTargetDimensions = $"{Math.Round(w):0} × {Math.Round(h):0} px";
            }
        }

        private void AccountsArea_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && DataContext is MainViewModel vm)
            {
                var w = Math.Max(0, fe.ActualWidth);
                var h = Math.Max(0, fe.ActualHeight);
                vm.BackgroundTargetDimensions = $"{Math.Round(w):0} × {Math.Round(h):0} px";
                fe.LayoutUpdated += AccountsArea_LayoutUpdated;
            }
        }

        private void MainWindow_ContentRendered(object? sender, EventArgs e)
        {
            if (AccountsArea != null && DataContext is MainViewModel vm)
            {
                var w = Math.Max(0, AccountsArea.ActualWidth);
                var h = Math.Max(0, AccountsArea.ActualHeight);
                vm.BackgroundTargetDimensions = $"{Math.Round(w):0} × {Math.Round(h):0} px";
            }
        }

        private void AccountsArea_LayoutUpdated(object? sender, EventArgs e)
        {
            if (sender is FrameworkElement fe && DataContext is MainViewModel vm)
            {
                var w = Math.Max(0, fe.ActualWidth);
                var h = Math.Max(0, fe.ActualHeight);
                if (w > 0 && h > 0)
                {
                    vm.BackgroundTargetDimensions = $"{Math.Round(w):0} × {Math.Round(h):0} px";
                }
            }
        }

        private async void DeleteGroupMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is AccountGroup group && DataContext is MainViewModel vm)
            {
                await vm.DeleteGroupAsync(group);
            }
        }

        private void EditGroupNameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is AccountGroup group && DataContext is MainViewModel vm)
            {
                var prompt = new BloxManager.Views.PromptWindow("Edit Group Name", "Enter a new name for this group:", group.Name);
                if (prompt.ShowDialog() == true && !string.IsNullOrWhiteSpace(prompt.InputText))
                {
                    var newName = prompt.InputText.Trim();
                    if (!string.IsNullOrEmpty(newName) && newName != group.Name)
                    {
                        vm.EditGroupName(group, newName);
                    }
                }
            }
        }

        private void GroupArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is AccountGroup group && DataContext is MainViewModel vm)
            {
                // If not holding Ctrl, clear previous selection
                if (Keyboard.Modifiers != ModifierKeys.Control)
                {
                    AccountList.UnselectAll();
                }

                // Select all accounts that belong to this group
                foreach (var account in group.Accounts)
                {
                    AccountList.SelectedItems.Add(account);
                }

                e.Handled = true;
            }
        }

    }
}
