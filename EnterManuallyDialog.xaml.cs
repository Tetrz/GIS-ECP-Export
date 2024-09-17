using System.Windows;

namespace GIS_ECP_Export
{
    /// <summary>
    /// Логика взаимодействия для EnterManuallyDialog.xaml
    /// </summary>
    public partial class EnterManuallyDialog : Window
    {
        public EnterManuallyDialog()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true; // Закрывает окно и возвращает true.
        }
    }
}
