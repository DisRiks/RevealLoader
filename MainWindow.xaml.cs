using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Microsoft.Win32;

namespace RevealLoader;

public partial class MainWindow : Window
{
    private readonly LauncherSettings _settings;
    private string _currentLang = "RU";

    public MainWindow()
    {
        InitializeComponent();
        _settings = SettingsService.Load();
        JavaPathBox.Text = _settings.JavaPath;
        if (!string.IsNullOrEmpty(_settings.JavaPath))
            JavaPathBox.Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xff, 0xff));
        RamMinBox.Text = _settings.RamMinMb.ToString();
        RamMaxBox.Text = _settings.RamMaxMb.ToString();
        ApplyLanguage();
        UpdateSelectionVisuals(animate: false);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        // Окно плавно проявляется.
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = ease
        });

        // Логотип проявляется чуть позже.
        LogoImage.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420))
        {
            BeginTime = TimeSpan.FromMilliseconds(100),
            EasingFunction = ease
        });

        // Индикатор статуса «дышит».
        StatusDot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromSeconds(0.9))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });

        // Свечение кнопки запуска пульсирует.
        if (PlayButton.Effect is DropShadowEffect glow)
        {
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.12, 0.5, TimeSpan.FromSeconds(1.6))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
        }

        ShowPage(HomePage);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    private void NavHome_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        SettingsPage.Visibility = Visibility.Collapsed;
        ShowPage(HomePage);
        IconHome.Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xff, 0xff));
        IconSettings.Foreground = new SolidColorBrush(Color.FromRgb(0x4d, 0x4d, 0x4d));
    }

    private void NavSettings_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        HomePage.Visibility = Visibility.Collapsed;
        ShowPage(SettingsPage);
        IconSettings.Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xff, 0xff));
        IconHome.Foreground = new SolidColorBrush(Color.FromRgb(0x4d, 0x4d, 0x4d));
    }

    /// <summary>Показывает страницу с лёгким сдвигом и проявлением.</summary>
    private void ShowPage(UIElement page)
    {
        if (page.RenderTransform is not TranslateTransform translate) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        translate.X = 16;
        page.Visibility = Visibility.Visible;
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = ease
        });
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = ease
        });
    }

    private void LangRu_Click(object sender, MouseButtonEventArgs e)
    {
        if (_currentLang == "RU") return;
        _currentLang = "RU";
        UpdateSelectionVisuals(animate: true);
        ApplyLanguage();
    }

    private void LangEn_Click(object sender, MouseButtonEventArgs e)
    {
        if (_currentLang == "EN") return;
        _currentLang = "EN";
        UpdateSelectionVisuals(animate: true);
        ApplyLanguage();
    }

    /// <summary>Двигает белый ползунок переключателя языка и обновляет подписи.</summary>
    private void UpdateSelectionVisuals(bool animate)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        bool ruSelected = _currentLang == "RU";

        LabelRu.Foreground = new SolidColorBrush(ruSelected
            ? Color.FromRgb(0x00, 0x00, 0x00)
            : Color.FromRgb(0x8a, 0x8a, 0x8a));
        LabelEn.Foreground = new SolidColorBrush(ruSelected
            ? Color.FromRgb(0x8a, 0x8a, 0x8a)
            : Color.FromRgb(0x00, 0x00, 0x00));

        var ruFlag = new DoubleAnimation(ruSelected ? 1.0 : 0.45, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
        var enFlag = new DoubleAnimation(ruSelected ? 0.45 : 1.0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
        if (animate)
        {
            ImgRu.BeginAnimation(OpacityProperty, ruFlag);
            ImgEn.BeginAnimation(OpacityProperty, enFlag);
        }
        else
        {
            ImgRu.Opacity = ruSelected ? 1.0 : 0.45;
            ImgEn.Opacity = ruSelected ? 0.45 : 1.0;
        }

        if (LangHighlight.RenderTransform is not TranslateTransform highlight) return;
        double offset = ruSelected ? 0 : LangHighlight.ActualWidth;
        if (animate)
        {
            highlight.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(offset, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        }
        else
        {
            highlight.X = offset;
        }
    }

    /// <summary>Обновляет сводку памяти и Java на главном экране.</summary>
    private void UpdateInfoStrip()
    {
        if (_settings == null) return;

        int min = int.TryParse(RamMinBox.Text.Trim(), out int parsedMin) ? parsedMin : _settings.RamMinMb;
        int max = int.TryParse(RamMaxBox.Text.Trim(), out int parsedMax) ? parsedMax : _settings.RamMaxMb;
        RamSummaryText.Text = $"{FormatMb(min)} – {FormatMb(max)}";

        string java = JavaPathBox.Text.Trim();
        JavaSummaryText.Text = string.IsNullOrEmpty(java)
            ? (_currentLang == "RU" ? "Авто" : "Auto")
            : Path.GetFileName(java);
        JavaSummaryText.ToolTip = string.IsNullOrEmpty(java) ? null : java;
    }

    private static string FormatMb(int mb) => mb % 1024 == 0
        ? $"{mb / 1024} GB"
        : $"{mb / 1024.0:0.#} GB";

    private void RamInput_TextChanged(object sender, TextChangedEventArgs e)
        => UpdateInfoStrip();

    /// <summary>Обновляет строку статуса с мягким проявлением.</summary>
    private void SetStatus(string text)
    {
        StatusText.Text = text;
        StatusText.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void ApplyLanguage()
    {
        if (_currentLang == "RU")
        {
            WelcomeAccent.Text = "RevealLoader";
            FreeText.Text = "Free";
            SymbolText.Text = "Ꝏ";
            RamCaption.Text = "ПАМЯТЬ";
            JavaCaption.Text = "JAVA";
            LaunchText.Text = "ЗАПУСК";
            StatusText.Text = "Готов к запуску";
            SettingsTitle.Text = "Настройки";
            JavaLabel.Text = "JAVA";
            BrowseBtn.Content = "…";
            JavaHintText.Text = "Укажите javaw.exe для запуска";
            MemoryLabel.Text = "ПАМЯТЬ";
            MinText.Text = "МИН";
            MaxText.Text = "МАКС";
            PresetsLabel.Text = "БЫСТРЫЙ ВЫБОР";
            RamHintText.Text = "Рекомендуется не менее 5 ГБ";
            SaveBtn.Content = "СОХРАНИТЬ";
        }
        else
        {
            WelcomeAccent.Text = "RevealLoader";
            FreeText.Text = "Free";
            SymbolText.Text = "Ꝏ";
            RamCaption.Text = "MEMORY";
            JavaCaption.Text = "JAVA";
            LaunchText.Text = "LAUNCH";
            StatusText.Text = "Ready to launch";
            SettingsTitle.Text = "Settings";
            JavaLabel.Text = "JAVA";
            BrowseBtn.Content = "…";
            JavaHintText.Text = "Select javaw.exe to launch";
            MemoryLabel.Text = "MEMORY";
            MinText.Text = "MIN";
            MaxText.Text = "MAX";
            PresetsLabel.Text = "QUICK SET";
            RamHintText.Text = "Recommended at least 5 GB";
            SaveBtn.Content = "SAVE";
        }
        UpdateInfoStrip();
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        SetStatus(_currentLang == "RU" ? "Запуск..." : "Launching...");
        try
        {
            var launcher = new MinecraftLauncher(_settings);
            await launcher.LaunchAsync();
            SetStatus(_currentLang == "RU" ? "Запущено" : "Running");
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
    }

    private void RamPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out int mb))
        {
            RamMaxBox.Text = mb.ToString();
            UpdateInfoStrip();
        }
    }

    private void BrowseJava_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Java|javaw.exe;java.exe|All files|*.*",
            Title = "Select javaw.exe"
        };
        if (dialog.ShowDialog() == true)
        {
            JavaPathBox.Text = dialog.FileName;
            JavaPathBox.Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xff, 0xff));
            UpdateInfoStrip();
        }
    }

    private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !char.IsDigit(e.Text, 0);
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RamMinBox.Text.Trim(), out int ramMin) || ramMin < 256)
        {
            CustomMessageBox.Show(
                _currentLang == "RU" ? "Мин. память: от 256 MB" : "Min memory: 256 MB minimum",
                _currentLang == "RU" ? "Ошибка" : "Error",
                MessageBoxIcon.Warning, this);
            return;
        }
        if (!int.TryParse(RamMaxBox.Text.Trim(), out int ramMax) || ramMax < 512)
        {
            CustomMessageBox.Show(
                _currentLang == "RU" ? "Макс. память: от 512 MB" : "Max memory: 512 MB minimum",
                _currentLang == "RU" ? "Ошибка" : "Error",
                MessageBoxIcon.Warning, this);
            return;
        }
        if (ramMin > ramMax)
        {
            CustomMessageBox.Show(
                _currentLang == "RU" ? "Min не может быть больше Max" : "Min cannot be greater than Max",
                _currentLang == "RU" ? "Ошибка" : "Error",
                MessageBoxIcon.Warning, this);
            return;
        }
        _settings.JavaPath = JavaPathBox.Text.Trim();
        _settings.RamMinMb = ramMin;
        _settings.RamMaxMb = ramMax;
        SettingsService.Save(_settings);
        UpdateInfoStrip();
        CustomMessageBox.Show(
            _currentLang == "RU" ? "Настройки сохранены!" : "Settings saved!",
            "RevealLoader",
            MessageBoxIcon.Info, this);
    }
}
