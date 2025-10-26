using System.Windows;
using System.Windows.Media;

namespace S3Mount.Helpers;

public static class DarkMessageBox
{
    public static MessageBoxResult Show(string message, string title = "S3 Mount Manager", MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 500,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(15, 20, 25)), // PrimaryBackground
            ShowInTaskbar = false,
            Topmost = true
        };
        
        // Enable dark mode for title bar (even though we're borderless)
        WindowHelper.EnableDarkModeForWindow(dialog);
        
        var grid = new System.Windows.Controls.Grid();
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        
        // Content area
        var contentBorder = new System.Windows.Controls.Border
        {
            Background = new SolidColorBrush(Color.FromRgb(26, 31, 46)), // SecondaryBackground
            BorderBrush = new SolidColorBrush(Color.FromRgb(61, 70, 86)), // BorderBrush
            BorderThickness = new Thickness(1),
            CornerRadius = new System.Windows.CornerRadius(8, 8, 0, 0),
            Padding = new Thickness(32, 24, 32, 24)
        };
        
        var contentStack = new System.Windows.Controls.StackPanel();
        
        // Icon and title
        var headerStack = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 16)
        };
        
        // Icon
        var iconText = icon switch
        {
            MessageBoxImage.Error => "?",
            MessageBoxImage.Warning => "??",
            MessageBoxImage.Information => "??",
            MessageBoxImage.Question => "?",
            _ => ""
        };
        
        if (!string.IsNullOrEmpty(iconText))
        {
            headerStack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = iconText,
                FontSize = 32,
                Margin = new Thickness(0, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        
        // Title
        headerStack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(243, 244, 246)), // PrimaryText
            VerticalAlignment = VerticalAlignment.Center
        });
        
        contentStack.Children.Add(headerStack);
        
        // Message
        contentStack.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = message,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175)), // SecondaryText
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22
        });
        
        contentBorder.Child = contentStack;
        System.Windows.Controls.Grid.SetRow(contentBorder, 0);
        grid.Children.Add(contentBorder);
        
        // Button area
        var buttonBorder = new System.Windows.Controls.Border
        {
            Background = new SolidColorBrush(Color.FromRgb(26, 31, 46)), // SecondaryBackground
            BorderBrush = new SolidColorBrush(Color.FromRgb(61, 70, 86)), // BorderBrush
            BorderThickness = new Thickness(1, 0, 1, 1),
            CornerRadius = new System.Windows.CornerRadius(0, 0, 8, 8),
            Padding = new Thickness(32, 20, 32, 20)
        };
        
        var buttonStack = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        
        MessageBoxResult result = MessageBoxResult.None;
        
        // Create buttons based on type
        switch (buttons)
        {
            case MessageBoxButton.OK:
                var okButton = CreateButton("OK", true);
                okButton.Click += (s, e) => { result = MessageBoxResult.OK; dialog.Close(); };
                buttonStack.Children.Add(okButton);
                break;
                
            case MessageBoxButton.OKCancel:
                var okButton2 = CreateButton("OK", true);
                okButton2.Click += (s, e) => { result = MessageBoxResult.OK; dialog.Close(); };
                buttonStack.Children.Add(okButton2);
                
                var cancelButton = CreateButton("Cancel", false);
                cancelButton.Click += (s, e) => { result = MessageBoxResult.Cancel; dialog.Close(); };
                cancelButton.Margin = new Thickness(12, 0, 0, 0);
                buttonStack.Children.Add(cancelButton);
                break;
                
            case MessageBoxButton.YesNo:
                var yesButton = CreateButton("Yes", true);
                yesButton.Click += (s, e) => { result = MessageBoxResult.Yes; dialog.Close(); };
                buttonStack.Children.Add(yesButton);
                
                var noButton = CreateButton("No", false);
                noButton.Click += (s, e) => { result = MessageBoxResult.No; dialog.Close(); };
                noButton.Margin = new Thickness(12, 0, 0, 0);
                buttonStack.Children.Add(noButton);
                break;
                
            case MessageBoxButton.YesNoCancel:
                var yesButton2 = CreateButton("Yes", true);
                yesButton2.Click += (s, e) => { result = MessageBoxResult.Yes; dialog.Close(); };
                buttonStack.Children.Add(yesButton2);
                
                var noButton2 = CreateButton("No", false);
                noButton2.Click += (s, e) => { result = MessageBoxResult.No; dialog.Close(); };
                noButton2.Margin = new Thickness(12, 0, 0, 0);
                buttonStack.Children.Add(noButton2);
                
                var cancelButton2 = CreateButton("Cancel", false);
                cancelButton2.Click += (s, e) => { result = MessageBoxResult.Cancel; dialog.Close(); };
                cancelButton2.Margin = new Thickness(12, 0, 0, 0);
                buttonStack.Children.Add(cancelButton2);
                break;
        }
        
        buttonBorder.Child = buttonStack;
        System.Windows.Controls.Grid.SetRow(buttonBorder, 1);
        grid.Children.Add(buttonBorder);
        
        dialog.Content = grid;
        dialog.ShowDialog();
        
        return result;
    }
    
    private static System.Windows.Controls.Button CreateButton(string text, bool isPrimary)
    {
        var button = new System.Windows.Controls.Button
        {
            Content = text,
            MinWidth = 100,
            Height = 40,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        
        if (isPrimary)
        {
            button.Background = new SolidColorBrush(Color.FromRgb(246, 130, 31)); // AccentOrange
            button.Foreground = Brushes.White;
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(246, 130, 31));
        }
        else
        {
            button.Background = new SolidColorBrush(Color.FromRgb(44, 53, 68)); // Surface
            button.Foreground = new SolidColorBrush(Color.FromRgb(243, 244, 246)); // PrimaryText
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(61, 70, 86)); // BorderBrush
        }
        
        button.BorderThickness = new Thickness(1);
        
        // Create rounded corners template
        var template = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button));
        var borderFactory = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Border));
        borderFactory.SetValue(System.Windows.Controls.Border.BackgroundProperty, new System.Windows.TemplateBindingExtension(System.Windows.Controls.Control.BackgroundProperty));
        borderFactory.SetValue(System.Windows.Controls.Border.BorderBrushProperty, new System.Windows.TemplateBindingExtension(System.Windows.Controls.Control.BorderBrushProperty));
        borderFactory.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new System.Windows.TemplateBindingExtension(System.Windows.Controls.Control.BorderThicknessProperty));
        borderFactory.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new System.Windows.CornerRadius(6));
        borderFactory.SetValue(System.Windows.Controls.Border.PaddingProperty, new Thickness(16, 8, 16, 8));
        
        var contentFactory = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
        contentFactory.SetValue(System.Windows.FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contentFactory.SetValue(System.Windows.FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        
        borderFactory.AppendChild(contentFactory);
        template.VisualTree = borderFactory;
        
        button.Template = template;
        
        // Hover effect
        button.MouseEnter += (s, e) =>
        {
            if (isPrimary)
            {
                button.Opacity = 0.9;
            }
            else
            {
                button.Background = new SolidColorBrush(Color.FromRgb(37, 45, 58)); // TertiaryBackground
            }
        };
        
        button.MouseLeave += (s, e) =>
        {
            if (isPrimary)
            {
                button.Opacity = 1.0;
            }
            else
            {
                button.Background = new SolidColorBrush(Color.FromRgb(44, 53, 68)); // Surface
            }
        };
        
        return button;
    }
}
