using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RevealLoader;

public enum MessageBoxIcon { Info, Warning, Error }

public partial class CustomMessageBox : Window
{
    public CustomMessageBox(string message, string title, MessageBoxIcon icon = MessageBoxIcon.Info, Window? owner = null)
    {
        InitializeComponent();
        if (owner != null)
            Owner = owner;

        TitleText.Text = title;
        MessageText.Text = message;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = ease
        });
        if (RootBorder.RenderTransform is TranslateTransform translate)
        {
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = ease
            });
        }
    }

    public static void Show(string message, string title, MessageBoxIcon icon = MessageBoxIcon.Info, Window? owner = null)
    {
        var box = new CustomMessageBox(message, title, icon, owner);
        box.ShowDialog();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
