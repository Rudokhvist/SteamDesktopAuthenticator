using SteamAuth;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator {
	public partial class TradePopupForm : Form {
		private SteamGuardAccount Acc;
		private List<Confirmation> Confirms = new List<Confirmation>();
		private bool Deny2, Accept2;

		public TradePopupForm() {
			InitializeComponent();
			lblStatus.Text = "";
		}

		public SteamGuardAccount Account {
			get => Acc;
			set { Acc = value; lblAccount.Text = Acc.AccountName; }
		}

		public Confirmation[] Confirmations {
			get => Confirms.ToArray();
			set => Confirms = new List<Confirmation>(value);
		}

		private void TradePopupForm_Load(object sender, EventArgs e) => Location = (Point) Size.Subtract(Screen.GetWorkingArea(this).Size, Size);

		private void BtnAccept_Click(object sender, EventArgs e) {
			if (!Accept2) {
				// Allow user to confirm first
				lblStatus.Text = "Press Accept again to confirm";
				btnAccept.BackColor = Color.FromArgb(128, 255, 128);
				Accept2 = true;
			} else {
				lblStatus.Text = "Accepting...";
				Acc.AcceptConfirmation(Confirms[0]);
				Confirms.RemoveAt(0);
				Reset();
			}
		}

		private void BtnDeny_Click(object sender, EventArgs e) {
			if (!Deny2) {
				lblStatus.Text = "Press Deny again to confirm";
				btnDeny.BackColor = Color.FromArgb(255, 255, 128);
				Deny2 = true;
			} else {
				lblStatus.Text = "Denying...";
				Acc.DenyConfirmation(Confirms[0]);
				Confirms.RemoveAt(0);
				Reset();
			}
		}

		private void Reset() {
			Deny2 = false;
			Accept2 = false;
			btnAccept.BackColor = Color.FromArgb(192, 255, 192);
			btnDeny.BackColor = Color.FromArgb(255, 255, 192);

			btnAccept.Text = "Accept";
			btnDeny.Text = "Deny";
			lblAccount.Text = "";
			lblStatus.Text = "";

			if (Confirms.Count == 0) {
				Hide();
			} else {
				//TODO: Re-add confirmation description support to SteamAuth.
				lblDesc.Text = "Confirmation";
			}
		}

		public void Popup() {
			Reset();
			Show();
		}
	}
}
