using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using UniversalScanner.DeviceSDK.Hikvision;

namespace UniversalScanner.Forms
{
    public class BatchHikvisionOnvifForm : Form
    {
        private List<string> _ips;
        private DataGridView gridResults;
        private TextBox txtOnvifUser;
        private TextBox txtOnvifPass;
        private CheckBox chkShowPassword;
        private Button btnStart;
        private Button btnCancel;
        private bool _isCancelled = false;

        public BatchHikvisionOnvifForm(List<string> ips)
        {
            _ips = ips;
            InitializeComponent();
            PopulateGrid();
        }

        private void InitializeComponent()
        {
            this.Text = "Batch Enable Hikvision ONVIF";
            this.Size = new Size(500, 420);
            this.StartPosition = FormStartPosition.CenterParent;

            Label lblUser = new Label { Text = "ONVIF User:", Location = new Point(15, 15), AutoSize = true };
            txtOnvifUser = new TextBox { Location = new Point(100, 10), Width = 150, Text = "admin" };

            Label lblPass = new Label { Text = "ONVIF Pass:", Location = new Point(270, 15), AutoSize = true };
            txtOnvifPass = new TextBox { Location = new Point(350, 10), Width = 120, UseSystemPasswordChar = true };

            chkShowPassword = new CheckBox { Text = "Show password", Location = new Point(350, 35), AutoSize = true };
            chkShowPassword.CheckedChanged += (s, ev) => 
            {
                txtOnvifPass.UseSystemPasswordChar = !chkShowPassword.Checked;
            };

            gridResults = new DataGridView
            {
                Location = new Point(15, 70),
                Size = new Size(455, 250),
                AllowUserToAddRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            gridResults.Columns.Add("IP", "IP Address");
            gridResults.Columns.Add("Status", "Status");
            gridResults.Columns.Add("Details", "Details");

            btnStart = new Button { Text = "Start", Location = new Point(300, 335), Width = 80 };
            btnStart.Click += BtnStart_Click;

            btnCancel = new Button { Text = "Cancel", Location = new Point(390, 335), Width = 80 };
            btnCancel.Click += BtnCancel_Click;

            this.Controls.Add(lblUser);
            this.Controls.Add(txtOnvifUser);
            this.Controls.Add(lblPass);
            this.Controls.Add(txtOnvifPass);
            this.Controls.Add(chkShowPassword);
            this.Controls.Add(gridResults);
            this.Controls.Add(btnStart);
            this.Controls.Add(btnCancel);
        }

        private void PopulateGrid()
        {
            foreach (var ip in _ips)
            {
                gridResults.Rows.Add(ip, "Pending", "");
            }
        }

        private async void BtnStart_Click(object sender, EventArgs e)
        {
            string onvifUser = txtOnvifUser.Text.Trim();
            string onvifPass = txtOnvifPass.Text;

            if (string.IsNullOrEmpty(onvifUser) || string.IsNullOrEmpty(onvifPass))
            {
                MessageBox.Show(this, "Username and password cannot be empty.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string adminUser, adminPass;
            UniversalLoginManager.GetUniversalCredentials(out adminUser, out adminPass);

            if (string.IsNullOrEmpty(adminUser) || string.IsNullOrEmpty(adminPass))
            {
                using (var loginForm = new UniversalLoginForm())
                {
                    if (loginForm.ShowDialog(this) == DialogResult.OK)
                    {
                        UniversalLoginManager.GetUniversalCredentials(out adminUser, out adminPass);
                    }
                    else
                    {
                        return;
                    }
                }
            }

            btnStart.Enabled = false;
            txtOnvifUser.Enabled = false;
            txtOnvifPass.Enabled = false;
            _isCancelled = false;

            for (int i = 0; i < gridResults.Rows.Count; i++)
            {
                if (_isCancelled)
                {
                    gridResults.Rows[i].Cells[1].Value = "Cancelled";
                    continue;
                }

                string ip = gridResults.Rows[i].Cells[0].Value.ToString();
                gridResults.Rows[i].Cells[1].Value = "Processing...";

                try
                {
                    var result = await HikvisionOnvifManager.ProvisionAsync(ip, adminUser, adminPass, onvifUser, onvifPass);
                    
                    if (result.Success)
                    {
                        gridResults.Rows[i].Cells[1].Value = "Success";
                        gridResults.Rows[i].Cells[2].Value = result.Stage;
                    }
                    else if (result.IsPartialSuccess)
                    {
                        gridResults.Rows[i].Cells[1].Value = "Partial Success";
                        gridResults.Rows[i].Cells[2].Value = result.ErrorMessage;
                    }
                    else
                    {
                        gridResults.Rows[i].Cells[1].Value = "Failed";
                        gridResults.Rows[i].Cells[2].Value = result.ErrorMessage;
                    }
                }
                catch (Exception ex)
                {
                    gridResults.Rows[i].Cells[1].Value = "Exception";
                    gridResults.Rows[i].Cells[2].Value = ex.Message;
                }
            }

            MessageBox.Show(this, "Batch processing complete.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
            btnCancel.Text = "Close";
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            if (btnStart.Enabled)
            {
                this.Close();
            }
            else
            {
                _isCancelled = true;
                btnCancel.Enabled = false;
            }
        }
    }
}
