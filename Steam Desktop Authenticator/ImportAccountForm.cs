using System;
using System.Collections.Generic;
using System.Windows.Forms;
using SteamAuth;
using Newtonsoft.Json;
using System.IO;

namespace Steam_Desktop_Authenticator {
	public partial class ImportAccountForm : Form {
		private readonly Manifest _manifest;
        private string _password = null;

		public ImportAccountForm() {
			InitializeComponent();
			_manifest = Manifest.GetManifest();

            if (!_manifest.CanProceedAction(out _password))
                Close();
		}

		private void BtnImport_Click(object sender, EventArgs e) {
            // read EncryptionKey from imput box
            var encryptionKey = txtBox.Text;

            var fullPath = string.Empty;
            var fileName = string.Empty;
            var fileContent = string.Empty;
            // Open file browser > to select the file
            using (var fileDialog = new OpenFileDialog()) {
                // Set filter options and filter index.
                fileDialog.Filter = "maFiles (.maFile)|*.maFile|All Files (*.*)|*.*";
                fileDialog.FilterIndex = 1;
                fileDialog.Multiselect = false;

                // Call the ShowDialog method to show the dialog box.
                var isOk = fileDialog.ShowDialog() == DialogResult.OK;
                if (!isOk)
                    return;

                fullPath = fileDialog.FileName;
                fileName = fileDialog.SafeFileName;
                fileContent = File.ReadAllText(fileDialog.FileName);
            }

            var shouldEncryptBack = !string.IsNullOrEmpty(_password);
            try {
                if (string.IsNullOrEmpty(encryptionKey)) {
                    #region Import maFile

                    var plainMaFile = JsonConvert.DeserializeObject<SteamGuardAccount>(fileContent);
                    if (plainMaFile.Session.SteamID == 0)
                        throw new Exception("Invalid SteamID");

                    _manifest.SaveAccount(plainMaFile, shouldEncryptBack, _password);
                    MessageBox.Show("Account Imported!");
                    return;

                    #endregion
                }

                #region Import Encrypted maFile

                //Read manifest.json encryption_iv encryption_salt
                var importFileNameFound = false;
                string Salt_Found = null;
                string IV_Found = null;

                //No directory means no manifest file anyways.
                ImportManifest newImportManifest = new ImportManifest();
                newImportManifest.Encrypted = false;
                newImportManifest.Entries = new List<ImportManifestEntry>();

                // extract folder path
                string path = fullPath.Replace(fileName, "");

                // extract fileName
                string ImportFileName = fullPath.Replace(path, "");

                string ImportManifestFile = path + "manifest.json";

                if (!File.Exists(ImportManifestFile)) {
                    MessageBox.Show("manifest.json is missing!\nImport Failed.");
                    return;
                }

                string ImportManifestContents = File.ReadAllText(ImportManifestFile);

                try {
                    var account = JsonConvert.DeserializeObject<ImportManifest>(ImportManifestContents);
                    foreach (var entry in account.Entries) {
                        string FileName = entry.Filename;

                        if (ImportFileName == FileName) {
                            importFileNameFound = true;
                            IV_Found = entry.IV;
                            Salt_Found = entry.Salt;
                        }
                    }
                }
                catch (Exception)
                {
                    MessageBox.Show("Invalid content inside manifest.json!\nImport Failed.");
                    return;
                }

                #region DECRYPT & Import

                if (!importFileNameFound) {
                    MessageBox.Show("Account not found inside manifest.json.\nImport Failed.");
                    return;
                }
                if (Salt_Found == null && IV_Found == null) {
                    MessageBox.Show("manifest.json does not contain encrypted data.\nYour account may be unencrypted!\nImport Failed.");
                    return;
                }
                if (IV_Found == null) {
                    MessageBox.Show("manifest.json does not contain: encryption_iv\nImport Failed.");
                    return;
                }
                if (Salt_Found == null) {
                    MessageBox.Show("manifest.json does not contain: encryption_salt\nImport Failed.");
                    return;
                }

                var decryptedText = FileEncryptor.DecryptData(encryptionKey, Salt_Found, IV_Found, fileContent);
                if (string.IsNullOrEmpty(decryptedText)) {
                    MessageBox.Show("Decryption Failed.\nImport Failed.");
                    return;
                }

                var maFile = JsonConvert.DeserializeObject<SteamGuardAccount>(decryptedText);
                if (maFile.Session.SteamID == 0) {
                    MessageBox.Show("Invalid SteamID.\nImport Failed.");
                    return;
                }

                _manifest.SaveAccount(maFile, shouldEncryptBack, _password);
                MessageBox.Show("Account Imported!\nYour Account in now Decrypted!");

                #endregion //DECRYPT & Import END

                #endregion //Import Encrypted maFile END

            }
            catch (JsonException) {
                MessageBox.Show("This file is not a valid SteamAuth maFile.\nImport Failed.");
            }
            catch (Exception ex) {
                MessageBox.Show("Failed to import SteamAuth maFile.\n" + ex.Message);
            }
        }

		private void BtnCancel_Click(object sender, EventArgs e) => Close();

		private void Import_maFile_Form_FormClosing(object sender, FormClosingEventArgs e) {
		}
	}


	public class AppManifest {
		[JsonProperty("encrypted")]
		public bool Encrypted { get; set; }
	}


	public class ImportManifest {
		[JsonProperty("encrypted")]
		public bool Encrypted { get; set; }

		[JsonProperty("entries")]
		public List<ImportManifestEntry> Entries { get; set; }
	}

	public class ImportManifestEntry {
		[JsonProperty("encryption_iv")]
		public string IV { get; set; }

		[JsonProperty("encryption_salt")]
		public string Salt { get; set; }

		[JsonProperty("filename")]
		public string Filename { get; set; }

		[JsonProperty("steamid")]
		public ulong SteamID { get; set; }
	}
}
