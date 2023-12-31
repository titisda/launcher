﻿using SpaciousStartMenu.DataTypes;
using SpaciousStartMenu.Env;
using SpaciousStartMenu.Extensions;
using SpaciousStartMenu.FileIO;
using SpaciousStartMenu.Processings;
using SpaciousStartMenu.Settings;
using SpaciousStartMenu.Shell;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SpaciousStartMenu.Views
{
    public partial class MainWindow : Window
    {
        private readonly LauncherDefinition _btnDef = new();
        private int _buttonCount = 0;
        private int _groupCount = 0;
        private int _groupTitleCount = 0;
        private PinWindow? _pinWindow = null;
        private AppSettings _settings = new();
        private readonly Style _groupTitleStyle = (Style)(Application.Current.FindResource("GroupTitleStyle"));
        private readonly Style _groupContainerStyle = (Style)(Application.Current.FindResource("GroupContainerStyle"));
        private readonly Style _groupSeparatorBorderStyle = (Style)(Application.Current.FindResource("GroupSeparatorBorderStyle"));
        private readonly Style _launchButtonStyle = (Style)(Application.Current.FindResource("LaunchButtonStyle"));
        private static readonly FontFamily _segoeMdl2Font = new("Segoe MDL2 Assets");
        private const string _colorMark = "\uE91F";
        private readonly NumberOfExecution _savingProcessOnExit;
        private UserInfo? _userInfo;
        private readonly Vm _vm = new();

        private class Vm : NotifyPropertyChanged
        {
            private double _scale = _defaultScale;
            public double Scale
            {
                get => _scale;
                set => SetProperty(ref _scale, value);
            }
        }

        private const double _defaultScale = 1.0;
        private static readonly double[] _scales = { 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.2, 1.5, 1.8, 2.0, 2.5, 3.0, 3.5, 4.0, 4.5, 5.0 };
        private static readonly double[] _scalesR = _scales.Reverse().ToArray();

        public MainWindow()
        {
            DataContext = _vm;
            InitializeComponent();
            _savingProcessOnExit = new NumberOfExecution(() =>
            {
                SaveAppSettings();
            });
            App.Current.TerminateProc = TerminateProc;
        }

        private void TitleBarMoreButton_Click(object sender, RoutedEventArgs e)
        {
            ClearKeyStatus();
            WindowContextMenu.IsOpen = true;
        }

        private void TitleBarMinButton_Click(object sender, RoutedEventArgs e) => MinimizeWindow();

        private void MinimizeWindow()
        {
            ClearKeyStatus();
            WindowState = WindowState.Minimized;
        }

        private void TitleBarCloseButton_Click(object sender, RoutedEventArgs e)
        {
            ClearKeyStatus();

            if (_settings.ConfirmCloseMenu)
            {
                if (this.Confirm(App.R("MsgConfirmCloseMenu")) != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            Close();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                App.Current.Resources["AccentBrush"] = new SolidColorBrush(SystemParameters.WindowGlassColor);

                LoadAppSettings();

                if (_settings.MinimizeStartup2 ||
                    App.MinimizeStartup)
                {
                    WindowState = WindowState.Minimized;
                }

                string filePath = App.GetLaunchDefineFilePath();
                CreateDefaultLaunchDefineFile(filePath);
                LoadLauncherDefine(filePath);

                if (_settings.ShowUserInTitleBar)
                {
                    SetUser(await GetUserAsync());
                }
                WindowContextMenu.PlacementTarget = this;
            }
            catch (Exception ex)
            {
                ShowException(ex);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _pinWindow?.Close();
                SaveAppSettings();
            }
            catch (Exception ex)
            {
                this.Error($"{App.R("MsgErrSaveSettings")}\n{ex}");
            }
        }

        private void Window_Deactivated(object sender, EventArgs e) => ClearKeyStatus();

        private async void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                await Task.Delay(5000);
                if (WindowState == WindowState.Maximized ||
                    WindowState == WindowState.Minimized)
                {
                    return;
                }
                if (Height < (SystemParameters.PrimaryScreenHeight / 3 * 2) ||
                    Width < (SystemParameters.PrimaryScreenWidth / 3 * 2))
                {
                    WindowState = WindowState.Maximized;
                }
            }
            catch
            {
            }
        }

        private async void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (_settings.EscKeyMinimize &&
                e.Key == Key.Escape)
            {
                WindowState = WindowState.Minimized;
            }
            else if ((Keyboard.Modifiers == ModifierKeys.Control) &&
                e.Key == Key.OemComma)
            {
                await ShowSettingsAsync();
            }

            UpdateKeyStatus();
        }

        private void UpdateKeyStatus()
        {
            switch (Keyboard.Modifiers)
            {
                case ModifierKeys.Control | ModifierKeys.Shift:
                    AdminMode.Visibility = Visibility.Visible;
                    MinBlockMode.Visibility = Visibility.Collapsed;
                    ZoomMode.Visibility = Visibility.Collapsed;
                    break;
                case ModifierKeys.Control:
                    AdminMode.Visibility = Visibility.Collapsed;
                    MinBlockMode.Visibility = _settings.DisabledMinimizeCtrlClick
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                    ZoomMode.Visibility = Visibility.Visible;
                    break;
                default:
                    ClearKeyStatus();
                    break;
            }
        }

        private void ClearKeyStatus()
        {
            AdminMode.Visibility = Visibility.Collapsed;
            MinBlockMode.Visibility = Visibility.Collapsed;
            ZoomMode.Visibility = Visibility.Collapsed;
        }

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            UpdateKeyStatus();
        }

        private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_settings.MarginDoubleClickMinimize &&
                e.ChangedButton == MouseButton.Left)
            {
                WindowState = WindowState.Minimized;
            }
        }

        private void Window_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            SetGroupToMenuTag(e.OriginalSource);
            ClearKeyStatus();
            SuppressContextMenuOpen(e);
        }

        private void SetGroupToMenuTag(object target)
        {
            StackPanel? GetGroupContainer(object target)
            {
                if (target is StackPanel sp &&
                    sp.Style == _groupContainerStyle)
                {
                    return sp;
                }

                if (target is TextBlock txt &&
                    txt.Parent is StackPanel sp2 &&
                    sp2.Style == _groupContainerStyle)
                {
                    return sp2;
                }

                return null;
            }

            var sp = GetGroupContainer(target);
            PinMenuItem.Tag = sp is not null ? sp.Tag : null;
        }

        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                ChangeScale(e.Delta > 0);
                e.Handled = true;
            }
        }

        private void ChangeScale(bool isZoom)
        {
            if (isZoom)
            {
                ZoomIn();
            }
            else
            {
                ZoomOut();
            }
        }

        private void PinMenuItem_Click(object sender, RoutedEventArgs e)
        {
            this.TryCatch(() =>
            {
                App.Current.Resources["AccentBrush"] = new SolidColorBrush(SystemParameters.WindowGlassColor);
                if (_pinWindow is null ||
                    !_pinWindow.IsVisible)
                {
                    int groupNo = 0;
                    if (sender is MenuItem item)
                    {
                        _ = int.TryParse(item.Tag?.ToString(), out groupNo);
                    }
                    _pinWindow = new PinWindow(
                        this,
                        () => LoadLauncherDefine(App.GetLaunchDefineFilePath()),
                        () => Activate(),
                        _settings,
                        groupNo)
                    {
                        Owner = this
                    };
                    _pinWindow.Show();
                }
                else
                {
                    _pinWindow.Activate();
                }
            });
        }

        public void SetDisabledStyle(bool disabled = true)
        {
            if (disabled)
            {
                DisabledBorder.Visibility = Visibility.Visible;
            }
            else
            {
                DisabledBorder.Visibility = Visibility.Collapsed;
            }
        }

        private async void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            await ShowSettingsAsync();
        }

        private async Task ShowSettingsAsync()
        {
            try
            {
                App.Current.Resources["AccentBrush"] = new SolidColorBrush(SystemParameters.WindowGlassColor);
                var window = new SettingsWindow(_settings)
                {
                    Owner = this
                };
                SetDisabledStyle();

                window.ShowDialog();

                SetScreenFromSettings(_settings);

                if (!string.IsNullOrEmpty(window.ExportFilePath))
                {
                    ExportSettings(window.ExportFilePath);
                    this.Info($"{App.R("MsgInfoExportComplete")}\n{window.ExportFilePath}");
                }
                else if (window.Imported)
                {
                    LoadLauncherDefine(App.GetLaunchDefineFilePath());
                    LoadAppSettings();

                    this.Info(App.R("MsgInfoImportComplete"));
                }
                else if (window.NeedReloadSettings)
                {
                    LoadLauncherDefine(App.GetLaunchDefineFilePath());
                }

                SetDisabledStyle(false);

                SetUser(await GetUserAsync(hide: !_settings.ShowUserInTitleBar));
            }
            catch (Exception ex)
            {
                SetDisabledStyle(false);
                this.Error(ex.ToString());
            }
        }

        private void ExportSettings(string exportFilePath)
        {
            SaveAppSettings();
            var asr = new AppSettingsReader();
            _settings = asr.ReadFromFile();

            SettingsExport.Export(
                App.Version,
                Environment.Version.ToString(),
                App.GetLaunchDefineFilePath(),
                App.GetAppSettingsFilePath(),
                exportFilePath);
        }

        private async void MenuFolderOpen_Click(object sender, RoutedEventArgs e)
        {
            await this.TryCatchAsync(async () =>
            {
                string appPath = App.GetAppPath();
                await ExecuteAsync(appPath, null, null);
                WindowState = WindowState.Minimized;
            });
        }

        private void MenuFolderOpenAndExit_Click(object sender, RoutedEventArgs e)
        {
            MenuFolderOpen_Click(sender, e);
            Close();
        }

        private void SetContainerScale(double scaleXY)
        {
            MainContainer.LayoutTransform = new ScaleTransform(scaleXY, scaleXY);
        }

        private void ShowException(Exception ex)
        {
            MainContainer.Children.Clear();

            var txt = new TextBox
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(5),
                IsReadOnly = true,
                Background = Brushes.Black,
                Foreground = Brushes.Red,
                Text = ex.ToString()
            };

            MainContainer.Children.Add(txt);
        }

        private void LoadAppSettings()
        {
            var asr = new AppSettingsReader();
            _settings = asr.ReadFromFile();
            CorrectSettings(_settings);
            _vm.Scale = _settings.Scale;
            SetScreen(_settings);
            SetScreenFromSettings(_settings);
        }

        private void CorrectSettings(AppSettings stg)
        {
            if (stg.Scale < _scales.First() ||
                _scales.Last() < stg.Scale)
            {
                stg.Scale = _defaultScale;
            }

            if (!Enum.IsDefined(typeof(HorizontalAlignment), stg.ModifireKeyStatusPosition) ||
                (stg.ModifireKeyStatusPosition != HorizontalAlignment.Left &&
                stg.ModifireKeyStatusPosition != HorizontalAlignment.Center &&
                stg.ModifireKeyStatusPosition != HorizontalAlignment.Right))
            {
                stg.ModifireKeyStatusPosition = HorizontalAlignment.Left;
            }

            if (!Enum.IsDefined(typeof(UserType), stg.ShowUserType))
            {
                stg.ShowUserType = UserType.UserName;
            }
        }

        private void SetScreen(AppSettings appStg)
        {
            _vm.Scale = appStg.Scale;
            SetContainerScale(appStg.Scale);
        }

        private void ZoomIn()
        {
            if (_scales.Last() == _vm.Scale)
            {
                return;
            }

            try
            {
                if (_scales.Last() < _vm.Scale)
                {
                    _vm.Scale = _scales.Last();
                }
                else
                {
                    _vm.Scale = _scales
                        .SkipWhile(x => x <= _vm.Scale)
                        .Take(1)
                        .FirstOrDefault();
                }
            }
            catch
            {
                _vm.Scale = 1.0;
            }
            finally
            {
                SetContainerScale(_vm.Scale);
            }
        }

        private void ZoomOut()
        {
            if (_vm.Scale == _scales.First())
            {
                return;
            }

            try
            {
                if (_vm.Scale < _scales.First())
                {
                    _vm.Scale = _scales.First();
                }
                else
                {
                    _vm.Scale = _scalesR
                        .SkipWhile(x => _vm.Scale <= x)
                        .Take(1)
                        .FirstOrDefault();
                }
            }
            catch
            {
                _vm.Scale = 1.0;
            }
            finally
            {
                SetContainerScale(_vm.Scale);
            }

        }

        /// <summary>
        /// Reflects the contents of the settings screen
        /// </summary>
        /// <param name="appStg"></param>
        private void SetScreenFromSettings(AppSettings appStg)
        {
            MenuItemFolderOpenAndExit.Visibility =
                appStg.ShowOpenAndExitMenuItem
                ? Visibility.Visible
                : Visibility.Collapsed;
            ModifireKeyStatusArea.HorizontalAlignment = appStg.ModifireKeyStatusPosition;
            if (appStg.ModifireKeyStatusPosition == HorizontalAlignment.Center)
            {
                Grid.SetColumn(ModifireKeyStatusArea, 0);
                Grid.SetColumnSpan(ModifireKeyStatusArea, 3);
            }
            else
            {
                Grid.SetColumn(ModifireKeyStatusArea, 1);
                Grid.SetColumnSpan(ModifireKeyStatusArea, 1);
            }
        }

        private async Task<string> GetUserAsync(bool hide = false)
        {
            if (hide)
            {
                return "";
            }

            string user;
            if (_userInfo is null)
            {
                _userInfo = new(_settings.ValueWhenDisplayNameNotFound);

                user = await Task.Run(() =>
                {
                    return _settings.ShowUserType == UserType.UserName
                            ? _userInfo.GetUserName()
                            : _userInfo.GetDisplayName();
                });
            }
            else
            {
                user = _settings.ShowUserType == UserType.UserName
                            ? _userInfo.GetUserName()
                            : _userInfo.GetDisplayName();
            }
            return user;
        }

        private void SetUser(string user)
        {
            if (string.IsNullOrEmpty(user))
            {
                TitleBarUserButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                UserId.Text = user;
                TitleBarUserButton.Visibility = Visibility.Visible;
            }
        }

        private void SaveAppSettings()
        {
            if (App.Abend)
            {
                return;
            }

            var appStg = GetScreen();
            var asw = new AppSettingsWriter();

            asw.WriteToFile(appStg);
        }

        private AppSettings GetScreen()
        {
            _settings.Scale = _vm.Scale;

            return _settings;
        }

        private void LoadLauncherDefine(string filePath)
        {
            MainContainer.Children.Clear();
            using var reader = new StreamReader(filePath, Encoding.UTF8);
            WrapPanel? btnContainer = null;

            _buttonCount = 0;
            _groupCount = 0;
            _groupTitleCount = 0;

            while (!reader.EndOfStream)
            {
                // TAB split
                var columns = reader.ReadLine()?.Split(LauncherDefinition.Delimiter);
                if (columns is null ||
                    columns.Length < 1 ||
                    string.IsNullOrWhiteSpace(columns[0]))
                {
                    // No data
                    continue;
                }

                /*
                MainContainer           ..... StackPanel
                    grpContainer        ..... StackPanel
                        Title           ..... TextBlock
                        btnContainer    ..... WrapPanel
                            Button
                            Button
                            :
                    grpContainer        ..... StackPanel
                        Title           ..... TextBlock
                        btnContainer    ..... WrapPanel
                            Button
                            Button
                            :
                */

                if (_btnDef.IsGroupSeparator(columns))
                {
                    MakeGroupSeparator(ref btnContainer, ref _groupCount);
                }
                else if (_btnDef.IsGroupTitle(columns))
                {
                    MakeGroup(
                        ref btnContainer,
                        ref _groupCount,
                        ref _groupTitleCount,
                        _btnDef.GetGroupTitle(columns));
                }
                else if (LauncherDefinition.RequiredColumns <= columns.Length)
                {
                    MakeButton(
                        columns,
                        ref btnContainer,
                        ref _buttonCount,
                        ref _groupCount,
                        ref _groupTitleCount);
                }
                else
                {
                    throw new Exception(
                        $"{App.R("MsgErrSettingsColumn")}\n{string.Join(LauncherDefinition.Delimiter, columns)}");
                }
            }

        }

        private void MakeGroupSeparator(ref WrapPanel? btnContainer, ref int groupCount)
        {
            var grpContainer = new StackPanel
            {
                Style = _groupContainerStyle
            };
            groupCount++;
            grpContainer.Tag = groupCount;
            MainContainer.Children.Add(grpContainer);

            grpContainer.Children.Add(new Border { Style = _groupSeparatorBorderStyle });

            btnContainer = new WrapPanel();
            grpContainer.Children.Add(btnContainer);
        }

        private void MakeButton(
            string[] columns,
            ref WrapPanel? btnContainer,
            ref int buttonCount,
            ref int groupCount,
            ref int groupTitleCount)
        {
            if (btnContainer is null)
            {
                MakeGroup(ref btnContainer, ref groupCount, ref groupTitleCount);
            }

            Button btn = CreateLaunchButton(
                columns[LauncherDefinition.ColorOrGroupTitleColumnIndex],
                columns[LauncherDefinition.TitleColumnIndex],
                columns[LauncherDefinition.PathColumnIndex],
                LauncherDefinition.WorkDirColumnIndex <= columns.Length - 1 ? columns[LauncherDefinition.WorkDirColumnIndex] : null,
                LauncherDefinition.ArgsColumnIndex <= columns.Length - 1 ? columns[LauncherDefinition.ArgsColumnIndex] : null);
            btn.Style = _launchButtonStyle;

            btnContainer?.Children.Add(btn);

            buttonCount++;
        }

        private void MakeGroup(
            ref WrapPanel?
            btnContainer,
            ref int groupCount,
            ref int groupTitleCount,
            string groupTitle = " ")
        {
            if (string.IsNullOrEmpty(groupTitle))
            {
                groupTitle = " ";
            }

            var grpContainer = new StackPanel
            {
                Style = _groupContainerStyle
            };
            groupCount++;
            groupTitleCount++;
            grpContainer.Tag = groupCount;
            MainContainer.Children.Add(grpContainer);

            grpContainer.Children.Add(
                CreateGroupTitle(
                    _settings.ShowSeqNoInGroupHeadline ? $"{groupTitleCount}. {groupTitle}" : groupTitle));

            btnContainer = new WrapPanel();
            grpContainer.Children.Add(btnContainer);
        }

        private void CreateDefaultLaunchDefineFile(string filePath)
        {
            if (!IsEmptyFile(filePath))
            {
                return;
            }

            using var sw = new StreamWriter(filePath, append: false, encoding: Encoding.UTF8);
            sw.Write(LauncherDefinition.GetDefaultData());
        }

        private bool IsEmptyFile(string filePath)
        {
            const int bomAndCrLfSize = 5;

            if (File.Exists(filePath))
            {
                var f = new FileInfo(filePath);
                return f.Length <= bomAndCrLfSize;
            }

            return true;
        }

        private TextBlock CreateGroupTitle(string title)
        {

            var txt = new TextBlock
            {
                Style = _groupTitleStyle,
                Text = title
            };

            return txt;
        }

        private Button CreateLaunchButton(
            string colorName,
            string text,
            string execute,
            string? workDir,
            string? args)
        {
            var btn = new Button();
            var txtContainer = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            var txtMark = new TextBlock
            {
                FontFamily = _segoeMdl2Font,
                Text = _colorMark,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };

            try
            {
                txtMark.Foreground = MarkColor.GetBrushFromColorName(colorName);
            }
            catch
            {
                throw new Exception($"{App.R("MsgErrColorName")}[{colorName}]");
            }

            txtContainer.Children.Add(txtMark);

            var txt = new TextBlock
            {
                Text = text
            };
            txtContainer.Children.Add(txt);

            btn.Content = txtContainer;
            btn.Click += async (_, _) =>
            {
                (bool success, bool isSpecialCmd) = await ExecuteAsync(execute, workDir, args);
                if (success && !isSpecialCmd)
                {
                    MinimizedWindow();
                }
            };

            return btn;
        }

        private void MinimizedWindow()
        {
            if ((Keyboard.IsKeyDown(Key.LeftCtrl) ||
                Keyboard.IsKeyDown(Key.RightCtrl)) &&
                _settings.DisabledMinimizeCtrlClick)
            {
                return;
            }
            WindowState = WindowState.Minimized;
        }

        private async Task<(bool, bool)> ExecuteAsync(string cmd, string? workDir, string? args)
        {
            bool success = true;
            bool isSpecialCommand = false;

            bool isAdmin = Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
            try
            {
                if (SpecialCommand.IsCommand(cmd))
                {
                    isSpecialCommand = true;
                    if (isAdmin)
                    {
                        this.Error($"{App.R("MsgErrSpecialCmdAdminStartup")}\n{cmd}");
                        success = false;
                        return (success, isSpecialCommand);
                    }
                    await RunSpecialCommandAsync(SpecialCommand.GetType(cmd));
                }
                else
                {
                    ShellExecution.Run(cmd, workDir, args, isAdmin);
                }

                return (success, isSpecialCommand);
            }
            catch
            {
                if (isAdmin)
                {
                    this.Error($"{App.R("MsgErrAdminStartup")}\n{cmd}");
                }
                else
                {
                    this.Error($"{App.R("MsgErrStartup")}\n{cmd}");
                }
                success = false;
                return (success, isSpecialCommand);
            }
        }

        private async Task RunSpecialCommandAsync(SpecialCommandType cmdType)
        {
            switch (cmdType)
            {
                case SpecialCommandType.System_Shutdown:
                    await RunExitCommandAsync(() => Win32.OperatingSystem.Shutdown());
                    break;
                case SpecialCommandType.System_Restart:
                    await RunExitCommandAsync(() => Win32.OperatingSystem.Restart());
                    break;
                case SpecialCommandType.System_Signout:
                    await RunExitCommandAsync(() => Win32.OperatingSystem.Signout());
                    break;
                case SpecialCommandType.App_Minimized:
                    MinimizeWindow();
                    break;
                case SpecialCommandType.App_ScrollToTop:
                    MainScroll.ScrollToTop();
                    break;
                case SpecialCommandType.App_ScrollToBottom:
                    MainScroll.ScrollToBottom();
                    break;
                case SpecialCommandType.Desktop_Show:
                    SpecialCommand.ShowDesktop();
                    break;
                case SpecialCommandType.Settings_Show:
                    await ShowSettingsAsync();
                    break;
                case SpecialCommandType.Explorer_CloseAllFolders:
                    SpecialCommand.CloseAllFolders();
                    break;
                case SpecialCommandType.Info_LaunchButtonCount:
                    this.Info($"{App.R("MsgInfoLaunchButtonCnt")}{_buttonCount}");
                    break;
                case SpecialCommandType.Info_GroupTitleCount:
                    this.Info(
                        $"{App.R("MsgInfoGroupTitleCnt")}{_groupTitleCount}\n{App.R("MsgInfoGroupSeparatorCnt")}{_groupCount - _groupTitleCount}");
                    break;
                default:
                    this.Error(App.R("MsgErrInvalidCommand"));
                    break;
            }
        }

        private void SuppressContextMenuOpen(ContextMenuEventArgs e)
        {
            if (e.Source != TitleBarMoreButton &&
                e.Source is Button)
            {
                e.Handled = true;
                return;
            }

            if (e.Source is TextBlock txt &&
                txt.Style != _groupTitleStyle)
            {
                // Text in the launch button
                e.Handled = true;
                return;
            }
        }

        private void TerminateProc() => _savingProcessOnExit.RunOnlyOnce();

        private void UserButton_Click(object sender, RoutedEventArgs e)
        {
            ClearKeyStatus();
            UserMenu.IsOpen = true;
        }

        private async void MenuSignout_Click(object sender, RoutedEventArgs e) =>
            await RunExitCommandAsync(() => Win32.OperatingSystem.Signout());

        private async Task RunExitCommandAsync(Action action)
        {
            _savingProcessOnExit.RunOnlyOnce();
            await Task.Delay(300);
            action();
        }

        private async void MenuShutdown_Click(object sender, RoutedEventArgs e) =>
            await RunExitCommandAsync(() => Win32.OperatingSystem.Shutdown());

        private async void MenuRestart_Click(object sender, RoutedEventArgs e) =>
            await RunExitCommandAsync(() => Win32.OperatingSystem.Restart());

        private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => ZoomOut();

        private void ZoomInButton_Click(object sender, RoutedEventArgs e) => ZoomIn();

    }

}
