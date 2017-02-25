using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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
using System.Drawing;
using Flurl;
using Flurl.Http;
using System.ComponentModel;
using System.Diagnostics;

namespace LuminousVector.Aoba
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
#if !DEBUG
		private bool willExit = false;
#else
		private bool willExit = true;
#endif


		public MainWindow()
		{
			InitializeComponent();
			Aoba.Init();
			//Set Values
			Load();
			//Tray Icon
			var cm = new System.Windows.Forms.ContextMenu(new System.Windows.Forms.MenuItem[]
			{
				new System.Windows.Forms.MenuItem("Restore", RestoreWindow),
				new System.Windows.Forms.MenuItem("Quit", (o, e) =>
				{
					Aoba.TrayIcon.Visible = false;
					willExit = true;
					Aoba.Save();
					Close();
				})
			});
			Aoba.TrayIcon.ContextMenu = cm;
			Aoba.TrayIcon.DoubleClick += RestoreWindow;
		}

		private void Load()
		{
			//Startup
			RunOnStartup.IsChecked = Aoba.Settings.RunAtStart;
			//After Load
			CopyLink.IsChecked = Aoba.Settings.CopyLink;
			OpenLink.IsChecked = Aoba.Settings.OpenLink;
			//Toasts
			ToastAll.IsChecked = ToastBox.IsEnabled = Aoba.Settings.ShowToasts;
			ToastCapture.IsChecked = Aoba.Settings.ToastCapture;
			ToastSuccess.IsChecked = Aoba.Settings.ToastSucess;
			ToastFailed.IsChecked = Aoba.Settings.ToastFailed;
			//Sounds
			SoundAll.IsChecked = SoundBox.IsEnabled = Aoba.Settings.PlaySounds;
			SoundCapture.IsChecked = Aoba.Settings.SoundCapure;
			SoundSuccess.IsChecked = Aoba.Settings.SoundSuccess;
			SoundFailed.IsChecked = Aoba.Settings.SoundFailed;
			//Image Format
			ImageFormat.SelectedIndex = (Aoba.Settings.Format == System.Drawing.Imaging.ImageFormat.Jpeg) ? 1 : 0;
			//Save Copy
			SaveCopy.IsChecked = SaveBox.IsEnabled = Aoba.Settings.SaveCopy;
			SaveLocation.Text = Aoba.Settings.SaveLocation;
			//Fullscreen Capture Mode
			FullscreenCaputue.SelectedIndex = (int)Aoba.Settings.FullscreenCapture;
			//Account
			if (Aoba.Settings.HasAuth)
				ShowLoggedIn();
			Username.Text = Aoba.Settings.Username;
		}

		private void Save()
		{
			//Startup
			Aoba.Settings.RunAtStart = (RunOnStartup.IsChecked == null) ? false : (bool)RunOnStartup.IsChecked;
			//After Load
			Aoba.Settings.CopyLink = (CopyLink.IsChecked == null) ? false : (bool)CopyLink.IsChecked;
			Aoba.Settings.OpenLink = (OpenLink.IsChecked == null) ? false : (bool)OpenLink.IsChecked;
			//Toasts
			Aoba.Settings.ShowToasts = (ToastAll.IsChecked == null) ? false : (bool)ToastAll.IsChecked;
			Aoba.Settings.ToastCapture = (ToastCapture.IsChecked == null) ? false : (bool)ToastCapture.IsChecked;
			Aoba.Settings.ToastSucess = (ToastSuccess.IsChecked == null) ? false : (bool)ToastSuccess.IsChecked;
			Aoba.Settings.ToastFailed = (ToastFailed.IsChecked == null) ? false : (bool)ToastFailed.IsChecked;
			//Sounds
			Aoba.Settings.PlaySounds = (SoundAll.IsChecked == null) ? false : (bool)SoundAll.IsChecked;
			Aoba.Settings.SoundCapure = (SoundCapture.IsChecked == null) ? false : (bool)SoundCapture.IsChecked;
			Aoba.Settings.SoundSuccess = (SoundSuccess.IsChecked == null) ? false : (bool)SoundSuccess.IsChecked;
			Aoba.Settings.SoundFailed = (SoundFailed.IsChecked == null) ? false : (bool)SoundFailed.IsChecked;
			//Image Format
			Aoba.Settings.Format = (ImageFormat.SelectedIndex == 0) ? System.Drawing.Imaging.ImageFormat.Png : System.Drawing.Imaging.ImageFormat.Jpeg;
			//Save Copy
			Aoba.Settings.SaveCopy = (SaveCopy.IsChecked == null) ? false : (bool)SaveCopy.IsChecked;
			Aoba.Settings.SaveLocation = SaveLocation.Text;
			//Fullscreen Capture Mode
			Aoba.Settings.FullscreenCapture = (DataStore.FullscreenCaptureMode)FullscreenCaputue.SelectedIndex;
			Aoba.Save();
		}

		internal void RestoreWindow(object sender, EventArgs e)
		{

			Aoba.TrayIcon.Visible = false;
			Show();
			if (WindowState != WindowState.Normal)
				WindowState = WindowState.Normal;
		}

		internal void HideWindow()
		{
			Hide();
			Aoba.TrayIcon.Visible = true;
		}

		protected override void OnStateChanged(EventArgs e)
		{
			Save();
			if (WindowState == WindowState.Minimized)
			{
				HideWindow();
			}
			base.OnStateChanged(e);
		}

		protected override void OnClosing(CancelEventArgs e)
		{
			Save();
			if (WindowState == WindowState.Normal)
			{
				if(!willExit)
					e.Cancel = true;
				HideWindow();
			}
			base.OnClosing(e);
		}

		private async void LoginButton_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				AccountBox.IsEnabled = false;
				Aoba.Settings.Username = Username.Text;
				Aoba.Settings.Password = Password.Password;
				await Aoba.Login();
				AccountBox.IsEnabled = true;
				ShowLoggedIn();
			}catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "Error");
				AccountBox.IsEnabled = true;
			}
			finally
			{
				Save();
			}
		}

		private object loginForm;

		private async void ShowLoggedIn()
		{
			if (loginForm == null)
				loginForm = AccountBox.Content;
			await Aoba.UpdateStats();
			var logoutButton = new Button();
			logoutButton.Content = $"Logout {Aoba.Settings.Username}";
			logoutButton.Click += (o, e) =>
			{
				Aoba.Settings.AuthToken = null;
				AccountBox.Content = loginForm;
				UserStatus.Content = "User not logged in...";
			};
			AccountBox.Content = logoutButton;
			UserStatus.Content = $"Upload Count: {Aoba.UserStats?.screenShotCount}";	
		}

		private void SaveCopy_Click(object sender, RoutedEventArgs e)
		{
			SaveBox.IsEnabled = (SaveCopy.IsChecked == null) ? false : (bool)SaveCopy.IsChecked;
		}

		private void SaveLocationButton_Click(object sender, RoutedEventArgs e)
		{
			using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
			{
				System.Windows.Forms.DialogResult result = dialog.ShowDialog();
				SaveLocation.Text = dialog.SelectedPath;
			}
		}

		private void SoundAll_Click(object sender, RoutedEventArgs e)
		{
			SoundBox.IsEnabled = (SoundAll.IsChecked == null) ? false : (bool)SoundAll.IsChecked;
		}

		private void ToastAll_Click(object sender, RoutedEventArgs e)
		{
			ToastBox.IsEnabled = (ToastAll.IsChecked == null) ? false : (bool)ToastAll.IsChecked;
		}
	}
}