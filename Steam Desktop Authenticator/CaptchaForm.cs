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
	public partial class CaptchaForm : Form {
		public bool Canceled = false;
		public string CaptchaGID = "";
		public string CaptchaURL = "";
		public string CaptchaCode => txtBox.Text;

		public CaptchaForm(string GID) {
			CaptchaGID = GID;
			CaptchaURL = "https://steamcommunity.com/public/captcha.php?gid=" + GID;
			InitializeComponent();
			pictureBoxCaptcha.Load(CaptchaURL);
		}

		private void BtnAccept_Click(object sender, EventArgs e) {
			Canceled = false;
			Close();
		}

		private void BtnCancel_Click(object sender, EventArgs e) {
			Canceled = true;
			Close();
		}
	}
}
