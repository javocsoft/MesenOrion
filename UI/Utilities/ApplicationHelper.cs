using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Mesen.Utilities
{
	public static class ApplicationHelper
	{
		public static Window? GetMainWindow()
		{
			if(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is Window wnd) {
				return wnd;
			}

			return null;
		}

		public static Window? GetActiveOrMainWindow()
		{
			if(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
				return desktop.Windows.Where(w => w.IsActive).FirstOrDefault() ?? GetMainWindow();
			}

			return GetMainWindow();
		}

		public static Window? GetActiveWindow()
		{
			if(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
				return desktop.Windows.Where(w => w.IsActive).FirstOrDefault();
			}

			return null;
		}

		public static T? GetExistingWindow<T>() where T : MesenWindow
		{
			if(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
				return desktop.Windows.Where(w => w is T).FirstOrDefault() as T;
			}

			return null;
		}

		public static T GetOrCreateUniqueWindow<T>(Control? centerParent, Func<T> createWindow) where T : MesenWindow
		{
			if(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
				T? wnd = desktop.Windows.Where(w => w is T).FirstOrDefault() as T;
				if(wnd == null) {
					wnd = createWindow();
					if(centerParent != null) {
						wnd.ShowCentered((Control)centerParent);
					} else {
						wnd.Show();
					}
					return wnd;
				} else {
					wnd.BringToFront();
					return wnd;
				}
			}

			throw new NotSupportedException();
		}

		public static List<Window> GetOpenedWindows()
		{
			if(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
				return new List<Window>(desktop.Windows);
			}

			return new List<Window>();
		}

		//Taken from Avalonia's code (MIT): https://github.com/AvaloniaUI/Avalonia/blob/master/src/Avalonia.Dialogs/AboutAvaloniaDialog.xaml.cs
		public static void OpenBrowser(string url)
		{
			if(OperatingSystem.IsLinux()) {
				// If no associated application/json MimeType is found xdg-open opens retrun error
				// but it tries to open it anyway using the console editor (nano, vim, other..)
				ShellExec($"xdg-open {url}", waitForExit: false);
			} else {
				using Process? process = Process.Start(new ProcessStartInfo {
					FileName = OperatingSystem.IsWindows() ? url : "open",
					Arguments = OperatingSystem.IsMacOS() ? $"{url}" : "",
					CreateNoWindow = true,
					UseShellExecute = OperatingSystem.IsWindows()
				});
			}
		}

		//Opens a local file or folder in the OS default handler. Unlike OpenBrowser, the path is passed
		//as a single argument (via ArgumentList), so paths with spaces/parentheses work correctly.
		public static void OpenFileOrFolder(string path)
		{
			try {
				ProcessStartInfo psi;
				if(OperatingSystem.IsWindows()) {
					psi = new ProcessStartInfo { FileName = path, UseShellExecute = true };
				} else {
					psi = new ProcessStartInfo { FileName = OperatingSystem.IsMacOS() ? "open" : "xdg-open", UseShellExecute = false };
					psi.ArgumentList.Add(path);
				}
				using Process? process = Process.Start(psi);
			} catch { }
		}

		//Opens the folder containing 'path' and, where supported, highlights the file within it.
		public static void ShowInFolder(string path)
		{
			try {
				if(OperatingSystem.IsWindows()) {
					ProcessStartInfo psi = new ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = false };
					psi.ArgumentList.Add("/select,");
					psi.ArgumentList.Add(path);
					Process.Start(psi);
					return;
				}
				if(OperatingSystem.IsMacOS()) {
					ProcessStartInfo psi = new ProcessStartInfo { FileName = "open", UseShellExecute = false };
					psi.ArgumentList.Add("-R");
					psi.ArgumentList.Add(path);
					Process.Start(psi);
					return;
				}
				//Linux: ask the file manager (via D-Bus) to reveal the file; fall back to opening the folder.
				try {
					ProcessStartInfo psi = new ProcessStartInfo { FileName = "dbus-send", UseShellExecute = false };
					psi.ArgumentList.Add("--print-reply");
					psi.ArgumentList.Add("--dest=org.freedesktop.FileManager1");
					psi.ArgumentList.Add("--type=method_call");
					psi.ArgumentList.Add("/org/freedesktop/FileManager1");
					psi.ArgumentList.Add("org.freedesktop.FileManager1.ShowItems");
					psi.ArgumentList.Add("array:string:" + new Uri(path).AbsoluteUri);
					psi.ArgumentList.Add("string:");
					Process? p = Process.Start(psi);
					if(p != null) {
						p.WaitForExit(2000);
						if(p.HasExited && p.ExitCode == 0) {
							return;
						}
					}
				} catch { }
				//Fallback: just open the containing folder
				string? dir = Path.GetDirectoryName(path);
				if(dir != null) {
					OpenFileOrFolder(dir);
				}
			} catch { }
		}

		private static void ShellExec(string cmd, bool waitForExit = true)
		{
			var escapedArgs = Regex.Replace(cmd, "(?=[`~!#&*()|;'<>])", "\\").Replace("\"", "\\\\\\\"");

			using(Process? process = Process.Start(
				 new ProcessStartInfo {
					 FileName = "/bin/sh",
					 Arguments = $"-c \"{escapedArgs}\"",
					 RedirectStandardOutput = true,
					 UseShellExecute = false,
					 CreateNoWindow = true,
					 WindowStyle = ProcessWindowStyle.Hidden
				 }
			)) {
				if(waitForExit) {
					process?.WaitForExit();
				}
			}
		}
	}
}
