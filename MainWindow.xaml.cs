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
    private LauncherUpdate? _update;

    public MainWindow()
    {
        InitializeComponent();
        UpdateService.CleanupBackup();
        _settings = SettingsService.Load();
        JavaPathBox.Text = _settings.JavaPath;
        if (!string.IsNullOrEmpty(_settings.JavaPath))
            JavaPathBox.Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xff, 0xff));
        RamMinBox.Text = _settings.RamMinMb.ToString();
        RamMaxBox.Text = _settings.RamMaxMb.ToString();
        SidebarVersion.Text = "v" + UpdateService.CurrentVersion;
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

        _ = CheckForUpdatesAsync();
    }

    /// <summary>Проверяет обновление лаунчера и при его наличии блокирует работу.</summary>
    private async Task CheckForUpdatesAsync()
    {
        _update = await UpdateService.GetLauncherUpdateAsync();
        if (_update != null)
            ShowUpdateOverlay(_update);
    }

    /// <summary>Блокирует интерфейс, пока пользователь не установит обязательное обновление.</summary>
    private void ShowUpdateOverlay(LauncherUpdate update)
    {
        UpdateTitle.Text = _currentLang == "RU" ? "Требуется обновление" : "Update required";
        UpdateMessage.Text = _currentLang == "RU"
            ? $"Доступна новая версия {update.Version}. Чтобы продолжить, обновите лаунчер."
            : $"Version {update.Version} is available. Update the launcher to continue.";
        UpdateNowButton.Content = _currentLang == "RU" ? "ОБНОВИТЬ" : "UPDATE";
        UpdateOverlay.Visibility = Visibility.Visible;
    }

    private async void UpdateNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_update is null)
            return;

        UpdateNowButton.IsEnabled = false;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateProgress.Value = 0;
        UpdateMessage.Text = _currentLang == "RU" ? "Скачивание обновления..." : "Downloading update...";

        try
        {
            var progress = new Progress<double>(value => UpdateProgress.Value = value);
            string downloaded = await UpdateService.DownloadAsync(_update, progress, CancellationToken.None);

            if (!UpdateService.VerifyHash(downloaded, _update.Sha256))
                throw new InvalidOperationException(_currentLang == "RU"
                    ? "Проверка хеша не пройдена, файл повреждён."
                    : "Hash check failed, the file is corrupted.");

            UpdateMessage.Text = _currentLang == "RU" ? "Установка..." : "Installing...";
            UpdateService.ApplyAndRestart(downloaded);
        }
        catch (Exception ex)
        {
            UpdateMessage.Text = _currentLang == "RU"
                ? $"Не удалось обновить: {ex.Message}"
                : $"Update failed: {ex.Message}";
            UpdateProgress.Visibility = Visibility.Collapsed;
            UpdateNowButton.IsEnabled = true;
        }
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
        => Navigate(HomePage, IconHome);

    private void NavHistory_Checked(object sender, RoutedEventArgs e)
        => Navigate(HistoryPage, IconHistory);

    private void NavSettings_Checked(object sender, RoutedEventArgs e)
        => Navigate(SettingsPage, IconSettings);

    /// <summary>Переключает страницу и подсветку иконок в сайдбаре.</summary>
    private void Navigate(FrameworkElement page, TextBlock activeIcon)
    {
        if (!IsLoaded) return;

        foreach (var other in new FrameworkElement[] { HomePage, HistoryPage, SettingsPage })
            if (!ReferenceEquals(other, page))
                other.Visibility = Visibility.Collapsed;

        ShowPage(page);

        var white = new SolidColorBrush(Color.FromRgb(0xff, 0xff, 0xff));
        var grey = new SolidColorBrush(Color.FromRgb(0x4d, 0x4d, 0x4d));
        IconHome.Foreground = ReferenceEquals(activeIcon, IconHome) ? white : grey;
        IconHistory.Foreground = ReferenceEquals(activeIcon, IconHistory) ? white : grey;
        IconSettings.Foreground = ReferenceEquals(activeIcon, IconSettings) ? white : grey;
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

    /// <summary>Строит список истории версий из вшитого файла.</summary>
    private void RebuildHistory()
    {
        if (HistoryList is null) return;
        HistoryList.Children.Clear();

        var entries = VersionHistoryService.Load();
        if (entries.Count == 0)
        {
            HistoryList.Children.Add(new TextBlock
            {
                Text = _currentLang == "RU" ? "История пока пуста" : "No history yet",
                Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                FontSize = 11,
                FontFamily = new FontFamily("Segoe UI"),
                Margin = new Thickness(2, 0, 0, 0)
            });
            return;
        }

        Version? current = typeof(MainWindow).Assembly.GetName().Version;
        foreach (var entry in entries)
            HistoryList.Children.Add(BuildHistoryCard(entry, current));
    }

    /// <summary>Собирает карточку одной записи истории: версия, тип и список изменений.</summary>
    private Border BuildHistoryCard(VersionEntry entry, Version? current)
    {
        bool ru = _currentLang == "RU";

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0a, 0x0a, 0x0a)),
            BorderBrush = new SolidColorBrush(entry.IsGlobal
                ? Color.FromRgb(0x2e, 0x2e, 0x2e)
                : Color.FromRgb(0x19, 0x19, 0x19)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18, 16, 18, 16),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var content = new StackPanel();

        // Шапка: версия слева, метки типа справа.
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var version = new TextBlock
        {
            Text = "v" + entry.Version,
            Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xff, 0xff)),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Consolas"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(version, 0);
        header.Children.Add(version);

        var badges = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        badges.Children.Add(CreateBadge(entry.IsGlobal
            ? (ru ? "ГЛОБАЛЬНОЕ" : "MAJOR")
            : (ru ? "ОБЫЧНОЕ" : "MINOR"), filled: entry.IsGlobal));

        if (current is not null && MatchesVersion(entry.Version, current))
            badges.Children.Add(CreateBadge(ru ? "ТЕКУЩАЯ" : "CURRENT", filled: false, highlight: true));

        Grid.SetColumn(badges, 2);
        header.Children.Add(badges);
        content.Children.Add(header);

        // Пункты изменений.
        foreach (string line in ru ? entry.Ru : entry.En)
        {
            var row = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var bullet = new TextBlock
            {
                Text = "—",
                Foreground = new SolidColorBrush(Color.FromRgb(0x3f, 0x3f, 0x3f)),
                FontSize = 12,
                FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(bullet, 0);
            row.Children.Add(bullet);

            var text = new TextBlock
            {
                Text = line,
                Foreground = new SolidColorBrush(Color.FromRgb(0xb5, 0xb5, 0xb5)),
                FontSize = 12,
                FontFamily = new FontFamily("Segoe UI"),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            content.Children.Add(row);
        }

        card.Child = content;
        return card;
    }

    /// <summary>Создаёт метку типа обновления. Залитая — белая с чёрным текстом.</summary>
    private static Border CreateBadge(string text, bool filled, bool highlight = false)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(9, 4, 9, 4),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(filled
                ? Color.FromRgb(0xff, 0xff, 0xff)
                : Color.FromRgb(0x11, 0x11, 0x11)),
            BorderBrush = new SolidColorBrush(filled
                ? Color.FromRgb(0xff, 0xff, 0xff)
                : (highlight ? Color.FromRgb(0x3d, 0x3d, 0x3d) : Color.FromRgb(0x26, 0x26, 0x26))),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(filled
                    ? Color.FromRgb(0x00, 0x00, 0x00)
                    : (highlight ? Color.FromRgb(0xcf, 0xcf, 0xcf) : Color.FromRgb(0x8f, 0x8f, 0x8f))),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas")
            }
        };
    }

    /// <summary>Совпадает ли версия из записи с текущей версией лаунчера.</summary>
    private static bool MatchesVersion(string text, Version current)
    {
        if (!Version.TryParse(text, out var parsed)) return false;
        int entryBuild = parsed.Build < 0 ? 0 : parsed.Build;
        int currentBuild = current.Build < 0 ? 0 : current.Build;
        return parsed.Major == current.Major
               && parsed.Minor == current.Minor
               && entryBuild == currentBuild;
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
            HistoryTitle.Text = "История версий";
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
            HistoryTitle.Text = "Version history";
        }
        UpdateInfoStrip();
        RebuildHistory();
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
