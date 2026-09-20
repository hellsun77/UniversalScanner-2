namespace UniversalScanner.Forms
{
    partial class OnvifBatchDateTimeConfigurationForm
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
            this.components = new System.ComponentModel.Container();
            this.groupBoxMode = new System.Windows.Forms.GroupBox();
            this.radioNTP = new System.Windows.Forms.RadioButton();
            this.radioManualFixed = new System.Windows.Forms.RadioButton();
            this.radioManualAdvancing = new System.Windows.Forms.RadioButton();
            this.radioSyncComputer = new System.Windows.Forms.RadioButton();
            this.groupBoxManual = new System.Windows.Forms.GroupBox();
            this.checkDaylightSavings = new System.Windows.Forms.CheckBox();
            this.comboTimeZone = new System.Windows.Forms.ComboBox();
            this.labelTimeZone = new System.Windows.Forms.Label();
            this.dateTimePickerTime = new System.Windows.Forms.DateTimePicker();
            this.dateTimePickerDate = new System.Windows.Forms.DateTimePicker();
            this.labelManualTime = new System.Windows.Forms.Label();
            this.groupBoxNTP = new System.Windows.Forms.GroupBox();
            this.comboNtpAddressType = new System.Windows.Forms.ComboBox();
            this.labelNtpAddressType = new System.Windows.Forms.Label();
            this.textNtpServer = new System.Windows.Forms.TextBox();
            this.labelNtpServer = new System.Windows.Forms.Label();
            this.dataGridViewCameras = new System.Windows.Forms.DataGridView();
            this.ColStatus = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColIPAddress = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColVendor = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColModel = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColCurrentTime = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColRequestedMode = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColResult = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColVerifiedTime = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColDifference = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.ColError = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.buttonStart = new System.Windows.Forms.Button();
            this.buttonCancel = new System.Windows.Forms.Button();
            this.buttonSelectFailed = new System.Windows.Forms.Button();
            this.buttonExport = new System.Windows.Forms.Button();
            this.buttonClose = new System.Windows.Forms.Button();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.labelProgress = new System.Windows.Forms.Label();
            this.labelSummary = new System.Windows.Forms.Label();
            this.groupBoxMode.SuspendLayout();
            this.groupBoxManual.SuspendLayout();
            this.groupBoxNTP.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewCameras)).BeginInit();
            this.SuspendLayout();
            // 
            // groupBoxMode
            // 
            this.groupBoxMode.Controls.Add(this.radioNTP);
            this.groupBoxMode.Controls.Add(this.radioManualFixed);
            this.groupBoxMode.Controls.Add(this.radioManualAdvancing);
            this.groupBoxMode.Controls.Add(this.radioSyncComputer);
            this.groupBoxMode.Location = new System.Drawing.Point(12, 12);
            this.groupBoxMode.Name = "groupBoxMode";
            this.groupBoxMode.Size = new System.Drawing.Size(350, 120);
            this.groupBoxMode.TabIndex = 0;
            this.groupBoxMode.TabStop = false;
            this.groupBoxMode.Text = "Batch Synchronization Mode";
            // 
            // radioNTP
            // 
            this.radioNTP.AutoSize = true;
            this.radioNTP.Location = new System.Drawing.Point(15, 88);
            this.radioNTP.Name = "radioNTP";
            this.radioNTP.Size = new System.Drawing.Size(127, 17);
            this.radioNTP.TabIndex = 3;
            this.radioNTP.Text = "Synchronize with NTP";
            this.radioNTP.UseVisualStyleBackColor = true;
            this.radioNTP.CheckedChanged += new System.EventHandler(this.syncMode_CheckedChanged);
            // 
            // radioManualFixed
            // 
            this.radioManualFixed.AutoSize = true;
            this.radioManualFixed.Location = new System.Drawing.Point(15, 65);
            this.radioManualFixed.Name = "radioManualFixed";
            this.radioManualFixed.Size = new System.Drawing.Size(183, 17);
            this.radioManualFixed.TabIndex = 2;
            this.radioManualFixed.Text = "Manual Time (Fixed for all cameras)";
            this.radioManualFixed.UseVisualStyleBackColor = true;
            this.radioManualFixed.CheckedChanged += new System.EventHandler(this.syncMode_CheckedChanged);
            // 
            // radioManualAdvancing
            // 
            this.radioManualAdvancing.AutoSize = true;
            this.radioManualAdvancing.Checked = true;
            this.radioManualAdvancing.Location = new System.Drawing.Point(15, 42);
            this.radioManualAdvancing.Name = "radioManualAdvancing";
            this.radioManualAdvancing.Size = new System.Drawing.Size(248, 17);
            this.radioManualAdvancing.TabIndex = 1;
            this.radioManualAdvancing.TabStop = true;
            this.radioManualAdvancing.Text = "Manual Time (Advancing based on elapsed batch time)";
            this.radioManualAdvancing.UseVisualStyleBackColor = true;
            this.radioManualAdvancing.CheckedChanged += new System.EventHandler(this.syncMode_CheckedChanged);
            // 
            // radioSyncComputer
            // 
            this.radioSyncComputer.AutoSize = true;
            this.radioSyncComputer.Location = new System.Drawing.Point(15, 19);
            this.radioSyncComputer.Name = "radioSyncComputer";
            this.radioSyncComputer.Size = new System.Drawing.Size(142, 17);
            this.radioSyncComputer.TabIndex = 0;
            this.radioSyncComputer.Text = "Synchronize to Computer";
            this.radioSyncComputer.UseVisualStyleBackColor = true;
            this.radioSyncComputer.CheckedChanged += new System.EventHandler(this.syncMode_CheckedChanged);
            // 
            // groupBoxManual
            // 
            this.groupBoxManual.Controls.Add(this.checkDaylightSavings);
            this.groupBoxManual.Controls.Add(this.comboTimeZone);
            this.groupBoxManual.Controls.Add(this.labelTimeZone);
            this.groupBoxManual.Controls.Add(this.dateTimePickerTime);
            this.groupBoxManual.Controls.Add(this.dateTimePickerDate);
            this.groupBoxManual.Controls.Add(this.labelManualTime);
            this.groupBoxManual.Location = new System.Drawing.Point(12, 140);
            this.groupBoxManual.Name = "groupBoxManual";
            this.groupBoxManual.Size = new System.Drawing.Size(350, 110);
            this.groupBoxManual.TabIndex = 1;
            this.groupBoxManual.TabStop = false;
            this.groupBoxManual.Text = "Time Configuration (For Manual modes)";
            // 
            // checkDaylightSavings
            // 
            this.checkDaylightSavings.AutoSize = true;
            this.checkDaylightSavings.Location = new System.Drawing.Point(200, 75);
            this.checkDaylightSavings.Name = "checkDaylightSavings";
            this.checkDaylightSavings.Size = new System.Drawing.Size(147, 17);
            this.checkDaylightSavings.TabIndex = 5;
            this.checkDaylightSavings.Text = "Enable Daylight Savings";
            this.checkDaylightSavings.UseVisualStyleBackColor = true;
            // 
            // comboTimeZone
            // 
            this.comboTimeZone.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboTimeZone.FormattingEnabled = true;
            this.comboTimeZone.Location = new System.Drawing.Point(90, 48);
            this.comboTimeZone.Name = "comboTimeZone";
            this.comboTimeZone.Size = new System.Drawing.Size(250, 21);
            this.comboTimeZone.TabIndex = 4;
            // 
            // labelTimeZone
            // 
            this.labelTimeZone.AutoSize = true;
            this.labelTimeZone.Location = new System.Drawing.Point(15, 51);
            this.labelTimeZone.Name = "labelTimeZone";
            this.labelTimeZone.Size = new System.Drawing.Size(61, 13);
            this.labelTimeZone.TabIndex = 3;
            this.labelTimeZone.Text = "Time Zone:";
            // 
            // dateTimePickerTime
            // 
            this.dateTimePickerTime.Format = System.Windows.Forms.DateTimePickerFormat.Time;
            this.dateTimePickerTime.Location = new System.Drawing.Point(240, 23);
            this.dateTimePickerTime.Name = "dateTimePickerTime";
            this.dateTimePickerTime.ShowUpDown = true;
            this.dateTimePickerTime.Size = new System.Drawing.Size(100, 20);
            this.dateTimePickerTime.TabIndex = 2;
            // 
            // dateTimePickerDate
            // 
            this.dateTimePickerDate.Format = System.Windows.Forms.DateTimePickerFormat.Short;
            this.dateTimePickerDate.Location = new System.Drawing.Point(90, 23);
            this.dateTimePickerDate.Name = "dateTimePickerDate";
            this.dateTimePickerDate.Size = new System.Drawing.Size(140, 20);
            this.dateTimePickerDate.TabIndex = 1;
            // 
            // labelManualTime
            // 
            this.labelManualTime.AutoSize = true;
            this.labelManualTime.Location = new System.Drawing.Point(15, 26);
            this.labelManualTime.Name = "labelManualTime";
            this.labelManualTime.Size = new System.Drawing.Size(61, 13);
            this.labelManualTime.TabIndex = 0;
            this.labelManualTime.Text = "Date/Time:";
            // 
            // groupBoxNTP
            // 
            this.groupBoxNTP.Controls.Add(this.comboNtpAddressType);
            this.groupBoxNTP.Controls.Add(this.labelNtpAddressType);
            this.groupBoxNTP.Controls.Add(this.textNtpServer);
            this.groupBoxNTP.Controls.Add(this.labelNtpServer);
            this.groupBoxNTP.Location = new System.Drawing.Point(12, 260);
            this.groupBoxNTP.Name = "groupBoxNTP";
            this.groupBoxNTP.Size = new System.Drawing.Size(350, 80);
            this.groupBoxNTP.TabIndex = 2;
            this.groupBoxNTP.TabStop = false;
            this.groupBoxNTP.Text = "NTP Settings";
            // 
            // comboNtpAddressType
            // 
            this.comboNtpAddressType.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboNtpAddressType.FormattingEnabled = true;
            this.comboNtpAddressType.Items.AddRange(new object[] {
            "IPv4",
            "DNS"});
            this.comboNtpAddressType.Location = new System.Drawing.Point(270, 23);
            this.comboNtpAddressType.Name = "comboNtpAddressType";
            this.comboNtpAddressType.Size = new System.Drawing.Size(70, 21);
            this.comboNtpAddressType.TabIndex = 3;
            // 
            // labelNtpAddressType
            // 
            this.labelNtpAddressType.AutoSize = true;
            this.labelNtpAddressType.Location = new System.Drawing.Point(230, 26);
            this.labelNtpAddressType.Name = "labelNtpAddressType";
            this.labelNtpAddressType.Size = new System.Drawing.Size(34, 13);
            this.labelNtpAddressType.TabIndex = 2;
            this.labelNtpAddressType.Text = "Type:";
            // 
            // textNtpServer
            // 
            this.textNtpServer.Location = new System.Drawing.Point(90, 23);
            this.textNtpServer.Name = "textNtpServer";
            this.textNtpServer.Size = new System.Drawing.Size(130, 20);
            this.textNtpServer.TabIndex = 1;
            // 
            // labelNtpServer
            // 
            this.labelNtpServer.AutoSize = true;
            this.labelNtpServer.Location = new System.Drawing.Point(15, 26);
            this.labelNtpServer.Name = "labelNtpServer";
            this.labelNtpServer.Size = new System.Drawing.Size(66, 13);
            this.labelNtpServer.TabIndex = 0;
            this.labelNtpServer.Text = "NTP Server:";
            // 
            // dataGridViewCameras
            // 
            this.dataGridViewCameras.AllowUserToAddRows = false;
            this.dataGridViewCameras.AllowUserToDeleteRows = false;
            this.dataGridViewCameras.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.dataGridViewCameras.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridViewCameras.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.ColStatus,
            this.ColIPAddress,
            this.ColVendor,
            this.ColModel,
            this.ColCurrentTime,
            this.ColRequestedMode,
            this.ColResult,
            this.ColVerifiedTime,
            this.ColDifference,
            this.ColError});
            this.dataGridViewCameras.Location = new System.Drawing.Point(375, 12);
            this.dataGridViewCameras.Name = "dataGridViewCameras";
            this.dataGridViewCameras.ReadOnly = true;
            this.dataGridViewCameras.RowHeadersVisible = false;
            this.dataGridViewCameras.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dataGridViewCameras.Size = new System.Drawing.Size(800, 480);
            this.dataGridViewCameras.TabIndex = 3;
            // 
            // ColStatus
            // 
            this.ColStatus.HeaderText = "Status";
            this.ColStatus.Name = "ColStatus";
            this.ColStatus.ReadOnly = true;
            this.ColStatus.Width = 120;
            // 
            // ColIPAddress
            // 
            this.ColIPAddress.HeaderText = "IP Address";
            this.ColIPAddress.Name = "ColIPAddress";
            this.ColIPAddress.ReadOnly = true;
            // 
            // ColVendor
            // 
            this.ColVendor.HeaderText = "Vendor";
            this.ColVendor.Name = "ColVendor";
            this.ColVendor.ReadOnly = true;
            this.ColVendor.Width = 80;
            // 
            // ColModel
            // 
            this.ColModel.HeaderText = "Model";
            this.ColModel.Name = "ColModel";
            this.ColModel.ReadOnly = true;
            this.ColModel.Width = 100;
            // 
            // ColCurrentTime
            // 
            this.ColCurrentTime.HeaderText = "Current Time";
            this.ColCurrentTime.Name = "ColCurrentTime";
            this.ColCurrentTime.ReadOnly = true;
            this.ColCurrentTime.Width = 130;
            // 
            // ColRequestedMode
            // 
            this.ColRequestedMode.HeaderText = "Req. Mode";
            this.ColRequestedMode.Name = "ColRequestedMode";
            this.ColRequestedMode.ReadOnly = true;
            this.ColRequestedMode.Width = 90;
            // 
            // ColResult
            // 
            this.ColResult.HeaderText = "Result";
            this.ColResult.Name = "ColResult";
            this.ColResult.ReadOnly = true;
            this.ColResult.Width = 80;
            // 
            // ColVerifiedTime
            // 
            this.ColVerifiedTime.HeaderText = "Verified Time";
            this.ColVerifiedTime.Name = "ColVerifiedTime";
            this.ColVerifiedTime.ReadOnly = true;
            this.ColVerifiedTime.Width = 130;
            // 
            // ColDifference
            // 
            this.ColDifference.HeaderText = "Difference";
            this.ColDifference.Name = "ColDifference";
            this.ColDifference.ReadOnly = true;
            this.ColDifference.Width = 80;
            // 
            // ColError
            // 
            this.ColError.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.ColError.HeaderText = "Error";
            this.ColError.Name = "ColError";
            this.ColError.ReadOnly = true;
            // 
            // buttonStart
            // 
            this.buttonStart.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonStart.Location = new System.Drawing.Point(670, 520);
            this.buttonStart.Name = "buttonStart";
            this.buttonStart.Size = new System.Drawing.Size(90, 30);
            this.buttonStart.TabIndex = 4;
            this.buttonStart.Text = "Start";
            this.buttonStart.UseVisualStyleBackColor = true;
            this.buttonStart.Click += new System.EventHandler(this.buttonStart_Click);
            // 
            // buttonCancel
            // 
            this.buttonCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonCancel.Enabled = false;
            this.buttonCancel.Location = new System.Drawing.Point(770, 520);
            this.buttonCancel.Name = "buttonCancel";
            this.buttonCancel.Size = new System.Drawing.Size(90, 30);
            this.buttonCancel.TabIndex = 5;
            this.buttonCancel.Text = "Cancel";
            this.buttonCancel.UseVisualStyleBackColor = true;
            this.buttonCancel.Click += new System.EventHandler(this.buttonCancel_Click);
            // 
            // buttonSelectFailed
            // 
            this.buttonSelectFailed.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonSelectFailed.Location = new System.Drawing.Point(870, 520);
            this.buttonSelectFailed.Name = "buttonSelectFailed";
            this.buttonSelectFailed.Size = new System.Drawing.Size(100, 30);
            this.buttonSelectFailed.TabIndex = 6;
            this.buttonSelectFailed.Text = "Select Failed";
            this.buttonSelectFailed.UseVisualStyleBackColor = true;
            this.buttonSelectFailed.Click += new System.EventHandler(this.buttonSelectFailed_Click);
            // 
            // buttonExport
            // 
            this.buttonExport.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonExport.Location = new System.Drawing.Point(980, 520);
            this.buttonExport.Name = "buttonExport";
            this.buttonExport.Size = new System.Drawing.Size(90, 30);
            this.buttonExport.TabIndex = 7;
            this.buttonExport.Text = "Export Results";
            this.buttonExport.UseVisualStyleBackColor = true;
            this.buttonExport.Click += new System.EventHandler(this.buttonExport_Click);
            // 
            // buttonClose
            // 
            this.buttonClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonClose.Location = new System.Drawing.Point(1080, 520);
            this.buttonClose.Name = "buttonClose";
            this.buttonClose.Size = new System.Drawing.Size(90, 30);
            this.buttonClose.TabIndex = 8;
            this.buttonClose.Text = "Close";
            this.buttonClose.UseVisualStyleBackColor = true;
            this.buttonClose.Click += new System.EventHandler(this.buttonClose_Click);
            // 
            // progressBar
            // 
            this.progressBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.progressBar.Location = new System.Drawing.Point(375, 495);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(800, 15);
            this.progressBar.TabIndex = 9;
            // 
            // labelProgress
            // 
            this.labelProgress.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.labelProgress.AutoSize = true;
            this.labelProgress.Location = new System.Drawing.Point(375, 520);
            this.labelProgress.Name = "labelProgress";
            this.labelProgress.Size = new System.Drawing.Size(350, 13);
            this.labelProgress.TabIndex = 10;
            this.labelProgress.Text = "Ready to start batch configuration. Select mode and click Start.";
            // 
            // labelSummary
            // 
            this.labelSummary.Location = new System.Drawing.Point(12, 360);
            this.labelSummary.Name = "labelSummary";
            this.labelSummary.Size = new System.Drawing.Size(350, 130);
            this.labelSummary.TabIndex = 11;
            this.labelSummary.Text = "";
            // 
            // OnvifBatchDateTimeConfigurationForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1184, 561);
            this.Controls.Add(this.labelSummary);
            this.Controls.Add(this.labelProgress);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.buttonClose);
            this.Controls.Add(this.buttonExport);
            this.Controls.Add(this.buttonSelectFailed);
            this.Controls.Add(this.buttonCancel);
            this.Controls.Add(this.buttonStart);
            this.Controls.Add(this.dataGridViewCameras);
            this.Controls.Add(this.groupBoxNTP);
            this.Controls.Add(this.groupBoxManual);
            this.Controls.Add(this.groupBoxMode);
            this.MinimumSize = new System.Drawing.Size(900, 500);
            this.Name = "OnvifBatchDateTimeConfigurationForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Batch ONVIF Date and Time Configuration";
            this.Load += new System.EventHandler(this.OnvifBatchDateTimeConfigurationForm_Load);
            this.groupBoxMode.ResumeLayout(false);
            this.groupBoxMode.PerformLayout();
            this.groupBoxManual.ResumeLayout(false);
            this.groupBoxManual.PerformLayout();
            this.groupBoxNTP.ResumeLayout(false);
            this.groupBoxNTP.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridViewCameras)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private System.Windows.Forms.GroupBox groupBoxMode;
        private System.Windows.Forms.RadioButton radioNTP;
        private System.Windows.Forms.RadioButton radioManualFixed;
        private System.Windows.Forms.RadioButton radioManualAdvancing;
        private System.Windows.Forms.RadioButton radioSyncComputer;
        private System.Windows.Forms.GroupBox groupBoxManual;
        private System.Windows.Forms.CheckBox checkDaylightSavings;
        private System.Windows.Forms.ComboBox comboTimeZone;
        private System.Windows.Forms.Label labelTimeZone;
        private System.Windows.Forms.DateTimePicker dateTimePickerTime;
        private System.Windows.Forms.DateTimePicker dateTimePickerDate;
        private System.Windows.Forms.Label labelManualTime;
        private System.Windows.Forms.GroupBox groupBoxNTP;
        private System.Windows.Forms.ComboBox comboNtpAddressType;
        private System.Windows.Forms.Label labelNtpAddressType;
        private System.Windows.Forms.TextBox textNtpServer;
        private System.Windows.Forms.Label labelNtpServer;
        private System.Windows.Forms.DataGridView dataGridViewCameras;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColStatus;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColIPAddress;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColVendor;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColModel;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColCurrentTime;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColRequestedMode;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColResult;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColVerifiedTime;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColDifference;
        private System.Windows.Forms.DataGridViewTextBoxColumn ColError;
        private System.Windows.Forms.Button buttonStart;
        private System.Windows.Forms.Button buttonCancel;
        private System.Windows.Forms.Button buttonSelectFailed;
        private System.Windows.Forms.Button buttonExport;
        private System.Windows.Forms.Button buttonClose;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Label labelProgress;
        private System.Windows.Forms.Label labelSummary;
    }
}
