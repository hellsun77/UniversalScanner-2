using System;
using System.Drawing;
using System.Windows.Forms;

namespace UniversalScanner
{
    public class DhcpWarningForm : Form
    {
        private Label lblWarning;
        private Button btnCancel;
        private Button btnAdvanced;

        public DhcpWarningForm()
        {
            InitializeComponent();
            AppVisualTheme.Apply(this);
        }

        private void InitializeComponent()
        {
            this.lblWarning = new System.Windows.Forms.Label();
            this.btnCancel = new System.Windows.Forms.Button();
            this.btnAdvanced = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // lblWarning
            // 
            this.lblWarning.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblWarning.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblWarning.Location = new System.Drawing.Point(12, 9);
            this.lblWarning.Name = "lblWarning";
            this.lblWarning.Size = new System.Drawing.Size(360, 80);
            this.lblWarning.TabIndex = 0;
            this.lblWarning.Text = "This adapter uses DHCP. Windows may disable DHCP and break your network if a temporary IP is added. Use a static adapter or allow Universal Scanner to create a temporary static profile and restore DHCP afterward.";
            // 
            // btnCancel
            // 
            this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(297, 102);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 1;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            // 
            // btnAdvanced
            // 
            this.btnAdvanced.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.btnAdvanced.DialogResult = System.Windows.Forms.DialogResult.Yes;
            this.btnAdvanced.Location = new System.Drawing.Point(90, 102);
            this.btnAdvanced.Name = "btnAdvanced";
            this.btnAdvanced.Size = new System.Drawing.Size(201, 23);
            this.btnAdvanced.TabIndex = 2;
            this.btnAdvanced.Text = "Advanced: Temporary Static Profile";
            this.btnAdvanced.UseVisualStyleBackColor = true;
            // 
            // DhcpWarningForm
            // 
            this.AcceptButton = this.btnCancel;
            this.CancelButton = this.btnCancel;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(384, 137);
            this.Controls.Add(this.btnAdvanced);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.lblWarning);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "DhcpWarningForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "DHCP Adapter Detected";
            this.ResumeLayout(false);

        }
    }
}
