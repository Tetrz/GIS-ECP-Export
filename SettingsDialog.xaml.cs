using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.WindowsAPICodePack.Dialogs;
using OpenQA.Selenium;


namespace GIS_ECP_Export
{
    /// <summary>
    /// Логика взаимодействия для SettingsDialog.xaml
    /// </summary>
    public partial class SettingsDialog : Window
    {
        IWebDriver Driver;

        private string BasePath;
        private string RequestsPath;

        /*
         * Настройки.
         */
        public (bool AuthType,
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
            bool ScreenshotCreate) SettingsDlg;

        public SettingsDialog((bool AuthType,
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
            bool ScreenshotCreate) settings, string baseDir)
        {
            InitializeComponent();

            /*
             * Загружаем настройки.
             */
            BasePath = baseDir;

            SettingsDlg = settings;

            // Тип аутентификации.
            if (SettingsDlg.AuthType)
            {
                AuthByECPRadioButton.IsChecked = true;
            }
            else
            {
                AuthByPasswordRadioButton.IsChecked = true;
            }

            // Выбрать подпись.
            SelectSignComboBox.SelectedIndex = SettingsDlg.NumSign;

            // Вход по электронной подписи.
            PinCodePasswordBox.Password = SettingsDlg.PinCode;

            // Войти как.
            LoginAsComboBox.SelectedIndex = SettingsDlg.NumOrg;

            // Путь к папке для сохранинения запросов.
            if (!string.IsNullOrEmpty(SettingsDlg.ReguestsFolderPath))
            {
                PathTextBlock.Text = SettingsDlg.ReguestsFolderPath;
            }
            else
            {
                PathTextBlock.Text = "Папка по умолчанию";
            }

            // Скрывать консоль с выводом отладочных данных.
            HideConsoleCheckBox.IsChecked = SettingsDlg.HideConsole;

            // Скачивать запросы при старте приложения.
            DownloadOnStartCheckBox.IsChecked = SettingsDlg.DownloadOnStart;

            // Автоматически закрывать приложение после скачивания запросов.
            AutoCloseAppCheckBox.IsChecked = SettingsDlg.AutoCloseApp;

            // Автоматически выходить из ГИС ЕЦП по завершении скачивания запросов.
            AutoExitGISECPCheckBox.IsChecked = SettingsDlg.AutoExitGISECP;

            // Логирование событий.
            LogCheckBox.IsChecked = SettingsDlg.Log;

            // Открыть папку с запросами по завершении их скачивания.
            OpenFolderCheckBox.IsChecked = SettingsDlg.OpenFolder;

            // Сохранять скриншоты экрана.
            ScreenshotCreateCheckBox.IsChecked = SettingsDlg.ScreenshotCreate;

            // Активируем или деактивируем контролы входа по электронной подписи.
            UpdateAuthByECPControls();
        }

        /*
         * Тип аутентификации.
         */

        // Войти вручную.
        private void AuthManuallyRadioButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsDlg.AuthType = false;

            UpdateAuthByECPControls();
        }

        // По электронной подписи.
        private void AuthByECPRadioButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsDlg.AuthType = true;

            UpdateAuthByECPControls();
        }

        // Контролы выбора подписи, пин-кода и организации доступны только при входе по электронной подписи.
        private void UpdateAuthByECPControls()
        {
            bool isEnabled = AuthByECPRadioButton.IsChecked == true;

            SelectSignComboBox.IsEnabled = isEnabled;
            PinCodePasswordBox.IsEnabled = isEnabled;
            LoginAsComboBox.IsEnabled = isEnabled;
        }

        // Выбрать подпись.
        private void SelectSignComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SettingsDlg.NumSign = SelectSignComboBox.SelectedIndex;
        }

        private void PinCodePasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            SettingsDlg.PinCode = PinCodePasswordBox.Password;
        }

        // Войти как.
        private void LoginAsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SettingsDlg.NumOrg = LoginAsComboBox.SelectedIndex;
        }

        // Путь к папке для сохранения запросов.
        private void SelectRequestsPathButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Создаем экземпляр современного диалога
                var dialog = new CommonOpenFileDialog();

                // Устанавливаем ключевое свойство: IsFolderPicker = true
                dialog.IsFolderPicker = true;

                // Опционально: устанавливаем заголовок окна
                dialog.Title = "Выберите папку для сохранения запросов";

                // Опционально: устанавливаем начальный каталог (например, "C:\")
                // dialog.InitialDirectory = "C:\\";

                // Показываем диалог модально и проверяем результат
                if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
                {
                    // Путь к папке.
                    SettingsDlg.ReguestsFolderPath = dialog.FileName;

                    // Обновляем путь в настройках приложения
                    PathTextBlock.Text = SettingsDlg.ReguestsFolderPath;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.ToString());
            }
        }

        // Скрывать консоль с выводом отладочных данных.
        private void HideConsoleCheckBox_Click(object sender, RoutedEventArgs e)
        {
            SettingsDlg.HideConsole = Convert.ToBoolean(HideConsoleCheckBox.IsChecked);
        }

        // Cкачивать запросы при старте приложения.
        private void DownloadOnStartCheckBox_Click(object sender, RoutedEventArgs e)
        {
            SettingsDlg.DownloadOnStart = Convert.ToBoolean(DownloadOnStartCheckBox.IsChecked);
        }

        // Автоматически закрывать приложение после скачивания запросов.
        private void AutoCloseAppCheckBox_Click(object sender, RoutedEventArgs e)
        {
            SettingsDlg.AutoCloseApp = Convert.ToBoolean(AutoCloseAppCheckBox.IsChecked);
        }

        // Автоматически выходить из ГИС ЕЦП по завершении скачивания запросов.
        private void AutoExitGISECPCheckBox_Click(object sender, RoutedEventArgs e)
        {
            SettingsDlg.AutoExitGISECP = Convert.ToBoolean(AutoExitGISECPCheckBox.IsChecked);
        }

        // Логирование событий.
        private void LogCheckBox_Click(object sender, RoutedEventArgs e)
        {
            SettingsDlg.Log = Convert.ToBoolean(LogCheckBox.IsChecked);
        }

        // Открыть папку с запросами по завершении их скачивания.
        private void OpenFolderCheckBox_Click(object sender, RoutedEventArgs e)
        {
            SettingsDlg.OpenFolder = Convert.ToBoolean(OpenFolderCheckBox.IsChecked);
        }

        // Сохранять скриншоты экрана.
        private void ScreenshotCreateCheckBox_Click(object sender, RoutedEventArgs e)
        {
            SettingsDlg.ScreenshotCreate = Convert.ToBoolean(ScreenshotCreateCheckBox.IsChecked);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true; // Закрываем окно и возвращаем true.
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false; // Закрываем окно и возвращаем false.
        }

        // Открываем браузер.
        private void OpenBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            BrowserDriverProvider browserDriverProvider = new BrowserDriverProvider();

            // Проверяем установлен ли браузер.
            string? browserPath = browserDriverProvider.CheckBrowserInstall();

            if (browserPath == null)
            {
                MessageBox.Show("Яндекс.Браузер не установлен.");
                return;
            }

            Driver = browserDriverProvider.DriverInit(browserPath, BasePath, "", SettingsDlg.HideConsole);
        }

        // Закрываем браузер.
        private void CloseBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            // Закрываем браузер и драйвер.
            if (Driver != null)
            {
                Driver.Quit();
                Driver.Dispose();
            }
        }
    }
}
