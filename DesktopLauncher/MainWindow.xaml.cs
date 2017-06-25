﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
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
using System.Windows.Resources;
using System.Windows.Shapes;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Management.Deployment;
using Windows.System;

namespace DesktopLauncher
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private List<ILaunchable> entries = new List<ILaunchable>();
        private HotKey.HotKey hotKey;
        private double initialHeight;
        private const double candidatesHeight = 150.0;

        public MainWindow()
        {
            InitializeComponent();
            initialHeight = Height;
        }        

        private void ShowLauncher()
        { 
            Show();
        }

        private void HideLauncher()
        {
            InputText.Text = "";
            Candidates.DataContext = null;
            Hide();
        }

        private void LoadOptions()
        {
            var settings = Properties.Settings.Default;

            (App.Current as App).ChangeTheme(settings.Theme);
            this.Opacity = (double)settings.Opacity;

            if (settings.WindowPosition.X >= 0)
            { 
                Left = settings.WindowPosition.X;
            }

            if(settings.WindowPosition.Y >= 0)
            {
                Top = Properties.Settings.Default.WindowPosition.Y;
            }

            if (hotKey != null)
            {
                hotKey.Pressed -= HotKey_Pressed;
                hotKey.Dispose();
            }
            var keyConverter = new KeyConverter();
            try
            {
                hotKey = new HotKey.HotKey(this, (ModifierKeys)settings.HotKeyModifiers, (Key)keyConverter.ConvertFromString(settings.HotKeyCharacter));
                hotKey.Pressed += HotKey_Pressed;
            }
            catch
            {
                MessageBox.Show("Hot-key is already in use. Please use another key.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            (NotifyIcon.ContextMenu.Items[0] as MenuItem).InputGestureText = new OptionsViewModel().HotKeyDescription;
        }

        private IList<ILaunchable> LoadAliases(IList<ILaunchable> entries)
        {
            var aliases = new List<ILaunchable>();
            var settings = Properties.Settings.Default;
            if (settings.Aliases?.Count > 0)
            {
                foreach (var alias in settings.Aliases)
                {
                    var fields = alias.Split(new char[] { '|' });
                    var name = fields[0];
                    var id = fields[1];
                    var entry = entries.Where(e => e.Id == id).Single();
                    if (entry != null && !(entry is AppAlias))
                    {
                        aliases.Add(new AppAlias(name, entry));
                    }
                }
            }
            return aliases;
        }

        private void LoadLaunchedCounts(IReadOnlyList<ILaunchable> entries)
        {
            var settings = Properties.Settings.Default;
            if (settings.LaunchCounts?.Count > 0)
            {
                foreach(var launchCount in settings.LaunchCounts)
                {
                    var fields = launchCount.Split("|".ToCharArray());
                    if(fields.Count() != 2)
                    {
                        continue;
                    }
                    var id = fields[0];
                    var launched = 0;

                    int.TryParse(fields[1], out launched);
                    var entry = entries.Where((e) => e.Id == id).Single();
                    if(entry != null)
                    {
                        entry.Launched = launched;
                    }
                }
            }
        }

        private void SaveLaunchedCounts(IReadOnlyList<ILaunchable> entries)
        {
            var settings = Properties.Settings.Default;
            var launchCounts = new System.Collections.Specialized.StringCollection();
            foreach(var entry in entries)
            {
                if(entry.Launched == 0)
                {
                    continue;
                }

                launchCounts.Add(string.Format("{0}|{1}", entry.Id, entry.Launched));
            }
            settings.LaunchCounts = launchCounts;
        }

        private void SaveOptions()
        {
            var settings = Properties.Settings.Default;

            settings.WindowPosition = new System.Drawing.Point((int)Left, (int)Top);

            settings.Save();
        }

        private async Task Rescan()
        {
            SaveLaunchedCounts(entries);

            LoadingIndicator.IsActive = true;
            InputText.IsEnabled = false;

            entries.Clear();
            entries.AddRange(await StoreApp.FindAllStoreApps());
            entries.AddRange(DesktopApp.FindAllDesktopApps());
            entries.AddRange(UriLauncher.FindAllUriLaunchers());
            entries.AddRange(LoadAliases(entries)); 
            entries.Sort((x, y) => x.Name.CompareTo(y.Name));

            LoadLaunchedCounts(entries);

            LoadingIndicator.IsActive = false;
            InputText.IsEnabled = true;
            InputText.Focus();
        }

        private void HotKey_Pressed(HotKey.HotKey obj)
        {
            if (IsVisible)
            {
                HideLauncher();
            }
            else
            {
                ShowLauncher();
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadOptions();
            await Rescan();
        }

        private void Window_Activated(object sender, EventArgs e)
        {

        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {            
            if (e.Key == Key.Escape)
            {
                HideLauncher();
                return;
            }

            if(e.Key == Key.Return)
            {
                (Candidates.SelectedItem as ILaunchable)?.LaunchAsync(InputText.Text);

                HideLauncher();
                return;
            }
        }

        private void InputText_TextChanged(object sender, TextChangedEventArgs e)
        {
            string text = (sender as TextBox).Text.ToLower();
            if(text.Length == 0)
            {
                Candidates.Height = 0.0;
                Height = initialHeight;

                Candidates.DataContext = null;
                return;
            }

            var keyword = text.Split(" ".ToCharArray()).First();
            var candidates = entries.Where(entry => entry.Name.ToLower().StartsWith(keyword)).ToList();
            candidates.AddRange(entries.Where(entry => entry.Name.ToLower().Contains(keyword)));
            // 起動回数の多いアプリが上位に表示されるように
            candidates.InsertRange(0, candidates.Where((entry) => entry.Launched > 0).OrderByDescending((entry) => entry.Launched));

            Candidates.DataContext = candidates.Distinct();
            Candidates.SelectedIndex = 0;

            Candidates.Height = candidatesHeight;
            Height = initialHeight + Candidates.Height + Candidates.Margin.Bottom;
        }       

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up)
            {
                if(Candidates.SelectedIndex > 0)
                {
                    Candidates.SelectedIndex--;
                }
            }

            if (e.Key == Key.Down)
            {
                if(Candidates.SelectedIndex < Candidates.Items.Count - 1)
                {
                    Candidates.SelectedIndex++;
                }
            }

            Candidates.ScrollIntoView(Candidates.SelectedItem);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
        }

        private void InputText_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if(e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void Candidates_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var listBox = sender as ListBox;
            if(listBox == null || listBox.SelectedItem == null)
            {
                return;
            }
            (listBox.SelectedItem as ILaunchable).LaunchAsync(InputText.Text);
            HideLauncher();
        }

        private async void Options_Click(object sender, RoutedEventArgs e)
        {
            var optionsWindow = new OptionsWindows();

            optionsWindow.Owner = this;
            optionsWindow.Apps = entries.Where(entry => (entry is AppAlias) == false).ToList().AsReadOnly();

            var result = optionsWindow.ShowDialog();
            if (result.HasValue && result.Value)
            {
                LoadOptions();
                await Rescan();
            }
        }

        private async void Rescan_Click(object sender, RoutedEventArgs e)
        {
            await Rescan();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            SaveOptions();
            SaveLaunchedCounts(entries);
            App.Current.Shutdown();
        }

        private void Show_Click(object sender, RoutedEventArgs e)
        {
            ShowLauncher();
        }
    }
}
