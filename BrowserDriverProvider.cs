using NLog;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System;
using System.IO;

namespace GIS_ECP_Export
{
    // Общий функционал.
    public class BrowserDriverProvider
    {
        // Проверка наличия браузера в системе.
        public string? CheckBrowserInstall()
        {
            string[] paths =
            {
                @"C:\Program Files\Yandex\YandexBrowser\Application\browser.exe",
                @"C:\Program Files (x86)\Yandex\YandexBrowser\Application\browser.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Yandex\YandexBrowser\Application\browser.exe")
            };

            foreach (var p in paths)
            {
                if (File.Exists(p)) return p;
            }

            return null;
        }

        // Инициализция драйвера и запуск браузера.
        public IWebDriver DriverInit(string browserPath, string basePath, string requestsPath, bool hideConsole)
        {
            // Опции запуска.
            var options = new ChromeOptions();

            // Используем альтернативный профиль браузера для избежания конфликтов с основным.
            options.AddArguments("--user-data-dir=" + Path.Combine(basePath, "AltBrowserProfile"));

            // Указываем путь к Яндекс.Браузеру.
            options.BinaryLocation = browserPath;

            // Путь к запросам.
            options.AddUserProfilePreference("download.default_directory", requestsPath);

            ChromeDriverService chromeDriverService;

            string driverPath = Path.Combine(basePath, @"Res\yandexdriver.exe");

            chromeDriverService = ChromeDriverService.CreateDefaultService(driverPath);

            // Отключаем проверку на соответствие версии драйвера и версии браузера. Возможна нестабильная работа.
            chromeDriverService.DisableBuildCheck = true;

            // Отображаем / скрываем консоль с отладочными данными.
            chromeDriverService.HideCommandPromptWindow = hideConsole;

            // Увеличиваем таймаут драйвера с 60 сек. по умолчанию до 2 минут, чтобы драйвер дождался загрузки браузера.
            var cmdTimeout = TimeSpan.FromSeconds(180);

            var driver = new ChromeDriver(chromeDriverService, options, cmdTimeout);

            try
            {
                driver.Manage().Window.Size = Properties.Settings.Default.BrowserWindowSize; // new System.Drawing.Size(1200, 1080);
                driver.Manage().Window.Position = Properties.Settings.Default.BrowserWindowPosition; // new System.Drawing.Point(0, 0);
            }
            catch
            {
                // ignore
                // TODO??
            }

            return driver;
        }
    }
}
