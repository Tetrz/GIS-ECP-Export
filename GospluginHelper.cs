using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using NLog;
using System;
using System.Diagnostics;
using System.Threading;

namespace GIS_ECP_Export
{

    // TODO Тестирование, отладка.
    public class GospluginHelper
    {        public static bool EnterPassword(bool logOn, Logger log, string pinCode, bool screenshotCreate, string? screenshotsPath)
        {
            using var automation = new UIA3Automation();

            if (logOn) log.Info("Вводим пин-код ЭЦП.");
            if (screenshotCreate) { MainWindow.CreateScreenshot(screenshotsPath); }

            // Небольшая задержка перед поиском окна.
            Thread.Sleep(2000);

            if (logOn) log.Info("Поиск окна \"Госплагина\".");
            if (screenshotCreate) { MainWindow.CreateScreenshot(screenshotsPath); }

            var window = WaitForElement(
                () => automation.GetDesktop()
                    .FindFirstDescendant(cf => cf.ByName("Госплагин"))
                    ?.AsWindow(),
                TimeSpan.FromSeconds(60),
                log,
                "Окно \"Госплагин\"");

            if (window == null)
            {
                if (logOn) log.Warn("Окно \"Госплагина\" не найдено.");
                if (screenshotCreate) { MainWindow.CreateScreenshot(screenshotsPath); }
                return false;
            }

            if (logOn) log.Info("Поиск текстового поля ввода пинкода.");
            if (screenshotCreate) { MainWindow.CreateScreenshot(screenshotsPath); }

            var passwordBox = WaitForElement(
                () => window.FindFirstDescendant(
                        cf => cf.ByControlType(ControlType.Edit))
                    ?.AsTextBox(),
                TimeSpan.FromSeconds(30),
                log,
                "Поле ввода PIN-кода");

            if (passwordBox == null)
            {
                if (logOn) log.Warn("Поле ввода PIN-кода не найдено.");
                if (screenshotCreate) { MainWindow.CreateScreenshot(screenshotsPath); }
                return false;
            }

            if (logOn) log.Info("Вводим PIN-код.");
            if (screenshotCreate) { MainWindow.CreateScreenshot(screenshotsPath); }

            passwordBox.Enter(pinCode);

            if (logOn) log.Info("Поиск кнопки \"OK\".");
            if (screenshotCreate) { MainWindow.CreateScreenshot(screenshotsPath); }

            var okButton = WaitForElement(
              () => window.FindFirstDescendant(
                      cf => cf.ByControlType(ControlType.Button)
                              .And(cf.ByName("OK")))
                  ?.AsButton(),
              TimeSpan.FromSeconds(30),
              log,
              "Кнопка \"OK\"");

            if (okButton == null)
            {
                if (logOn) log.Warn("Кнопка \"OK\" не найдена.");
                if (screenshotCreate) { MainWindow.CreateScreenshot(screenshotsPath); }
                return false;
            }

            if (logOn) log.Info("Нажимаем кнопку \"OK\".");
            if (screenshotCreate) { MainWindow.CreateScreenshot(screenshotsPath); }

            okButton.Invoke();

            if (logOn) log.Info("Пин-код ЭЦП введён. Вход.");
            if (screenshotCreate) { MainWindow.CreateScreenshot(screenshotsPath); }

            return true;
        }

        private static T? WaitForElement<T>(
            Func<T?> finder,
            TimeSpan timeout,
            Logger? log = null,
            string? elementName = null,
            TimeSpan? interval = null)
            where T : class
        {
            var sw = Stopwatch.StartNew();

            var result = Retry.WhileNull(
                finder,
                timeout,
                interval ?? TimeSpan.FromMilliseconds(100))
                .Result;

            sw.Stop();

            if (log != null)
            {
                if (result != null)
                {
                    log.Info($"Элемент \"{elementName}\" найден за {sw.ElapsedMilliseconds} мс.");
                }
                else
                {
                    log.Warn($"Элемент \"{elementName}\" не найден за {sw.ElapsedMilliseconds} мс.");
                }
            }

            return result;
        }
    }
}