using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Mesen.Config;
using Mesen.Interop;
using Mesen.ViewModels;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Mesen.Windows
{
	public class NetplayStartServerWindow : MesenWindow
	{
		public NetplayStartServerWindow()
		{
			InitializeComponent();
#if DEBUG
			this.AttachDevTools();
#endif
			TextBox? ipBox = this.FindControl<TextBox>("IpBox");
			if(ipBox != null) {
				ipBox.Text = GetLocalIpAddresses();
			}
		}

		private void InitializeComponent()
		{
			AvaloniaXamlLoader.Load(this);
		}

		private static string GetLocalIpAddresses()
		{
			List<string> addresses = new List<string>();
			try {
				foreach(NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces()) {
					if(ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) {
						continue;
					}
					foreach(UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses) {
						if(ip.Address.AddressFamily == AddressFamily.InterNetwork) {
							string addr = ip.Address.ToString();
							if(!addresses.Contains(addr)) {
								addresses.Add(addr);
							}
						}
					}
				}
			} catch {
				//Ignore - just leave the IP as unknown if it can't be determined
			}
			return addresses.Count > 0 ? string.Join(", ", addresses) : "?";
		}

		private async void CopyPassword_OnClick(object sender, RoutedEventArgs e)
		{
			if(DataContext is NetplayConfig cfg && Clipboard != null) {
				await Clipboard.SetTextAsync(cfg.ServerPassword ?? "");
			}
		}

		private async void CopyIp_OnClick(object sender, RoutedEventArgs e)
		{
			TextBox? ipBox = this.FindControl<TextBox>("IpBox");
			if(ipBox != null && Clipboard != null) {
				await Clipboard.SetTextAsync(ipBox.Text ?? "");
			}
		}

		private async void GetPublicIp_OnClick(object sender, RoutedEventArgs e)
		{
			TextBox? box = this.FindControl<TextBox>("PublicIpBox");
			Button? button = this.FindControl<Button>("GetPublicIpButton");
			if(box == null) {
				return;
			}

			//Opt-in: only contacts an external service when the user clicks the button.
			if(button != null) {
				button.IsEnabled = false;
			}
			box.Text = "...";
			box.Text = await FetchPublicIpAsync();
			if(button != null) {
				button.IsEnabled = true;
			}
		}

		private static async Task<string> FetchPublicIpAsync()
		{
			//Try several providers in case one is down/blocked.
			string[] services = { "https://api.ipify.org", "https://ifconfig.me/ip", "https://icanhazip.com" };
			using HttpClient client = new HttpClient();
			client.Timeout = TimeSpan.FromSeconds(5);
			foreach(string url in services) {
				try {
					string ip = (await client.GetStringAsync(url)).Trim();
					if(ip.Length > 0) {
						return ip;
					}
				} catch {
					//Try the next provider
				}
			}
			return "(failed)";
		}

		private async void CopyPublicIp_OnClick(object sender, RoutedEventArgs e)
		{
			TextBox? box = this.FindControl<TextBox>("PublicIpBox");
			if(box != null && Clipboard != null) {
				await Clipboard.SetTextAsync(box.Text ?? "");
			}
		}

		private void Ok_OnClick(object sender, RoutedEventArgs e)
		{
			NetplayConfig cfg = (NetplayConfig)DataContext!;
			ConfigManager.Config.Netplay = cfg.Clone();

			Close(true);

			NetplayApi.StartServer(cfg.ServerPort, cfg.ServerPassword);
		}

		private void Cancel_OnClick(object sender, RoutedEventArgs e)
		{
			Close(false);
		}
	}
}
