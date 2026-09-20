namespace UniversalScanner.Forms
{
    partial class MotionDetectionForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.lblIpAddress = new System.Windows.Forms.Label();
            this.lblVendor = new System.Windows.Forms.Label();
            this.lblModel = new System.Windows.Forms.Label();
            this.lblChannel = new System.Windows.Forms.Label();
            this.lblConfigSource = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            this.lblRegion = new System.Windows.Forms.Label();
            this.lblGridInfo = new System.Windows.Forms.Label();
            this.chkEnabled = new System.Windows.Forms.CheckBox();
            this.radFullImage = new System.Windows.Forms.RadioButton();
            this.radKeepExisting = new System.Windows.Forms.RadioButton();
            this.radClearArea = new System.Windows.Forms.RadioButton();
            this.numSensitivity = new System.Windows.Forms.NumericUpDown();
            this.lblSensitivity = new System.Windows.Forms.Label();
            this.btnRead = new System.Windows.Forms.Button();
            this.btnSelectAll = new System.Windows.Forms.Button();
            this.btnClear = new System.Windows.Forms.Button();
            this.btnApply = new System.Windows.Forms.Button();
            this.btnTestEventMonitor = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.grpInfo = new System.Windows.Forms.GroupBox();
            this.grpStatus = new System.Windows.Forms.GroupBox();
            this.grpControls = new System.Windows.Forms.GroupBox();
            this.pnlGrid = new System.Windows.Forms.Panel();
            this.lblInstructions = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.numSensitivity)).BeginInit();
            this.grpInfo.SuspendLayout();
            this.grpStatus.SuspendLayout();
            this.grpControls.SuspendLayout();
            this.SuspendLayout();
            
            // grpInfo
            this.grpInfo.Controls.Add(this.lblIpAddress);
            this.grpInfo.Controls.Add(this.lblVendor);
            this.grpInfo.Controls.Add(this.lblModel);
            this.grpInfo.Controls.Add(this.lblChannel);
            this.grpInfo.Controls.Add(this.lblConfigSource);
            this.grpInfo.Location = new System.Drawing.Point(12, 12);
            this.grpInfo.Name = "grpInfo";
            this.grpInfo.Size = new System.Drawing.Size(320, 120);
            this.grpInfo.TabIndex = 0;
            this.grpInfo.TabStop = false;
            this.grpInfo.Text = "Camera Info";
            
            // lblIpAddress
            this.lblIpAddress.AutoSize = true;
            this.lblIpAddress.Location = new System.Drawing.Point(10, 25);
            this.lblIpAddress.Name = "lblIpAddress";
            this.lblIpAddress.Size = new System.Drawing.Size(61, 13);
            this.lblIpAddress.Text = "IP Address:";
            // lblVendor
            this.lblVendor.AutoSize = true;
            this.lblVendor.Location = new System.Drawing.Point(10, 42);
            this.lblVendor.Name = "lblVendor";
            this.lblVendor.Size = new System.Drawing.Size(44, 13);
            this.lblVendor.Text = "Vendor:";
            // lblModel
            this.lblModel.AutoSize = true;
            this.lblModel.Location = new System.Drawing.Point(10, 59);
            this.lblModel.Name = "lblModel";
            this.lblModel.Size = new System.Drawing.Size(39, 13);
            this.lblModel.Text = "Model:";
            // lblChannel
            this.lblChannel.AutoSize = true;
            this.lblChannel.Location = new System.Drawing.Point(10, 76);
            this.lblChannel.Name = "lblChannel";
            this.lblChannel.Size = new System.Drawing.Size(49, 13);
            this.lblChannel.Text = "Channel:";
            // lblConfigSource
            this.lblConfigSource.AutoSize = true;
            this.lblConfigSource.Location = new System.Drawing.Point(10, 93);
            this.lblConfigSource.Name = "lblConfigSource";
            this.lblConfigSource.Size = new System.Drawing.Size(111, 13);
            this.lblConfigSource.Text = "Configuration Source:";
            
            // grpStatus
            this.grpStatus.Controls.Add(this.lblStatus);
            this.grpStatus.Controls.Add(this.lblRegion);
            this.grpStatus.Controls.Add(this.lblGridInfo);
            this.grpStatus.Location = new System.Drawing.Point(340, 12);
            this.grpStatus.Name = "grpStatus";
            this.grpStatus.Size = new System.Drawing.Size(320, 120);
            this.grpStatus.TabIndex = 1;
            this.grpStatus.TabStop = false;
            this.grpStatus.Text = "Current Configuration";
            
            // lblStatus
            this.lblStatus.AutoSize = true;
            this.lblStatus.Location = new System.Drawing.Point(10, 30);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(40, 13);
            this.lblStatus.Text = "Status: Unknown";
            // lblRegion
            this.lblRegion.AutoSize = true;
            this.lblRegion.Location = new System.Drawing.Point(10, 55);
            this.lblRegion.Name = "lblRegion";
            this.lblRegion.Size = new System.Drawing.Size(44, 13);
            this.lblRegion.Text = "Region: Unknown";
            // lblGridInfo
            this.lblGridInfo.AutoSize = true;
            this.lblGridInfo.Location = new System.Drawing.Point(10, 80);
            this.lblGridInfo.Name = "lblGridInfo";
            this.lblGridInfo.Size = new System.Drawing.Size(29, 13);
            this.lblGridInfo.Text = "Grid: Unknown";
            
            // grpControls
            this.grpControls.Controls.Add(this.chkEnabled);
            this.grpControls.Controls.Add(this.radFullImage);
            this.grpControls.Controls.Add(this.radKeepExisting);
            this.grpControls.Controls.Add(this.radClearArea);
            this.grpControls.Controls.Add(this.lblSensitivity);
            this.grpControls.Controls.Add(this.numSensitivity);
            this.grpControls.Location = new System.Drawing.Point(12, 140);
            this.grpControls.Name = "grpControls";
            this.grpControls.Size = new System.Drawing.Size(220, 170);
            this.grpControls.TabIndex = 2;
            this.grpControls.TabStop = false;
            this.grpControls.Text = "Settings";
            
            // chkEnabled
            this.chkEnabled.AutoSize = true;
            this.chkEnabled.Location = new System.Drawing.Point(13, 25);
            this.chkEnabled.Name = "chkEnabled";
            this.chkEnabled.Size = new System.Drawing.Size(147, 17);
            this.chkEnabled.Text = "Enable Motion Detection";
            
            // radKeepExisting
            this.radKeepExisting.AutoSize = true;
            this.radKeepExisting.Location = new System.Drawing.Point(13, 55);
            this.radKeepExisting.Name = "radKeepExisting";
            this.radKeepExisting.Size = new System.Drawing.Size(120, 17);
            this.radKeepExisting.Text = "Keep Existing Area";
            this.radKeepExisting.Checked = true;
            // radFullImage
            this.radFullImage.AutoSize = true;
            this.radFullImage.Location = new System.Drawing.Point(13, 75);
            this.radFullImage.Name = "radFullImage";
            this.radFullImage.Size = new System.Drawing.Size(74, 17);
            this.radFullImage.Text = "Full Image";
            // radClearArea
            this.radClearArea.AutoSize = true;
            this.radClearArea.Location = new System.Drawing.Point(13, 95);
            this.radClearArea.Name = "radClearArea";
            this.radClearArea.Size = new System.Drawing.Size(74, 17);
            this.radClearArea.Text = "Clear Area";
            
            // lblSensitivity
            this.lblSensitivity.AutoSize = true;
            this.lblSensitivity.Location = new System.Drawing.Point(10, 125);
            this.lblSensitivity.Name = "lblSensitivity";
            this.lblSensitivity.Size = new System.Drawing.Size(57, 13);
            this.lblSensitivity.Text = "Sensitivity:";
            // numSensitivity
            this.numSensitivity.Location = new System.Drawing.Point(13, 142);
            this.numSensitivity.Name = "numSensitivity";
            this.numSensitivity.Size = new System.Drawing.Size(120, 20);
            
            // pnlGrid
            this.pnlGrid.Location = new System.Drawing.Point(240, 140);
            this.pnlGrid.Name = "pnlGrid";
            this.pnlGrid.Size = new System.Drawing.Size(420, 280);
            this.pnlGrid.BackColor = System.Drawing.Color.Black;
            this.pnlGrid.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            
            // lblInstructions
            this.lblInstructions.AutoSize = true;
            this.lblInstructions.Location = new System.Drawing.Point(240, 425);
            this.lblInstructions.Name = "lblInstructions";
            this.lblInstructions.Size = new System.Drawing.Size(300, 13);
            this.lblInstructions.Text = "Click and drag on the grid to modify the motion detection area.";
            
            // btnRead
            this.btnRead.Location = new System.Drawing.Point(12, 330);
            this.btnRead.Name = "btnRead";
            this.btnRead.Size = new System.Drawing.Size(220, 30);
            this.btnRead.Text = "Read Current Settings";
            this.btnRead.Click += new System.EventHandler(this.btnRead_Click);
            
            // btnSelectAll
            this.btnSelectAll.Location = new System.Drawing.Point(240, 445);
            this.btnSelectAll.Name = "btnSelectAll";
            this.btnSelectAll.Size = new System.Drawing.Size(75, 23);
            this.btnSelectAll.Text = "Select All";
            this.btnSelectAll.Click += new System.EventHandler(this.btnSelectAll_Click);
            // btnClear
            this.btnClear.Location = new System.Drawing.Point(321, 445);
            this.btnClear.Name = "btnClear";
            this.btnClear.Size = new System.Drawing.Size(75, 23);
            this.btnClear.Text = "Clear";
            this.btnClear.Click += new System.EventHandler(this.btnClear_Click);
            
            // btnApply
            this.btnApply.Location = new System.Drawing.Point(420, 480);
            this.btnApply.Name = "btnApply";
            this.btnApply.Size = new System.Drawing.Size(75, 30);
            this.btnApply.Text = "Apply";
            this.btnApply.Click += new System.EventHandler(this.btnApply_Click);
            
            // btnTestEventMonitor
            this.btnTestEventMonitor.Location = new System.Drawing.Point(12, 480);
            this.btnTestEventMonitor.Name = "btnTestEventMonitor";
            this.btnTestEventMonitor.Size = new System.Drawing.Size(150, 30);
            this.btnTestEventMonitor.Text = "Test in Event Monitor";
            this.btnTestEventMonitor.Click += new System.EventHandler(this.btnTestEventMonitor_Click);
            
            // btnCancel
            this.btnCancel.Location = new System.Drawing.Point(501, 480);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 30);
            this.btnCancel.Text = "Cancel";
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            
            // Form
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(680, 520);
            this.Controls.Add(this.grpInfo);
            this.Controls.Add(this.grpStatus);
            this.Controls.Add(this.grpControls);
            this.Controls.Add(this.pnlGrid);
            this.Controls.Add(this.lblInstructions);
            this.Controls.Add(this.btnRead);
            this.Controls.Add(this.btnSelectAll);
            this.Controls.Add(this.btnClear);
            this.Controls.Add(this.btnApply);
            this.Controls.Add(this.btnTestEventMonitor);
            this.Controls.Add(this.btnCancel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "MotionDetectionForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Motion Detection";
            
            ((System.ComponentModel.ISupportInitialize)(this.numSensitivity)).EndInit();
            this.grpInfo.ResumeLayout(false);
            this.grpInfo.PerformLayout();
            this.grpStatus.ResumeLayout(false);
            this.grpStatus.PerformLayout();
            this.grpControls.ResumeLayout(false);
            this.grpControls.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.Label lblIpAddress;
        private System.Windows.Forms.Label lblVendor;
        private System.Windows.Forms.Label lblModel;
        private System.Windows.Forms.Label lblChannel;
        private System.Windows.Forms.Label lblConfigSource;
        
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Label lblRegion;
        private System.Windows.Forms.Label lblGridInfo;
        
        private System.Windows.Forms.CheckBox chkEnabled;
        private System.Windows.Forms.RadioButton radFullImage;
        private System.Windows.Forms.RadioButton radKeepExisting;
        private System.Windows.Forms.RadioButton radClearArea;
        
        private System.Windows.Forms.NumericUpDown numSensitivity;
        private System.Windows.Forms.Label lblSensitivity;
        
        private System.Windows.Forms.Button btnRead;
        private System.Windows.Forms.Button btnSelectAll;
        private System.Windows.Forms.Button btnClear;
        private System.Windows.Forms.Button btnApply;
        private System.Windows.Forms.Button btnTestEventMonitor;
        private System.Windows.Forms.Button btnCancel;
        
        private System.Windows.Forms.GroupBox grpInfo;
        private System.Windows.Forms.GroupBox grpStatus;
        private System.Windows.Forms.GroupBox grpControls;
        private System.Windows.Forms.Panel pnlGrid;
        private System.Windows.Forms.Label lblInstructions;
    }
}
