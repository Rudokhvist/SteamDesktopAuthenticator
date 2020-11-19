using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using SteamAuth;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Net;
using Newtonsoft.Json;
using System.Threading;
using System.Drawing;
using System.Linq;

namespace Steam_Desktop_Authenticator {
	public partial class MainForm : Form {
		private SteamGuardAccount CurrentAccount = null;
		private SteamGuardAccount[] AllAccounts;
		private readonly List<string> UpdatedSessions = new List<string>();
		private Manifest Manifest;
		private static readonly SemaphoreSlim ConfirmationsSemaphore = new SemaphoreSlim(1, 1);

		private long SteamTime = 0;
		private long CurrentSteamChunk = 0;
		private string PassKey = null;
		private bool StartSilentFlag = false;

		// Forms
		private readonly TradePopupForm PopupFrm = new TradePopupForm();

		protected override void WndProc(ref System.Windows.Forms.Message message) {
			if (message.Msg == SingleInstance.WM_SHOWFIRSTINSTANCE) {
				Show();
				WindowState = FormWindowState.Normal;
			}
			base.WndProc(ref message);
		}

		public MainForm() => InitializeComponent();

		public void SetEncryptionKey(string key) => PassKey = key;

		public void StartSilent(bool silent) => StartSilentFlag = silent;

		// Form event handlers

		private void MainForm_Shown(object sender, EventArgs e) {
			labelVersion.Text = string.Format("v{0}", Application.ProductVersion);
			try {
				Manifest = Manifest.GetManifest();
			} catch (ManifestParseException) {
				MessageBox.Show("Unable to read your settings. Try restating SDA.", "Steam Desktop Authenticator", MessageBoxButtons.OK, MessageBoxIcon.Error);
				Close();
			}

			// Make sure we don't show that welcome dialog again
			Manifest.FirstRun = false;
			Manifest.Save();

			// Tick first time manually to sync time
			TimerSteamGuard_Tick(new object(), EventArgs.Empty);

			if (Manifest.Encrypted) {
				if (PassKey == null) {
					PassKey = Manifest.PromptForPassKey();
					if (PassKey == null) {
						Application.Exit();
					}
				}

				btnManageEncryption.Text = "Manage Encryption";
			} else {
				btnManageEncryption.Text = "Setup Encryption";
			}

			btnManageEncryption.Enabled = Manifest.Entries.Count > 0;

			LoadSettings();
			LoadAccountsList();

			CheckForUpdates();

			if (StartSilentFlag) {
				WindowState = FormWindowState.Minimized;
			}
		}

		private void MainForm_Load(object sender, EventArgs e) => trayIcon.Icon = Icon;

		private void MainForm_Resize(object sender, EventArgs e) {
			if (WindowState == FormWindowState.Minimized) {
				Hide();
			}
		}

		private void MainForm_FormClosing(object sender, FormClosingEventArgs e) => Application.Exit();


		// UI Button handlers

		private void BtnSteamLogin_Click(object sender, EventArgs e) {
			LoginForm loginForm = new LoginForm();
			loginForm.ShowDialog();
			LoadAccountsList();
		}

		private async void BtnTradeConfirmations_Click(object sender, EventArgs e) {
			if (CurrentAccount == null) {
				return;
			}

			string oText = btnTradeConfirmations.Text;
			btnTradeConfirmations.Text = "Loading...";
			await RefreshAccountSession(CurrentAccount);
			btnTradeConfirmations.Text = oText;

			try {
				ConfirmationFormWeb confirms = new ConfirmationFormWeb(CurrentAccount);
				confirms.Show();
			} catch (Exception) {
				DialogResult res = MessageBox.Show("You are missing a dependency required to view your trade confirmations.\nWould you like to install it now?", "Trade confirmations failed to open", MessageBoxButtons.YesNo);
				if (res == DialogResult.Yes) {
					new InstallRedistribForm(true).ShowDialog();
				}
			}
		}

		private void BtnManageEncryption_Click(object sender, EventArgs e) {
			if (Manifest.Encrypted) {
				InputForm currentPassKeyForm = new InputForm("Enter current passkey", true);
				currentPassKeyForm.ShowDialog();

				if (currentPassKeyForm.Canceled) {
					return;
				}

				string curPassKey = currentPassKeyForm.txtBox.Text;

				InputForm changePassKeyForm = new InputForm("Enter new passkey, or leave blank to remove encryption.");
				changePassKeyForm.ShowDialog();

				if (changePassKeyForm.Canceled && !string.IsNullOrEmpty(changePassKeyForm.txtBox.Text)) {
					return;
				}

				InputForm changePassKeyForm2 = new InputForm("Confirm new passkey, or leave blank to remove encryption.");
				changePassKeyForm2.ShowDialog();

				if (changePassKeyForm2.Canceled && !string.IsNullOrEmpty(changePassKeyForm.txtBox.Text)) {
					return;
				}

				string newPassKey = changePassKeyForm.txtBox.Text;
				string confirmPassKey = changePassKeyForm2.txtBox.Text;

				if (newPassKey != confirmPassKey) {
					MessageBox.Show("Passkeys do not match.");
					return;
				}

				if (newPassKey.Length == 0) {
					newPassKey = null;
				}

				string action = newPassKey == null ? "remove" : "change";
				if (!Manifest.ChangeEncryptionKey(curPassKey, newPassKey)) {
					MessageBox.Show("Unable to " + action + " passkey.");
				} else {
					MessageBox.Show("Passkey successfully " + action + "d.");
					LoadAccountsList();
				}
			} else {
				PassKey = Manifest.PromptSetupPassKey();
				LoadAccountsList();
			}
		}

		private void LabelUpdate_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) {
			if (NewVersion == null || CurrentVersion == null) {
				CheckForUpdates();
			} else {
				CompareVersions();
			}
		}

		private void BtnCopy_Click(object sender, EventArgs e) => CopyLoginToken();


		// Tool strip menu handlers

		private void MenuQuit_Click(object sender, EventArgs e) => Application.Exit();

		private void MenuRemoveAccountFromManifest_Click(object sender, EventArgs e) {
			if (Manifest.Encrypted) {
				MessageBox.Show("You cannot remove accounts from the manifest file while it is encrypted.", "Remove from manifest", MessageBoxButtons.OK, MessageBoxIcon.Error);
			} else {
				DialogResult res = MessageBox.Show("This will remove the selected account from the manifest file.\nUse this to move a maFile to another computer.\nThis will NOT delete your maFile.", "Remove from manifest", MessageBoxButtons.OKCancel);
				if (res == DialogResult.OK) {
					Manifest.RemoveAccount(CurrentAccount, false);
					MessageBox.Show("Account removed from manifest.\nYou can now move its maFile to another computer and import it using the File menu.", "Remove from manifest");
					LoadAccountsList();
				}
			}
		}

		private void MenuLoginAgain_Click(object sender, EventArgs e) => PromptRefreshLogin(CurrentAccount);

		private void MenuImportAccount_Click(object sender, EventArgs e) {
			ImportAccountForm currentImport_maFile_Form = new ImportAccountForm();
			currentImport_maFile_Form.ShowDialog();
			LoadAccountsList();
		}

		private void MenuSettings_Click(object sender, EventArgs e) {
			new SettingsForm().ShowDialog();
			Manifest = Manifest.GetManifest(true);
			LoadSettings();
		}

		private void MenuDeactivateAuthenticator_Click(object sender, EventArgs e) {
			if (CurrentAccount == null) {
				return;
			}

			DialogResult res = MessageBox.Show("Would you like to remove Steam Guard completely?\nYes - Remove Steam Guard completely.\nNo - Switch back to Email authentication.", "Remove Steam Guard", MessageBoxButtons.YesNoCancel);
			int scheme = 0;
			if (res == DialogResult.Yes) {
				scheme = 2;
			} else if (res == DialogResult.No) {
				scheme = 1;
			} else if (res == DialogResult.Cancel) {
				scheme = 0;
			}

			if (scheme != 0) {
				string confCode = CurrentAccount.GenerateSteamGuardCode();
				InputForm confirmationDialog = new InputForm(string.Format("Removing Steam Guard from {0}. Enter this confirmation code: {1}", CurrentAccount.AccountName, confCode));
				confirmationDialog.ShowDialog();

				if (confirmationDialog.Canceled) {
					return;
				}

				string enteredCode = confirmationDialog.txtBox.Text.ToUpper();
				if (enteredCode != confCode) {
					MessageBox.Show("Confirmation codes do not match. Steam Guard not removed.");
					return;
				}

				bool success = CurrentAccount.DeactivateAuthenticator(scheme);
				if (success) {
					MessageBox.Show(string.Format("Steam Guard {0}. maFile will be deleted after hitting okay. If you need to make a backup, now's the time.", (scheme == 2 ? "removed completely" : "switched to emails")));
					Manifest.RemoveAccount(CurrentAccount);
					LoadAccountsList();
				} else {
					MessageBox.Show("Steam Guard failed to deactivate.");
				}
			} else {
				MessageBox.Show("Steam Guard was not removed. No action was taken.");
			}
		}

		private async void MenuRefreshSession_Click(object sender, EventArgs e) {
			bool status = await RefreshAccountSession(CurrentAccount);
			if (status == true) {
				MessageBox.Show("Your session has been refreshed.", "Session refresh", MessageBoxButtons.OK, MessageBoxIcon.Information);
				Manifest.SaveAccount(CurrentAccount, Manifest.Encrypted, PassKey);
			} else {
				MessageBox.Show("Failed to refresh your session.\nTry using the \"Login again\" option.", "Session refresh", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		// Tray menu handlers
		private void TrayIcon_MouseDoubleClick(object sender, MouseEventArgs e) => TrayRestore_Click(sender, EventArgs.Empty);

		private void TrayRestore_Click(object sender, EventArgs e) {
			Show();
			WindowState = FormWindowState.Normal;
		}

		private void TrayQuit_Click(object sender, EventArgs e) => Application.Exit();

		private void TrayTradeConfirmations_Click(object sender, EventArgs e) => BtnTradeConfirmations_Click(sender, e);

		private void TrayCopySteamGuard_Click(object sender, EventArgs e) {
			if (txtLoginToken.Text != "") {
				Clipboard.SetText(txtLoginToken.Text);
			}
		}

		private void TrayAccountList_SelectedIndexChanged(object sender, EventArgs e) => listAccounts.SelectedIndex = trayAccountList.SelectedIndex;


		// Misc UI handlers
		private void ListAccounts_SelectedValueChanged(object sender, EventArgs e) {
			for (int i = 0; i < AllAccounts.Length; i++) {
				// Check if index is out of bounds first
				if (i < 0 || listAccounts.SelectedIndex < 0) {
					continue;
				}

				SteamGuardAccount account = AllAccounts[i];
				if (account.AccountName == (string) listAccounts.Items[listAccounts.SelectedIndex]) {
					trayAccountList.Text = account.AccountName;
					CurrentAccount = account;
					LoadAccountInfo();
					break;
				}
			}
		}

		private void TxtAccSearch_TextChanged(object sender, EventArgs e) {
			List<string> names = new List<string>(GetAllNames());
			names = names.FindAll(new Predicate<string>(IsFilter));

			listAccounts.Items.Clear();
			listAccounts.Items.AddRange(names.ToArray());

			trayAccountList.Items.Clear();
			trayAccountList.Items.AddRange(names.ToArray());
		}


		// Timers

		private async void TimerSteamGuard_Tick(object sender, EventArgs e) {
			lblStatus.Text = "Aligning time with Steam...";
			SteamTime = await TimeAligner.GetSteamTimeAsync();
			lblStatus.Text = "";

			CurrentSteamChunk = SteamTime / 30L;
			int secondsUntilChange = (int) (SteamTime - (CurrentSteamChunk * 30L));

			LoadAccountInfo();
			if (CurrentAccount != null) {
				pbTimeout.Value = 30 - secondsUntilChange;
			}
		}

		private async void TimerTradesPopup_Tick(object sender, EventArgs e) {
			if (CurrentAccount == null || PopupFrm.Visible) {
				return;
			}

			if (!ConfirmationsSemaphore.Wait(0)) {
				return; //Only one thread may access this critical section at once. Mutex is a bad choice here because it'll cause a pileup of threads.
			}

			List<Confirmation> confs = new List<Confirmation>();
			Dictionary<SteamGuardAccount, List<Confirmation>> autoAcceptConfirmations = new Dictionary<SteamGuardAccount, List<Confirmation>>();

			SteamGuardAccount[] accs =
				Manifest.CheckAllAccounts ? AllAccounts : new SteamGuardAccount[] { CurrentAccount };

			try {
				lblStatus.Text = "Checking confirmations...";

				foreach (SteamGuardAccount acc in accs) {
					try {
						Confirmation[] tmp = await acc.FetchConfirmationsAsync();
						foreach (Confirmation conf in tmp) {
							if ((conf.ConfType == Confirmation.EConfirmationType.MarketSellTransaction && Manifest.AutoConfirmMarketTransactions) ||
								(conf.ConfType == Confirmation.EConfirmationType.Trade && Manifest.AutoConfirmTrades)) {
								if (!autoAcceptConfirmations.ContainsKey(acc)) {
									autoAcceptConfirmations[acc] = new List<Confirmation>();
								}

								autoAcceptConfirmations[acc].Add(conf);
							} else {
								confs.Add(conf);
							}
						}
					} catch (SteamGuardAccount.WGTokenInvalidException) {
						lblStatus.Text = "Refreshing session";
						await acc.RefreshSessionAsync(); //Don't save it to the HDD, of course. We'd need their encryption passkey again.
						lblStatus.Text = "";
					} catch (SteamGuardAccount.WGTokenExpiredException) {
						//Prompt to relogin
						PromptRefreshLogin(acc);
						break; //Don't bombard a user with login refresh requests if they have multiple accounts. Give them a few seconds to disable the autocheck option if they want.
					} catch (WebException) {

					}
				}

				lblStatus.Text = "";

				if (confs.Count > 0) {
					PopupFrm.Confirmations = confs.ToArray();
					PopupFrm.Popup();
				}
				if (autoAcceptConfirmations.Count > 0) {
					foreach (SteamGuardAccount acc in autoAcceptConfirmations.Keys) {
						Confirmation[] confirmations = autoAcceptConfirmations[acc].ToArray();
						acc.AcceptMultipleConfirmations(confirmations);
					}
				}
			} catch (SteamGuardAccount.WGTokenInvalidException) {
				lblStatus.Text = "";
			}

			ConfirmationsSemaphore.Release();
		}

		// Other methods

		private void CopyLoginToken() {
			string text = txtLoginToken.Text;
			if (!string.IsNullOrEmpty(text)) {
				Clipboard.SetText(text);
			}
		}

		/// <summary>
		/// Refresh this account's session data using their OAuth Token
		/// </summary>
		/// <param name="account">The account to refresh</param>
		/// <param name="attemptRefreshLogin">Whether or not to prompt the user to re-login if their OAuth token is expired.</param>
		/// <returns></returns>
		private async Task<bool> RefreshAccountSession(SteamGuardAccount account, bool attemptRefreshLogin = true) {
			if (account == null) {
				return false;
			}

			try {
				bool refreshed = await account.RefreshSessionAsync();
				return refreshed; //No exception thrown means that we either successfully refreshed the session or there was a different issue preventing us from doing so.
			} catch (SteamGuardAccount.WGTokenExpiredException) {
				if (!attemptRefreshLogin) {
					return false;
				}

				PromptRefreshLogin(account);

				return await RefreshAccountSession(account, false);
			}
		}

		/// <summary>
		/// Display a login form to the user to refresh their OAuth Token
		/// </summary>
		/// <param name="account">The account to refresh</param>
		private void PromptRefreshLogin(SteamGuardAccount account) {
			LoginForm loginForm = new LoginForm(LoginForm.ELoginType.Refresh, account);
			loginForm.ShowDialog();
		}

		/// <summary>
		/// Load UI with the current account info, this is run every second
		/// </summary>
		private void LoadAccountInfo() {
			if (CurrentAccount != null && SteamTime != 0) {
				PopupFrm.Account = CurrentAccount;
				txtLoginToken.Text = CurrentAccount.GenerateSteamGuardCodeForTime(SteamTime);
				groupAccount.Text = "Account: " + CurrentAccount.AccountName;
			}
		}

		/// <summary>
		/// Decrypts files and populates list UI with accounts
		/// </summary>
		private void LoadAccountsList() {
			CurrentAccount = null;

			listAccounts.Items.Clear();
			listAccounts.SelectedIndex = -1;

			trayAccountList.Items.Clear();
			trayAccountList.SelectedIndex = -1;

			AllAccounts = Manifest.GetAllAccounts(PassKey);

			if (AllAccounts.Length > 0) {
				for (int i = 0; i < AllAccounts.Length; i++) {
					SteamGuardAccount account = AllAccounts[i];
					listAccounts.Items.Add(account.AccountName);
					trayAccountList.Items.Add(account.AccountName);
				}

				listAccounts.SelectedIndex = 0;
				trayAccountList.SelectedIndex = 0;

				listAccounts.Sorted = true;
				trayAccountList.Sorted = true;
			}
			menuDeactivateAuthenticator.Enabled = btnTradeConfirmations.Enabled = AllAccounts.Length > 0;
		}

		private void ListAccounts_KeyDown(object sender, KeyEventArgs e) {
			if (e.Control) {
				if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) {
					int to = listAccounts.SelectedIndex - (e.KeyCode == Keys.Up ? 1 : -1);
					Manifest.MoveEntry(listAccounts.SelectedIndex, to);
					LoadAccountsList();
				}
				return;
			}

			if (!IsKeyAChar(e.KeyCode) && !IsKeyADigit(e.KeyCode)) {
				return;
			}

			txtAccSearch.Focus();
			txtAccSearch.Text = e.KeyCode.ToString();
			txtAccSearch.SelectionStart = 1;
		}

		private static bool IsKeyAChar(Keys key) => key >= Keys.A && key <= Keys.Z;

		private static bool IsKeyADigit(Keys key) => (key >= Keys.D0 && key <= Keys.D9) || (key >= Keys.NumPad0 && key <= Keys.NumPad9);

		private bool IsFilter(string f) {
			if (txtAccSearch.Text.StartsWith("~")) {
				try {
					return Regex.IsMatch(f, txtAccSearch.Text);
				} catch (Exception) {
					return true;
				}

			} else {
				return f.Contains(txtAccSearch.Text);
			}
		}

		private string[] GetAllNames() {
			string[] itemArray = new string[AllAccounts.Length];
			for (int i = 0; i < itemArray.Length; i++) {
				itemArray[i] = AllAccounts[i].AccountName;
			}
			return itemArray;
		}

		private void LoadSettings() {
			timerTradesPopup.Enabled = Manifest.PeriodicChecking;
			timerTradesPopup.Interval = Manifest.PeriodicCheckingInterval * 1000;
		}

		// Logic for version checking
		private Version NewVersion = null;
		private Version CurrentVersion = null;
		private WebClient UpdateClient = null;
		private string UpdateUrl = null;
		private bool StartupUpdateCheck = true;

		private void CheckForUpdates() {
			if (UpdateClient == null) {
				UpdateClient = new WebClient();
				UpdateClient.DownloadStringCompleted += UpdateClient_DownloadStringCompleted;
				UpdateClient.Headers.Add("Content-Type", "application/json");
				UpdateClient.Headers.Add("User-Agent", "Steam Desktop Authenticator");
				UpdateClient.DownloadStringAsync(new Uri("https://api.github.com/repos/Ryzhehvost/SteamDesktopAuthenticator/releases/latest"));
			}
		}

		private void CompareVersions() {
			if (NewVersion > CurrentVersion) {
				labelUpdate.Text = "Download new version"; // Show the user a new version is available if they press no
				DialogResult updateDialog = MessageBox.Show(string.Format("A new version is available! Would you like to download it now?\nYou will update from version {0} to {1}", Application.ProductVersion, NewVersion.ToString()), "New Version", MessageBoxButtons.YesNo);
				if (updateDialog == DialogResult.Yes) {
					Process.Start(UpdateUrl);
				}
			} else {
				if (!StartupUpdateCheck) {
					MessageBox.Show(string.Format("You are using the latest version: {0}", Application.ProductVersion));
				}
			}

			NewVersion = null; // Check the api again next time they check for updates
			UpdateClient = null; // Set to null to indicate it's done checking
			StartupUpdateCheck = false; // Set when it's done checking on startup
		}

		private void UpdateClient_DownloadStringCompleted(object sender, DownloadStringCompletedEventArgs e) {
			try {
				dynamic resultObject = JsonConvert.DeserializeObject(e.Result);
				NewVersion = new Version(resultObject.tag_name.Value);
				CurrentVersion = new Version(Application.ProductVersion);
				UpdateUrl = resultObject.assets.First.browser_download_url.Value;
				CompareVersions();
			} catch (Exception) {
				MessageBox.Show("Failed to check for updates.");
			}
		}

		private void MainForm_KeyDown(object sender, KeyEventArgs e) {
			if (e.KeyCode == Keys.C && e.Modifiers == Keys.Control) {
				CopyLoginToken();
			}
		}

		private void PanelButtons_SizeChanged(object sender, EventArgs e) {
			int totButtons = panelButtons.Controls.OfType<Button>().Count();

			Point curPos = new Point(0, 0);
			foreach (Button but in panelButtons.Controls.OfType<Button>()) {
				but.Width = panelButtons.Width / totButtons;
				but.Location = curPos;
				curPos = new Point(curPos.X + but.Width, 0);
			}
		}

		private void ListAccounts_SelectedIndexChanged(object sender, EventArgs e) {

		}
	}
}
