using NLog;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Navigation;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace GIS_ECP_Export
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private IWebDriver Driver;

        private DateTime StartDate;

        private string BasePath = AppDomain.CurrentDomain.BaseDirectory;
        private string RequestsPath;

        // Папка для скриншотов - "screenshots" в папке с логами.
        private string ScreenshotsPath = Path.Combine(GetLogsPath(), "screenshots");

        // Максимальное количество папок с датами в папке скриншотов.
        private const int ScreenshotsFoldersMaxCount = 20;

        private double PercentComplete;
        private string ResponceCountGlobal;
        private double PlusPercent;

        private string Status; // Информация для пользователя.

        private int RequestsPrevSessionCount = -1;

        private int LoadedQueriesCount = 0;

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private CancellationTokenSource _cts;

        /*
         * Настройки.
         */
        private (bool AuthType,
            int NumSign,
            string PinCode,
            string Login,
            string Password,
            int NumOrg,
            string ReguestsFolderPath,
            bool HideConsole,
            bool DownloadOnStart,
            bool AutoCloseApp,
            bool AutoExitGISECP,
            bool Log,
            bool OpenFolder,
            bool ScreenshotCreate) Settings;

        private class ProgressReport
        {
            public string? ProgressText { get; set; }
            public double ProgressVal { get; set; }
            public string? Status { get; set; }
        }

        public MainWindow()
        {
            InitializeComponent();

            // Загружаем настройки.
            LoadSettings();

            StartDate = DateTime.Now.AddDays(-1);

            // Дата за которую скачиваюся запросы.
            StartDateDatePicker.SelectedDate = StartDate;

            // Позиция и размер окна.
            this.Height = Properties.Settings.Default.WindowHeight;
            this.Width = Properties.Settings.Default.WindowWidth;
            this.Top = Properties.Settings.Default.WindowTop;
            this.Left = Properties.Settings.Default.WindowLeft;

            Settings.DownloadOnStart = Properties.Settings.Default.DownloadOnStart;

            this.ContentRendered += MainWindow_ContentRendered;
        }

        // Автоматическое скачивание запросов.
        private async void MainWindow_ContentRendered(object sender, EventArgs e)
        {
            if (Settings.DownloadOnStart)
            {
                await StartCountdownAsync("Автоматическое скачивание запросов через:", 15);

                ActionStart(true);
            }
        }

        // Выбираем дату запросов.
        private void StartDateDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StartDateDatePicker.SelectedDate.HasValue)
            {
                StartDate = StartDateDatePicker.SelectedDate.Value;
            }
            // Деактивируем кнопку "Открыть папку с запросами".
            OpenFolderButton.IsEnabled = false;
        }

        // Диалог "Настройки".
        private void Settings_Button_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SettingsDialog(Settings, BasePath);
            dialog.Owner = this; // Желательно установить владельца окна.

            bool? result = dialog.ShowDialog();

            if (result == true) // Пользователь нажал OK.
            {
                // Загружаем настройки.
                Settings = dialog.SettingsDlg;
            }
        }

        // Вызов "Справки".
        private void Help_Button_Click(object sender, RoutedEventArgs e)
        {
            string pdfPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                @"Res\Help.pdf");

            if (File.Exists(pdfPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = pdfPath,
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show("Файл справки не найден.");
            }
        }

        // Вызов диалога "О программе".
        private void About_Button_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AboutDialog();
            dialog.Owner = this; // Желательно установить владельца окна.

            dialog.ShowDialog();
        }

        // Переход на веб-страницу при нажатии на гиперссылку.
        private void Donate_Button_Click(object sender, RequestNavigateEventArgs e)
        {
            HyperlinkHelper.Navigate(sender, e);
        }

        // Переход на calcs101.ru при нажатии на гиперссылку в нижней полосе.
        private void AdLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            HyperlinkHelper.Navigate(sender, e);
        }

        // Экспорт запросов.
        private void ExportRequestButton_Click(object? sender, RoutedEventArgs? e)
        {
            // Если уже выполняется задача — отменяем её.
            if (_cts != null)
            {
                _cts.Cancel();
                return;
            }

            ActionStart(true);
        }

        // Подписание запросов.
        private void SignRequestsButton_Click(object sender, RoutedEventArgs e)
        {
            // Если уже выполняется задача — отменяем её.
            if (_cts != null)
            {
                _cts.Cancel();
                return;
            }

            ActionStart(false);
        }

        private async void ActionStart(bool startMode)
        {
            try
            {
                ExportButton.Content = "Отмена";

                // Фиксируем время начала работы.
                DateTime taskStart = DateTime.Now;

                // Деактивируем кнопку "Открыть папку с запросами".
                OpenFolderButton.IsEnabled = false;

                StatusTextBlock.Text = "Запуск ===>";

                // Логирование.
                if (Settings.Log) Log.Info("Запуск ===>");
                if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                if (Settings.Log)
                {
                    Log.Info("Состояние настроек: Тип аутентификации - " + (Settings.AuthType ? "вход по электронной подписи" : "войти вручную") + ", Выбрать подпись - подпись " + (Settings.NumSign + 1) + ", Пин-код - " + ((!string.IsNullOrEmpty(Settings.PinCode)) ? "установлен" : "не установлен") + ", Войти как - организация " + (Settings.NumOrg + 1) + ", Папка для запросов - " + ((!string.IsNullOrEmpty(Settings.ReguestsFolderPath)) ? Settings.ReguestsFolderPath : "по умолчанию") + ", Скрывать консоль - " + (Settings.HideConsole ? "да" : "нет") + ", Скачивать запросы при старте - " + (Settings.DownloadOnStart ? "да" : "нет") + ", Автоматическое закрытие - приложения - " + (Settings.AutoCloseApp ? "да" : "нет") + ", Автоматический выход из ГИС ЕЦП - " + (Settings.AutoExitGISECP ? "да" : "нет") + ", Автоматически открывать папку с запросами - " + (Settings.OpenFolder ? "да" : "нет") + ", Сохранять скриншоты - " + (Settings.ScreenshotCreate ? "да" : "нет") + "."
                    );

                    Log.Info($"IWebDriver Assembly: {typeof(IWebDriver).Assembly.FullName}");
                    Log.Info($"IWebDriver Location: {typeof(IWebDriver).Assembly.Location}");
                    Log.Info($"WebDriver type Assembly: {typeof(OpenQA.Selenium.WebDriver).Assembly.FullName}");
                    Log.Info($"WebDriver type Location: {typeof(OpenQA.Selenium.WebDriver).Assembly.Location}");
                }
                if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                // Проверка наличия браузера в системе.
                BrowserDriverProvider browserDriverProvider = new BrowserDriverProvider();

                string? browserPath = browserDriverProvider.CheckBrowserInstall();

                if (browserPath == null)
                {
                    // Логирование.
                    if (Settings.Log) Log.Error("Яндекс.Браузер не установлен. Работа приложения невозможна.");
                    if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                    MessageBox.Show("Яндекс.Браузер не установлен. Работа приложения невозможна.");

                    return;
                }

                // Путь к папке для сохранения файлов.
                if (!string.IsNullOrEmpty(Settings.ReguestsFolderPath))
                {
                    RequestsPath = Path.Combine(Settings.ReguestsFolderPath, StartDate.ToString("yyyy.MM.dd"));
                }
                else
                {
                    string defaultFolder = Path.Combine(BasePath, "Requests");

                    // Проверяем наличие папки для скачивания запросов по умолчанию.
                    if (!Directory.Exists(defaultFolder))
                    {
                        // Создаём папку для скачивания запросов по умолчанию.
                        Directory.CreateDirectory(defaultFolder);
                    }

                    // Путь к папке с запросами в папкке по умолчанию.
                    RequestsPath = Path.Combine(defaultFolder, StartDate.ToString("yyyy.MM.dd"));
                }

                bool exportStart;

                // Создаём рабочую папку.
                exportStart = CheckPrevSessionResult();

                if (exportStart)
                {
                    // Инициализция драйвера и запуск браузера.
                    Driver = browserDriverProvider.DriverInit(browserPath, BasePath, RequestsPath, Settings.HideConsole);

                    // Логирование.
                    if (Settings.Log) Log.Info("Драйвер и браузер запущены.");
                    if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                    // Служебная информация о браузере и драйвере.
                    ServiceInfo();

                    /*
                     * Ожидаем полного отсутствия изменений DOM - полной загрузки страницы.
                     */

                    if (Settings.Log) Log.Info("Попытка перехода в личный кабинет.");
                    if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                    Driver.Navigate().GoToUrl("https://lk-new.sfr.gov.ru/archive/#/request-registers");

                    if (Settings.Log) Log.Info("Ожидание загрузки страницы.");
                    if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                    WaitForPageLoad(Driver);

                    if (Settings.Log) Log.Info("Текущая страница: " + Driver.Url);
                    if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                    var uri = new Uri(Driver.Url);

                    // Выполняем вход, если требуется.
                    if (uri.Host.Equals("esia.gosuslugi.ru", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
                    {
                        StatusTextBlock.Text = "Вход в Госуслуги.";

                        // Логирование.
                        if (Settings.Log) Log.Info("Вход в Госуслуги.");
                        if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                        EnterToGisECP();
                    }

                    ProgressBarTextBlock.Text = "0 из 0";
                    PlusPercent = 0;
                    PercentComplete = 0;

                    ProgressBar.Value = 0; // Сбрасываем ProgressBar.

                    // Создаём токен отмены и прогресс
                    _cts = new CancellationTokenSource();
                    var progress = new Progress<ProgressReport>(report =>
                    {
                        ProgressBarTextBlock.Text = report?.ProgressText ?? "0";
                        ProgressBar.Value = report?.ProgressVal ?? 0;
                        StatusTextBlock.Text = report?.Status ?? "...";
                    });

                    try
                    {
                        // Фоновая работа на потоках пула.
                        if (startMode)
                        {
                            await Task.Run(() => Worker_DoWorkAsync(progress, _cts.Token));
                        }
                        else
                        {
                            await Task.Run(() => SignRequests(progress, _cts.Token));
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        //ExportButton.Content = "Экспорт";
                        StatusTextBlock.Text = "Экспорт отменён";

                        Driver.Quit();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка: {ex.Message}");

                        ExportButton.Content = "Экспорт";
                        StatusTextBlock.Text = string.Empty;
                    }
                    finally
                    {
                        _cts = null;
                        ExportButton.Content = "Экспорт";
                        StatusTextBlock.Text = string.Empty;
                        StatusTextBlock.Text = "";

                        // Закрываем браузер и драйвер.
                        if (Driver != null)
                        {
                            Driver.Quit();
                            Driver.Dispose();
                        }

                        // Логирование.
                        if (Settings.Log)
                        {
                            TimeSpan diff = DateTime.Now - taskStart;
                            Log.Info("Время работы: " + diff.ToString(@"hh\:mm\:ss") + ".");
                        }
                        if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                        // Активируем кнопку "Открыть папку с запросами".
                        OpenFolderButton.IsEnabled = true;

                        // Открывать запросы.
                        if (Settings.OpenFolder) Process.Start("explorer.exe", RequestsPath);
                    }
                }
                else
                {
                    ProgressBarTextBlock.Text = "скачаны";
                    ProgressBar.Value = 100;
                    ExportButton.Content = "Экспорт";

                    StatusTextBlock.Text = "";

                    // Логирование.
                    if (Settings.Log)
                    {
                        TimeSpan diff = DateTime.Now - taskStart;
                        Log.Info("Запросы скачаны ранее. Время работы: " + diff.ToString(@"hh\:mm\:ss") + ".");
                    }
                    if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                    // Активируем кнопку "Открыть папку с запросами".
                    OpenFolderButton.IsEnabled = true;

                    // Открывать запросы.
                    if (Settings.OpenFolder) Process.Start("explorer.exe", RequestsPath);
                }

                if (Settings.AutoCloseApp)
                {
                    // Автоматическое закрытие приложения через 15 сек.
                    await StartCountdownAsync("Приложение закроется через:", 15);

                    this.Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");

                ExportButton.Content = "Экспорт";
                StatusTextBlock.Text = string.Empty;

                // Логирование.
                if (Settings.Log) Log.Error(ex.Message);
                if (Settings.ScreenshotCreate) { CreateScreenshot(); }
            }
        }

        public static void WaitForPageLoad(IWebDriver driver, int timeoutSeconds = 30)
        {
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds));

            wait.Until(d =>
            {
                return ((IJavaScriptExecutor)d)
                    .ExecuteScript("return document.readyState")
                    .Equals("complete");
            });
        }

        // Служебная информация о браузере и драйвере.
        private void ServiceInfo()
        {
            try
            {
                // Отображаем служебную информацию.
                ICapabilities capabilities = ((OpenQA.Selenium.WebDriver)Driver).Capabilities;

                var SeleniumWebDriverVersion = (capabilities.GetCapability("chrome") as Dictionary<string, object>)["chromedriverVersion"].ToString().Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries)[0];

                var SeleniumWebBrowserVersion = capabilities.GetCapability("browserVersion");

                InfoTextBlock.Inlines.Clear();
                string info = "driver ver. - " + SeleniumWebDriverVersion + ", browser ver. - " + SeleniumWebBrowserVersion + ".";
                InfoTextBlock.Inlines.Add(new Run(info));
                // Логирование.
                if (Settings.Log) Log.Info("Инфо: " + info);
                if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                // Проверка соответствия версии драйвера и браузера.
                if (Convert.ToInt32(SeleniumWebDriverVersion.Substring(0, 3)) < Convert.ToInt32(SeleniumWebBrowserVersion.ToString().Substring(0, 3)))
                {
                    // Версии не соответствуют - сообщение об обновлении драйвера и ссылкой на него.
                    var hyperlink = new Hyperlink(new Run("\r\nОбновите драйвер."))
                    {
                        NavigateUri = new Uri("https://github.com/yandex/YandexDriver/releases"),
                        Foreground = Brushes.Red,
                        FontWeight = FontWeights.Bold
                    };

                    hyperlink.Click += (s, e) =>
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = hyperlink.NavigateUri.AbsoluteUri,
                            UseShellExecute = true
                        });
                    };

                    InfoTextBlock.Inlines.Add(hyperlink);
                }
                if (Convert.ToInt32(SeleniumWebDriverVersion.Substring(0, 3)) > Convert.ToInt32(SeleniumWebBrowserVersion.ToString().Substring(0, 3)))
                {
                    InfoTextBlock.Inlines.Add(new Run("\r\nОбновите браузер.") { Foreground = Brushes.Red, FontWeight = FontWeights.Bold });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");

                // Логирование.
                if (Settings.Log) Log.Error(ex.Message);
                if (Settings.ScreenshotCreate) { CreateScreenshot(); }
            }
        }

        // Вход в ГИС ЕЦП.
        private void EnterToGisECP()
        {
            IWebElement element;

            WebDriverWait wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(300));
            wait.PollingInterval = TimeSpan.FromMilliseconds(200);

            bool result;

            /*
             * Вход по электронной подписи
             */
            if (Settings.AuthType)
            {
                // Ищем кнопку "Электронная подпись".
                result = ElementCheck(By.XPath("//button[contains(., 'Электронная подпись')]"));

                if (!result)
                {
                    MessageBox.Show("Аутентификация: Не найдена кнопка \"Электронная подпись\". Процесс прерван.");

                    // Логирование.
                    if (Settings.Log) Log.Error("Аутентификация: Не найдена кнопка \"Электронная подпись\". Процесс прерван.");
                    if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                    return;
                }

                // Нажимаем кнопку "Электронная подпись".
                element = Driver.FindElement(By.XPath("//button[contains(., 'Электронная подпись')]"));
                HighlightAndClick(Driver, element);

                // Ищем кнопку "Продолжить".
                result = ElementCheck(By.XPath("/html/body/esia-root/div/esia-login/div/div[1]/esia-eds/div/button"));

                if (!result)
                {
                    MessageBox.Show("Аутентификация: Не найдена кнопка \"Продолжить\". Процесс прерван.");

                    // Логирование.
                    if (Settings.Log) Log.Error("Аутентификация: Не найдена кнопка \"Продолжить\". Процесс прерван.");
                    if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                    return;
                }

                // Нажимаем на кнопку "Продолжить".
                element = Driver.FindElement(By.XPath("/html/body/esia-root/div/esia-login/div/div[1]/esia-eds/div/button"));
                HighlightAndClick(Driver, element);

                // Ищем первую доступную электронную подпись.
                result = ElementCheck(By.XPath("/html/body/esia-root/div/esia-login/div/div[1]/esia-eds/div/button[" + (Settings.NumSign + 1) + "]/h3"));

                if (!result)
                {
                    MessageBox.Show("Аутентификация: нет доступной электронной подписи. Процесс прерван.");

                    // Логирование.
                    if (Settings.Log) Log.Error("Аутентификация: нет доступной электронной подписи. Процесс прерван.");
                    if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                    return;
                }

                // Выбираем электронную подпись.
                element = Driver.FindElement(By.XPath("/html/body/esia-root/div/esia-login/div/div[1]/esia-eds/div/button[" + (Settings.NumSign + 1) + "]/h3"));
                HighlightAndClick(Driver, element);

                // Вводим пин-код в диалоге Госплагина.
                Task.Run(() =>
                {
                    bool enterResult = GospluginHelper.EnterPassword(Settings.Log, Log, Settings.PinCode, Settings.ScreenshotCreate, ScreenshotsPath);

                    if (!enterResult)
                    {
                        if (Settings.Log) Log.Info("Не удалось выполнить вход по ЭЦП. Процесс прерван.");
                        if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                        MessageBox.Show("Не удалось выполнить вход по ЭЦП. Процесс прерван.");

                        return;
                    }
                });

                // Ищем организацию для входа.
                result = ElementCheck(By.XPath("/html/body/section/div/div/div/div[1]/div/div[" + (Settings.NumOrg + 1) + "]/div/label"));

                if (!result)
                {
                    if (Settings.Log) Log.Info("Не удалось выбрать организацию в ЛК. Процесс прерван.");
                    if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                    MessageBox.Show("Не удалось выбрать организацию в ЛК. Процесс прерван.");

                    return;
                }

                // Нажимаем на организацию для входа.
                element = Driver.FindElement(By.XPath("/html/body/section/div/div/div/div[1]/div/div[" + (Settings.NumOrg + 1) + "]/div/label"));
                HighlightAndClick(Driver, element);
            }
            else
            { /*
               * Вход вручную.
               */
                var dialog = new EnterManuallyDialog();
                dialog.Owner = this; // Желательно установить владельца окна.

                dialog.ShowDialog();
            }
        }

        // Таймер задержки.
        private async Task StartCountdownAsync(string message, int delay)
        {
            for (int timer = delay; timer > 0; timer--)
            {
                StatusTextBlock.Text = message + " " + timer + " сек.";

                await Task.Delay(1000);
            }

            StatusTextBlock.Text = "";
        }

        // Скачивание запросов.
        private void Worker_DoWorkAsync(IProgress<ProgressReport> progress, CancellationToken token)
        {
            try
            {
                // Локальная функция для отправки прогресса.
                void Report()
                {
                    var rep = new ProgressReport
                    {
                        ProgressText = ResponceCountGlobal,
                        ProgressVal = PercentComplete,
                        Status = Status
                    };

                    progress?.Report(rep);
                }

                // Отмена операции.
                token.ThrowIfCancellationRequested();

                IWebElement element;
                bool result;

                WebDriverWait wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(60)); // Ожидание.
                wait.PollingInterval = TimeSpan.FromMilliseconds(200); // Интервал запросов.

                string notDownloadedResponse = "";

                int numCommon = 1;
                int num = 1;

                // Переключаемся на открытую вкладку с запросом. [0]- default, [1] - new window
                IList<string> windowHandles;

                windowHandles = new List<string>(Driver.WindowHandles);

                Driver.SwitchTo().Window(windowHandles[0]);

                // Выбираем запросы по дате.
                Driver.Navigate().GoToUrl("https://lk-new.sfr.gov.ru/archive/#/request-registers?commonArchiveRequests_requestDate_begin=" + StartDate.ToString("yyyy-MM-dd") + "T00%3A00%3A00&commonArchiveRequests_requestDate_end=" + StartDate.ToString("yyyy-MM-dd") + "T23%3A59%3A59&w2_requestDate_begin=" + StartDate.ToString("yyyy-MM-dd") + "T00 %3A00%3A00&w2_requestDate_end=" + StartDate.ToString("yyyy-MM-dd") + "T23%3A59%3A59");

                Driver.Navigate().Refresh();

                if (!Driver.Url.StartsWith("https://lk-new.sfr.gov.ru/")) // TODO Редко, но вход в ГИС ЕЦП не происходит. В связи с подвисанием на интерфейсе Госуслуг. Разобраться в причинах.
                {
                    // Логирование.
                    if (Settings.Log) Log.Error("Не выполнен вход в ГИС ЕЦП. Процесс прерван.");
                    if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("Не выполнен вход в ГИС ЕЦП. Процесс прерван.");
                    });

                    return;
                }

                Regex responseCountRegex = new Regex("[0-9]+");

                // Ищем "Найдено XX результатов" внизу страницы.
                if (ElementCheck(By.ClassName("totalPaginationCount")))
                {
                    // Отмена операции.
                    token.ThrowIfCancellationRequested();

                    Status = "Есть запросы. Ожидаем готовности реестра с запросами.";
                    Report();

                    // Логирование.
                    if (Settings.Log) Log.Info("Есть запросы. Ожидаем готовности реестра с запросами.");
                    if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                    // Создаём папку.
                    DirectoryInfo dir = Directory.CreateDirectory(RequestsPath);

                    // Получаем значение с кол-вом результатов.
                    string responseCountRaw = Driver.FindElement(By.ClassName("totalPaginationCount")).Text;

                    // Кол-во запросов на скачивание.
                    double responseCount = Convert.ToInt32(responseCountRegex.Match(responseCountRaw).ToString());

                    // Ожидаем готовность реестра с запросами.
                    result = ElementCheck(By.ClassName("n2o-advanced-table-content"));

                    if (!result)
                    {
                        Status = "Реестр с запросами не готов. Процесс прерван.";
                        Report();

                        // Логирование.
                        if (Settings.Log) Log.Error("Реестр с запросами не готов. Процесс прерван.");
                        if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show("Реестр с запросами не готов. Процесс прерван.");
                        });

                        return;
                    }

                    PlusPercent = 100 / responseCount;
                    ResponceCountGlobal = "0 из " + responseCount;
                    PercentComplete = 0;
                    Report();

                    WebDriverWait waitHandle = new WebDriverWait(Driver, TimeSpan.FromSeconds(120));

                    // Докачка запросов с предыдущей сессии.
                    if (RequestsPrevSessionCount > 0)
                    {
                        int b = RequestsPrevSessionCount / 50;

                        // Запросов > 50 - переходим на нужную страницу.
                        if (b > 0)
                        {
                            for (var n = 0; n < b; n++)
                            {
                                // Щелкаем на позиции.
                                element = Driver.FindElement(By.ClassName("next-button"));
                                HighlightAndClick(Driver, element);

                                Thread.Sleep(3000);
                            }

                            // Пролистываем страницу вверх.
                            ((IJavaScriptExecutor)Driver).ExecuteScript("window.scrollTo(0, 0);");

                            num = RequestsPrevSessionCount - b * 50 + 1;
                            numCommon = RequestsPrevSessionCount + 1;
                        }
                        else
                        { // Запросов < 50 - работаем на текущей странице.

                            num = RequestsPrevSessionCount + 1; // Номер скачиваемого запроса.
                            numCommon = RequestsPrevSessionCount + 1;
                        }

                        ResponceCountGlobal = RequestsPrevSessionCount + " из " + responseCount;
                        PercentComplete = PlusPercent * RequestsPrevSessionCount;
                        Report();
                    }

                    Status = "Скачиваем запросы из реестра.";
                    Report();

                    // Логирование.
                    if (Settings.Log) Log.Info("Скачиваем запросы из реестра за " + StartDate.ToString("dd.MM.yyyy") + ".");
                    if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                    // Получаем позиции из реестра с запросами.
                    while (numCommon <= responseCount)
                    {
                        // TODO Логирование.
                        if (Settings.Log && numCommon > 0) Log.Info("Экспортируем запрос.");

                        // Отмена операции.
                        token.ThrowIfCancellationRequested();

                        // Логирование.
                        if (Settings.Log) Log.Info("Открыто вкладок: " + Driver.WindowHandles.Count + ". Ожидаем закрытия лишнихвкладок.");
                        if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                        // Ожидаем закрытия лишних вкладок. Должна быть открыта 1.
                        waitHandle.Until(d => d.WindowHandles.Count == 1);

                        // Логирование.
                        if (Settings.Log) Log.Info("Работаем со строкой (запросом) из реестра.");
                        if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                        // Работаем со строкой (запросом) из реестра.
                        StableClick(Driver, By.CssSelector("tr.n2o-advanced-table-row--with-action.row-click"), num - 1);

                        // Переключаемся на открытую вкладку c запросом.
                        Driver.SwitchTo().Window(Driver.WindowHandles.Last());

                        // Ожидаем открытия 2 вкладки. Должна быть открыта 2 с запросом.
                        waitHandle.Until(d => d.WindowHandles.Count == 2);

                        // Логирование.
                        if (Settings.Log) Log.Info("Открыта вкладка с запросом.");
                        if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                        // Ожидаем доступности кнопки "Скачать запрос".
                        ReloadPage(progress, By.XPath("//span[contains(.,'Скачать запрос')]"), token);

                        // Скачиваем запрос.
                        element = Driver.FindElement(By.XPath("//span[contains(.,'Скачать запрос')]"));
                        HighlightAndClick(Driver, element);

                        // Логирование.
                        if (Settings.Log) Log.Info("Кнопка \"Скачать запрос\" нажата");
                        if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                        // Ожидаем доступности Ф.И.О. гражданина.
                        result = ElementCheck(By.XPath("//*[@id=\"n2o-root\"]/div/section/div/section/div[2]/div/div/div/div/div/div[3]/div[3]/div/div[1]/div[3]/div/div/div[5]/div[2]/div[1]/div/div/div/div[1]"));

                        if (!result)
                        {
                            Status = "Ф.И.О. гражданина недоступно. Процесс прерван.";
                            Report();

                            // Логирование.
                            if (Settings.Log) Log.Error("Ф.И.О. гражданина недоступно. Процесс прерван.");
                            if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                MessageBox.Show("Ф.И.О. гражданина недоступно. Процесс прерван.");
                            });

                            return;
                        }

                        // Копируем ФИО гражданина.
                        string citizenName = Driver.FindElement(By.XPath("//*[@id=\"n2o-root\"]/div/section/div/section/div[2]/div/div/div/div/div/div[3]/div[3]/div/div[1]/div[3]/div/div/div[5]/div[2]/div[1]/div/div/div/div[1]")).Text;

                        // Логирование.
                        if (Settings.Log) Log.Info("Ожидание скачивания файла.");
                        if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                        // Ожидаем скачивание файла.
                        WaitForDownloadLoop(progress, numCommon, ref notDownloadedResponse, token);

                        // Путь к последнему скачанному файлу.
                        string lastFile = new DirectoryInfo(RequestsPath)
                            .GetFiles("*.pdf")
                            .OrderByDescending(f => f.LastWriteTime)
                            .ThenBy(f => f.Name)
                            .Select(f => f.FullName)
                        .ToArray()[0];

                        string? pdfError;
                        bool pdfValid = false;

                        while (!pdfValid)
                        {
                            pdfValid = IsPdfValid(lastFile, out pdfError);

                            if (!pdfValid)
                            {
                                Status = $"Невалидный PDF: {lastFile}. Ошибка: {pdfError}. Повторное скачивание.";
                                Report();

                                // Логирование ошибки, уведомление пользователя и пропуск переименования
                                if (Settings.Log)
                                {
                                    Log.Error($"Невалидный PDF: {lastFile}. Ошибка: {pdfError}. Повторное скачивание.");
                                }
                                if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                                if (File.Exists(lastFile))
                                {
                                    File.Delete(lastFile);
                                }

                                // Пытаемся скачать файл заново.
                                WaitForDownloadLoop(progress, numCommon, ref notDownloadedResponse, token);
                            }
                        }

                        // Переименовываем файл только если он валиден
                        RenameFile(citizenName);

                        // Закрываем вкладку.
                        Driver.Close();

                        // Логирование.
                        if (Settings.Log) Log.Info("Вкладка с запросом закрыта.");
                        if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                        // Переключаемся на первую вкладку.
                        Driver.SwitchTo().Window(Driver.WindowHandles[0]);

                        // Логирование.
                        if (Settings.Log) Log.Info("Переход на первую вкладку выполнен.");
                        if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                        // Переходим на следующую страницу.
                        if (numCommon % 50 == 0 && numCommon != responseCount)
                        {
                            // Щелкаем на позиции.
                            element = Driver.FindElement(By.ClassName("next-button"));
                            HighlightAndClick(Driver, element);

                            // Пролистываем страницу вверх.
                            ((IJavaScriptExecutor)Driver).ExecuteScript("window.scrollTo(0, 0);");

                            num = 0;
                        }

                        PercentComplete += PlusPercent;
                        ResponceCountGlobal = numCommon + " из " + responseCount;
                        Report();

                        num++;
                        numCommon++;

                        // Логирование.
                        if (Settings.Log) Log.Info("Экспорт запросов: " + ResponceCountGlobal + ".");
                        if (Settings.ScreenshotCreate) { CreateScreenshot(); }
                    }

                    if (notDownloadedResponse.Length > 0)
                    {
                        notDownloadedResponse = "Не скачаны запросы:" + notDownloadedResponse;

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show(notDownloadedResponse);
                        });
                    }

                    PercentComplete = 100;
                    Report();

                    if (Settings.Log) Log.Info("Консолидация запросов в единый PDF документ.");
                    if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                    // Объединяем PDF файлы запросов в единый файл для удобства дальнейшей печати.
                    PdfMerge();

                    // Сохраняем кол-во скачанных запросов.
                    LoadedQueriesCount += numCommon - 1;

                    num = 1;
                    numCommon = 1;
                }
                else
                {
                    ResponceCountGlobal = "нет запросов";

                    PercentComplete = 100;
                    Report();

                    // Логирование.
                    if (Settings.Log) Log.Info("Нет запросов.");
                    if (Settings.ScreenshotCreate) { CreateScreenshot(); }
                }

                // Отмена операции.
                token.ThrowIfCancellationRequested();

                // Очищаем куки для выхода из ГИС ЕЦП.
                if (Settings.AutoExitGISECP)
                {
                    ClearDomain(Driver, "https://lk-new.sfr.gov.ru");
                    ClearDomain(Driver, "https://sso.sfr.gov.ru");
                    ClearDomain(Driver, "https://esia.gosuslugi.ru");
                    ClearDomain(Driver, "https://gosuslugi.ru");

                    Status = $"Выход из ЛК. Очистка cookie файлов.";
                    Report();

                    Log.Info("Выход из ЛК. Очистка cookie файлов.");
                }

                // Сохраняем позицию и размер браузера.
                Properties.Settings.Default.BrowserWindowSize = Driver.Manage().Window.Size;
                Properties.Settings.Default.BrowserWindowPosition = Driver.Manage().Window.Position;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");

                ExportButton.Content = "Экспорт";
                StatusTextBlock.Text = string.Empty;

                // Логирование.
                if (Settings.Log) Log.Error(ex.Message);
                if (Settings.ScreenshotCreate) { CreateScreenshot(); }
            }
        }

        // Загрузка настроек.
        private void LoadSettings()
        {
            Settings = (Properties.Settings.Default.AuthType,
                        Properties.Settings.Default.NumSign,
                        Properties.Settings.Default.PinCode,
                        Properties.Settings.Default.Login,
                        Properties.Settings.Default.Password,
                        Properties.Settings.Default.NumOrg,
                        Properties.Settings.Default.ReguestsFolderPath,
                        Properties.Settings.Default.HideConsole,
                        Properties.Settings.Default.DownloadOnStart,
                        Properties.Settings.Default.AutoCloseApp,
                        Properties.Settings.Default.AutoExitGISECP,
                        Properties.Settings.Default.Log,
                        Properties.Settings.Default.OpenFolder,
                        Properties.Settings.Default.ScreenshotCreate);
        }

        // Открыть папку с запросами.
        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Открывать запросы.
                Process.Start("explorer.exe", RequestsPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");

                // Логирование.
                if (Settings.Log) Log.Error(ex.Message);
                if (Settings.ScreenshotCreate) { CreateScreenshot(); }
            }
        }

        // Проверяет корректное завершение предыдущей рабочей сессии.
        private bool CheckPrevSessionResult()
        {
            // Папка существует?
            if (Directory.Exists(RequestsPath))
            {
                // Проверяем, есть ли файлы с предыдущей загрузки.
                string[] myFiles = new DirectoryInfo(RequestsPath).GetFiles("*.pdf").OrderByDescending(f => f.LastWriteTime).ThenBy(f => f.Name).Select(f => f.FullName).ToArray();

                if (myFiles.Count() > 0)
                {
                    // Проверяем были ли загружены все файлы.
                    string s = myFiles[0];
                    bool Cont = s.Contains("Merged");

                    if (Cont)
                    {
                        return false;
                    }

                    RequestsPrevSessionCount = myFiles.Count();
                }
            }

            return true;
        }

        // Проверяем существует ли элемент.
        private bool ElementCheck(By elementPath, int waitTime = 90)
        {
            WebDriverWait wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(waitTime)); // Ожидание 90 сек.
            wait.PollingInterval = TimeSpan.FromMilliseconds(200);

            bool result;

            try
            {
                var element = wait.Until(driver =>
                {
                    try
                    {
                        return driver.FindElement(
                            elementPath);
                    }
                    catch (NoSuchElementException)
                    {
                        return null;
                    }
                });

                result = element.Displayed;
            }
            catch (WebDriverTimeoutException)
            {
                result = false; // элемент не найден или не стал кликабельным вовремя
            }
            catch (NoSuchElementException)
            {
                result = false; // элемент вообще не существует
            }

            return result;
        }

        // Клик по позиции.
        public static void StableClick(IWebDriver driver, By by, int index = 0)
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    var rows = driver.FindElements(by);
                    var el = rows[index];
                    HighlightAndClick(driver, el);
                    return;
                }
                catch (StaleElementReferenceException)
                {
                    Thread.Sleep(100);
                }
                catch (ElementClickInterceptedException)
                {
                    Thread.Sleep(100);
                }
            }

            throw new Exception("Не удалось выполнить стабильный клик по элементу");
        }

        // Обновление страницы, пока к её элементам не будет доступа.
        private void ReloadPage(IProgress<ProgressReport> progress, By by, CancellationToken token)
        {
            // Локальная функция для отправки прогресса.
            void Report()
            {
                var rep = new ProgressReport
                {
                    ProgressText = ResponceCountGlobal,
                    ProgressVal = PercentComplete,
                    Status = Status
                };

                progress?.Report(rep);
            }

            /*
             * Ожидаем доступности кнопки "Скачать запрос". Перезагружаем страницу при необходимости.
             */
            bool pageRespExist = false;
            int pageRespExistCount = 0;

            while (!pageRespExist)
            {
                token.ThrowIfCancellationRequested();

                if (IsElementPresent(by))
                {
                    pageRespExist = true;

                    Status = "";
                    Report();
                }
                else
                {
                    pageRespExistCount++;

                    if (pageRespExistCount > 10)
                    {
                        Status = "Задержка в работе ГИС ЕЦП. Обновляем страницу с запросом.";
                        Report();

                        if (Settings.Log) Log.Info("Задержка в работе ГИС ЕЦП. Обновляем страницу с запросом.");
                        if (Settings.ScreenshotCreate) { CreateScreenshot(); }

                        // Обновляем страницу.
                        token.ThrowIfCancellationRequested();
                        Driver.Navigate().Refresh();

                        token.ThrowIfCancellationRequested();
                        Thread.Sleep(pageRespExistCount);

                        pageRespExistCount = 0;
                    }
                    else
                    {
                        if (pageRespExistCount > 1)
                        {
                            for (var time = pageRespExistCount; time > 0; time--)
                            {
                                token.ThrowIfCancellationRequested();

                                // Передаём статус - "Ожидание: xx сек."
                                Status = "Ожидание: " + time + " сек. Попытка " + (pageRespExistCount - 1) + ".";
                                Report();

                                token.ThrowIfCancellationRequested();
                                Thread.Sleep(1000);
                            }
                        }
                        else
                        {
                            token.ThrowIfCancellationRequested();

                            Thread.Sleep(1000);
                        }
                    }
                }
            }
        }

        // Проверка существования элемента.
        private bool IsElementPresent(By by)
        {
            try
            {
                Driver.FindElement(by);
                return true;
            }
            catch (NoSuchElementException)
            {
                return false;
            }
        }

        // Переименовываем файл.
        private void RenameFile(string citizenName)
        {
            // Переименовываем скачанный файл в соответствии с шаблоном "ФИО гражданина - Запрос в архив №ЗСПО-324-000071079".
            string[] myFiles = new DirectoryInfo(RequestsPath).GetFiles("*.pdf").OrderByDescending(f => f.LastWriteTime).ThenBy(f => f.Name).Select(f => f.FullName).ToArray();

            string[] myFilesShort = new DirectoryInfo(RequestsPath).GetFiles("*.pdf").OrderByDescending(f => f.LastWriteTime).ThenBy(f => f.Name).Select(f => f.Name).ToArray();

            Random rnd = new Random();

            System.IO.File.Move(myFiles[0], RequestsPath + "\\" + citizenName + "_" + rnd.Next(2048) + " - " + myFilesShort[0]);
        }

        // Объединяем PDF файлы запросов в единый файл для удобства дальнейшей печати.
        private void PdfMerge()
        {
            // Массив путей к файлам.
            string[] myFiles2 = new DirectoryInfo(RequestsPath).GetFiles("*.pdf").OrderByDescending(f => f.LastWriteTime).ThenBy(f => f.Name).Select(f => f.FullName).ToArray();

            PdfDocument inputDocument;

            // Создаём конечный PDF.
            PdfDocument outputDocument = new PdfDocument();

            // Показать последовательные страницы в развороте. Требуется Acrobat 5 или выше.
            outputDocument.PageLayout = PdfPageLayout.TwoColumnLeft;

            // Страница документа.
            PdfPage page;

            foreach (string file in myFiles2)
            {
                // Открываем документ.
                inputDocument = PdfReader.Open(@file, PdfDocumentOpenMode.Import);

                for (var num2 = 0; num2 < inputDocument.Pages.Count; num2++)
                {
                    // Читаем страницу из исходного документа.
                    page = inputDocument.Pages[num2];

                    // Копируем страницу в конечный документ.
                    page = outputDocument.AddPage(page);

                    // Добавляем пустую страницу к запросам, имеющим нечётное кол-во страниц. Для того, чтобы исключить ситуацию, когда на одном листе напечатаны 2 запроса.
                    if (inputDocument.Pages.Count == 1 && num2 == 0 || inputDocument.Pages.Count == 3 && num2 == 2)
                    {
                        page = outputDocument.AddPage();
                    }
                }
            }

            // Сохраняем конечный PDF.
            string filename2 = RequestsPath + "\\" + StartDate.ToString("yyyy.MM.dd") + "_Merged.pdf";

            outputDocument.Save(filename2);
        }

        // Очистка куки по доменному имени. Выход из ГИС ЕЦП.
        private void ClearDomain(IWebDriver driver, string url)
        {
            driver.Navigate().GoToUrl(url);
            var cookies = driver.Manage().Cookies.AllCookies;

            foreach (var cookie in cookies)
                driver.Manage().Cookies.DeleteCookie(cookie);
        }

        // Проверка валидности PDF файла.
        private bool IsPdfValid(string filePath, out string? error)
        {
            error = null;

            try
            {
                using var document = PdfReader.Open(filePath);
                return document.PageCount > 0;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // Делаем скриншот экрана и сохраняем его в папку "screenshots" с логами.
        private void CreateScreenshot()
        {
            CreateScreenshot(ScreenshotsPath);
        }

        // Папка с логами - см. "fileName" файлового лога в NLog.config.
        private static string GetLogsPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        }

        // Ротация папок с датами - оставляем не более ScreenshotsFoldersMaxCount последних.
        private static void RotateScreenshotsFolders(string screenshotsPath)
        {
            try
            {
                // Отбираем только папки с датами в формате "yyyy.MM.dd" и сортируем от новых к старым.
                DirectoryInfo[] dateFolders = new DirectoryInfo(screenshotsPath)
                    .GetDirectories()
                    .Where(d => DateTime.TryParseExact(d.Name, "yyyy.MM.dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
                    .OrderByDescending(d => d.Name)
                    .ToArray();

                // Удаляем лишние папки - самые старые.
                foreach (DirectoryInfo dateFolder in dateFolders.Skip(ScreenshotsFoldersMaxCount))
                {
                    dateFolder.Delete(true);
                }
            }
            catch (Exception ex)
            {
                LogManager.GetCurrentClassLogger().Error("Не удалось выполнить ротацию папок со скриншотами: " + ex.Message);
            }
        }

        // Делаем скриншот экрана и сохраняем его в папку с текущей датой.
        public static void CreateScreenshot(string? screenshotsPath)
        {
            // Путь к папке для скриншотов не передан.
            if (string.IsNullOrEmpty(screenshotsPath)) return;

            // Папка со скриншотами.
            string screenshotsRootPath = screenshotsPath!;

            try
            {
                // Проверяем наличие папки для скриншотов.
                if (!Directory.Exists(screenshotsRootPath))
                {
                    Directory.CreateDirectory(screenshotsRootPath);
                }

                // Текущая дата - папка рабочей сессии.
                string dateNow = DateTime.Now.ToString("yyyy.MM.dd");

                string datePath = Path.Combine(screenshotsRootPath, dateNow);

                // Проверяем наличие папки с текущей датой.
                if (!Directory.Exists(datePath))
                {
                    Directory.CreateDirectory(datePath);
                }

                // Удаляем старые папки с датами.
                RotateScreenshotsFolders(screenshotsRootPath);

                // Размеры виртуального экрана - с учётом всех мониторов.
                int screenWidth = System.Windows.Forms.SystemInformation.VirtualScreen.Width;
                int screenHeight = System.Windows.Forms.SystemInformation.VirtualScreen.Height;
                int screenLeft = System.Windows.Forms.SystemInformation.VirtualScreen.Left;
                int screenTop = System.Windows.Forms.SystemInformation.VirtualScreen.Top;

                using System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(screenWidth, screenHeight, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

                using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(screenLeft, screenTop, 0, 0, new System.Drawing.Size(screenWidth, screenHeight));
                }

                // Параметры кодирования JPEG.
                System.Drawing.Imaging.ImageCodecInfo jpegCodec = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);

                System.Drawing.Imaging.EncoderParameters jpegParams = new System.Drawing.Imaging.EncoderParameters(1);

                jpegParams.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)90);

                // Имя файла - текущая дата и время с секундами.
                string screenshotName = "Screenshot_" + DateTime.Now.ToString("yyyy.MM.dd_HH.mm.ss");

                // На несколько скриншотов в одну секунду - добавляем порядковый номер.
                int num = 1;

                while (true)
                {
                    string screenshotPath = Path.Combine(datePath, screenshotName + (num > 1 ? "_" + num : "") + ".jpg");

                    if (!File.Exists(screenshotPath)) break;

                    num++;
                }

                // Сохраняем скриншот в формате JPEG.
                bitmap.Save(Path.Combine(datePath, screenshotName + (num > 1 ? "_" + num : "") + ".jpg"), jpegCodec, jpegParams);
            }
            catch (Exception ex)
            {
                LogManager.GetCurrentClassLogger().Error("Не удалось сохранить скриншот: " + ex.Message);
            }
        }

        // Ожидаем скачивание файла.
        // TODO Тестирование, отладка.
        private void WaitForDownloadLoop(IProgress<ProgressReport> progress, int numCommon, ref string notDownloadedResponse, CancellationToken token)
        {
            // Локальная функция для отправки прогресса.
            void Report()
            {
                var rep = new ProgressReport
                {
                    ProgressText = ResponceCountGlobal,
                    ProgressVal = PercentComplete,
                    Status = Status
                };

                progress?.Report(rep);
            }

            string[] checkFilesShort;

            int exitCount = 0;

            for (var pageRespExistCount = 0; pageRespExistCount < 60; pageRespExistCount++)
            {
                token.ThrowIfCancellationRequested();

                checkFilesShort = new DirectoryInfo(RequestsPath)
                    .GetFiles("*.pdf")
                    .OrderByDescending(f => f.LastWriteTime)
                    .ThenBy(f => f.Name)
                    .Select(f => f.Name)
                    .ToArray();

                // Сравниваем количество скачанных файлов с ожидаемым.
                if (checkFilesShort.Length != numCommon)
                {
                    if (pageRespExistCount > 1)
                    {
                        for (var time = pageRespExistCount; time > 0; time--)
                        {
                            token.ThrowIfCancellationRequested();

                            // Передаём статус - "Ожидание: xx сек."
                            Status = "Ожидание: " + time + " сек. Попытка " + (pageRespExistCount - 1) + ".";
                            Report();

                            token.ThrowIfCancellationRequested();

                            Thread.Sleep(1000);
                        }
                    }
                    else
                    {
                        token.ThrowIfCancellationRequested();

                        Thread.Sleep(1000);
                    }
                }
                else
                {
                    // Очищаем статус.
                    Status = "";
                    Report();

                    break;
                }

                // Каждые 5 итераций пробуем обновить страницу и заново инициировать скачивание.
                if (pageRespExistCount > 0 && pageRespExistCount % 5 == 0)
                {
                    if (exitCount == 100)
                    {
                        notDownloadedResponse += " " + numCommon;
                        break;
                    }

                    // Обновляем страницу.
                    Driver.Navigate().Refresh();

                    // Ожидаем доступности кнопки "Скачать запрос".
                    ReloadPage(progress, By.XPath("//span[contains(.,'Скачать запрос') ]"), token);

                    // Скачиваем запрос повторно.
                    var element = Driver.FindElement(By.XPath("//span[contains(.,'Скачать запрос')]"));
                    HighlightAndClick(Driver, element);

                    exitCount++;
                }
            }
        }

        // Клик с подсветкой элемента.
        private static void HighlightAndClick(IWebDriver driver, IWebElement element)
        {
            IJavaScriptExecutor js =
                (IJavaScriptExecutor)driver;

            js.ExecuteScript(
                "arguments[0].style.border='3px solid red';" +
                "arguments[0].style.background='yellow';" +
                "arguments[0].style.padding='0px 4px 0px 4px';",
                element);

            Thread.Sleep(300);

            element.Click();
        }

        // Закрытие приложения.
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // Сохраняем позицию и размер окна приложения.
            Properties.Settings.Default.WindowHeight = this.Height;
            Properties.Settings.Default.WindowWidth = this.Width;
            Properties.Settings.Default.WindowTop = this.Top;
            Properties.Settings.Default.WindowLeft = this.Left;

            Properties.Settings.Default.AuthType = Settings.AuthType;
            Properties.Settings.Default.NumSign = Settings.NumSign;
            Properties.Settings.Default.PinCode = Settings.PinCode;
            Properties.Settings.Default.Login = Settings.Login;
            Properties.Settings.Default.Password = Settings.Password;
            Properties.Settings.Default.NumOrg = Settings.NumOrg;
            Properties.Settings.Default.ReguestsFolderPath = Settings.ReguestsFolderPath;
            Properties.Settings.Default.HideConsole = Settings.HideConsole;
            Properties.Settings.Default.DownloadOnStart = Settings.DownloadOnStart;
            Properties.Settings.Default.AutoCloseApp = Settings.AutoCloseApp;
            Properties.Settings.Default.AutoExitGISECP = Settings.AutoExitGISECP;
            Properties.Settings.Default.Log = Settings.Log;
            Properties.Settings.Default.OpenFolder = Settings.OpenFolder;
            Properties.Settings.Default.ScreenshotCreate = Settings.ScreenshotCreate;

            Properties.Settings.Default.LoadedQueriesCount += LoadedQueriesCount;

            Properties.Settings.Default.Save();
        }
    }
}