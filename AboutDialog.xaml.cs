using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Navigation;

namespace GIS_ECP_Export
{
    /// <summary>
    /// Логика взаимодействия для AboutDialog.xaml
    /// </summary>
    public partial class AboutDialog : Window
    {
        public AboutDialog()
        {
            InitializeComponent();

            LoadedQueriesCount.Text = Properties.Settings.Default.LoadedQueriesCount.ToString();
        }

        // Переход на веб-страницу при нажатии на гиперссылку.
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            HyperlinkHelper.Navigate(sender, e);
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true; // Закрывает окно и возвращает true.
        }
    }

    // Версия приложения.
    public static class AppInfo
    {
        public static string Version =>
            Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion.Split('+')[0].TrimEnd() + "."
            ?? "Неизвестно";
    }
}