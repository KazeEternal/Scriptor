using Scripts.Scriptor;
using System;
using System.Collections.Generic;
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

namespace GUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Logger.Event += Logger_Event;
            Logger.Warning += Logger_Warning;
            Logger.Error += Logger_Error;
        }

        private void Logger_Event(string format, object[] args)
        {
            
        }

        private void Logger_Warning(string format, object[] args)
        {
            
        }

        private void Logger_Error(string format, object[] args)
        {
            
        }
    }
}
