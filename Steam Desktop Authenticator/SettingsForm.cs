using System;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator {
	public partial class SettingsForm : Form {
		readonly Manifest Manifest;
		readonly bool FullyLoaded = false;

		public SettingsForm() {
			InitializeComponent();

			// Get latest manifest
			Manifest = Manifest.GetManifest(true);

			chkPeriodicChecking.Checked = Manifest.PeriodicChecking;
			numPeriodicInterval.Value = Manifest.PeriodicCheckingInterval;
			chkCheckAll.Checked = Manifest.CheckAllAccounts;
			chkConfirmMarket.Checked = Manifest.AutoConfirmMarketTransactions;
			chkConfirmTrades.Checked = Manifest.AutoConfirmTrades;

			SetControlsEnabledState(chkPeriodicChecking.Checked);

			FullyLoaded = true;
		}

		private void SetControlsEnabledState(bool enabled) => numPeriodicInterval.Enabled = chkCheckAll.Enabled = chkConfirmMarket.Enabled = chkConfirmTrades.Enabled = enabled;

		private void ShowWarning(CheckBox affectedBox) {
			if (FullyLoaded) {

				DialogResult result = MessageBox.Show("Warning: enabling this will severely reduce the security of your items! Use of this option is at your own risk. Would you like to continue?", "Warning!", MessageBoxButtons.YesNo);
				if (result == DialogResult.No) {
					affectedBox.Checked = false;
				}
			}
		}

		private void BtnSave_Click(object sender, EventArgs e) {
			Manifest.PeriodicChecking = chkPeriodicChecking.Checked;
			Manifest.PeriodicCheckingInterval = (int) numPeriodicInterval.Value;
			Manifest.CheckAllAccounts = chkCheckAll.Checked;
			Manifest.AutoConfirmMarketTransactions = chkConfirmMarket.Checked;
			Manifest.AutoConfirmTrades = chkConfirmTrades.Checked;
			Manifest.Save();
			Close();
		}

		private void ChkPeriodicChecking_CheckedChanged(object sender, EventArgs e) => SetControlsEnabledState(chkPeriodicChecking.Checked);

		private void ChkConfirmMarket_CheckedChanged(object sender, EventArgs e) {
			if (chkConfirmMarket.Checked) {
				ShowWarning(chkConfirmMarket);
			}
		}

		private void ChkConfirmTrades_CheckedChanged(object sender, EventArgs e) {
			if (chkConfirmTrades.Checked) {
				ShowWarning(chkConfirmTrades);
			}
		}
	}
}
