using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace EjectDisk
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void btn_Click(object sender, RoutedEventArgs e)
        {
            DiskInfo disk = lbx.SelectedItem as DiskInfo;
            if (disk == null)
            {
                return;
            }
            DispartProcess p = new DispartProcess();
            IsEnabled = false;
            try
            {
                await p.OfflineAndOnlineAsync(disk.ID);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "错误");
            }
            finally
            {
                await LoadDisksAsync();
                IsEnabled = true;
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadDisksAsync();
        }

        private async Task LoadDisksAsync()
        {
            var enable = IsEnabled;
            IsEnabled = false;
            DispartProcess p = new DispartProcess();
            lbx.ItemsSource = await p.GetDisksAsync();
            IsEnabled = enable;
        }
        private async void btnReload_Click(object sender, RoutedEventArgs e)
        {
            await LoadDisksAsync();
        }
        private async void btnDismount_Click(object sender, RoutedEventArgs e)
        {
            DiskInfo disk = lbx.SelectedItem as DiskInfo;
            if (disk == null)
            {
                return;
            }
            DispartProcess p = new DispartProcess();
            IsEnabled = false;
            try
            {
                await p.DismountAsync(disk);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "错误");
            }
            finally
            {
                await LoadDisksAsync();
                IsEnabled = true;
            }
        }

    }
}