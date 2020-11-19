using System;
using System.Windows.Forms;
using SteamAuth;

namespace Steam_Desktop_Authenticator {
	public partial class LoginForm : Form {
		public SteamGuardAccount Account;
		public ELoginType LoginReason;

		public LoginForm(ELoginType loginReason = ELoginType.Initial, SteamGuardAccount account = null) {
			InitializeComponent();
			LoginReason = loginReason;
			Account = account;

			try {
				if (LoginReason != ELoginType.Initial) {
					txtUsername.Text = account.AccountName;
					txtUsername.Enabled = false;
				}

				if (LoginReason == ELoginType.Refresh) {
					labelLoginExplanation.Text = "Your Steam credentials have expired. For trade and market confirmations to work properly, please login again.";
				}
			} catch (Exception) {
				MessageBox.Show("Failed to find your account. Try closing and re-opening SDA.", "Login Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
				Close();
			}
		}

		public void SetUsername(string username) => txtUsername.Text = username;

		public string FilterPhoneNumber(string phoneNumber) => phoneNumber.Replace("-", "").Replace("(", "").Replace(")", "");

		public bool PhoneNumberOkay(string phoneNumber) {
			if (phoneNumber == null || phoneNumber.Length == 0) {
				return false;
			}

			return phoneNumber[0] == '+';
		}

		private void BtnSteamLogin_Click(object sender, EventArgs e) {
			string username = txtUsername.Text;
			string password = txtPassword.Text;

			if (LoginReason == ELoginType.Refresh) {
				RefreshLogin(username, password);
				return;
			}

			UserLogin userLogin = new UserLogin(username, password);
			bool Moving = false;

			ELoginResult response;
			while ((response = userLogin.DoLogin()) != ELoginResult.LoginOkay) {
				switch (response) {
					case ELoginResult.NeedEmail:
						InputForm emailForm = new InputForm("Enter the code sent to your email:");
						emailForm.ShowDialog();
						if (emailForm.Canceled) {
							Close();
							return;
						}

						userLogin.EmailCode = emailForm.txtBox.Text;
						break;

					case ELoginResult.NeedCaptcha:
						CaptchaForm captchaForm = new CaptchaForm(userLogin.CaptchaGID);
						captchaForm.ShowDialog();
						if (captchaForm.Canceled) {
							Close();
							return;
						}

						userLogin.CaptchaText = captchaForm.CaptchaCode;
						break;

					case ELoginResult.Need2FA:
						DialogResult result = MessageBox.Show("This account already has a mobile authenticator linked to it.\nIf you want to move authenticator to this device, press \"Yes\" (old authenticator will become inoperable!).", "Authenticator Exist", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);
						if (result == DialogResult.Yes) {
							Moving = true;
						} else {
							Close();
							return;
						}
						break;

					case ELoginResult.BadRSA:
						MessageBox.Show("Error logging in: Steam returned \"BadRSA\".", "Login Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
						Close();
						return;

					case ELoginResult.BadCredentials:
						MessageBox.Show("Error logging in: Username or password was incorrect.", "Login Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
						Close();
						return;

					case ELoginResult.TooManyFailedLogins:
						MessageBox.Show("Error logging in: Too many failed logins, try again later.", "Login Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
						Close();
						return;

					case ELoginResult.GeneralFailure:
						MessageBox.Show("Error logging in: Steam returned \"GeneralFailure\".", "Login Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
						Close();
						return;
				}

				if (Moving) {
					break;
				}
			}

			//Login succeeded

			SessionData session = userLogin.Session;
			AuthenticatorLinker linker = new AuthenticatorLinker(session);

			AuthenticatorLinker.ELinkResult linkResponse;
			if (Moving) {
				linkResponse = linker.MoveAuthenticator();
				if (linkResponse == AuthenticatorLinker.ELinkResult.GeneralFailure) {
					MessageBox.Show("Failed to move authenticator to this device.", "Failed to move", MessageBoxButtons.OK, MessageBoxIcon.Error);
					Close();
					return;
				}
			} else {
				while ((linkResponse = linker.AddAuthenticator()) != AuthenticatorLinker.ELinkResult.AwaitingFinalization) {
					switch (linkResponse) {
						case AuthenticatorLinker.ELinkResult.MustProvidePhoneNumber:
							string phoneNumber = "";
							while (!PhoneNumberOkay(phoneNumber)) {
								InputForm phoneNumberForm = new InputForm("Enter your phone number in the following format: +{cC} phoneNumber. EG, +1 123-456-7890");
								phoneNumberForm.txtBox.Text = "+1 ";
								phoneNumberForm.ShowDialog();
								if (phoneNumberForm.Canceled) {
									Close();
									return;
								}

								phoneNumber = FilterPhoneNumber(phoneNumberForm.txtBox.Text);
							}
							linker.PhoneNumber = phoneNumber;
							break;

						case AuthenticatorLinker.ELinkResult.MustRemovePhoneNumber:
							linker.PhoneNumber = null;
							break;

						case AuthenticatorLinker.ELinkResult.MustConfirmEmail:
							MessageBox.Show("Please check your email, and click the link Steam sent you before continuing.");
							break;

						case AuthenticatorLinker.ELinkResult.GeneralFailure:
							MessageBox.Show("Error adding your phone number. Steam returned \"GeneralFailure\".");
							Close();
							return;
					}
				}
			}

			Manifest manifest = Manifest.GetManifest();
			string passKey = null;
			if (manifest.Entries.Count == 0) {
				passKey = manifest.PromptSetupPassKey("Please enter an encryption passkey. Leave blank or hit cancel to not encrypt (VERY INSECURE).");
			} else if (manifest.Entries.Count > 0 && manifest.Encrypted) {
				bool passKeyValid = false;
				while (!passKeyValid) {
					InputForm passKeyForm = new InputForm("Please enter your current encryption passkey.");
					passKeyForm.ShowDialog();
					if (!passKeyForm.Canceled) {
						passKey = passKeyForm.txtBox.Text;
						passKeyValid = manifest.VerifyPasskey(passKey);
						if (!passKeyValid) {
							MessageBox.Show("That passkey is invalid. Please enter the same passkey you used for your other accounts.");
						}
					} else {
						Close();
						return;
					}
				}
			}

			if (!Moving) {
				//Save the file immediately; losing this would be bad.
				if (!manifest.SaveAccount(linker.LinkedAccount, passKey != null, passKey)) {
					manifest.RemoveAccount(linker.LinkedAccount);
					MessageBox.Show("Unable to save mobile authenticator file. The mobile authenticator has not been linked.");
					Close();
					return;
				}

				MessageBox.Show("The Mobile Authenticator has not yet been linked. Before finalizing the authenticator, please write down your revocation code: " + linker.LinkedAccount.RevocationCode);
			}

			AuthenticatorLinker.EFinalizeResult finalizeResponse = AuthenticatorLinker.EFinalizeResult.GeneralFailure;
			while (finalizeResponse != AuthenticatorLinker.EFinalizeResult.Success) {
				InputForm smsCodeForm = new InputForm("Please input the SMS code sent to your phone.");
				smsCodeForm.ShowDialog();
				if (smsCodeForm.Canceled) {
					manifest.RemoveAccount(linker.LinkedAccount);
					Close();
					return;
				}
				if (!Moving) {
					InputForm confirmRevocationCode = new InputForm("Please enter your revocation code to ensure you've saved it.");
					confirmRevocationCode.ShowDialog();
					if (confirmRevocationCode.txtBox.Text.ToUpper() != linker.LinkedAccount.RevocationCode) {
						MessageBox.Show("Revocation code incorrect; the authenticator has not been linked.");
						manifest.RemoveAccount(linker.LinkedAccount);
						Close();
						return;
					}
				}
				string smsCode = smsCodeForm.txtBox.Text;
				if (Moving) {
					finalizeResponse = linker.FinalizeMoveAuthenticator(smsCode);
				} else {
					finalizeResponse = linker.FinalizeAddAuthenticator(smsCode);
				}
				switch (finalizeResponse) {
					case AuthenticatorLinker.EFinalizeResult.BadSMSCode:
						continue;

					case AuthenticatorLinker.EFinalizeResult.UnableToGenerateCorrectCodes:
						MessageBox.Show("Unable to generate the proper codes to finalize this authenticator. The authenticator should not have been linked. In the off-chance it was, please write down your revocation code, as this is the last chance to see it: " + linker.LinkedAccount.RevocationCode);
						manifest.RemoveAccount(linker.LinkedAccount);
						Close();
						return;

					case AuthenticatorLinker.EFinalizeResult.GeneralFailure:
						MessageBox.Show("Unable to finalize this authenticator. The authenticator should not have been linked. In the off-chance it was, please write down your revocation code, as this is the last chance to see it: " + linker.LinkedAccount.RevocationCode);
						manifest.RemoveAccount(linker.LinkedAccount);
						Close();
						return;
				}
			}

			//Linked, finally. Re-save with FullyEnrolled property.
			manifest.SaveAccount(linker.LinkedAccount, passKey != null, passKey);
			MessageBox.Show("Mobile authenticator successfully linked. Please write down your revocation code: " + linker.LinkedAccount.RevocationCode);
			Close();
		}

		/// <summary>
		/// Handles logging in to refresh session data. i.e. changing steam password.
		/// </summary>
		/// <param name="username">Steam username</param>
		/// <param name="password">Steam password</param>
		private async void RefreshLogin(string username, string password) {
			long steamTime = await TimeAligner.GetSteamTimeAsync();
			Manifest man = Manifest.GetManifest();

			Account.FullyEnrolled = true;

			UserLogin mUserLogin = new UserLogin(username, password);
			ELoginResult response;
			while ((response = mUserLogin.DoLogin()) != ELoginResult.LoginOkay) {
				switch (response) {
					case ELoginResult.NeedCaptcha:
						CaptchaForm captchaForm = new CaptchaForm(mUserLogin.CaptchaGID);
						captchaForm.ShowDialog();
						if (captchaForm.Canceled) {
							Close();
							return;
						}

						mUserLogin.CaptchaText = captchaForm.CaptchaCode;
						break;

					case ELoginResult.Need2FA:
						mUserLogin.TwoFactorCode = Account.GenerateSteamGuardCodeForTime(steamTime);
						break;

					case ELoginResult.BadRSA:
						MessageBox.Show("Error logging in: Steam returned \"BadRSA\".", "Login Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
						Close();
						return;

					case ELoginResult.BadCredentials:
						MessageBox.Show("Error logging in: Username or password was incorrect.", "Login Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
						Close();
						return;

					case ELoginResult.TooManyFailedLogins:
						MessageBox.Show("Error logging in: Too many failed logins, try again later.", "Login Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
						Close();
						return;

					case ELoginResult.GeneralFailure:
						MessageBox.Show("Error logging in: Steam returned \"GeneralFailure\".", "Login Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
						Close();
						return;
				}
			}

			Account.Session = mUserLogin.Session;

			HandleManifest(man, true);
		}

		private void HandleManifest(Manifest man, bool IsRefreshing = false) {
			string passKey = null;
			if (man.Entries.Count == 0) {
				passKey = man.PromptSetupPassKey("Please enter an encryption passkey. Leave blank or hit cancel to not encrypt (VERY INSECURE).");
			} else if (man.Entries.Count > 0 && man.Encrypted) {
				bool passKeyValid = false;
				while (!passKeyValid) {
					InputForm passKeyForm = new InputForm("Please enter your current encryption passkey.");
					passKeyForm.ShowDialog();
					if (!passKeyForm.Canceled) {
						passKey = passKeyForm.txtBox.Text;
						passKeyValid = man.VerifyPasskey(passKey);
						if (!passKeyValid) {
							MessageBox.Show("That passkey is invalid. Please enter the same passkey you used for your other accounts.");
						}
					} else {
						Close();
						return;
					}
				}
			}

			man.SaveAccount(Account, passKey != null, passKey);
			if (IsRefreshing) {
				MessageBox.Show("Your login session was refreshed.");
			} else {
				MessageBox.Show("Mobile authenticator successfully linked. Please write down your revocation code: " + Account.RevocationCode);
			}
			Close();
		}

		private void LoginForm_Load(object sender, EventArgs e) {
			if (Account != null && Account.AccountName != null) {
				txtUsername.Text = Account.AccountName;
			}
		}

		public enum ELoginType {
			Initial,
			Refresh
		}
	}
}
