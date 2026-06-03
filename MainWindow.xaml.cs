using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.TextFormatting;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GameProj
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        private int selectedButtonIndex = 0; // 0=Начать, 1=Выход

        public MainWindow()
        {

            InitializeComponent();
            //Устанавливаем фокус на первую кнопку
            BtnStart.Focus();

            // Подключаем вывод Debug-сообщений в консоль
            Debug.Listeners.Add(new ConsoleTraceListener());



            // Привязываем событие выхода из игры
            GameCanvas.OnExitToMenu += ExitToMenu;
        }



        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (GameCanvas.Visibility == Visibility.Visible)
            {
                // Если в игре — обрабатываем только Esc
                if (e.Key == Key.Escape)
                    ExitToMenu();
                return;
            }

            // Только если в меню
            switch (e.Key)
            {
                case Key.Down:
                    selectedButtonIndex = (selectedButtonIndex + 1) % 3;
                    e.Handled = true;
                    break;

                case Key.Up:
                    selectedButtonIndex = (selectedButtonIndex - 1 + 3) % 3;
                    e.Handled = true;
                    break;

                case Key.Enter:
                    SelectCurrentButton();
                    e.Handled = true;
                    break;
            }
        }



        private void SelectCurrentButton()
        {
            switch (selectedButtonIndex)
            {
                case 0: StartGame(); break;
                case 1: StartGame(); break;
                case 2: Application.Current.Shutdown(); break;
            }
        }

        private void StartGame()
        {

            MainMenu.Visibility = Visibility.Collapsed;
            GameCanvas.Visibility = Visibility.Visible;

            GameCanvas.Restart(); 
            GameCanvas.Focus();
        }

        private void ExitToMenu()
        {
            GameCanvas.Visibility = Visibility.Collapsed;
            MainMenu.Visibility = Visibility.Visible;

            // Возвращаем фокус на "Начать"
            selectedButtonIndex = 0;
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e) => StartGame();
        private void BtnExit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

      
    }
}
