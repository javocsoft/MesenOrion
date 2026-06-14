using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Localization;
using Mesen.ViewModels;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Mesen.Windows
{
	public class NetplayConnectWindow : MesenWindow
	{
		public NetplayConnectWindow()
		{
			InitializeComponent();
#if DEBUG
			this.AttachDevTools();
#endif
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private async void CopyPassword_OnClick(object sender, RoutedEventArgs e)
		{
			if(DataContext is NetplayConfig cfg && Clipboard != null) {
				await Clipboard.SetTextAsync(cfg.Password ?? "");
			}
		}

		private async void TestConnection_OnClick(object sender, RoutedEventArgs e)
		{
			if(DataContext is not NetplayConfig cfg) {
				return;
			}
			string host = cfg.Host ?? "";
			int port = cfg.Port;
			if(host.Trim().Length == 0) {
				return;
			}

			Button? button = this.FindControl<Button>("TestButton");
			TextBlock? result = this.FindControl<TextBlock>("TestResult");
			if(result != null) {
				result.Text = ResourceHelper.GetMessage("TestingConnection");
			}
			if(button != null) {
				button.IsEnabled = false;
			}

			bool reachable = false;
			try {
				using TcpClient client = new TcpClient();
				Task connectTask = client.ConnectAsync(host, port);
				if(await Task.WhenAny(connectTask, Task.Delay(5000)) == connectTask) {
					await connectTask; //Observe any exception
					reachable = client.Connected;
				}
			} catch {
				reachable = false;
			}

			if(result != null) {
				result.Text = ResourceHelper.GetMessage(reachable ? "ConnectionReachable" : "ConnectionUnreachable");
			}
			if(button != null) {
				button.IsEnabled = true;
			}
		}

		private void Ok_OnClick(object sender, RoutedEventArgs e)
		{
			NetplayConfig cfg = (NetplayConfig)DataContext!;
			ConfigManager.Config.Netplay = cfg.Clone();

			Close(true);

			Task.Run(() => {
				NetplayApi.Connect(cfg.Host, cfg.Port, cfg.Password, false);
			});
		}

		private void Cancel_OnClick(object sender, RoutedEventArgs e)
		{
			Close(false);
		}
	}
}