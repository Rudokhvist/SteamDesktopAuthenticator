using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using CefSharp;
using CefSharp.WinForms;
using SteamAuth;

namespace Steam_Desktop_Authenticator {
	public partial class ConfirmationFormWeb : Form {
		private readonly ChromiumWebBrowser Browser;
		private readonly string SteamCookies;
		private readonly SteamGuardAccount SteamAccount;
		private string TradeID;

		public ConfirmationFormWeb(SteamGuardAccount steamAccount) {
			InitializeComponent();
			SteamAccount = steamAccount;
			Text = string.Format("Trade Confirmations - {0}", steamAccount.AccountName);

			CefSettings settings = new CefSettings {
				PersistSessionCookies = false,
				Locale = "en-US",
				UserAgent = "Mozilla/5.0 (Linux; Android 6.0; Nexus 6P Build/XXXXX; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/47.0.2526.68 Mobile Safari/537.36"
			};
			SteamCookies = string.Format("mobileClientVersion=0 (2.1.3); mobileClient=android; steamid={0}; steamLogin={1}; steamLoginSecure={2}; Steam_Language=english; dob=;", steamAccount.Session.SteamID.ToString(), steamAccount.Session.SteamLogin, steamAccount.Session.SteamLoginSecure);

			if (!Cef.IsInitialized) {
				Cef.Initialize(settings);
			}

			Browser = new ChromiumWebBrowser(steamAccount.GenerateConfirmationURL()) {
				Dock = DockStyle.Fill,
			};
			splitContainer1.Panel2.Controls.Add(Browser);

			BrowserRequestHandler handler = new BrowserRequestHandler {
				Cookies = SteamCookies
			};
			Browser.RequestHandler = handler;
			Browser.AddressChanged += Browser_AddressChanged;
			Browser.LoadingStateChanged += Browser_LoadingStateChanged;
		}

		private void Browser_LoadingStateChanged(object sender, LoadingStateChangedEventArgs e) {
			// This looks really ugly, but it's easier than implementing steam's steammobile:// protocol using CefSharp
			// We override the page's GetValueFromLocalURL() to pass in the keys for sending ajax requests
			Debug.WriteLine("IsLoading: " + e.IsLoading);
			if (e.IsLoading == false) {
				// Generate url for details
				string urlParams = SteamAccount.GenerateConfirmationQueryParams("details" + TradeID);

				string script = string.Format(@"window.GetValueFromLocalURL = 
                function(url, timeout, success, error, fatal) {{            
                    console.log(url);
                    if(url.indexOf('steammobile://steamguard?op=conftag&arg1=allow') !== -1) {{
                        // send confirmation (allow)
                        success('{0}');
                    }} else if(url.indexOf('steammobile://steamguard?op=conftag&arg1=cancel') !== -1) {{
                        // send confirmation (cancel)
                        success('{1}');
                    }} else if(url.indexOf('steammobile://steamguard?op=conftag&arg1=details') !== -1) {{
                        // get details
                        success('{2}');
                    }}
                }}", SteamAccount.GenerateConfirmationQueryParams("allow"), SteamAccount.GenerateConfirmationQueryParams("cancel"), urlParams);
				try {
					Browser.ExecuteScriptAsync(script);
				} catch (Exception) {
					Debug.WriteLine("Failed to execute script");
				}
			}
		}

		private void Browser_AddressChanged(object sender, AddressChangedEventArgs e) {
			string[] urlparts = Browser.Address.Split('#');
			if (urlparts.Length > 1) {
				TradeID = urlparts[1].Replace("conf_", "");
			}
		}

		private void BtnRefresh_Click(object sender, EventArgs e) => Browser.Load(SteamAccount.GenerateConfirmationURL());

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
			bool bHandled = false;
			switch (keyData) {
				case Keys.F5:
					Browser.Load(SteamAccount.GenerateConfirmationURL());
					bHandled = true;
					break;
				case Keys.F1:
					Browser.ShowDevTools();
					bHandled = true;
					break;
			}
			return bHandled;
		}
	}
}
